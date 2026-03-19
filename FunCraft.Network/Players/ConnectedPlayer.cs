namespace FunCraft.Network.Players
{
    using Connections;

    public sealed class ConnectedPlayer(Guid uuid, ReadOnlyMemory<byte> username, IPacketSender sender, PlayerContext context)
    {
        public Guid Uuid { get; } = uuid;
        public ReadOnlyMemory<byte> Username { get; } = username;
        public IPacketSender Sender { get; } = sender;
        public PlayerContext Context { get; } = context;
        public int EntityId => Context.EntityId;
    }
}