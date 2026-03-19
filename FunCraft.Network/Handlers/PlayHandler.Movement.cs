using System.Buffers;

namespace FunCraft.Network.Handlers
{
    using Protocol.Packets.Play.Incoming;
    using Protocol.Packets.Play.Outgoing;

    internal sealed partial class PlayHandler
    {
        private async ValueTask HandleSetPlayerPositionAsync(
            ReadOnlySequence<byte> payload, CancellationToken ct)
        {
            var reader = new SequenceReader<byte>(payload);
            var packet = new SetPlayerPositionPacket();

            if (!packet.TryRead(ref reader))
            {
                return;
            }

            ctx.X = packet.X;
            ctx.Y = packet.Y;
            ctx.Z = packet.Z;

            _playerBody?.RecordMovement(ctx.X, ctx.Y, ctx.Z);

            BroadcastPositionOnly();
            await CheckPickupsAsync(ct);
            CheckChunkCross(ct);
        }

        private async ValueTask HandleSetPlayerPositionAndRotationAsync(
            ReadOnlySequence<byte> payload, CancellationToken ct)
        {
            var reader = new SequenceReader<byte>(payload);
            var packet = new SetPlayerPositionAndRotationPacket();

            if (!packet.TryRead(ref reader))
            {
                return;
            }

            ctx.X = packet.X;
            ctx.Y = packet.Y;
            ctx.Z = packet.Z;
            ctx.Yaw = packet.Yaw;
            ctx.Pitch = packet.Pitch;

            _playerBody?.RecordMovement(ctx.X, ctx.Y, ctx.Z);

            BroadcastPositionAndRotation();
            await CheckPickupsAsync(ct);
            CheckChunkCross(ct);
        }

        private void HandleSetPlayerRotation(ReadOnlySequence<byte> payload)
        {
            var reader = new SequenceReader<byte>(payload);
            var packet = new SetPlayerRotationPacket();

            if (!packet.TryRead(ref reader))
            {
                return;
            }

            ctx.Yaw = packet.Yaw;
            ctx.Pitch = packet.Pitch;

            // Movement key = entityId for body rotation, ~entityId for head rotation.
            _ = registry.BroadcastMovementAsync(ctx.EntityId, new UpdateEntityRotationPacket
            {
                EntityId = ctx.EntityId,
                Yaw = ctx.Yaw,
                Pitch = ctx.Pitch,
                OnGround = false,
            }, excludeUuid: ctx.Uuid);

            _ = registry.BroadcastMovementAsync(~ctx.EntityId, new SetHeadRotationPacket
            {
                EntityId = ctx.EntityId,
                HeadYaw = ctx.Yaw,
            }, excludeUuid: ctx.Uuid);
        }

        private void BroadcastPositionOnly()
        {
            var dx = ctx.X - _prevBroadcastX;
            var dy = ctx.Y - _prevBroadcastY;
            var dz = ctx.Z - _prevBroadcastZ;

            if (Math.Abs(dx) > TeleportThreshold
                || Math.Abs(dy) > TeleportThreshold
                || Math.Abs(dz) > TeleportThreshold)
            {
                // Teleport replaces the position slot for this entity.
                _ = registry.BroadcastMovementAsync(ctx.EntityId, new TeleportEntityPacket
                {
                    EntityId = ctx.EntityId,
                    X = ctx.X,
                    Y = ctx.Y,
                    Z = ctx.Z,
                    Yaw = ctx.Yaw,
                    Pitch = ctx.Pitch,
                }, excludeUuid: ctx.Uuid);
            }
            else
            {
                _ = registry.BroadcastMovementAsync(ctx.EntityId, new UpdateEntityPositionPacket
                {
                    EntityId = ctx.EntityId,
                    DeltaX = EncodeDelta(dx),
                    DeltaY = EncodeDelta(dy),
                    DeltaZ = EncodeDelta(dz),
                    OnGround = false,
                }, excludeUuid: ctx.Uuid);
            }

            _prevBroadcastX = ctx.X;
            _prevBroadcastY = ctx.Y;
            _prevBroadcastZ = ctx.Z;
        }

        private void BroadcastPositionAndRotation()
        {
            var dx = ctx.X - _prevBroadcastX;
            var dy = ctx.Y - _prevBroadcastY;
            var dz = ctx.Z - _prevBroadcastZ;

            if (Math.Abs(dx) > TeleportThreshold
                || Math.Abs(dy) > TeleportThreshold
                || Math.Abs(dz) > TeleportThreshold)
            {
                registry.BroadcastMovementAsync(ctx.EntityId, new TeleportEntityPacket
                {
                    EntityId = ctx.EntityId,
                    X = ctx.X,
                    Y = ctx.Y,
                    Z = ctx.Z,
                    Yaw = ctx.Yaw,
                    Pitch = ctx.Pitch,
                }, excludeUuid: ctx.Uuid);
            }
            else
            {
                registry.BroadcastMovementAsync(ctx.EntityId, new UpdateEntityPositionAndRotationPacket
                {
                    EntityId = ctx.EntityId,
                    DeltaX = EncodeDelta(dx),
                    DeltaY = EncodeDelta(dy),
                    DeltaZ = EncodeDelta(dz),
                    Yaw = ctx.Yaw,
                    Pitch = ctx.Pitch,
                    OnGround = false,
                }, excludeUuid: ctx.Uuid);
            }

            // Head rotation goes into its own supersession slot (~entityId).
            registry.BroadcastMovementAsync(~ctx.EntityId, new SetHeadRotationPacket
            {
                EntityId = ctx.EntityId,
                HeadYaw = ctx.Yaw,
            }, excludeUuid: ctx.Uuid);

            _prevBroadcastX = ctx.X;
            _prevBroadcastY = ctx.Y;
            _prevBroadcastZ = ctx.Z;
        }

        private static short EncodeDelta(double delta) => (short)(delta * 4096.0);

        private void CheckChunkCross(CancellationToken ct)
        {
            if (!_spawnAcknowledged)
            {
                return;
            }

            var cx = WorldToChunk(ctx.X);
            var cz = WorldToChunk(ctx.Z);

            if (cx != _lastChunkX || cz != _lastChunkZ)
            {
                _ = UpdateChunksAsync(cx, cz, ct);
            }
        }
    }
}