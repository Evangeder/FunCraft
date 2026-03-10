namespace FunCraft.Protocol.Packets.Login.Outgoing
{
    using IO;
    using Types;

    public class LoginDisconnect : IPacket
    {
        public const int Id = 0x00;
        public int PacketId => Id;

        public int GetLength() => McString.GetSize(Reason);

        public required string Reason { private get; init; }

        public void Write(Span<byte> destination, out int bytesWritten)
        {
            var writer = new PacketWriter(destination);
            writer.WriteString(Reason);
            bytesWritten = writer.BytesWritten;
        }
    }
}
