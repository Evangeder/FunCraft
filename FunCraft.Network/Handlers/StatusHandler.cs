using System.Buffers;
using System.Text.Json;

namespace FunCraft.Network.Handlers
{
    using Players;
    using Protocol.Models;
    using Protocol.Packets;
    using Protocol.Packets.Status.Incoming;
    using Protocol.Packets.Status.Outgoing;

    internal class StatusHandler(string[] motd, int maxPlayers, IPlayerRegistry registry) : AsyncHandlerBase
    {
        internal override async ValueTask<ConnectionState> HandleAsync(
            int packetId,
            ReadOnlySequence<byte> payload,
            CancellationToken ct)
        {
            switch (packetId)
            {
                case StatusRequestPacket.Id:
                    {
                        var status = new ServerStatus(
                            new ServerVersion("1.21.10", 773),
                            new ServerPlayers(maxPlayers, registry.Count),
                            new ServerDescription(motd[Random.Shared.Next(motd.Length)])
                                );

                        await Sender.SendAsync(new StatusResponsePacket
                        {
                            JsonResponse = JsonSerializer.Serialize(
                                status, ServerStatusContext.Default.ServerStatus)
                        }, ct);
                        break;
                    }

                case PingRequestPacket.Id:
                    {
                        var reader = new SequenceReader<byte>(payload);
                        var ping = new PingRequestPacket();
                        ping.TryRead(ref reader);
                        await Sender.SendAsync(new PongResponsePacket { Payload = ping.Payload }, ct);
                        break;
                    }
            }

            return ConnectionState.Status;
        }
    }
}