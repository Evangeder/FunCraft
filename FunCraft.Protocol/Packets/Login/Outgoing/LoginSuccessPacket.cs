namespace FunCraft.Protocol.Packets.Login.Outgoing
{
    using IO;
    using Types;

    public class LoginSuccessPacket : IPacket
    {
        public const int Id = 0x02;
        public int PacketId => Id;

        public int GetLength() =>
            16 +
            McString.GetSize(PlayerName.Span) +
            VarInt.GetSize(PropertyCount);

        public Guid PlayerGuid { private get; init; }

        /// <summary>Pre-encoded UTF-8 bytes of the player name.</summary>
        public ReadOnlyMemory<byte> PlayerName { private get; init; } = ReadOnlyMemory<byte>.Empty;

        /// <summary>
        /// Number of player properties (skin, cape etc.) from Mojang auth.
        /// 0 for offline mode.
        /// </summary>
        public int PropertyCount { private get; init; }

        public void Write(Span<byte> destination, out int bytesWritten)
        {
            var writer = new PacketWriter(destination);
            writer.WriteGuid(PlayerGuid);
            writer.WriteString(PlayerName.Span);
            writer.WriteVarInt(PropertyCount);
            bytesWritten = writer.BytesWritten;
        }
    }
}