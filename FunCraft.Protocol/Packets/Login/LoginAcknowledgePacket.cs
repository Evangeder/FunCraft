using System.Buffers;

namespace FunCraft.Protocol.Packets.Login
{
    public class LoginAcknowledgePacket : IIncomingPacket
    {
        public const int Id = 0x03;

        public bool TryRead(ref SequenceReader<byte> reader)
        {
            throw new NotImplementedException();
        }
    }
}
