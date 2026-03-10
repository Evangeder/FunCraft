using System.Buffers;

namespace FunCraft.Network.Handlers
{
    using FunCraft.World;
    using Data.Players;
    using Protocol.Packets;
    using Protocol.Packets.Play.Incoming;
    using Protocol.Packets.Play.Outgoing;
    using World;

    /// <summary>
    /// Handles the Play connection state.
    /// </summary>
    internal class PlayHandler(IWorldSource world, PlayerContext ctx, IPlayerRepository players) : AsyncHandlerBase
    {
        private const int ViewDistance = 2;
        private const int InitialEntityId = 1;
        private const int InitialTeleportId = 1;
        private const long InitialWorldAge = 0;
        private const long NoonTimeOfDay = 6000;
        private const int RespawnGameEvent = 13;
        private const float RespawnGameEventValue = 0f;

        private const double DefaultSpawnX = 0.5;
        private const double DefaultSpawnY = 65.0;
        private const double DefaultSpawnZ = 0.5;

        private static readonly TimeSpan KeepAliveInterval = TimeSpan.FromSeconds(10);

        private bool _spawnAcknowledged;
        private long _lastKeepAliveId;

        internal async ValueTask OnEnterAsync(CancellationToken ct)
        {
            var record = await players.GetByUuidAsync(ctx.Uuid, ct);

            if (record is not null)
            {
                ctx.X = record.X;
                ctx.Y = record.Y;
                ctx.Z = record.Z;
                ctx.Yaw = record.Yaw;
                ctx.Pitch = record.Pitch;
            }
            else
            {
                ctx.X = DefaultSpawnX;
                ctx.Y = DefaultSpawnY;
                ctx.Z = DefaultSpawnZ;
            }

            await Sender.SendAsync(new LoginPlayPacket
            {
                EntityId = InitialEntityId,
                DimensionType = "minecraft:overworld",
                DimensionName = "minecraft:overworld"
            }, ct);

            await Sender.SendAsync(new SynchronizePlayerPositionPacket
            {
                TeleportId = InitialTeleportId,
                X = ctx.X,
                Y = ctx.Y,
                Z = ctx.Z,
                VelocityX = 0,
                VelocityY = 0,
                VelocityZ = 0,
                Yaw = ctx.Yaw,
                Pitch = ctx.Pitch,
                Flags = 0
            }, ct);

            await Sender.SendAsync(new SetCenterChunkPacket
            {
                ChunkX = (int)Math.Floor(ctx.X) >> 4,
                ChunkZ = (int)Math.Floor(ctx.Z) >> 4
            }, ct);

            await Sender.SendAsync(new SetChunkCacheRadiusPacket { ViewDistance = ViewDistance }, ct);

            await Sender.SendAsync(new UpdateTimePacket
            {
                WorldAge = InitialWorldAge,
                TimeOfDay = NoonTimeOfDay,
                TimeOfDayIncreasing = false
            }, ct);

            _ = KeepAliveLoopAsync(ct);
        }

        internal override async ValueTask<ConnectionState> HandleAsync(
            int packetId,
            ReadOnlySequence<byte> payload,
            CancellationToken ct)
        {
            switch (packetId)
            {
                case ConfirmTeleportationPacket.Id:
                    if (!_spawnAcknowledged)
                    {
                        _spawnAcknowledged = true;
                        await SendWorldAsync(ct);
                    }
                    break;

                case ServerboundKeepAlivePacket.Id:
                    HandleKeepAlive(payload);
                    break;

                case ChunkBatchReceivedPacket.Id:
                    HandleChunkBatchReceived(payload);
                    break;

                case SetPlayerPositionPacket.Id:
                    HandleSetPlayerPosition(payload);
                    break;

                case SetPlayerPositionAndRotationPacket.Id:
                    HandleSetPlayerPositionAndRotation(payload);
                    break;

                case SetPlayerRotationPacket.Id:
                    HandleSetPlayerRotation(payload);
                    break;

                // Acknowledged but no server-side action needed yet.
                case 0x0C: // Client Tick End
                case 0x20: // Set Player Movement Flags
                case 0x27: // Player Abilities
                case 0x29: // Player Command
                case 0x2A: // Player Input
                case 0x2B: // Player Loaded
                    break;

                default:
                    Console.WriteLine($"[PlayHandler] unhandled packet 0x{packetId:X2}");
                    break;
            }

            return ConnectionState.Play;
        }

        private void HandleSetPlayerPosition(ReadOnlySequence<byte> payload)
        {
            var reader = new SequenceReader<byte>(payload);
            var packet = new SetPlayerPositionPacket();
            if (!packet.TryRead(ref reader)) return;

            ctx.X = packet.X;
            ctx.Y = packet.Y;
            ctx.Z = packet.Z;
        }

        private void HandleSetPlayerPositionAndRotation(ReadOnlySequence<byte> payload)
        {
            var reader = new SequenceReader<byte>(payload);
            var packet = new SetPlayerPositionAndRotationPacket();
            if (!packet.TryRead(ref reader)) return;

            ctx.X = packet.X;
            ctx.Y = packet.Y;
            ctx.Z = packet.Z;
            ctx.Yaw = packet.Yaw;
            ctx.Pitch = packet.Pitch;
        }

        private void HandleSetPlayerRotation(ReadOnlySequence<byte> payload)
        {
            var reader = new SequenceReader<byte>(payload);
            var packet = new SetPlayerRotationPacket();
            if (!packet.TryRead(ref reader)) return;

            ctx.Yaw = packet.Yaw;
            ctx.Pitch = packet.Pitch;
        }

        private async Task KeepAliveLoopAsync(CancellationToken ct)
        {
            try
            {
                while (!ct.IsCancellationRequested)
                {
                    await Task.Delay(KeepAliveInterval, ct);
                    _lastKeepAliveId = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
                    await Sender.SendAsync(new ClientboundKeepAlivePacket
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
            if (!packet.TryRead(ref reader)) return;

            if (packet.KeepAliveId != _lastKeepAliveId)
            {
                Console.WriteLine($"[PlayHandler] keep-alive mismatch — sent {_lastKeepAliveId}, got {packet.KeepAliveId}");
            }
        }

        private async ValueTask SendWorldAsync(CancellationToken ct)
        {
            await Sender.SendAsync(new GameEventPacket
            {
                Event = RespawnGameEvent,
                Value = RespawnGameEventValue
            }, ct);

            var centerX = (int)Math.Floor(ctx.X) >> 4;
            var centerZ = (int)Math.Floor(ctx.Z) >> 4;
            const int total = (ViewDistance * 2 + 1) * (ViewDistance * 2 + 1);

            await Sender.SendAsync(new ChunkBatchStartPacket(), ct);

            for (var dx = -ViewDistance; dx <= ViewDistance; dx++)
            {
                for (var dz = -ViewDistance; dz <= ViewDistance; dz++)
                {
                    var column = world.GetChunk(centerX + dx, centerZ + dz);
                    var data = ChunkSerializer.Serialize(column);
                    await Sender.SendAsync(new ChunkDataPacket
                    {
                        ChunkX = centerX + dx,
                        ChunkZ = centerZ + dz,
                        ChunkData = data,
                    }, ct);
                }
            }

            await Sender.SendAsync(new ChunkBatchFinishedPacket { BatchSize = total }, ct);
        }

        private static void HandleChunkBatchReceived(ReadOnlySequence<byte> payload)
        {
            var reader = new SequenceReader<byte>(payload);
            var packet = new ChunkBatchReceivedPacket();
            if (packet.TryRead(ref reader))
            {
                Console.WriteLine($"[PlayHandler] client wants {packet.DesiredChunksPerTick:F2} chunks/tick");
            }
        }
    }
}