using System.Collections.Concurrent;

namespace FunCraft.Network.Players
{
    using Protocol.Packets;

    public sealed class PlayerRegistry : IPlayerRegistry
    {
        private readonly ConcurrentDictionary<Guid, ConnectedPlayer> _players = new();

        public int Count => _players.Count;

        public void Register(ConnectedPlayer player) =>
            _players[player.Uuid] = player;

        public void Unregister(Guid uuid) =>
            _players.TryRemove(uuid, out _);

        public IReadOnlyList<ConnectedPlayer> GetAll() =>
            [.. _players.Values];

        public Task BroadcastAsync(IPacket packet, CancellationToken ct = default) =>
            BroadcastAsync(packet, excludeUuid: Guid.Empty, ct);

        public async Task BroadcastAsync(IPacket packet, Guid excludeUuid, CancellationToken ct = default)
        {
            foreach (var player in _players.Values)
            {
                if (player.Uuid == excludeUuid)
                {
                    continue;
                }

                try
                {
                    await player.Sender.SendAsync(packet, ct);
                }
                catch
                {
                     /* player disconnected mid-broadcast — skip */
                }
            }
        }
    }
}