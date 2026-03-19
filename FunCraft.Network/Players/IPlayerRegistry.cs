namespace FunCraft.Network.Players
{
    using Protocol.Packets;

    public interface IPlayerRegistry
    {
        int Count { get; }
        void Register(ConnectedPlayer player);
        void Unregister(Guid uuid);
        IReadOnlyList<ConnectedPlayer> GetAll();
        ConnectedPlayer? TryGet(Guid uuid);
        Task BroadcastAsync(IPacket packet, CancellationToken ct = default);
        Task BroadcastAsync(IPacket packet, Guid excludeUuid, CancellationToken ct = default);
        Task BroadcastRawAsync(IPacket packet, Guid excludeUuid, CancellationToken ct = default);
        Task BroadcastMovementAsync(int movementKey, IPacket packet, Guid excludeUuid);
    }
}