using System.Text;
using System.Text.Json;

namespace FunCraft.RegistryBuilder
{
    internal class RegistryBundler(string generatedFolder)
    {
        // Registries where we send NBT codec data.
        // Key = registry name suffix after "minecraft:"
        // Value = relative path under data/minecraft/
        private static readonly Dictionary<string, string> NbtRegistries = new()
        {
            ["dimension_type"] = "dimension_type",
            ["worldgen/biome"] = "worldgen/biome",
            ["damage_type"] = "damage_type",
            ["chat_type"] = "chat_type",
            ["banner_pattern"] = "banner_pattern",
            ["wolf_variant"] = "wolf_variant",
            ["wolf_sound_variant"] = "wolf_sound_variant",
            ["painting_variant"] = "painting_variant",
            ["trim_material"] = "trim_material",
            ["trim_pattern"] = "trim_pattern",
            ["instrument"] = "instrument",
            ["jukebox_song"] = "jukebox_song",
            ["cat_variant"] = "cat_variant",
            ["chicken_variant"] = "chicken_variant",
            ["cow_variant"] = "cow_variant",
            ["frog_variant"] = "frog_variant",
            ["pig_variant"] = "pig_variant",
        };

        public void Write(string outputPath)
        {
            var reportsPath = Path.Combine(generatedFolder, "reports", "registries.json");
            using var doc = JsonDocument.Parse(File.ReadAllBytes(reportsPath));

            // Collect registry entries in protocol_id order
            var registries = new List<(string Id, List<(string EntryId, int ProtocolId)> Entries)>();

            foreach (var reg in doc.RootElement.EnumerateObject())
            {
                var entries = new List<(string, int)>();
                if (reg.Value.TryGetProperty("entries", out var entriesEl))
                {
                    foreach (var entry in entriesEl.EnumerateObject())
                        entries.Add((entry.Name, entry.Value.GetProperty("protocol_id").GetInt32()));
                }

                entries.Sort((a, b) => a.Item2.CompareTo(b.Item2));
                registries.Add((reg.Name, entries));
            }

            foreach (var (suffix, relPath) in NbtRegistries)
            {
                var regId = $"minecraft:{suffix}";
                var dataDir = Path.Combine(generatedFolder, "data", "minecraft", relPath);
                if (!Directory.Exists(dataDir)) continue;

                var existingIndex = registries.FindIndex(r => r.Id == regId);
                if (existingIndex >= 0)
                {
                    continue;
                }

                var entries = Directory.EnumerateFiles(dataDir, "*.json", SearchOption.TopDirectoryOnly)
                    .OrderBy(f => f)
                    .Select((f, i) => ($"minecraft:{Path.GetFileNameWithoutExtension(f)}", i))
                    .ToList();

                if (entries.Count > 0)
                    registries.Add((regId, entries));
            }

            using var ms = new MemoryStream();

            WriteVarInt(ms, registries.Count);

            foreach (var (regId, entries) in registries)
            {
                WriteString(ms, regId);
                WriteVarInt(ms, entries.Count);

                Console.Write($"  {regId} ({entries.Count} entries)");

                // Does this registry have NBT data?
                var suffix = regId.StartsWith("minecraft:") ? regId["minecraft:".Length..] : regId;
                var hasNbtDir = NbtRegistries.TryGetValue(suffix, out var relPath);
                var dataDir = hasNbtDir
                    ? Path.Combine(generatedFolder, "data", "minecraft", relPath!)
                    : null;

                SchemaAnalyzer? analyzer = null;
                CompoundSchemaNode? schema = null;
                if (dataDir != null && Directory.Exists(dataDir))
                {
                    analyzer = new SchemaAnalyzer();
                    schema = analyzer.Analyze(dataDir);
                    Console.Write(" [NBT]");
                }

                Console.WriteLine();

                foreach (var (entryId, _) in entries)
                {
                    WriteString(ms, entryId);

                    if (schema == null || dataDir == null)
                    {
                        ms.WriteByte(0); // hasNbt = false
                        continue;
                    }

                    // Entry name: "minecraft:overworld" → file "overworld.json"
                    var entryName = entryId.Contains(':') ? entryId[(entryId.IndexOf(':') + 1)..] : entryId;
                    // Handle nested paths like "worldgen/biome/plains"
                    var filePath = Path.Combine(dataDir, entryName.Replace('/', Path.DirectorySeparatorChar) + ".json");

                    if (!File.Exists(filePath))
                    {
                        ms.WriteByte(0); // hasNbt = false
                        Console.WriteLine($"    WARNING: missing {filePath}");
                        continue;
                    }

                    using var entryDoc = JsonDocument.Parse(File.ReadAllBytes(filePath));
                    var nbtBytes = NbtBinaryWriter.WriteNetworkNbt(entryDoc.RootElement, schema!, analyzer!);

                    ms.WriteByte(1); // hasNbt = true
                    WriteVarInt(ms, nbtBytes.Length);
                    ms.Write(nbtBytes);
                }
            }

            Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);
            File.WriteAllBytes(outputPath, ms.ToArray());
            Console.WriteLine($"\nWrote {ms.Length:N0} bytes → {outputPath}");
        }

        /// <summary>
        /// Writes a standalone blocks.bin containing every block state needed for
        /// server-side placement lookups. Format:
        /// <code>
        ///   VarInt  stateCount
        ///   for each state:
        ///     String  key        e.g. "minecraft:oak_log[axis=x]"  (props sorted A-Z)
        ///     VarInt  stateId    global palette ID
        ///   VarInt  defaultCount
        ///   for each default:
        ///     String  blockName  e.g. "minecraft:oak_log"
        ///     VarInt  stateId    default state global palette ID
        /// </code>
        /// </summary>
        public void WriteBlocks(string outputPath)
        {
            Console.WriteLine($"Writing blocks.bin...");
            var blocksPath = Path.Combine(generatedFolder, "reports", "blocks.json");
            if (!File.Exists(blocksPath))
            {
                Console.WriteLine($"WARNING: blocks.json not found at {blocksPath}, skipping blocks.bin");
                return;
            }

            using var doc = JsonDocument.Parse(File.ReadAllBytes(blocksPath));

            var states = new List<(string Key, int StateId)>();
            var defaults = new List<(string Name, int StateId)>();

            foreach (var block in doc.RootElement.EnumerateObject())
            {
                if (!block.Value.TryGetProperty("states", out var statesEl)) continue;

                int defaultId = -1;
                bool foundDefault = false;

                foreach (var state in statesEl.EnumerateArray())
                {
                    var stateId = state.GetProperty("id").GetInt32();

                    if (!foundDefault && state.TryGetProperty("default", out var defProp) && defProp.GetBoolean())
                    {
                        defaultId = stateId;
                        foundDefault = true;
                    }

                    if (state.TryGetProperty("properties", out var propsEl))
                    {
                        // Build "name[k=v,k=v]" with props sorted A-Z.
                        var pairs = new List<(string K, string V)>();
                        foreach (var prop in propsEl.EnumerateObject())
                            pairs.Add((prop.Name, prop.Value.GetString()!));
                        pairs.Sort((a, b) => string.Compare(a.K, b.K, StringComparison.Ordinal));

                        var propStr = string.Join(",", pairs.Select(p => $"{p.K}={p.V}"));
                        states.Add(($"{block.Name}[{propStr}]", stateId));
                    }
                    else
                    {
                        // Single-state block — the keyed entry equals the bare name.
                        states.Add((block.Name, stateId));
                    }
                }

                // Fall back to first state if none was marked default.
                if (!foundDefault)
                    defaultId = statesEl.EnumerateArray().First().GetProperty("id").GetInt32();

                defaults.Add((block.Name, defaultId));
            }

            using var ms = new MemoryStream();
            WriteVarInt(ms, states.Count);
            foreach (var (key, id) in states)
            {
                WriteString(ms, key);
                WriteVarInt(ms, id);
            }
            WriteVarInt(ms, defaults.Count);
            foreach (var (name, id) in defaults)
            {
                WriteString(ms, name);
                WriteVarInt(ms, id);
            }

            Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);
            File.WriteAllBytes(outputPath, ms.ToArray());
            Console.WriteLine($"Wrote {ms.Length:N0} bytes → {outputPath}  ({states.Count} states, {defaults.Count} defaults)");
        }

        private static void WriteVarInt(Stream s, int value)
        {
            var uv = (uint)value;
            while (true)
            {
                if ((uv & ~0x7Fu) == 0)
                {
                    s.WriteByte((byte)uv);
                    return;
                }

                s.WriteByte((byte)((uv & 0x7F) | 0x80));
                uv >>= 7;
            }
        }

        private static void WriteString(Stream s, string value)
        {
            var bytes = Encoding.UTF8.GetBytes(value);
            WriteVarInt(s, bytes.Length);
            s.Write(bytes);
        }
    }
}