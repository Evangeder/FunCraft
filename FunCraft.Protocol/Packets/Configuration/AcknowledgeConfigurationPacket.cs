using System.Buffers;

namespace FunCraft.Protocol.Packets.Configuration
{
    public class AcknowledgeConfigurationPacket : IIncomingPacket
    {
        public const int Id = 0x03;
        public bool TryRead(ref SequenceReader<byte> reader)
        {
            throw new NotImplementedException();
        }
    }
}
