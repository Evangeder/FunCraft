namespace FunCraft.Network.Handlers
{
    using Connections;
    using Protocol.Packets;
    using Protocol.Packets.Configuration;
    using System.Buffers;

    internal class ConfigurationHandler
    {
        internal required IPacketSender Sender { get; init; }

        internal ConnectionState Handle(int packetId, ReadOnlySequence<byte> payload)
        {
            return packetId switch
            {
                AcknowledgeConfigurationPacket.Id => ConnectionState.Play,
                _ => ConnectionState.Configuration,
            };
        }

        internal async ValueTask OnEnterAsync(CancellationToken ct)
        {
            await Sender.SendAsync(new FinishConfigurationPacket(), ct);
        }
    }
}
