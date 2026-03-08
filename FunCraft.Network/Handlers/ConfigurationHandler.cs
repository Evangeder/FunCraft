namespace FunCraft.Network.Handlers
{
    using Connections;
    using Protocol.Packets.Configuration;

    internal class ConfigurationHandler
    {
        internal required IPacketSender Sender { get; init; }

        internal async ValueTask OnEnterAsync(CancellationToken ct)
        {
            await Sender.SendAsync(new FinishConfigurationPacket(), ct);
        }
    }
}
