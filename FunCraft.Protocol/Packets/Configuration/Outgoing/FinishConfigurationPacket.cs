namespace FunCraft.Protocol.Packets.Configuration.Outgoing
{
    public class FinishConfigurationPacket : IPacket
    {
        public const int Id = 0x03;
        public int PacketId => Id;

        public int GetLength() => 0;

        public void Write(Span<byte> destination, out int bytesWritten) => bytesWritten = 0;
    }
}
