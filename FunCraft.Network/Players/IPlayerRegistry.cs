namespace FunCraft.Network.Players
{
    using Protocol.Packets;

    public interface IPlayerRegistry
    {
        int Count { get; }

        void Register(ConnectedPlayer player);
        void Unregister(Guid uuid);

        IReadOnlyList<ConnectedPlayer> GetAll();

        /// <summary>
        /// Sends <paramref name="packet"/> to every connected player.
        /// </summary>
        Task BroadcastAsync(IPacket packet, CancellationToken ct = default);

        /// <summary>
        /// Sends <paramref name="packet"/> to every player except <paramref name="excludeUuid"/>.
        /// </summary>
        Task BroadcastAsync(IPacket packet, Guid excludeUuid, CancellationToken ct = default);
    }
}