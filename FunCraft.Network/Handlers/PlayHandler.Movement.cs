using System.Buffers;

namespace FunCraft.Network.Handlers
{
    using Protocol.Packets.Play.Incoming;

    internal sealed partial class PlayHandler
    {
        // Movement handlers update ctx state only.
        // All position/rotation broadcasts are handled by PlayerPositionBroadcaster
        // (a 20Hz BackgroundService) which fans out to all connections in a single
        // controlled pass — removing O(N²) broadcast work from the packet processing
        // hot path entirely.

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