using System.Collections.Concurrent;

namespace FunCraft.Network.Players
{
    using Protocol.IO;
    using Protocol.Packets;
    using Protocol.Types;

    public sealed class PlayerRegistry : IPlayerRegistry
    {
        private readonly ConcurrentDictionary<Guid, ConnectedPlayer> _players = new();
        private readonly object _snapshotLock = new();

        // Copy-on-write snapshot. Rebuilt under _snapshotLock on every Register/Unregister.
        // All broadcast paths read this via Volatile — zero ConcurrentDictionary contention
        // on the hot movement-broadcast path.
        private ConnectedPlayer[] _snapshot = [];

        public int Count => _players.Count;

        public void Register(ConnectedPlayer player)
        {
            _players[player.Uuid] = player;
            RebuildSnapshot();
        }

        public void Unregister(Guid uuid)
        {
            _players.TryRemove(uuid, out _);
            RebuildSnapshot();
        }

        private void RebuildSnapshot()
        {
            lock (_snapshotLock)
            {
                var values = _players.Values;
                var next = new ConnectedPlayer[values.Count];
                var i = 0;

                foreach (var p in values)
                {
                    next[i++] = p;
                }

                Volatile.Write(ref _snapshot, next);
            }
        }

        public IReadOnlyList<ConnectedPlayer> GetAll() =>
            Volatile.Read(ref _snapshot);

        public ConnectedPlayer? TryGet(Guid uuid) =>
            _players.GetValueOrDefault(uuid);

        public async Task BroadcastAsync(IPacket packet, CancellationToken ct = default) =>
            await BroadcastAsync(packet, excludeUuid: Guid.Empty, ct);

        public async Task BroadcastAsync(IPacket packet, Guid excludeUuid, CancellationToken ct = default)
        {
            foreach (var player in Volatile.Read(ref _snapshot))
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
                    /* player disconnected mid-broadcast */
                }
            }
        }

        public Task BroadcastRawAsync(IPacket packet, Guid excludeUuid, CancellationToken ct = default)
        {
            var framed = SerializeToFrame(packet);

            foreach (var player in Volatile.Read(ref _snapshot))
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

        public Task BroadcastMovement(int movementKey, IPacket packet, Guid excludeUuid)
        {
            // Serialise once, fan out to all connections via snapshot — no ConcurrentDictionary
            // lock taken on this path. Each connection stores the frame with supersession
            // semantics: if this key already has a pending frame, it is overwritten.
            var framed = SerializeToFrame(packet);

            // Extract the underlying byte[] — SerializeToFrame always allocates a fresh
            // array, so TryGetArray is guaranteed to succeed.
            if (!System.Runtime.InteropServices.MemoryMarshal.TryGetArray(framed, out var seg)
                || seg.Array is null)
            {
                return Task.CompletedTask;
            }

            var frameArray = seg.Array;

            foreach (var player in Volatile.Read(ref _snapshot))
            {
                if (player.Uuid == excludeUuid)
                {
                    continue;
                }

                try
                {
                    player.Sender.EnqueueMovementFrame(movementKey, frameArray);
                }
                catch
                {
                    /* player disconnected */
                }
            }

            return Task.CompletedTask;
        }

        private static ReadOnlyMemory<byte> SerializeToFrame(IPacket packet)
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
            return buf.AsMemory();
        }
    }
}