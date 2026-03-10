using System.Buffers;
using System.Text.Json;

namespace FunCraft.Network.Handlers
{
    using Protocol.Packets;
    using Protocol.Packets.Status.Incoming;
    using Protocol.Packets.Status.Outgoing;
    using Protocol.Models;

    internal class StatusHandler : AsyncHandlerBase
    {
        internal override async ValueTask<ConnectionState> HandleAsync(int packetId, ReadOnlySequence<byte> payload, CancellationToken ct)
        {
            switch (packetId)
            {
                case StatusRequestPacket.Id:
                    var status = new ServerStatus(
                        new ServerVersion("1.21.10", 773),
                        new ServerPlayers(100, 0),
                        new ServerDescription("A FunC#raft Server")
                    );

                    var packet = new StatusResponsePacket
                    {
                        JsonResponse = JsonSerializer.Serialize(status, ServerStatusContext.Default.ServerStatus)
                    };
                    await Sender.SendAsync(packet, ct);
                    break;

                case PingRequestPacket.Id:
                    var ping = new PingRequestPacket();
                    var reader = new SequenceReader<byte>(payload);
                    ping.TryRead(ref reader);
                    await Sender.SendAsync(new PongResponsePacket { Payload = ping.Payload }, ct);
                    break;
            }

            return ConnectionState.Status;
        }
    }
}
