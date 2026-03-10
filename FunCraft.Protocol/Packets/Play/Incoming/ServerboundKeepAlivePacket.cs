using System.Buffers;
using System.Buffers.Binary;

namespace FunCraft.Protocol.Packets.Play.Incoming
{
    /// <summary>
    /// 0x1B — Keep Alive (C→S)
    /// <br/>Sent by the client in response to <see cref="Outgoing.ClientboundKeepAlivePacket"/>.
    /// <br/>The ID must match the one sent by the server.
    /// </summary>
    public class ServerboundKeepAlivePacket : IIncomingPacket
    {
        public const int Id = 0x1B;

        public long KeepAliveId { get; private set; }

        public bool TryRead(ref SequenceReader<byte> reader)
        {
            if (reader.Remaining < sizeof(long))
            {
                return false;
            }

            Span<byte> buf = stackalloc byte[sizeof(long)];
            reader.TryCopyTo(buf);
            reader.Advance(sizeof(long));

            KeepAliveId = BinaryPrimitives.ReadInt64BigEndian(buf);
            return true;
        }
    }
}