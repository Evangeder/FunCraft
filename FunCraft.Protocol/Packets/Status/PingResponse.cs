namespace FunCraft.Protocol.Packets.Status
{
    using IO;

    public class PingResponse : IPacket
    {
        public int PacketId => 0x01;
        public int GetLength() => sizeof(long);

        public required long Payload { private get; init; }

        public void Write(Span<byte> destination, out int bytesWritten)
        {
            var writer = new PacketWriter(destination);
            writer.WriteLong(Payload);
            bytesWritten = writer.BytesWritten;
        }
    }
}
