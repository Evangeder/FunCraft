namespace FunCraft.Protocol.Packets.Configuration
{
    public class FinishConfigurationPacket : IPacket
    {
        public int PacketId => 0x03;

        public int GetLength() => 0;

        public void Write(Span<byte> destination, out int bytesWritten) => bytesWritten = 0;
    }
}
