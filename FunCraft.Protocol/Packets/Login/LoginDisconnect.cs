namespace FunCraft.Protocol.Packets.Login
{
    using IO;
    using Types;

    public class LoginDisconnect : IPacket
    {
        public int PacketId => 0x00;
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
