using System.Buffers;

namespace FunCraft.Protocol.Packets
{
    public interface IIncomingPacket
    {
        bool TryRead(ref SequenceReader<byte> reader);
    }
}
