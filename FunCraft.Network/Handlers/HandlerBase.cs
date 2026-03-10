using System.Buffers;

namespace FunCraft.Network.Handlers
{
    using Protocol.Packets;
    using Connections;

    internal abstract class HandlerBase
    {
        internal required IPacketSender Sender { get; init; }
    }

    internal abstract class AsyncHandlerBase : HandlerBase
    {
        internal abstract ValueTask<ConnectionState> HandleAsync(
            int packetId,
            ReadOnlySequence<byte> payload,
            CancellationToken ct);
    }

    internal abstract class SyncHandlerBase : HandlerBase
    {
        internal abstract ConnectionState Handle(
            int packetId,
            ReadOnlySequence<byte> payload);
    }
}
