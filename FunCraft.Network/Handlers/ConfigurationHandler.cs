using System.Buffers;

namespace FunCraft.Network.Handlers
{
    using Protocol.Packets;
    using Protocol.Packets.Configuration.Incoming;
    using Protocol.Packets.Configuration.Outgoing;
    using Protocol.Packets.Registry.Incoming;
    using Protocol.Packets.Registry.Outgoing;
    using Protocol.Registry;

    internal class ConfigurationHandler : AsyncHandlerBase
    {
        internal override async ValueTask<ConnectionState> HandleAsync(int packetId, ReadOnlySequence<byte> payload, CancellationToken ct)
        {
            switch (packetId)
            {
                case ServerboundKnownPacksPacket.Id:
                    await SendRegistriesAsync(ct);
                    return ConnectionState.Configuration;

                case AcknowledgeConfigurationPacket.Id:
                    return ConnectionState.Play;

                default:
                    return ConnectionState.Configuration;
            }
            ;
        }

        internal async ValueTask OnEnterAsync(CancellationToken ct)
        {
            await Sender.SendAsync(new KnownPacksPacket(), ct);
        }

        internal async ValueTask SendRegistriesAsync(CancellationToken ct)
        {
            foreach (var framed in RegistryLoader.FramedPackets)
            {
                await Sender.SendRawAsync(framed, ct);
            }

            await Sender.SendAsync(new FinishConfigurationPacket(), ct);
        }
    }
}