using System.Buffers;

namespace FunCraft.Protocol.Packets.Registry
{
    using Types;

    public class ServerboundKnownPacksPacket : IIncomingPacket
    {
        public const int Id = 0x07;
        
        public bool TryRead(ref SequenceReader<byte> reader)
        {
            if (!VarInt.TryRead(ref reader, out var count))
            {
                return false;
            }

            for (var i = 0; i < count; i++)
            {
                if (!McString.TryRead(ref reader, out _))
                {
                    return false;
                }

                if (!McString.TryRead(ref reader, out _))
                {
                    return false;
                }

                if (!McString.TryRead(ref reader, out _))
                {
                    return false;
                }
            }
            return true;
        }
    }
}
