using System.Reflection;
using System.Text.Json;
namespace FunCraft.Protocol.Registry
{
    using NBT;
    using Packets.Registry;

    /// <summary>
    /// Loads registries from the embedded registries.json resource and builds
    /// <see cref="RegistryDataPacket"/> instances ready to send during Configuration state.
    /// 
    /// Call <see cref="Load"/> once at startup. The resulting packets are immutable
    /// and can be reused across all connections.
    /// </summary>
    public static class RegistryLoader
    {
        private static IReadOnlyList<RegistryDataPacket>? _packets;

        public static IReadOnlyList<RegistryDataPacket> Packets =>
            _packets ?? throw new InvalidOperationException("RegistryLoader.Load() has not been called.");

        /// <summary>
        /// Reads the embedded registries.json, converts each registry entry to NBT,
        /// and caches the resulting packets. Call once from Program.cs or server startup.
        /// </summary>
        public static void Load()
        {
            var json = ReadEmbeddedJson();
            _packets = BuildPackets(json);
        }

        private static JsonDocument ReadEmbeddedJson()
        {
            var assembly = Assembly.GetExecutingAssembly();

            // The embedded resource name follows: {DefaultNamespace}.{FolderPath}.{FileName}
            var resourceName = assembly
                .GetManifestResourceNames()
                .FirstOrDefault(n => n.EndsWith("registries.json"))
                ?? throw new FileNotFoundException("Embedded resource 'registries.json' not found.");

            using var stream = assembly.GetManifestResourceStream(resourceName)
                ?? throw new FileNotFoundException($"Could not open resource stream: {resourceName}");

            return JsonDocument.Parse(stream);
        }

        private static List<RegistryDataPacket> BuildPackets(JsonDocument doc)
        {
            var packets = new List<RegistryDataPacket>();

            foreach (var registry in doc.RootElement.EnumerateObject())
            {
                var registryName = registry.Name; // e.g. "minecraft:dimension_type"
                var entries = new List<RegistryEntry>();

                foreach (var entry in registry.Value.EnumerateObject())
                {
                    var entryName = entry.Name; // e.g. "minecraft:overworld"
                    var nbtBytes = NbtConverter.Convert(entry.Value);
                    entries.Add(new RegistryEntry(entryName, nbtBytes));
                }

                packets.Add(new RegistryDataPacket(registryName, entries));
            }

            return packets;
        }
    }
}