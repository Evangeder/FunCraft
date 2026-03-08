using System.Buffers;

namespace FunCraft.Network.Handlers
{
    using Connections;
    using Protocol.Packets;

    internal class PlayHandler
    {
        internal required IPacketSender Sender { get; init; }

        internal ValueTask<ConnectionState> HandleAsync(int packetId, ReadOnlySequence<byte> payload, CancellationToken ct)
        {
            return ValueTask.FromResult(ConnectionState.Play);
        }
    }
}
