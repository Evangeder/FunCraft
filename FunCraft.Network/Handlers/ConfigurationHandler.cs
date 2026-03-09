namespace FunCraft.Network.Handlers
{
    using Connections;
    using Protocol.Packets.Registry;
    using Protocol.Packets;
    using Protocol.Packets.Configuration;
    using Protocol.Registry;

    internal class ConfigurationHandler
    {
        internal required IPacketSender Sender { get; init; }

        internal async ValueTask<ConnectionState> HandleAsync(int packetId, CancellationToken ct)
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
            };
        }

        internal async ValueTask OnEnterAsync(CancellationToken ct)
        {
            await Sender.SendAsync(new KnownPacksPacket(), ct);
        }

        internal async ValueTask SendRegistriesAsync(CancellationToken ct)
        {
            foreach (var registryPacket in RegistryLoader.Packets)
            {
                await Sender.SendAsync(registryPacket, ct);
            }
            await Sender.SendAsync(new FinishConfigurationPacket(), ct);
        }
    }
}
