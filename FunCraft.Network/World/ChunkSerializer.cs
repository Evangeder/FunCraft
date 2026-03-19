using FunCraft.Protocol.Types;
using System.Buffers;
using System.Buffers.Binary;

namespace FunCraft.Network.World
{
    using FunCraft.World.Chunks;

    /// <summary>
    /// Converts a <see cref="ChunkColumn"/> into the protocol-773 binary payload
    /// expected by <c>ChunkDataPacket</c>: heightmaps + sections + block entities + light.
    ///
    /// <para>
    /// Allocation budget per <see cref="Serialize"/> call:
    /// <list type="bullet">
    ///   <item>One <c>int[]</c> rented for both heightmaps (512 ints, returned before return).</item>
    ///   <item>One <c>int[]</c> rented as palette scratch across all 24 sections (4096 ints, returned before return).</item>
    ///   <item>One <c>long[]</c> rented per section for packed block data (returned per section).</item>
    ///   <item>One <c>MemoryStream</c> buffer for the output, returned as the final <c>byte[]</c> via <c>ms.ToArray()</c>.</item>
    /// </list>
    /// All other operations (palette building, reverse-map lookups via binary search,
    /// heightmap packing) are allocation-free.
    /// </para>
    /// </summary>
    public static class ChunkSerializer
    {
        private const int HmBpe = 9;
        private const int HmEntriesPerLong = 64 / HmBpe;
        private const int HmNumLongs = (256 + HmEntriesPerLong - 1) / HmEntriesPerLong;

        private const int HmTypeWorldSurface = 1;
        private const int HmTypeMotionBlocking = 4;

        private const int LightSections = 26;
        private const long AllSectionBits = (1L << LightSections) - 1;

        // Indirect (indirect-mapped) mode thresholds for block states:
        //   0       -> single-valued palette
        //   4-8     -> indirect palette
        //   >=9 (15) -> direct (global palette)
        private const int DirectBpe = 15;
        private const int MaxIndirectBpe = 8;

        /// <summary>
        /// Serialises <paramref name="column"/> to a new byte array.
        /// This is the payload written AFTER the two chunk coordinate ints in
        /// <c>ChunkDataPacket</c>.
        /// </summary>
        public static byte[] Serialize(ChunkColumn column)
        {
            using var ms = new MemoryStream(16_384);

            // Rent one buffer for both heightmaps (256 ints each = 512 total).
            var heightBuf = ArrayPool<int>.Shared.Rent(512);

            // Rent one scratch buffer reused across all 24 sections for palette building.
            // After BuildPaletteIntoScratch(), scratch[0..paletteCount-1] holds the
            // sorted unique palette values; remainder is undefined.
            var paletteScratch = ArrayPool<int>.Shared.Rent(ChunkSection.Volume);

            try
            {
                WriteHeightmaps(ms, column, heightBuf);
                WriteChunkSections(ms, column, paletteScratch);
                WriteBlockEntities(ms);
                WriteLightData(ms);
            }
            finally
            {
                ArrayPool<int>.Shared.Return(heightBuf);
                ArrayPool<int>.Shared.Return(paletteScratch);
            }

            return ms.ToArray();
        }

        private static void WriteHeightmaps(Stream s, ChunkColumn column, int[] heightBuf)
        {
            var motionBlocking = heightBuf.AsSpan(0, 256);
            var worldSurface = heightBuf.AsSpan(256, 256);

            ComputeHeightmap(column, motionBlocking, includeMotionBlocking: true);
            ComputeHeightmap(column, worldSurface, includeMotionBlocking: false);

            WriteVarInt(s, 2);

            WriteVarInt(s, HmTypeMotionBlocking);
            WritePackedLongs(s, motionBlocking, HmBpe, 256);

            WriteVarInt(s, HmTypeWorldSurface);
            WritePackedLongs(s, worldSurface, HmBpe, 256);
        }

        /// <summary>
        /// Fills <paramref name="output"/> (must be length 256) with surface heights.
        /// Each value is the Y coordinate of the highest solid block + 1,
        /// relative to world minimum (value 0 means no solid blocks in that column).
        /// </summary>
        private static void ComputeHeightmap(ChunkColumn column, Span<int> output, bool includeMotionBlocking)
        {
            for (var z = 0; z < 16; z++)
            {
                for (var x = 0; x < 16; x++)
                {
                    for (var y = 319; y >= ChunkColumn.WorldMinY; y--)
                    {
                        var block = column.GetBlock(x, y, z);

                        if (block.IsAir)
                        {
                            continue;
                        }

                        output[z * 16 + x] = (y - ChunkColumn.WorldMinY) + 1;
                        break;
                    }
                }
            }
        }

        /// <summary>
        /// Writes <paramref name="count"/> values from <paramref name="values"/> packed
        /// at <paramref name="bpe"/> bits-per-entry into a prefixed array of longs.
        /// No heap allocation — streams longs one at a time.
        /// </summary>
        private static void WritePackedLongs(Stream s, ReadOnlySpan<int> values, int bpe, int count)
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

        private static void WriteChunkSections(Stream s, ChunkColumn column, int[] paletteScratch)
        {
            // Two-pass: measure total bytes first (required by protocol — length prefix before data),
            // then write. Both passes use the same scratch array; BuildPaletteIntoScratch
            // always re-fills it from section.Blocks so stale data from a previous section
            // is harmless.
            var totalBytes = 0;

            for (var i = 0; i < ChunkColumn.SectionCount; i++)
            {
                totalBytes += MeasureSection(column.GetSection(i), paletteScratch);
            }

            WriteVarInt(s, totalBytes);

            for (var i = 0; i < ChunkColumn.SectionCount; i++)
            {
                WriteSection(s, column.GetSection(i), paletteScratch);
            }
        }

        private static int MeasureSection(ChunkSection section, int[] paletteScratch)
        {
            var size = 2;
            size += MeasureBlockStateContainer(section, paletteScratch);

            // Biome container: 1 byte (bpe=0) + VarInt(0) = 2 bytes.
            size += 2;
            return size;
        }

        private static int MeasureBlockStateContainer(ChunkSection section, int[] paletteScratch)
        {
            if (section.IsUniform(out var uniform))
            {
                // bpe byte (0) + VarInt(blockId)
                return 1 + VarInt.GetSize(uniform.Id);
            }

            BuildPaletteIntoScratch(section, paletteScratch, out var paletteCount, out var bpe);

            if (bpe <= MaxIndirectBpe)
            {
                var entriesPerLong = 64 / bpe;
                var numLongs = (ChunkSection.Volume + entriesPerLong - 1) / entriesPerLong;

                // Sum palette VarInt sizes without LINQ.
                var paletteVarIntSum = 0;

                for (var i = 0; i < paletteCount; i++)
                {
                    paletteVarIntSum += VarInt.GetSize(paletteScratch[i]);
                }

                return 1 + VarInt.GetSize(paletteCount) + paletteVarIntSum + numLongs * 8;
            }
            else
            {
                const int entriesPerLong = 64 / DirectBpe;
                const int numLongs = (ChunkSection.Volume + entriesPerLong - 1) / entriesPerLong;

                // bpe byte + packed data
                return 1 + numLongs * 8;
            }
        }

        private static void WriteSection(Stream s, ChunkSection section, int[] paletteScratch)
        {
            WriteI16(s, section.BlockCount);
            WriteBlockStateContainer(s, section, paletteScratch);
            WriteBiomeContainer(s);
        }

        private static void WriteBlockStateContainer(Stream s, ChunkSection section, int[] paletteScratch)
        {
            if (section.IsUniform(out var uniform))
            {
                s.WriteByte(0);
                WriteVarInt(s, uniform.Id);
                return;
            }

            BuildPaletteIntoScratch(section, paletteScratch, out var paletteCount, out var bpe);

            if (bpe <= MaxIndirectBpe)
            {
                s.WriteByte((byte)bpe);
                WriteVarInt(s, paletteCount);

                for (var i = 0; i < paletteCount; i++)
                {
                    WriteVarInt(s, paletteScratch[i]);
                }

                WriteIndirectData(s, section, paletteScratch, paletteCount, bpe);
            }
            else
            {
                s.WriteByte(DirectBpe);
                WriteDirectData(s, section);
            }
        }

        /// <summary>
        /// Collects all block state IDs from <paramref name="section"/> into
        /// <paramref name="scratch"/>, sorts them, then deduplicates in-place so that
        /// <c>scratch[0..paletteCount-1]</c> holds the sorted unique palette entries.
        /// Sets <paramref name="bpe"/> to the bits-per-entry for this palette.
        /// <para>
        /// <paramref name="scratch"/> must be at least <see cref="ChunkSection.Volume"/> (4096) ints.
        /// </para>
        /// </summary>
        private static void BuildPaletteIntoScratch(
            ChunkSection section, int[] scratch, out int paletteCount, out int bpe)
        {
            var blocks = section.Blocks;

            for (var i = 0; i < blocks.Length; i++)
            {
                scratch[i] = blocks[i].Id;
            }

            // Sort in-place — O(n log n), no allocation.
            scratch.AsSpan(0, ChunkSection.Volume).Sort();

            // Dedup in-place: unique values land in scratch[0..paletteCount-1].
            paletteCount = 0;
            var prev = -1;

            for (var i = 0; i < ChunkSection.Volume; i++)
            {
                if (scratch[i] != prev)
                {
                    scratch[paletteCount++] = scratch[i];
                    prev = scratch[i];
                }
            }

            bpe = Math.Max(4, CeilLog2(paletteCount));

            if (bpe > MaxIndirectBpe)
            {
                bpe = DirectBpe;
            }
        }

        /// <summary>
        /// Writes section blocks packed into longs using the given <paramref name="palette"/>.
        /// Uses a rented <c>long[]</c> for packing; palette index lookup is O(log n) binary
        /// search on the sorted <paramref name="palette"/> span — no Dictionary allocation.
        /// </summary>
        private static void WriteIndirectData(
            Stream s, ChunkSection section,
            int[] palette, int paletteCount, int bpe)
        {
            var entriesPerLong = 64 / bpe;
            var numLongs = (ChunkSection.Volume + entriesPerLong - 1) / entriesPerLong;

            var longs = ArrayPool<long>.Shared.Rent(numLongs);

            try
            {
                longs.AsSpan(0, numLongs).Clear();

                var blocks = section.Blocks;

                for (var i = 0; i < blocks.Length; i++)
                {
                    var paletteIndex = BinarySearch(palette, paletteCount, blocks[i].Id);
                    var longIndex = i / entriesPerLong;
                    var shift = (i % entriesPerLong) * bpe;
                    longs[longIndex] |= ((long)paletteIndex) << shift;
                }

                for (var i = 0; i < numLongs; i++)
                {
                    WriteI64(s, longs[i]);
                }
            }
            finally
            {
                ArrayPool<long>.Shared.Return(longs);
            }
        }

        private static void WriteDirectData(Stream s, ChunkSection section)
        {
            const int entriesPerLong = 64 / DirectBpe;
            const int numLongs = (ChunkSection.Volume + entriesPerLong - 1) / entriesPerLong;

            var longs = ArrayPool<long>.Shared.Rent(numLongs);

            try
            {
                longs.AsSpan(0, numLongs).Clear();

                var blocks = section.Blocks;

                for (var i = 0; i < blocks.Length; i++)
                {
                    var longIndex = i / entriesPerLong;
                    var shift = (i % entriesPerLong) * DirectBpe;
                    longs[longIndex] |= ((long)blocks[i].Id) << shift;
                }

                for (var i = 0; i < numLongs; i++)
                {
                    WriteI64(s, longs[i]);
                }
            }
            finally
            {
                ArrayPool<long>.Shared.Return(longs);
            }
        }

        /// <summary>
        /// Binary search on a sorted <paramref name="array"/> segment of length
        /// <paramref name="count"/>. Returns the index of <paramref name="value"/>.
        /// The value is guaranteed to exist (block IDs come from the same section that
        /// built the palette), so the not-found path is unreachable in practice.
        /// </summary>
        private static int BinarySearch(int[] array, int count, int value)
        {
            var lo = 0;
            var hi = count - 1;

            while (lo <= hi)
            {
                var mid = (lo + hi) >> 1;

                if (array[mid] == value)
                {
                    return mid;
                }

                if (array[mid] < value)
                {
                    lo = mid + 1;
                }
                else
                {
                    hi = mid - 1;
                }
            }

            // Should never reach here if the palette was built from this section.
            return 0;
        }

        // Biomes are always single-valued (plains = 0) for now.
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
                if ((uv & ~0x7Fu) == 0)
                {
                    s.WriteByte((byte)uv);
                    return;
                }

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