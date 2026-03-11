using System.Buffers.Binary;

namespace FunCraft.Network.World
{
    using FunCraft.World.Chunks;

    /// <summary>
    /// Converts a <see cref="ChunkColumn"/> into the protocol-773 binary payload
    /// expected by <c>ChunkDataPacket</c>: heightmaps + sections + block entities + light.
    /// </summary>
    public static class ChunkSerializer
    {
        private const int HmBpe = 9;
        private const int HmEntriesPerLong = 64 / HmBpe;   // 7
        private const int HmNumLongs = (256 + HmEntriesPerLong - 1) / HmEntriesPerLong; // 37

        private const int HmTypeWorldSurface = 1;
        private const int HmTypeMotionBlocking = 4;

        private const int LightSections = 26;  // SectionCount + 2 boundary
        private const long AllSectionBits = (1L << LightSections) - 1;

        // Indirect (indirect-mapped) mode thresholds for block states:
        //   0       → single-valued palette
        //   4-8     → indirect palette
        //   ≥9 (15) → direct (global palette)
        private const int DirectBpe = 15;
        private const int MaxIndirectBpe = 8;

        /// <summary>
        /// Serialises <paramref name="column"/> to a new byte array.
        /// <br/>This is the payload written AFTER the two chunk coordinate ints in
        /// <c>ChunkDataPacket</c>.
        /// </summary>
        public static byte[] Serialize(ChunkColumn column)
        {
            using var ms = new MemoryStream(16_384);
            WriteHeightmaps(ms, column);
            WriteChunkSections(ms, column);
            WriteBlockEntities(ms);
            WriteLightData(ms);
            return ms.ToArray();
        }

        private static void WriteHeightmaps(Stream s, ChunkColumn column)
        {
            var motionBlocking = ComputeHeightmap(column, includeMotionBlocking: true);
            var worldSurface = ComputeHeightmap(column, includeMotionBlocking: false);

            WriteVarInt(s, 2);

            WriteVarInt(s, HmTypeMotionBlocking);
            WritePackedLongs(s, motionBlocking, HmBpe, 256);

            WriteVarInt(s, HmTypeWorldSurface);
            WritePackedLongs(s, worldSurface, HmBpe, 256);
        }

        /// <summary>
        /// Returns a 256-element array (column-major: z*16+x) of surface heights.
        /// Each value is the Y coordinate of the highest solid block + 1,
        /// relative to world minimum (i.e., value 0 means no solid blocks).
        /// </summary>
        private static int[] ComputeHeightmap(ChunkColumn column, bool includeMotionBlocking)
        {
            var heights = new int[256];
            for (var z = 0; z < 16; z++)
                for (var x = 0; x < 16; x++)
                {
                    for (var y = 319; y >= ChunkColumn.WorldMinY; y--)
                    {
                        var block = column.GetBlock(x, y, z);

                        if (block.IsAir)
                        {
                            continue;
                        }

                        heights[z * 16 + x] = (y - ChunkColumn.WorldMinY) + 1;
                        break;
                    }
                }
            return heights;
        }

        /// <summary>
        /// Writes <paramref name="count"/> values from <paramref name="values"/> packed
        /// at <paramref name="bpe"/> bits-per-entry into a prefixed array of longs.
        /// </summary>
        private static void WritePackedLongs(Stream s, int[] values, int bpe, int count)
        {
            var entriesPerLong = 64 / bpe;
            var numLongs = (count + entriesPerLong - 1) / entriesPerLong;

            WriteVarInt(s, numLongs);

            var shift = 0;
            var current = 0L;

            for (var i = 0; i < count; i++)
            {
                current |= ((long)values[i] & ((1L << bpe) - 1)) << shift;
                shift += bpe;

                if (shift + bpe <= 64)
                {
                    continue;
                }

                WriteI64(s, current);
                current = 0L;
                shift = 0;
            }

            if (shift > 0)
            {
                WriteI64(s, current);
            }
        }

        private static void WriteChunkSections(Stream s, ChunkColumn column)
        {
            using var buf = new MemoryStream(4096);
            for (var i = 0; i < ChunkColumn.SectionCount; i++)
            {
                WriteSection(buf, column.GetSection(i));
            }

            var bytes = buf.ToArray();
            WriteVarInt(s, bytes.Length);
            s.Write(bytes);
        }

        private static void WriteSection(Stream s, ChunkSection section)
        {
            WriteI16(s, section.BlockCount);
            WriteBlockStateContainer(s, section);
            WriteBiomeContainer(s);
        }

        private static void WriteBlockStateContainer(Stream s, ChunkSection section)
        {
            if (section.IsUniform(out var uniform))
            {
                s.WriteByte(0);
                WriteVarInt(s, uniform.Id);
                return;
            }

            var palette = BuildPalette(section, out var bpe);

            if (bpe <= MaxIndirectBpe)
            {
                s.WriteByte((byte)bpe);
                WriteVarInt(s, palette.Count);
                foreach (var id in palette)
                    WriteVarInt(s, id);
                WriteIndirectData(s, section, palette, bpe);
            }
            else
            {
                s.WriteByte(DirectBpe);
                WriteDirectData(s, section);
            }
        }

        private static List<int> BuildPalette(ChunkSection section, out int bpe)
        {
            var set = new HashSet<int>();
            foreach (var b in section.Blocks)
                set.Add(b);

            var palette = new List<int>(set);
            palette.Sort();

            bpe = Math.Max(4, CeilLog2(palette.Count));
            if (bpe > MaxIndirectBpe) bpe = DirectBpe;
            return palette;
        }

        private static void WriteIndirectData(Stream s, ChunkSection section,
            List<int> palette, int bpe)
        {
            var reverseMap = new Dictionary<int, int>(palette.Count);
            for (var i = 0; i < palette.Count; i++)
                reverseMap[palette[i]] = i;

            var entriesPerLong = 64 / bpe;
            var numLongs = (ChunkSection.Volume + entriesPerLong - 1) / entriesPerLong;
            var longs = new long[numLongs];

            var blocks = section.Blocks;
            for (var i = 0; i < blocks.Length; i++)
            {
                var paletteIndex = reverseMap[blocks[i]];
                var longIndex = i / entriesPerLong;
                var shift = (i % entriesPerLong) * bpe;
                longs[longIndex] |= ((long)paletteIndex) << shift;
            }

            //WriteVarInt(s, numLongs);
            foreach (var l in longs)
            {
                WriteI64(s, l);
            }
        }

        private static void WriteDirectData(Stream s, ChunkSection section)
        {
            const int entriesPerLong = 64 / DirectBpe;
            const int numLongs = (ChunkSection.Volume + entriesPerLong - 1) / entriesPerLong;
            var longs = new long[numLongs];

            var blocks = section.Blocks;
            for (var i = 0; i < blocks.Length; i++)
            {
                var longIndex = i / entriesPerLong;
                var shift = (i % entriesPerLong) * DirectBpe;
                longs[longIndex] |= (long)blocks[i] << shift;
            }

            foreach (var l in longs)
            {
                WriteI64(s, l);
            }
        }

        // Biomes are always single-valued (plains = 0) for now
        private static void WriteBiomeContainer(Stream s)
        {
            s.WriteByte(0);
            WriteVarInt(s, 0);
        }

        private static void WriteBlockEntities(Stream s) => WriteVarInt(s, 0);

        private static void WriteLightData(Stream s)
        {
            // All sections dark, no arrays transmitted — player sees natural skylight
            // from the time-of-day GameEvent we already sent.
            WriteBitSet(s, 0L);              // skylight mask   — none lit
            WriteBitSet(s, 0L);              // block light mask — none lit
            WriteBitSet(s, AllSectionBits);  // empty skylight mask — all empty
            WriteBitSet(s, AllSectionBits);  // empty block light mask — all empty
            WriteVarInt(s, 0);               // skylight array count
            WriteVarInt(s, 0);               // block light array count
        }

        private static void WriteBitSet(Stream s, long value)
        {
            WriteVarInt(s, 1);
            WriteI64(s, value);
        }

        private static void WriteVarInt(Stream s, int value)
        {
            var uv = (uint)value;
            while (true)
            {
                if ((uv & ~0x7Fu) == 0) { s.WriteByte((byte)uv); return; }
                s.WriteByte((byte)((uv & 0x7F) | 0x80));
                uv >>= 7;
            }
        }

        private static void WriteI16(Stream s, short value)
        {
            s.WriteByte((byte)(value >> 8));
            s.WriteByte((byte)value);
        }

        private static void WriteI64(Stream s, long value)
        {
            Span<byte> buf = stackalloc byte[8];
            BinaryPrimitives.WriteInt64BigEndian(buf, value);
            s.Write(buf);
        }

        private static int CeilLog2(int x) =>
            x <= 1 ? 0 : (int)Math.Ceiling(Math.Log2(x));
    }
}