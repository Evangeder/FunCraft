
namespace FunCraft.Protocol.Packets.Login
{
    using IO;
    using Types;

    public class LoginSuccessPacket : IPacket
    {
        public int PacketId => 0x02;
        public int GetLength() =>
            16 +
            McString.GetSize(PlayerName) +
            VarInt.GetSize(PropertyCount);

        public Guid PlayerGuid { private get; init; }
        public string PlayerName { private get; init; } = string.Empty;

        /// <summary>
        /// Number of player properties (skin, cape etc.) from Mojang auth.
        /// <br/>0 for offline mode, populated from session server in online mode.
        /// </summary>
        public int PropertyCount { private get; init; }

        public void Write(Span<byte> destination, out int bytesWritten)
        {
            var writer = new PacketWriter(destination);
            writer.WriteGuid(PlayerGuid);
            writer.WriteString(PlayerName);
            writer.WriteVarInt(PropertyCount);
            bytesWritten = writer.BytesWritten;
        }
    }
}
