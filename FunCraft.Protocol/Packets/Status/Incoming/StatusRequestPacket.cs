using System.Buffers;

namespace FunCraft.Protocol.Packets.Status.Incoming
{
    public class StatusRequestPacket : IIncomingPacket
    {
        public const int Id = 0x00;

        public bool TryRead(ref SequenceReader<byte> reader)
        {
            return true;
        }
    }
}
