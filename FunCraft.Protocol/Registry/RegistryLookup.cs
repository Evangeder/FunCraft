using System.Collections.Frozen;

namespace FunCraft.Protocol.Registry
{
    public static class RegistryLookup
    {
#pragma warning disable CS8618
        private static FrozenDictionary<uint, FrozenDictionary<uint, int>> _registries;
        private static FrozenDictionary<uint, int> _blockStates;
        private static FrozenDictionary<uint, int> _blockDefaults;
        private static FrozenDictionary<uint, HashSet<string>> _blockProperties;
        private static FrozenDictionary<int, byte[]> _itemNames;
        private static FrozenDictionary<int, byte[]> _blockNameByStateId;
        private static FrozenDictionary<ushort, float> _hardnessByStateId;
#pragma warning restore CS8618

        public static void Build(ReadOnlySpan<byte> data)
        {
            var pos = 0;
            var result = new Dictionary<uint, Dictionary<uint, int>>();
            var itemHash = Hash("minecraft:item"u8);
            var itemNames = new Dictionary<int, byte[]>();

            var registryCount = ReadVarInt(data, ref pos);
            for (var r = 0; r < registryCount; r++)
            {
                var registryKey = ReadString(data, ref pos);
                var regHash = Hash(registryKey);
                var entryCount = ReadVarInt(data, ref pos);
                var hashedMap = new Dictionary<uint, int>(entryCount);

                for (var e = 0; e < entryCount; e++)
                {
                    var entryKey = ReadString(data, ref pos);
                    hashedMap[Hash(entryKey)] = e;

                    if (regHash == itemHash)
                    {
                        itemNames.TryAdd(e, entryKey.ToArray());
                    }

                    var hasNbt = data[pos++] != 0;
                    if (!hasNbt)
                    {
                        continue;
                    }

                    var nbtLen = ReadVarInt(data, ref pos);
                    pos += nbtLen;
                }

                result[regHash] = hashedMap;
            }

            _itemNames = itemNames.ToFrozenDictionary();

            _registries = result.ToFrozenDictionary(
                kvp => kvp.Key,
                kvp => kvp.Value.ToFrozenDictionary());
        }

        public static float GetHardness(ushort stateId)
            => _hardnessByStateId.GetValueOrDefault(stateId, 1.5f);

        public static bool IsZeroHardness(ushort stateId)
            => GetHardness(stateId) == 0f;

        public static void LoadBlocks(ReadOnlySpan<byte> data)
        {
            var pos = 0;

            var stateCount = ReadVarInt(data, ref pos);
            var states = new Dictionary<uint, int>(stateCount);
            var props = new Dictionary<uint, HashSet<string>>();
            var stateNames = new List<(int StateId, string BlockName)>(stateCount);

            for (var i = 0; i < stateCount; i++)
            {
                var key = ReadString(data, ref pos);
                var stateId = ReadVarInt(data, ref pos);

                states[Hash(key)] = stateId;

                var bracketIdx = IndexOf(key, (byte)'[');
                var nameSpan = bracketIdx >= 0 ? key[..bracketIdx] : key;

                // Intern the block name string so all state variants of the same block
                // (e.g. all 64 states of oak_stairs) share one string object rather than
                // each creating their own. Without interning, "minecraft:oak_stairs" was
                // allocated ~64 times in stateNames, one per state variant.
                var nameStr = string.Intern(Utf8ToString(nameSpan));
                stateNames.Add((stateId, nameStr));

                if (bracketIdx < 0)
                {
                    continue;
                }

                var blockHash = Hash(key[..bracketIdx]);

                if (!props.TryGetValue(blockHash, out var propSet))
                {
                    propSet = new HashSet<string>(StringComparer.Ordinal);
                    props[blockHash] = propSet;
                }

                var inside = key[(bracketIdx + 1)..^1];
                var start = 0;

                for (var ci = 0; ci <= inside.Length; ci++)
                {
                    if (ci != inside.Length && inside[ci] != (byte)',')
                    {
                        continue;
                    }

                    if (ci > start)
                    {
                        var pair = inside[start..ci];
                        var eq = IndexOf(pair, (byte)'=');

                        // Intern property name strings ("waterlogged", "facing", "powered", etc.).
                        // Each name appears as a property on hundreds of blocks; without interning,
                        // "waterlogged" alone produced 411 separate equal string objects in Gen2.
                        if (eq >= 0)
                        {
                            propSet.Add(string.Intern(Utf8ToString(pair[..eq])));
                        }
                    }

                    start = ci + 1;
                }
            }

            var defaultCount = ReadVarInt(data, ref pos);
            var defaults = new Dictionary<uint, int>(defaultCount);

            for (var i = 0; i < defaultCount; i++)
            {
                var name = ReadString(data, ref pos);
                var stateId = ReadVarInt(data, ref pos);
                defaults[Hash(name)] = stateId;
            }

            var hardnessCache = new Dictionary<string, float>(256, StringComparer.Ordinal);
            var hardnessMap = new Dictionary<ushort, float>(stateCount);
            var nameCache = new Dictionary<string, byte[]>(256, StringComparer.Ordinal);
            var nameByState = new Dictionary<int, byte[]>(stateCount);

            foreach (var (stateId, blockName) in stateNames)
            {
                if (!hardnessCache.TryGetValue(blockName, out var h))
                {
                    h = BlockHardnessTable.Get(blockName);

                    if (h == -2f)
                    {
                        h = 1.5f;
                    }

                    hardnessCache[blockName] = h;
                }

                hardnessMap[(ushort)stateId] = h;

                if (!nameCache.TryGetValue(blockName, out var nameBytes))
                {
                    nameBytes = System.Text.Encoding.UTF8.GetBytes(blockName);
                    nameCache[blockName] = nameBytes;
                }

                nameByState[stateId] = nameBytes;
            }

            _blockStates = states.ToFrozenDictionary();
            _blockDefaults = defaults.ToFrozenDictionary();
            _blockProperties = props.ToFrozenDictionary(k => k.Key, k => k.Value);
            _hardnessByStateId = hardnessMap.ToFrozenDictionary();
            _blockNameByStateId = nameByState.ToFrozenDictionary();

            BlockHardnessTable.Dispose();
        }

        public static int GetId(ReadOnlySpan<byte> registry, ReadOnlySpan<byte> entryName)
        {
            if (_registries.TryGetValue(Hash(registry), out var map) &&
                map.TryGetValue(Hash(entryName), out var id))
            {
                return id;
            }

            return -1;
        }

        public static int GetItemId(ReadOnlySpan<byte> name)
            => GetId("minecraft:item"u8, name);

        public static ushort GetBlockId(ReadOnlySpan<byte> name)
        {
            if (_blockDefaults.TryGetValue(Hash(name), out var id))
            {
                return (ushort)id;
            }

            return 0;
        }

        public static int GetBlockStateId(ReadOnlySpan<byte> fullKey)
        {
            if (_blockStates.TryGetValue(Hash(fullKey), out var id))
            {
                return id;
            }

            return -1;
        }

        public static ReadOnlyMemory<byte> GetBlockName(int stateId)
            => _blockNameByStateId.TryGetValue(stateId, out var name) ? name : ReadOnlyMemory<byte>.Empty;

        public static ReadOnlyMemory<byte> GetItemName(int id)
            => _itemNames.TryGetValue(id, out var name) ? name : new ReadOnlyMemory<byte>();

        public static HashSet<string>? GetBlockProperties(ReadOnlySpan<byte> blockName)
        {
            _blockProperties.TryGetValue(Hash(blockName), out var p);
            return p;
        }

        private static uint Hash(ReadOnlySpan<byte> data)
        {
            const uint fnvPrime = 16777619;
            var hash = 2166136261u;
            foreach (var b in data) { hash ^= b; hash *= fnvPrime; }
            return hash;
        }

        private static int ReadVarInt(ReadOnlySpan<byte> data, ref int pos)
        {
            var value = 0; var shift = 0;
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

        private static int IndexOf(ReadOnlySpan<byte> span, byte value)
        {
            for (var i = 0; i < span.Length; i++)
            {
                if (span[i] == value)
                {
                    return i;
                }
            }

            return -1;
        }

        private static string Utf8ToString(ReadOnlySpan<byte> span)
            => System.Text.Encoding.UTF8.GetString(span);
    }
}