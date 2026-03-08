using System.Buffers;
using FunCraft.Protocol.Packets.Handshaking;

namespace FunCraft.Network.Handlers
{
    using Protocol.Packets;

    internal class HandshakeHandler
    {
        internal ConnectionState Handle(int packetId, ReadOnlySequence<byte> payload)
        {
            if (packetId != HandshakePacket.PacketId)
            {
                // unknown packet, don't parse.
                return ConnectionState.Handshaking;
            }

            var packet = new HandshakePacket();
            var span = payload.IsSingleSegment ? payload.FirstSpan : payload.ToArray(); // TODO: optimize multi-segment case

            if (!packet.TryRead(span, out _))
            {
                // malformed packet
                return ConnectionState.Handshaking;
            }

            return packet.NextState switch
            {
                (int)ConnectionState.Status => ConnectionState.Status,
                (int)ConnectionState.Login => ConnectionState.Login,
                _ => ConnectionState.Handshaking
            };
        }
    }
}
