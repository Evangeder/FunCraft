using System.Buffers;
using System.Text.Json;
using Microsoft.Extensions.Logging;

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
                        new ServerVersion("1.20.1", 763),
                        new ServerPlayers(100, 0),
                        new ServerDescription("A FunCraft Server")
                    );

                    var json = JsonSerializer.Serialize(status, ServerStatusContext.Default.ServerStatus);
                    var packet = new StatusResponse()
                    {
                        JsonResponse = json
                    };
                    Console.WriteLine(json);
                    await Sender.SendAsync(packet, ct);
                    break;

                case PingRequest.Id:
                    var ping = new PingRequest();
                    var span = payload.IsSingleSegment ? payload.FirstSpan : payload.ToArray(); // TODO fix
                    ping.TryRead(span, out _);
                    await Sender.SendAsync(new PingResponse { Payload = ping.Payload }, ct);
                    break;
            }

            return ConnectionState.Status;
        }
    }
}
