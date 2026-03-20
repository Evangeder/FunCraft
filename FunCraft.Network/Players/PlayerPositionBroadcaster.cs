using Microsoft.Extensions.Hosting;

namespace FunCraft.Network.Players
{
    using Protocol.Packets.Play.Outgoing;

    /// <summary>
    /// Central 20Hz position broadcast service.
    ///
    /// <para>
    /// Previously, each movement packet handler called
    /// <see cref="IPlayerRegistry.BroadcastMovement"/> inline inside
    /// <c>ProcessPacketsAsync</c>. With 1000 bots sending movement at 20Hz,
    /// that was 60 000 packets/sec each fanning out to 999 connections —
    /// 60M <see cref="Connections.IPacketSender.EnqueueMovementFrame"/> calls/sec
    /// serialised through a single per-connection packet queue. The queue backed up
    /// indefinitely, causing minute-long latency on all other packets.
    /// </para>
    ///
    /// <para>
    /// This service ticks at 20Hz (every 50ms) and broadcasts every player's current
    /// position/rotation to all other players in one controlled pass. Movement packet
    /// handlers now only update <see cref="PlayerContext"/> fields — no fan-out at all.
    /// The broadcast work is fully off the hot packet-processing path.
    /// </para>
    /// </summary>
    public sealed class PlayerPositionBroadcaster(IPlayerRegistry registry) : BackgroundService
    {
        private const double TeleportThreshold = 8.0;
        private const double DoubleTolerance = 0.000000001;
        // Per-entity last-broadcast state. Maintained entirely by this service;
        // no other thread reads or writes these values.
        // Key = EntityId (int).
        private readonly Dictionary<int, BroadcastState> _lastState = new();

        private readonly struct BroadcastState(
            double x, double y, double z, float yaw, float pitch)
        {
            public readonly double X = x;
            public readonly double Y = y;
            public readonly double Z = z;
            public readonly float Yaw = yaw;
            public readonly float Pitch = pitch;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(50));

            while (await timer.WaitForNextTickAsync(stoppingToken))
            {
                var players = registry.GetAll();

                // Broadcast each player's current position/rotation to all others.
                foreach (var player in players)
                {
                    var ctx = player.Context;
                    var entityId = ctx.EntityId;

                    var x = ctx.X;
                    var y = ctx.Y;
                    var z = ctx.Z;
                    var yaw = ctx.Yaw;
                    var pitch = ctx.Pitch;

                    if (!_lastState.TryGetValue(entityId, out var prev))
                    {
                        // First tick for this player — record state, no broadcast yet.
                        // The initial spawn packets already sent the position.
                        _lastState[entityId] = new BroadcastState(x, y, z, yaw, pitch);
                        continue;
                    }

                    var dx = x - prev.X;
                    var dy = y - prev.Y;
                    var dz = z - prev.Z;
                    var posChanged = dx != 0 || dy != 0 || dz != 0;
                    var rotChanged = yaw != prev.Yaw || pitch != prev.Pitch;

                    if (!posChanged && !rotChanged)
                    {
                        continue;
                    }

                    // Choose the most informative packet type.
                    // BroadcastMovement serializes once and fans out to all
                    // connections via the lock-free supersession slot arrays.
                    if (posChanged)
                    {
                        if (Math.Abs(dx) > TeleportThreshold
                            || Math.Abs(dy) > TeleportThreshold
                            || Math.Abs(dz) > TeleportThreshold)
                        {
                            registry.BroadcastMovement(entityId, new TeleportEntityPacket
                            {
                                EntityId = entityId,
                                X = x,
                                Y = y,
                                Z = z,
                                Yaw = yaw,
                                Pitch = pitch,
                            }, excludeUuid: player.Uuid);
                        }
                        else if (rotChanged)
                        {
                            registry.BroadcastMovement(entityId, new UpdateEntityPositionAndRotationPacket
                            {
                                EntityId = entityId,
                                DeltaX = EncodeDelta(dx),
                                DeltaY = EncodeDelta(dy),
                                DeltaZ = EncodeDelta(dz),
                                Yaw = yaw,
                                Pitch = pitch,
                                OnGround = false,
                            }, excludeUuid: player.Uuid);
                        }
                        else
                        {
                            registry.BroadcastMovement(entityId, new UpdateEntityPositionPacket
                            {
                                EntityId = entityId,
                                DeltaX = EncodeDelta(dx),
                                DeltaY = EncodeDelta(dy),
                                DeltaZ = EncodeDelta(dz),
                                OnGround = false,
                            }, excludeUuid: player.Uuid);
                        }
                    }
                    else
                    {
                        // Rotation only.
                        registry.BroadcastMovement(entityId, new UpdateEntityRotationPacket
                        {
                            EntityId = entityId,
                            Yaw = yaw,
                            Pitch = pitch,
                            OnGround = false,
                        }, excludeUuid: player.Uuid);
                    }

                    // Head rotation is always sent when yaw changes, regardless of position.
                    if (rotChanged || posChanged)
                    {
                        registry.BroadcastMovement(~entityId, new SetHeadRotationPacket
                        {
                            EntityId = entityId,
                            HeadYaw = yaw,
                        }, excludeUuid: player.Uuid);
                    }

                    _lastState[entityId] = new BroadcastState(x, y, z, yaw, pitch);
                }

                // Prune state for players who have disconnected since last tick.
                if (_lastState.Count > players.Count)
                {
                    var activeIds = new HashSet<int>(players.Count);

                    foreach (var p in players)
                    {
                        activeIds.Add(p.EntityId);
                    }

                    var toRemove = new List<int>();

                    foreach (var id in _lastState.Keys)
                    {
                        if (!activeIds.Contains(id))
                        {
                            toRemove.Add(id);
                        }
                    }

                    foreach (var id in toRemove)
                    {
                        _lastState.Remove(id);
                    }
                }
            }
        }

        private static short EncodeDelta(double delta) => (short)(delta * 4096.0);
    }
}