namespace FunCraft.Protocol.Registry
{
    using Properties;

    /// <summary>
    /// <br/>Resolves registry entry names to their numeric protocol IDs by parsing
    /// registries.bin directly — no dependency on RegistryPacketLoader.
    /// <br/>Entry protocol ID == 0-based index in the sorted-by-protocol_id list.
    /// </summary>
    public static class RegistryLookup
    {
#pragma warning disable CS8618
        private static IReadOnlyDictionary<uint, IReadOnlyDictionary<uint, int>> _registries;
#pragma warning restore CS8618

        public static void Build(ReadOnlySpan<byte> data)
        {
            var pos = 0;

            var framed = new Dictionary<uint, Dictionary<uint, int>>();

            var registryCount = ReadVarInt(data, ref pos);
            for (var r = 0; r < registryCount; r++)
            {
                var hashedRegistryKey = Hash(ReadString(data, ref pos));
                var entryCount = ReadVarInt(data, ref pos);
                var hashedMap = new Dictionary<uint, int>(entryCount);

                for (var e = 0; e < entryCount; e++)
                {
                    var hashedKey = Hash(ReadString(data, ref pos));
                    hashedMap[hashedKey] = e;
                    var hasNbt = data[pos++] != 0;

                    if (!hasNbt)
                    {
                        continue;
                    }

                    var nbtLen = ReadVarInt(data, ref pos);
                    pos += nbtLen;
                }


                framed[hashedRegistryKey] = hashedMap;
            }

            _registries = framed.ToDictionary(
                kvp => kvp.Key,
                kvp => (IReadOnlyDictionary<uint, int>)kvp.Value
            );
        }

        /// <summary>
        /// Returns the protocol ID, or -1 if not found.
        /// </summary>
        public static int GetId(ReadOnlySpan<byte> registry, ReadOnlySpan<byte> entryName)
        {
            if (_registries.TryGetValue(Hash(registry), out var map) &&
                map.TryGetValue(Hash(entryName), out var id))
                return id;
            return -1;
        }

        /// <summary>
        /// Shorthand for minecraft:item.
        /// </summary>
        public static int GetItemId(ReadOnlySpan<byte> name)
            => GetId("minecraft:item"u8, name);

        /// <summary>
        /// Shorthand for minecraft:block.
        /// </summary>
        public static int GetBlockId(ReadOnlySpan<byte> name)
            => GetId("minecraft:block"u8, name);

        private static uint Hash(ReadOnlySpan<byte> data)
        {
            const uint fnvPrime = 16777619;
            var hash = 2166136261;

            foreach (var b in data)
            {
                hash ^= b;
                hash *= fnvPrime;
            }

            return hash;
        }

        private static int ReadVarInt(ReadOnlySpan<byte> data, ref int pos)
        {
            var value = 0;
            var shift = 0;
            while (true)
            {
                var b = data[pos++];
                value |= (b & 0x7F) << shift;
                if ((b & 0x80) == 0)
                {
                    return value;
                }
                shift += 7;
            }
        }

        private static ReadOnlySpan<byte> ReadString(ReadOnlySpan<byte> data, ref int pos)
        {
            var len = ReadVarInt(data, ref pos);
            var str = data[pos..(pos + len)];
            pos += len;
            return str;
        }
    }
}