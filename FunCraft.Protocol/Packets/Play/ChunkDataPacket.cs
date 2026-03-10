using System.Buffers.Binary;

namespace FunCraft.Protocol.Packets.Play
{
    using IO;

    /// <summary>
    /// <b>0x2C</b> — Chunk Data and Update Light (S→C, protocol 773 / 1.21.5+)
    /// </summary>
    public class ChunkDataPacket : IPacket
    {
        public const int Id = 0x2C;
        public int PacketId => Id;

        /// <summary>
        /// 384 blocks tall / 16 per section
        /// </summary>
        private const int SectionCount = 24;

        /// <summary>
        /// SectionCount + 2 boundary sections
        /// </summary>
        private const int LightSections = 26;

        // Heightmap constants (protocol 773 Prefixed Array format)

        /// <summary>
        /// Calculated by Ceil(Log2(384 blocks tall + 1))
        /// </summary>
        private const int HeightmapBitsPerEntry = 9;
        private const int HeightmapEntriesPerLong = 64 / HeightmapBitsPerEntry;
        private const int HeightmapLongs = (256 + HeightmapEntriesPerLong - 1) / HeightmapEntriesPerLong;
        private const int HeightmapTypeWorldSurface = 1;
        private const int HeightmapTypeMotionBlocking = 4;

        public required int ChunkX { get; init; }
        public required int ChunkZ { get; init; }

        // All chunks in this void world are identical — build the shared payload once.
        private static readonly byte[] CommonData = BuildCommonData();

        public int GetLength() => sizeof(int) + sizeof(int) + CommonData.Length;

        public void Write(Span<byte> destination, out int bytesWritten)
        {
            var writer = new PacketWriter(destination);
            writer.WriteInt(ChunkX);
            writer.WriteInt(ChunkZ);
            writer.WriteRawBytes(CommonData);
            bytesWritten = writer.BytesWritten;
        }

        // --- Static data builder ------------------------------------------------
        // TODO: Implement world generation and saving
        // TODO: Implement reading and parsing world chunks
        // TODO: Make the server send actual block data

        private static byte[] BuildCommonData()
        {
            using var ms = new MemoryStream(8192);
            WriteHeightmaps(ms);
            WriteChunkSections(ms);
            WriteBlockEntities(ms);
            WriteLightData(ms);
            return ms.ToArray();
        }

        /// <summary>
        /// Protocol 773 heightmap format: Prefixed Array of Heightmap.
        /// <br/>Each entry: VarInt type + Prefixed Array of Long (all zeros for void world).
        /// </summary>
        private static void WriteHeightmaps(Stream s)
        {
            WriteVarInt(s, 2); // send 2 heightmap types

            // MOTION_BLOCKING (type 4)
            WriteVarInt(s, HeightmapTypeMotionBlocking);
            WriteHeightmapData(s);

            // WORLD_SURFACE (type 1)
            WriteVarInt(s, HeightmapTypeWorldSurface);
            WriteHeightmapData(s);
        }

        /// <summary>
        /// Writes one heightmap's Prefixed Array of Long — 37 longs of zeros.
        /// <br/>BPE=9; entries_per_long=7; num_longs=ceil(256/7)=37.
        /// <br/>All zero because the void world has no blocks (maximum height = 0 everywhere).
        /// </summary>
        private static void WriteHeightmapData(Stream s)
        {
            WriteVarInt(s, HeightmapLongs);
            for (var i = 0; i < HeightmapLongs * 8; i++)
            {
                s.WriteByte(0x00);
            }
        }

        /// <summary>
        /// Writes all 24 chunk sections (all air) as a Prefixed Array of Byte.
        /// <br/>Protocol 773: data array length is NOT written; client computes it from BPE.
        /// <br/>For BPE = 0 (single-valued palette), data array is empty → nothing written.
        /// <code>
        /// Each section = 6 bytes (protocol 773):
        ///   Short(0)    block count
        ///   UByte(0)    block bitsPerEntry = 0  → single-valued
        ///   VarInt(0)   single block value      → air (state 0)
        ///               [no data array length, no data]
        ///   UByte(0)    biome bitsPerEntry = 0  → single-valued
        ///   VarInt(0)   single biome value      → biome 0
        ///               [no data array length, no data]
        /// </code>
        /// </summary>
        private static void WriteChunkSections(Stream s)
        {
            const int bytesPerSection = 6;
            const int totalBytes = SectionCount * bytesPerSection;

            WriteVarInt(s, totalBytes);

            for (var i = 0; i < SectionCount; i++)
            {
                // Block states — single-valued palette (all air)

                // block count = 0
                s.WriteByte(0x00);
                s.WriteByte(0x00);

                // bitsPerEntry = 0 → single-valued
                s.WriteByte(0x00);

                // value = 0 (air)
                // [data array omitted — BPE 0 → length 0, not sent]
                s.WriteByte(0x00);

                // Biomes — single-valued palette

                // bitsPerEntry = 0 → single-valued
                s.WriteByte(0x00);
                // value = 0 (first biome in registry)
                // [data array omitted]
                s.WriteByte(0x00);
            }
        }

        private static void WriteBlockEntities(Stream s) => WriteVarInt(s, 0);

        /// <summary>
        /// Declares all 26 light sections "empty" in both sky and block light.
        /// No arrays are transmitted; the client treats all sections as fully dark.
        /// The player will be in darkness until we send proper skylight or a time-of-day packet.
        /// </summary>
        private static void WriteLightData(Stream s)
        {
            const long allSectionsBit = (1L << LightSections) - 1;

            // Skylight Mask — no lit sections
            WriteBitSet(s, 0L);

            // Block Light Mask — no lit sections
            WriteBitSet(s, 0L);

            // Empty Skylight Mask — all 26 are dark
            WriteBitSet(s, allSectionsBit);

            // Empty Block Light Mask
            WriteBitSet(s, allSectionsBit);

            // skylight array count
            WriteVarInt(s, 0);

            // block light array count
            WriteVarInt(s, 0);
        }

        // ─── Stream helpers ──────────────────────────────────────────────────────

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

        private static void WriteI64(Stream s, long value)
        {
            Span<byte> buf = stackalloc byte[8];
            BinaryPrimitives.WriteInt64BigEndian(buf, value);
            s.Write(buf);
        }
    }
}