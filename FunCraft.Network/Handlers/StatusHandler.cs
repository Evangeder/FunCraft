using System.Buffers;
using System.Text.Json;

namespace FunCraft.Network.Handlers
{
    using Protocol.Packets;
    using Protocol.Packets.Status;
    using Protocol.Models;
    using Connections;

    internal class StatusHandler
    {
        internal required IPacketSender Sender { get; init; }

        internal async ValueTask<ConnectionState> HandleAsync(int packetId, ReadOnlySequence<byte> payload,
            CancellationToken ct)
        {
            switch (packetId)
            {
                case StatusRequest.Id:
                    var status = new ServerStatus(
                        new ServerVersion("1.21.10", 773),
                        new ServerPlayers(100, 0),
                        new ServerDescription("A FunC#raft Server")
                    );

                    var packet = new StatusResponse
                    {
                        JsonResponse = JsonSerializer.Serialize(status, ServerStatusContext.Default.ServerStatus)
                    };
                    await Sender.SendAsync(packet, ct);
                    break;

                case PingRequest.Id:
                    var ping = new PingRequest();
                    var reader = new SequenceReader<byte>(payload);
                    ping.TryRead(ref reader);
                    await Sender.SendAsync(new PingResponse { Payload = ping.Payload }, ct);
                    break;
            }

            return ConnectionState.Status;
        }
    }
}
