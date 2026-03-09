using System.Buffers;

namespace FunCraft.Network.Handlers
{
    using Connections;
    using Protocol.Packets;
    using Protocol.Packets.Play;

    internal class PlayHandler
    {
        internal required IPacketSender Sender { get; init; }

        internal async ValueTask OnEnterAsync(CancellationToken ct)
        {
            Console.WriteLine("PlayHandler::OnEnterAsync()");
            await Sender.SendAsync(new LoginPlayPacket
            {
                EntityId = 1,
                DimensionType = "minecraft:overworld",
                DimensionName = "minecraft:overworld"
            }, ct);
        }

        internal static ConnectionState Handle()
        {
            return ConnectionState.Play;
        }
    }
}
