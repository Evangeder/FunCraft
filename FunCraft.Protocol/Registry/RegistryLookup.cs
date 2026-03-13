namespace FunCraft.Protocol.Registry
{
    public static class RegistryLookup
    {
#pragma warning disable CS8618
#pragma warning disable CA1859
        // Registry forward map: registry_hash → (entry_name_hash → protocol_id)
        // Populated by Build() from registries.bin — unchanged from original.
        private static IReadOnlyDictionary<uint, IReadOnlyDictionary<uint, int>> _registries;

        // Block state maps populated by LoadBlocks() from blocks.bin.
        // keyed state lookup: full_key_hash  → global palette state ID
        private static IReadOnlyDictionary<uint, int> _blockStates;
        // default state lookup: block_name_hash → default global palette state ID
        private static IReadOnlyDictionary<uint, int> _blockDefaults;
        // property name set: block_name_hash → set of property names on that block
        private static IReadOnlyDictionary<uint, HashSet<string>> _blockProperties;
        // reverse item lookup: item protocol_id → UTF-8 name bytes
        private static IReadOnlyDictionary<int, byte[]> _itemNames;
#pragma warning restore CA1859
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

            _registries = result.ToDictionary(
                kvp => kvp.Key,
                kvp => (IReadOnlyDictionary<uint, int>)kvp.Value);
            _itemNames = itemNames;
        }

        private static Dictionary<ushort, float> _hardnessByStateId = [];

        /// <summary>
        /// Returns the hardness of the block at the given state ID.
        /// <list type="bullet">
        ///   <item><term>0f</term><description>instant-break (client sends only StartedDigging)</description></item>
        ///   <item><term>-1f</term><description>unbreakable in survival</description></item>
        ///   <item><term>&gt; 0</term><description>normal hardness; break time ≈ hardness × 1.5 s bare-hand</description></item>
        /// </list>
        /// </summary>
        public static float GetHardness(ushort stateId)
            => _hardnessByStateId.TryGetValue(stateId, out var h) ? h : 1.5f;

        /// <summary>Returns true if the block at this state ID breaks instantly
        /// (client sends only <c>StartedDigging</c>, no <c>FinishedDigging</c>).</summary>
        public static bool IsZeroHardness(ushort stateId) => GetHardness(stateId) == 0f;

        /// <summary>
        /// Loads block state data from <paramref name="data"/> (the contents of blocks.bin).
        /// Must be called once at startup after <see cref="Build"/>.
        /// </summary>
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
                var nameStr = Utf8ToString(nameSpan);
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
                    if (ci != inside.Length && inside[ci] != (byte) ',')
                    {
                        continue;
                    }

                    if (ci > start)
                    {
                        var pair = inside[start..ci];
                        var eq = IndexOf(pair, (byte)'=');
                        if (eq >= 0) propSet.Add(Utf8ToString(pair[..eq]));
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
            }

            _blockStates = states;
            _blockDefaults = defaults;
            _blockProperties = props.ToDictionary(k => k.Key, k => (HashSet<string>)k.Value);
            _hardnessByStateId = hardnessMap;

            BlockHardnessTable.Dispose();
        }

        /// <summary>
        /// Returns the protocol ID for the entry, or -1 if not found.
        /// </summary>
        public static int GetId(ReadOnlySpan<byte> registry, ReadOnlySpan<byte> entryName)
        {
            if (_registries.TryGetValue(Hash(registry), out var map) &&
                map.TryGetValue(Hash(entryName), out var id))
            {
                return id;
            }

            return -1;
        }

        /// <summary>
        /// Item protocol ID from minecraft:item, or -1.
        /// </summary>
        public static int GetItemId(ReadOnlySpan<byte> name)
            => GetId("minecraft:item"u8, name);

        /// <summary>
        /// Default block state ID (global palette) for a block name, or 0.
        /// </summary>
        public static ushort GetBlockId(ReadOnlySpan<byte> name)
        {
            if (_blockDefaults.TryGetValue(Hash(name), out var id))
            {
                return (ushort)id;
            }

            return 0;
        }

        /// <summary>
        /// Block state ID for a fully-qualified key such as
        /// <c>"minecraft:oak_log[axis=x]"</c>, or -1 if not found.
        /// </summary>
        public static int GetBlockStateId(ReadOnlySpan<byte> fullKey)
        {
            if (_blockStates.TryGetValue(Hash(fullKey), out var id))
            {
                return id;
            }

            return -1;
        }

        /// <summary>
        /// Returns the UTF-8 name bytes for an item protocol ID, or empty if unknown.
        /// </summary>
        public static ReadOnlyMemory<byte> GetItemName(int id)
            => _itemNames.TryGetValue(id, out var name) ? name : new ReadOnlyMemory<byte>();

        /// <summary>
        /// Returns the set of property names for the given block, or null for
        /// single-state blocks.
        /// </summary>
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