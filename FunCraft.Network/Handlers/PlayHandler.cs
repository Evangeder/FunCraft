using System.Buffers;

namespace FunCraft.Network.Handlers
{
    using FunCraft.World;
    using FunCraft.World.Blocks;
    using Commands;
    using Data.Players;
    using Data.Inventory;
    using Players;
    using Protocol.Packets;
    using Protocol.Packets.Play.Incoming;
    using Protocol.Packets.Play.Outgoing;
    using Protocol.Types;
    using World;

    internal class PlayHandler(IWorldSource world, PlayerContext ctx, IPlayerRepository players,
        IInventoryRepository inventory, IPlayerRegistry registry, CommandDispatcher commands) : AsyncHandlerBase
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

        private readonly CommandDispatcher _localCommands = new();

        private bool _spawnAcknowledged;
        private long _lastKeepAliveId;
        private int _lastChunkX = int.MinValue;
        private int _lastChunkZ = int.MinValue;
        private readonly HashSet<(int, int)> _loadedChunks = [];
        private readonly SemaphoreSlim _chunkLock = new(1, 1);

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
                ChunkX = WorldToChunk(ctx.X),
                ChunkZ = WorldToChunk(ctx.Z)
            }, ct);

            await Sender.SendAsync(new SetChunkCacheRadiusPacket { ViewDistance = ViewDistance }, ct);

            await Sender.SendAsync(new UpdateTimePacket
            {
                WorldAge = InitialWorldAge,
                TimeOfDay = NoonTimeOfDay,
                TimeOfDayIncreasing = false
            }, ct);

            var existing = registry.GetAll();
            if (existing.Count > 0)
            {
                await Sender.SendAsync(BuildInfoUpdate(existing), ct);
            }

            var self = new ConnectedPlayer(ctx.Uuid, ctx.Username, Sender, ctx);
            registry.Register(self);

            await Sender.SendAsync(BuildInfoUpdate([self]), ct);
            await registry.BroadcastRawAsync(BuildInfoUpdate([self]), excludeUuid: ctx.Uuid, ct);

            _localCommands.Register(new TestCommand(ctx));
            _localCommands.Register(new RespawnCommand(ctx));

            var savedHotbar = await inventory.GetHotbarAsync(ctx.Uuid, ct);
            if (savedHotbar is not null)
            {
                for (var i = 0; i < 9; i++)
                {
                    if (savedHotbar[i].IsEmpty) continue;
                    ctx.Hotbar[i] = savedHotbar[i];
                    await Sender.SendAsync(new SetContainerSlotPacket
                    {
                        WindowId = 0,
                        StateId = 0,
                        Slot = (short)(36 + i),
                        ItemId = savedHotbar[i].ItemId,
                        Count = savedHotbar[i].Count,
                    }, ct);
                }
            }

            _ = KeepAliveLoopAsync(ct);

            await registry.BroadcastAsync(new SystemChatMessagePacket { Content = $"Player '§e{ctx.Username}§f' joined the game." }, ct);
            await Sender.SendAsync(new SystemChatMessagePacket { Content = "Hello, welcome to the FunC#raft server!" }, ct);
            await Sender.SendAsync(new SystemChatMessagePacket { Content = "This server is heavily in development." }, ct);
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
                        await SendInitialChunksAsync(ct);
                    }
                    break;

                case ServerboundKeepAlivePacket.Id:
                    HandleKeepAlive(payload);
                    break;

                case ChunkBatchReceivedPacket.Id:
                    HandleChunkBatchReceived(payload);
                    break;

                case SetPlayerPositionPacket.Id:
                    HandleSetPlayerPosition(payload, ct);
                    break;

                case SetPlayerPositionAndRotationPacket.Id:
                    HandleSetPlayerPositionAndRotation(payload, ct);
                    break;

                case SetPlayerRotationPacket.Id:
                    HandleSetPlayerRotation(payload);
                    break;

                case ChatCommandPacket.Id:
                    await HandleChatCommandAsync(payload, ct);
                    break;

                case ChatMessagePacket.Id:
                    await HandleChatAsync(payload, ct);
                    break;

                case PlayerActionPacket.Id:
                    await HandlePlayerActionAsync(payload, ct);
                    break;

                case ClickContainerPacket.Id:
                    HandleClickContainer(payload);
                    break;

                case SetHeldItemPacket.Id:
                    HandleSetHeldItem(payload);
                    break;

                case UseItemOnPacket.Id:
                    await HandleUseItemOnAsync(payload, ct);
                    break;

                // Acknowledged but no server-side action needed yet.
                case 0x0C: // Client Tick End
                case 0x20: // Set Player Movement Flags
                case 0x27: // Player Abilities
                case 0x29: // Player Command
                case 0x2A: // Player Input
                case 0x2B: // Player Loaded
                case 0x3C: // Swing Arm
                case 0x12: // Close Container
                    break;

                default:
                    //Console.WriteLine($"[PlayHandler] unhandled 0x{packetId:X2}");
                    break;
            }

            return ConnectionState.Play;
        }

        // ─── Chunk streaming ─────────────────────────────────────────────────────

        /// <summary>Sends the initial 5×5 grid on first teleport confirmation.</summary>
        private async ValueTask SendInitialChunksAsync(CancellationToken ct)
        {
            await Sender.SendAsync(new GameEventPacket
            {
                Event = RespawnGameEvent,
                Value = RespawnGameEventValue
            }, ct);

            var cx = WorldToChunk(ctx.X);
            var cz = WorldToChunk(ctx.Z);
            _lastChunkX = cx;
            _lastChunkZ = cz;

            var batch = ChunksInView(cx, cz);

            await Sender.SendAsync(new ChunkBatchStartPacket(), ct);
            foreach (var (x, z) in batch)
            {
                await SendChunkAsync(x, z, ct);
                _loadedChunks.Add((x, z));
            }
            await Sender.SendAsync(new ChunkBatchFinishedPacket { BatchSize = batch.Count }, ct);
        }

        /// <summary>
        /// Called whenever the player's chunk X/Z changes.
        /// </summary>
        private async Task UpdateChunksAsync(int newCx, int newCz, CancellationToken ct)
        {
            if (!await _chunkLock.WaitAsync(0, ct))
            {
                return;
            }

            try
            {
                _lastChunkX = newCx;
                _lastChunkZ = newCz;

                await Sender.SendAsync(new SetCenterChunkPacket
                {
                    ChunkX = newCx,
                    ChunkZ = newCz
                }, ct);

                var newSet = ChunksInView(newCx, newCz);
                var toLoad = newSet.Except(_loadedChunks).ToList();
                var toUnload = _loadedChunks.Except(newSet).ToList();

                foreach (var (x, z) in toUnload)
                {
                    await Sender.SendAsync(new UnloadChunkPacket { ChunkX = x, ChunkZ = z }, ct);
                    _loadedChunks.Remove((x, z));
                }

                if (toLoad.Count > 0)
                {
                    await Sender.SendAsync(new ChunkBatchStartPacket(), ct);
                    foreach (var (x, z) in toLoad)
                    {
                        await SendChunkAsync(x, z, ct);
                        _loadedChunks.Add((x, z));
                    }
                    await Sender.SendAsync(new ChunkBatchFinishedPacket { BatchSize = toLoad.Count }, ct);
                }
            }
            finally
            {
                _chunkLock.Release();
            }
        }

        private async ValueTask SendChunkAsync(int chunkX, int chunkZ, CancellationToken ct)
        {
            var column = world.GetChunk(chunkX, chunkZ);
            var data = ChunkSerializer.Serialize(column);
            await Sender.SendAsync(new ChunkDataPacket
            {
                ChunkX = chunkX,
                ChunkZ = chunkZ,
                ChunkData = data
            }, ct);
        }

        private static HashSet<(int, int)> ChunksInView(int cx, int cz)
        {
            var set = new HashSet<(int, int)>((ViewDistance * 2 + 1) * (ViewDistance * 2 + 1));
            for (var dx = -ViewDistance; dx <= ViewDistance; dx++)
            for (var dz = -ViewDistance; dz <= ViewDistance; dz++)
                set.Add((cx + dx, cz + dz));
            return set;
        }

        private void HandleSetPlayerPosition(ReadOnlySequence<byte> payload, CancellationToken ct)
        {
            var reader = new SequenceReader<byte>(payload);
            var packet = new SetPlayerPositionPacket();
            if (!packet.TryRead(ref reader)) return;

            ctx.X = packet.X;
            ctx.Y = packet.Y;
            ctx.Z = packet.Z;
            CheckChunkCross(ct);
        }

        private void HandleSetPlayerPositionAndRotation(ReadOnlySequence<byte> payload, CancellationToken ct)
        {
            var reader = new SequenceReader<byte>(payload);
            var packet = new SetPlayerPositionAndRotationPacket();
            if (!packet.TryRead(ref reader)) return;

            ctx.X = packet.X;
            ctx.Y = packet.Y;
            ctx.Z = packet.Z;
            ctx.Yaw = packet.Yaw;
            ctx.Pitch = packet.Pitch;
            CheckChunkCross(ct);
        }

        private void HandleSetPlayerRotation(ReadOnlySequence<byte> payload)
        {
            var reader = new SequenceReader<byte>(payload);
            var packet = new SetPlayerRotationPacket();
            if (!packet.TryRead(ref reader)) return;

            ctx.Yaw = packet.Yaw;
            ctx.Pitch = packet.Pitch;
        }

        private void CheckChunkCross(CancellationToken ct)
        {
            if (!_spawnAcknowledged) return;
            var cx = WorldToChunk(ctx.X);
            var cz = WorldToChunk(ctx.Z);
            if (cx != _lastChunkX || cz != _lastChunkZ)
                _ = UpdateChunksAsync(cx, cz, ct);
        }

        private async ValueTask HandlePlayerActionAsync(ReadOnlySequence<byte> payload, CancellationToken ct)
        {
            var reader = new SequenceReader<byte>(payload);
            var packet = new PlayerActionPacket();
            if (!packet.TryRead(ref reader))
            {
                return;
            }

            // Only break on FinishedDigging.
            // Creative mode sends only StartedDigging — TODO when game-mode tracking is added.
            if (packet.Status is PlayerActionPacket.ActionStatus.FinishedDigging)
            {
                var pos = packet.Location;
                var column = world.GetChunk(WorldToChunk(pos.X), WorldToChunk(pos.Z));
                column.SetBlock(pos.X, pos.Y, pos.Z, WellKnownBlocks.Air);

                var update = new BlockUpdatePacket
                {
                    Location = pos,
                    BlockState = WellKnownBlocks.Air.Id
                };
                await registry.BroadcastAsync(update, ct);
            }

            await Sender.SendAsync(new AcknowledgeBlockChangePacket { SequenceId = packet.Sequence }, ct);
        }

        // item protocol_id  ->  default block state ID
        // Extend this table as more items are added.
        private static readonly Dictionary<int, ushort> ItemToBlockState = new()
        {
            [28] = 10,  // minecraft:dirt  item 28 -> block state 10
        };

        // Face index -> (dx, dy, dz) offset applied to the clicked block position.
        private static readonly (int dx, int dy, int dz)[] FaceOffsets =
        [
            (  0, -1,  0 ),  // 0  -Y  bottom
            (  0, +1,  0 ),  // 1  +Y  top
            (  0,  0, -1 ),  // 2  -Z  north
            (  0,  0, +1 ),  // 3  +Z  south
            ( -1,  0,  0 ),  // 4  -X  west
            ( +1,  0,  0 ),  // 5  +X  east
        ];

        private void HandleClickContainer(ReadOnlySequence<byte> payload)
        {
            var reader = new SequenceReader<byte>(payload);
            var packet = new ClickContainerPacket();
            if (!packet.TryRead(ref reader)) return;

            // Only mirror player inventory (window 0).
            if (packet.WindowId != 0) return;

            // Apply each reported slot change to our server-side hotbar.
            // Wire slots 36–44 = hotbar indices 0–8.
            foreach (var (wireSlot, itemId, count) in packet.ChangedSlots)
            {
                if (wireSlot < 36 || wireSlot > 44) continue;
                var hotbarIndex = wireSlot - 36;
                ctx.Hotbar[hotbarIndex] = itemId > 0
                    ? new HotbarSlot(itemId, count)
                    : HotbarSlot.Empty;
            }
        }

        private void HandleSetHeldItem(ReadOnlySequence<byte> payload)
        {
            var reader = new SequenceReader<byte>(payload);
            var packet = new SetHeldItemPacket();
            if (!packet.TryRead(ref reader))
            {
                return;
            }
            ctx.HeldSlot = Math.Clamp(packet.Slot, (short)0, (short)8);
        }

        private async ValueTask HandleUseItemOnAsync(ReadOnlySequence<byte> payload, CancellationToken ct)
        {
            var reader = new SequenceReader<byte>(payload);
            var packet = new UseItemOnPacket();
            if (!packet.TryRead(ref reader)) return;

            await Sender.SendAsync(new AcknowledgeBlockChangePacket { SequenceId = packet.Sequence }, ct);

            var held = ctx.Hotbar[ctx.HeldSlot];
            if (held.IsEmpty)
            {
                return;
            }

            if (!ItemToBlockState.TryGetValue(held.ItemId, out var blockStateId)) return;

            if (packet.Face < 0 || packet.Face >= FaceOffsets.Length) return;
            var (dx, dy, dz) = FaceOffsets[packet.Face];
            var placePos = new BlockPosition(
                packet.Location.X + dx,
                packet.Location.Y + dy,
                packet.Location.Z + dz);

            var playerBlockX = (int)Math.Floor(ctx.X);
            var playerBlockY = (int)Math.Floor(ctx.Y);
            var playerBlockZ = (int)Math.Floor(ctx.Z);
            if (placePos.X == playerBlockX &&
                (placePos.Y == playerBlockY || placePos.Y == playerBlockY + 1) &&
                placePos.Z == playerBlockZ)
                return;

            var column = world.GetChunk(
                (int)Math.Floor((double)placePos.X / 16),
                (int)Math.Floor((double)placePos.Z / 16));
            column.SetBlock(placePos.X, placePos.Y, placePos.Z, new BlockState(blockStateId));

            await registry.BroadcastRawAsync(new BlockUpdatePacket
            {
                Location = placePos,
                BlockState = blockStateId,
            }, Guid.Empty, ct);
        }

        private async ValueTask HandleChatAsync(ReadOnlySequence<byte> payload, CancellationToken ct)
        {
            var reader = new SequenceReader<byte>(payload);
            var packet = new ChatMessagePacket();
            if (!packet.TryRead(ref reader)) return;

            var message = packet.Message.Trim();
            if (string.IsNullOrEmpty(message)) return;

            //if (await _localCommands.TryDispatchAsync(message, respond, Sender, ct))
            //{
            //    return;
            //}

            if (await commands.TryDispatchAsync(message, respond: text => Sender.SendAsync(new SystemChatMessagePacket {Content = text}, ct).AsTask(), sender: Sender, ct))
            {
                return;
            }

            var chatLine = $"§7<§f{ctx.Username}§7> {message}";
            await registry.BroadcastRawAsync(new SystemChatMessagePacket { Content = chatLine }, Guid.Empty, ct);
        }

        private async ValueTask HandleChatCommandAsync(ReadOnlySequence<byte> payload, CancellationToken ct)
        {
            var reader = new SequenceReader<byte>(payload);
            var packet = new ChatCommandPacket();
            if (!packet.TryRead(ref reader)) return;

            var withSlash = '/' + packet.Command;
            Func<string, Task> respond = text => Sender.SendAsync(new SystemChatMessagePacket { Content = text }, ct).AsTask();

            if (!await _localCommands.TryDispatchAsync(withSlash, respond, Sender, ct))
            {
                await commands.TryDispatchAsync(withSlash, respond, Sender, ct);
            }
        }

        private static PlayerInfoUpdatePacket BuildInfoUpdate(IReadOnlyList<ConnectedPlayer> players)
        {
            var entries = new PlayerInfoUpdatePacket.PlayerInfoEntry[players.Count];
            for (var i = 0; i < players.Count; i++)
            {
                var p = players[i];
                entries[i] = new PlayerInfoUpdatePacket.PlayerInfoEntry
                {
                    Uuid = p.Uuid,
                    Username = p.Username,
                    Listed = true,
                    Latency = 0,
                };
            }

            return new PlayerInfoUpdatePacket
            {
                Players = entries
            };
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
            if (!packet.TryRead(ref reader))
            {
                return;
            }

            if (packet.KeepAliveId != _lastKeepAliveId)
            {
                //Console.WriteLine($"[PlayHandler] keep-alive mismatch — sent {_lastKeepAliveId}, got {packet.KeepAliveId}");
            }
        }

        private void HandleChunkBatchReceived(ReadOnlySequence<byte> payload)
        {
            var reader = new SequenceReader<byte>(payload);
            var packet = new ChunkBatchReceivedPacket();
            if (packet.TryRead(ref reader))
            {
                //Console.WriteLine($"[PlayHandler] client wants {packet.DesiredChunksPerTick:F2} chunks/tick");
            }
        }

        private static int WorldToChunk(double worldCoord) => (int)Math.Floor(worldCoord) >> 4;
    }
}