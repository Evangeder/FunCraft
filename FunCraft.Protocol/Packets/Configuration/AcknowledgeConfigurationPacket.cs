namespace FunCraft.Protocol.Packets.Configuration
{
    public class AcknowledgeConfigurationPacket : IIncomingPacket
    {
        public const int Id = 0x03;
        public bool TryRead(ReadOnlySpan<byte> source, out int bytesRead)
        {
            throw new NotImplementedException();
        }
    }
}
