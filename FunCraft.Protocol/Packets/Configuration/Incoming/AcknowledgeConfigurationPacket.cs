using System.Buffers;

namespace FunCraft.Protocol.Packets.Configuration.Incoming
{
    public class AcknowledgeConfigurationPacket : IIncomingPacket
    {
        public const int Id = 0x03;
        public bool TryRead(ref SequenceReader<byte> reader)
        {
            throw new InvalidOperationException("This packet contains no data and should never be read.");
        }
    }
}
