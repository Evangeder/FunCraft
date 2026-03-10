using System.Buffers;

namespace FunCraft.Network.Handlers
{
    using FunCraft.World;
    using Protocol.Packets;
    using Protocol.Packets.Play.Incoming;
    using Protocol.Packets.Play.Outgoing;
    using World;

    /// <summary>
    /// Handles the Play connection state.
    /// <br/>TODO: Expand properties to allow sending personalized data
    /// </summary>
    internal class PlayHandler(IWorldSource world) : AsyncHandlerBase
    {
        private const int SpawnTeleportId = 1;

        /// <summary>
        /// Radius — (2r+1)^2 = 25 chunks total, for now
        /// </summary>
        private const int ViewDistance = 2;

        private const double SpawnX = 0.5;
        private const double SpawnY = 65.0;
        private const double SpawnZ = 0.5;

        private static readonly TimeSpan KeepAliveInterval = TimeSpan.FromSeconds(10);

        private bool _spawnAcknowledged;
        private long _lastKeepAliveId;

        internal async ValueTask OnEnterAsync(CancellationToken ct)
        {
            await Sender.SendAsync(new LoginPlayPacket
            {
                EntityId = 1,
                DimensionType = "minecraft:overworld",
                DimensionName = "minecraft:overworld"
            }, ct);

            await Sender.SendAsync(new SynchronizePlayerPositionPacket
            {
                TeleportId = SpawnTeleportId,
                X = SpawnX,
                Y = SpawnY,
                Z = SpawnZ,
                VelocityX = 0,
                VelocityY = 0,
                VelocityZ = 0,
                Yaw = 0f,
                Pitch = 0f,
                Flags = 0
            }, ct);

            await Sender.SendAsync(new SetCenterChunkPacket { ChunkX = 0, ChunkZ = 0 }, ct);
            await Sender.SendAsync(new SetChunkCacheRadiusPacket { ViewDistance = ViewDistance }, ct);
            await Sender.SendAsync(new UpdateTimePacket
            {
                WorldAge = 0,
                TimeOfDay = 6000, // noon
                TimeOfDayIncreasing = false
            }, ct);

            _ = KeepAliveLoopAsync(ct);
        }

        internal override async ValueTask<ConnectionState> HandleAsync(int packetId, ReadOnlySequence<byte> payload, CancellationToken ct)
        {
            switch (packetId)
            {
                case ConfirmTeleportationPacket.Id when !_spawnAcknowledged:
                    _spawnAcknowledged = true;
                    await SendWorldAsync(ct);
                    break;

                case ServerboundKeepAlivePacket.Id:
                    HandleKeepAlive(payload);
                    break;

                case ChunkBatchReceivedPacket.Id:
                    HandleChunkBatchReceived(payload);
                    break;

                // High-frequency packets — no response needed.
                case 0x2B: // Client Loaded
                case 0x0C: // Client Tick End
                case 0x1D: // Set Player Position
                case 0x1E: // Set Player Position and Rotation
                case 0x1F: // Set Player Rotation
                case 0x20: // Set Player Movement Flags
                case 0x27: // Player Abilities
                case 0x29: // Player Command
                case 0x2A: // Player Input
                    break;

                default:
                    Console.WriteLine($"PlayHandler: unhandled packet 0x{packetId:X2}");
                    break;
            }

            return ConnectionState.Play;
        }

        private async Task KeepAliveLoopAsync(CancellationToken ct)
        {
            try
            {
                while (!ct.IsCancellationRequested)
                {
                    await Task.Delay(KeepAliveInterval, ct);
                    _lastKeepAliveId = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
                    await Sender.SendAsync(new ClientboundKeepAlivePacket()
                    {
                        KeepAliveId = _lastKeepAliveId
                    }, ct);
                }
            }
            catch (OperationCanceledException) { }
        }

        private void HandleKeepAlive(ReadOnlySequence<byte> payload)
        {
            var reader = new SequenceReader<byte>(payload);
            var packet = new ServerboundKeepAlivePacket();

            if (!packet.TryRead(ref reader))
            {
                Console.WriteLine("PlayHandler: failed to read keep-alive response");
                return;
            }

            if (packet.KeepAliveId != _lastKeepAliveId)
            {
                Console.WriteLine(
                    $"PlayHandler: keep-alive ID mismatch — sent {_lastKeepAliveId}, got {packet.KeepAliveId}");
            }
        }

        private async ValueTask SendWorldAsync(CancellationToken ct)
        {
            await Sender.SendAsync(new GameEventPacket { Event = 13, Value = 0f }, ct);

            const int total = (ViewDistance * 2 + 1) * (ViewDistance * 2 + 1);

            await Sender.SendAsync(new ChunkBatchStartPacket(), ct);

            for (var chunkX = -ViewDistance; chunkX <= ViewDistance; chunkX++)
            for (var chunkZ = -ViewDistance; chunkZ <= ViewDistance; chunkZ++)
            {
                var column = world.GetChunk(chunkX, chunkZ);
                var data = ChunkSerializer.Serialize(column);
                await Sender.SendAsync(new ChunkDataPacket
                {
                    ChunkX = chunkX,
                    ChunkZ = chunkZ,
                    ChunkData = data,
                }, ct);
            }

            await Sender.SendAsync(new ChunkBatchFinishedPacket { BatchSize = total }, ct);
        }

        private static void HandleChunkBatchReceived(ReadOnlySequence<byte> payload)
        {
            var reader = new SequenceReader<byte>(payload);
            var packet = new ChunkBatchReceivedPacket();

            if (packet.TryRead(ref reader))
            {
                Console.WriteLine($"PlayHandler: client wants {packet.DesiredChunksPerTick:F2} chunks/tick");
            }
        }
    }
}
