using System.Collections.Concurrent;

namespace FunCraft.Network.Players
{
    using Protocol.IO;
    using Protocol.Packets;
    using Protocol.Types;

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

        public async Task BroadcastAsync(IPacket packet, CancellationToken ct = default) =>
            await BroadcastAsync(packet, excludeUuid: Guid.Empty, ct);

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
        public Task BroadcastRawAsync(IPacket packet, Guid excludeUuid, CancellationToken ct = default)
        {
            var payloadLength = packet.GetLength();
            var idLength = VarInt.GetSize(packet.PacketId);
            var totalLength = payloadLength + idLength;
            var frameLength = VarInt.GetSize(totalLength) + totalLength;

            var buf = new byte[frameLength];
            var writer = new PacketWriter(buf);
            writer.WriteVarInt(totalLength);
            writer.WriteVarInt(packet.PacketId);
            packet.Write(buf.AsSpan(writer.BytesWritten), out _);
            var framed = buf.AsMemory();

            foreach (var player in _players.Values)
            {
                if (player.Uuid == excludeUuid)
                {
                    continue;
                }

                try
                {
                    player.Sender.EnqueueRaw(framed);
                }
                catch
                {
                     /* player disconnected */
                }
            }

            return Task.CompletedTask;
        }
    }
}