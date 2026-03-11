namespace FunCraft.Network.Players
{
    using Connections;

    public sealed class ConnectedPlayer(Guid uuid, string username, IPacketSender sender, PlayerContext context)
    {
        public Guid Uuid { get; } = uuid;
        public string Username { get; } = username;
        public IPacketSender Sender { get; } = sender;
        public PlayerContext Context { get; } = context;
    }
}