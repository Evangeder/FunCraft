namespace FunCraft.Network.Handlers
{
    using Connections;
    using Protocol.Packets.Registry;
    using Protocol.Packets;
    using Protocol.Packets.Configuration;
    using Protocol.Registry;
    using System.Buffers;

    internal class ConfigurationHandler
    {
        internal required IPacketSender Sender { get; init; }

        internal async ValueTask<ConnectionState> HandleAsync(int packetId, ReadOnlySequence<byte> payload)
        {
            switch (packetId)
            {
                case ServerboundKnownPacksPacket.Id:
                    await SendRegistriesAsync(CancellationToken.None);
                    return ConnectionState.Configuration;

                case AcknowledgeConfigurationPacket.Id:
                    return ConnectionState.Play;

                default:
                    return ConnectionState.Configuration;
            };
        }

        internal async ValueTask SendRegistriesAsync(CancellationToken ct)
        {
            await Sender.SendAsync(new KnownPacksPacket(), ct);

            foreach (var registryPacket in RegistryLoader.Packets)
            {
                await Sender.SendAsync(registryPacket, ct);
            }
            await Sender.SendAsync(new FinishConfigurationPacket(), ct);
        }
    }
}
