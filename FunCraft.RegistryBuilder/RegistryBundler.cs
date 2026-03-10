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

                SchemaAnalyzer analyzer = null;
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
                    var nbtBytes = NbtBinaryWriter.WriteNetworkNbt(entryDoc.RootElement, schema!, analyzer);

                    ms.WriteByte(1); // hasNbt = true
                    WriteVarInt(ms, nbtBytes.Length);
                    ms.Write(nbtBytes);
                }
            }

            Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);
            File.WriteAllBytes(outputPath, ms.ToArray());
            Console.WriteLine($"\nWrote {ms.Length:N0} bytes → {outputPath}");
        }

        private static void WriteVarInt(Stream s, int value)
        {
            var uv = (uint) value;
            while (true)
            {
                if ((uv & ~0x7Fu) == 0)
                {
                    s.WriteByte((byte) uv);
                    return;
                }

                s.WriteByte((byte) ((uv & 0x7F) | 0x80));
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