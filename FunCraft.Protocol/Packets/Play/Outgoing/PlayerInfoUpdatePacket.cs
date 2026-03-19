namespace FunCraft.Protocol.Packets.Play.Outgoing
{
    using Types;

    /// <summary>
    /// 0x44 — Player Info Update (S→C)<br/>
    /// Updates the TAB list. Covers three actions: Add Player (0x01),
    /// Update Listed (0x08), Update Latency (0x10).
    ///
    /// <para>
    /// Serialization writes directly into a pre-sized <c>byte[]</c> computed from
    /// the exact wire layout — no intermediate <c>MemoryStream</c> or second copy.
    /// The result is cached in <see cref="_cache"/> so the expensive serialization
    /// happens only once per packet instance.
    /// </para>
    /// </summary>
    public sealed class PlayerInfoUpdatePacket : IPacket
    {
        private const int BitMaskAddPlayer = 0x01;
        private const int BitMaskUpdateListed = 0x08;
        private const int BitMaskUpdateLatency = 0x10;

        public const int Id = 0x44;
        public int PacketId => Id;

        private const byte Actions = BitMaskAddPlayer | BitMaskUpdateListed | BitMaskUpdateLatency;

        public required IReadOnlyList<PlayerInfoEntry> Players { get; init; }

        public sealed class PlayerInfoEntry
        {
            public required Guid Uuid { get; init; }
            public required ReadOnlyMemory<byte> Username { get; init; }
            public required int Latency { get; init; }
            public required bool Listed { get; init; }
        }

        private byte[]? _cache;
        private byte[] Cache => _cache ??= Serialize();

        public int GetLength() => Cache.Length;

        public void Write(Span<byte> destination, out int bytesWritten)
        {
            Cache.AsSpan().CopyTo(destination);
            bytesWritten = Cache.Length;
        }

        private byte[] Serialize()
        {
            // Compute exact wire size upfront so we allocate exactly once and write
            // directly — no MemoryStream, no intermediate buffer, no ToArray() copy.
            var totalSize = 1 + VarInt.GetSize(Players.Count); // Actions + player count

            foreach (var p in Players)
            {
                totalSize += 16;                                              // UUID (128-bit)
                totalSize += VarInt.GetSize(p.Username.Length) + p.Username.Length; // MC string
                totalSize += VarInt.GetSize(0);                              // property count = 0 (1 byte)
                totalSize += 1;                                               // Listed bool
                totalSize += VarInt.GetSize(p.Latency);
            }

            var buf = new byte[totalSize];
            var pos = 0;

            buf[pos++] = Actions;
            pos += VarInt.Write(buf.AsSpan(pos), Players.Count);

            foreach (var p in Players)
            {
                WriteGuidTo(buf, ref pos, p.Uuid);
                WriteStringTo(buf, ref pos, p.Username.Span);
                pos += VarInt.Write(buf.AsSpan(pos), 0); // no properties
                buf[pos++] = p.Listed ? (byte)1 : (byte)0;
                pos += VarInt.Write(buf.AsSpan(pos), p.Latency);
            }

            return buf;
        }

        private static void WriteGuidTo(byte[] buf, ref int pos, Guid guid)
        {
            Span<byte> raw = stackalloc byte[16];
            guid.TryWriteBytes(raw);

            // Minecraft UUID wire order: big-endian MSB first with byte-swap on the
            // first two groups (matches the existing WriteGuid logic in PacketWriter).
            buf[pos + 0] = raw[3];
            buf[pos + 1] = raw[2];
            buf[pos + 2] = raw[1];
            buf[pos + 3] = raw[0];
            buf[pos + 4] = raw[5];
            buf[pos + 5] = raw[4];
            buf[pos + 6] = raw[7];
            buf[pos + 7] = raw[6];
            buf[pos + 8] = raw[8];
            buf[pos + 9] = raw[9];
            buf[pos + 10] = raw[10];
            buf[pos + 11] = raw[11];
            buf[pos + 12] = raw[12];
            buf[pos + 13] = raw[13];
            buf[pos + 14] = raw[14];
            buf[pos + 15] = raw[15];

            pos += 16;
        }

        private static void WriteStringTo(byte[] buf, ref int pos, ReadOnlySpan<byte> utf8)
        {
            pos += VarInt.Write(buf.AsSpan(pos), utf8.Length);
            utf8.CopyTo(buf.AsSpan(pos));
            pos += utf8.Length;
        }
    }
}