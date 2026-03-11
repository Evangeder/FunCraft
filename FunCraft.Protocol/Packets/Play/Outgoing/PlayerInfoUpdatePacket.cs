using System.Text;

namespace FunCraft.Protocol.Packets.Play.Outgoing
{
    using Types;

    /// <summary>
    /// 0x44 — Player Info Update (S→C)<br/>
    /// Updates the TAB list. This implementation covers the three actions needed
    /// for a full player add: <c>Add Player</c> (0x01), <c>Update Listed</c> (0x08),
    /// <c>Update Latency</c> (0x10).
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
            public required string Username { get; init; }
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
            using var ms = new MemoryStream(256);

            ms.WriteByte(Actions);
            WriteVarInt(ms, Players.Count);

            foreach (var p in Players)
            {
                WriteGuid(ms, p.Uuid);
                WriteString(ms, p.Username);
                WriteVarInt(ms, 0);
                ms.WriteByte(p.Listed ? (byte)1 : (byte)0);
                WriteVarInt(ms, p.Latency);
            }

            return ms.ToArray();
        }

        private static void WriteVarInt(Stream s, int value)
        {
            Span<byte> buf = stackalloc byte[5];
            var written = VarInt.Write(buf, value);
            s.Write(buf[..written]);
        }

        private static void WriteString(Stream s, string value)
        {
            var bytes = Encoding.UTF8.GetBytes(value);
            WriteVarInt(s, bytes.Length);
            s.Write(bytes);
        }

        private static void WriteGuid(Stream s, Guid guid)
        {
            Span<byte> raw = stackalloc byte[16];
            guid.TryWriteBytes(raw);
            s.WriteByte(raw[3]); s.WriteByte(raw[2]); s.WriteByte(raw[1]); s.WriteByte(raw[0]);
            s.WriteByte(raw[5]); s.WriteByte(raw[4]);
            s.WriteByte(raw[7]); s.WriteByte(raw[6]);
            s.Write(raw[8..]);
        }
    }
}