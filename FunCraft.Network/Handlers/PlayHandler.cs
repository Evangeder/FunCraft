using System.Buffers;
using FunCraft.Protocol.Registry;
using Microsoft.Extensions.Logging;

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

    // I'm leaving this here for future logging (to do not forget how to do it optimally)
    public static partial class Log
    {
        [LoggerMessage(
            EventId = 1003,
            Level = LogLevel.Debug,
            Message = "Payload: {Payload}")]
        public static partial void Payload(this ILogger logger, string payload);
    }

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

        private static readonly byte[] MsgWelcome =
            "Hello, welcome to the FunC#raft server!"u8.ToArray();
        private static readonly byte[] MsgInDev =
            "This server is heavily in development."u8.ToArray();

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
            _localCommands.Register(new GiveCommand(ctx));
            _localCommands.Register(new HelpCommand(_localCommands, commands));

            // Rent a buffer, fill from DB, copy into ctx, return immediately.
            var rentedInv = ArrayPool<InventorySlot>.Shared.Rent(InventorySlot.InventorySize);
            try
            {
                rentedInv.AsSpan(0, InventorySlot.InventorySize).Clear();
                var hadSaved = await inventory.TryGetInventoryAsync(ctx.Uuid, rentedInv.AsMemory(0, InventorySlot.InventorySize), ct);
                if (hadSaved)
                    rentedInv.AsSpan(0, InventorySlot.InventorySize).CopyTo(ctx.Inventory);
            }
            finally
            {
                ArrayPool<InventorySlot>.Shared.Return(rentedInv);
            }

            // Send the full 46-slot window to the client so it mirrors our server state.
            var slots = new (int ItemId, int Count)[InventorySlot.InventorySize];
            for (var i = 0; i < InventorySlot.InventorySize; i++)
                slots[i] = (ctx.Inventory[i].ItemId, ctx.Inventory[i].Count);

            await Sender.SendAsync(new SetContainerContentPacket
            {
                WindowId = 0,
                StateId = ctx.NextStateId(),
                Slots = slots,
            }, ct);

            _ = KeepAliveLoopAsync(ct);

            await registry.BroadcastAsync(new SystemChatMessagePacket
            {
                Content = ConcatBytes("Player '\u00a7e"u8, ctx.Username.Span, "\u00a7f' joined the game."u8)
            }, ct);
            await Sender.SendAsync(new SystemChatMessagePacket { Content = MsgWelcome }, ct);
            await Sender.SendAsync(new SystemChatMessagePacket { Content = MsgInDev }, ct);
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
                    HandleSetPlayerPositionAsync(payload, ct);
                    break;

                case SetPlayerPositionAndRotationPacket.Id:
                    HandleSetPlayerPositionAndRotationAsync(payload, ct);
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
                    await HandleClickContainerAsync(payload, ct);
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
                    break;

                case 0x12: // Close Container // TODO: Make actual packet for that lol
                    HandleCloseContainer();
                    break;


                default:
                    Console.WriteLine($"[PlayHandler] unhandled 0x{packetId:X2}");
                    break;
            }

            return ConnectionState.Play;
        }

        /// <summary>
        /// Sends the initial 5×5 grid on first teleport confirmation.
        /// </summary>
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
        /// <br/>TODO: Remove LINQ
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
            {
                for (var dz = -ViewDistance; dz <= ViewDistance; dz++)
                {
                    set.Add((cx + dx, cz + dz));
                }
            }

            return set;
        }

        private void HandleSetPlayerPositionAsync(ReadOnlySequence<byte> payload, CancellationToken ct)
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
            CheckChunkCrossAsync(ct);
        }

        private void HandleSetPlayerPositionAndRotationAsync(ReadOnlySequence<byte> payload, CancellationToken ct)
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
            CheckChunkCrossAsync(ct);
        }

        private void HandleSetPlayerRotation(ReadOnlySequence<byte> payload)
        {
            var reader = new SequenceReader<byte>(payload);
            var packet = new SetPlayerRotationPacket();
            if (!packet.TryRead(ref reader)) return;

            ctx.Yaw = packet.Yaw;
            ctx.Pitch = packet.Pitch;
        }

        private void CheckChunkCrossAsync(CancellationToken ct)
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

        // TODO creative mode when game-mode tracking is added.
        private async ValueTask HandlePlayerActionAsync(ReadOnlySequence<byte> payload, CancellationToken ct)
        {
            var reader = new SequenceReader<byte>(payload);
            var packet = new PlayerActionPacket();

            if (!packet.TryRead(ref reader))
            {
                return;
            }

            var pos = packet.Location;
            var column = world.GetChunk(WorldToChunk(pos.X), WorldToChunk(pos.Z));
            var block = column.GetBlock(pos.X, pos.Y, pos.Z);
            var hardness = RegistryLookup.GetHardness(block);

            if (packet.Status is PlayerActionPacket.ActionStatus.FinishedDigging
                || (packet.Status is PlayerActionPacket.ActionStatus.StartedDigging && hardness == 0))
            {
                column.SetBlock(pos.X, pos.Y, pos.Z, WellKnownBlocks.Air);

                var update = new BlockUpdatePacket
                {
                    Location = pos,
                    BlockState = WellKnownBlocks.Air.Id
                };
                await registry.BroadcastRawAsync(update, Guid.Empty, ct);
            }

            await Sender.SendAsync(new AcknowledgeBlockChangePacket { SequenceId = packet.Sequence }, ct);
        }

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

        private async ValueTask HandleClickContainerAsync(ReadOnlySequence<byte> payload, CancellationToken ct)
        {
            var reader = new SequenceReader<byte>(payload);
            var packet = new ClickContainerPacket();

            if (!packet.TryRead(ref reader))
            {
                return;
            }

            if (packet.WindowId != 0)
            {
                return;
            }

            switch (packet.Mode)
            {
                case 0:
                    ApplyNormalClick(packet.Slot, packet.Button);
                    break;
                case 1:
                    ApplyShiftClick(packet.Slot);
                    break;
                case 2:
                    ApplyHotbarSwap(packet.Slot, packet.Button);
                    break;
                case 5:
                    ApplyDrag(packet.Slot, packet.Button);
                    break;
                case 6:
                    ApplyDoubleClick();
                    break;
                    // mode 3 = middle-click (creative) — NYI
            }

            // Confirm server-authoritative state back to client so StateId stays in sync.
            // Without this the client accumulates a delta and re-sends stale slot state,
            // which overwrites earlier placements on the server.
            await SendInventorySync(ct);
        }

        // ── Mode 6 — double-click: collect matching items into cursor ────────────

        private void ApplyDoubleClick()
        {
            if (ctx.CursorItem.IsEmpty)
            {
                return;
            }

            const int max = 64;
            if (ctx.CursorItem.Count >= max)
            {
                return;
            }

            // Sweep all 46 slots; partial stacks first, then full stacks.
            // This matches vanilla's behaviour: partials are consumed before full stacks.
            Sweep(fullStacksOnly: false);
            if (ctx.CursorItem.Count < max)
            {
                Sweep(fullStacksOnly: true);
            }

            return;

            void Sweep(bool fullStacksOnly)
            {
                for (var i = 0; i < InventorySlot.InventorySize && ctx.CursorItem.Count < max; i++)
                {
                    ref var inv = ref ctx.Inventory[i];
                    if (inv.IsEmpty || inv.ItemId != ctx.CursorItem.ItemId) continue;
                    if (fullStacksOnly && inv.Count < max) continue;
                    if (!fullStacksOnly && inv.Count >= max) continue;

                    var take = Math.Min(max - ctx.CursorItem.Count, inv.Count);
                    ctx.CursorItem = new InventorySlot(ctx.CursorItem.ItemId, ctx.CursorItem.Count + take);
                    var left = inv.Count - take;
                    inv = left > 0 ? new InventorySlot(inv.ItemId, left) : InventorySlot.Empty;
                }
            }
        }

        private ValueTask SendInventorySync(CancellationToken ct)
        {
            var pool = ArrayPool<(int ItemId, int Count)>.Shared;
            var slots = pool.Rent(InventorySlot.InventorySize);

            try
            {
                for (var i = 0; i < InventorySlot.InventorySize; i++)
                {
                    slots[i] = (ctx.Inventory[i].ItemId, ctx.Inventory[i].Count);
                }

                var packetSlots = slots.AsSpan(0, InventorySlot.InventorySize).ToArray();

                return Sender.SendAsync(new SetContainerContentPacket
                {
                    WindowId = 0,
                    StateId = ctx.NextStateId(),
                    Slots = packetSlots,
                    CarriedItemId = ctx.CursorItem.ItemId,
                    CarriedItemCount = ctx.CursorItem.Count,
                }, ct);
            }
            finally
            {
                pool.Return(slots);
            }
        }

        private void HandleCloseContainer()
        {
            if (ctx.CursorItem.IsEmpty)
            {
                return;
            }

            // Try to return cursor item to the first available inventory slot (9-44).
            // TODO: if no space, spawn a dropped item entity instead.
            for (var i = 9; i <= 44; i++)
            {
                if (!ctx.Inventory[i].IsEmpty)
                {
                    continue;
                }

                ctx.Inventory[i] = ctx.CursorItem;
                ctx.CursorItem = InventorySlot.Empty;

                return;
            }

            // No free slot — item is lost for now
            // TODO: drop on ground
            ctx.CursorItem = InventorySlot.Empty;
        }


        private void ApplyNormalClick(short slot, byte button)
        {
            if (slot < 0)
            {
                if (button == 0)
                {
                    ctx.CursorItem = InventorySlot.Empty;
                }
                else if (!ctx.CursorItem.IsEmpty)
                {
                    ctx.CursorItem = ctx.CursorItem.Count > 1
                        ? new InventorySlot(ctx.CursorItem.ItemId, ctx.CursorItem.Count - 1)
                        : InventorySlot.Empty;
                }

                return;
            }

            if (slot >= InventorySlot.InventorySize)
            {
                return;
            }

            ref var inv = ref ctx.Inventory[slot];

            switch (button)
            {
                case 0:
                    if (ctx.CursorItem.IsEmpty)
                    {
                        ctx.CursorItem = inv;
                        inv = InventorySlot.Empty;
                    }
                    else if (inv.IsEmpty)
                    {
                        inv = ctx.CursorItem;
                        ctx.CursorItem = InventorySlot.Empty;
                    }
                    else if (ctx.CursorItem.ItemId == inv.ItemId)
                    {
                        var total = ctx.CursorItem.Count + inv.Count;
                        const int max = 64;
                        inv = new InventorySlot(inv.ItemId, Math.Min(total, max));
                        ctx.CursorItem = total > max
                            ? new InventorySlot(ctx.CursorItem.ItemId, total - max)
                            : InventorySlot.Empty;
                    }
                    else
                    {
                        (ctx.CursorItem, inv) = (inv, ctx.CursorItem);
                    }
                    break;

                case 1:
                    switch (ctx.CursorItem, inv)
                    {
                        case ({ IsEmpty: true }, { IsEmpty: false }):
                            var take = (inv.Count + 1) / 2;
                            var leave = inv.Count - take;
                            ctx.CursorItem = new InventorySlot(inv.ItemId, take);
                            inv = leave > 0 ? new InventorySlot(inv.ItemId, leave) : InventorySlot.Empty;
                            break;

                        case ({ IsEmpty: false }, { IsEmpty: true }):
                            inv = new InventorySlot(ctx.CursorItem.ItemId, 1);
                            ctx.CursorItem = ctx.CursorItem.Count > 1
                                ? new InventorySlot(ctx.CursorItem.ItemId, ctx.CursorItem.Count - 1)
                                : InventorySlot.Empty;
                            break;


                        case ({ IsEmpty: false }, _) when ctx.CursorItem.ItemId == inv.ItemId:
                            if (inv.Count < 64) // todo Get stack sizes per itemId - can get through items.json dumped from mcserver
                            {
                                inv = new InventorySlot(inv.ItemId, inv.Count + 1);
                                ctx.CursorItem = ctx.CursorItem.Count > 1
                                    ? new InventorySlot(ctx.CursorItem.ItemId, ctx.CursorItem.Count - 1)
                                    : InventorySlot.Empty;
                            }
                            break;

                        case ({ IsEmpty: false }, { IsEmpty: false }):
                            (ctx.CursorItem, inv) = (inv, ctx.CursorItem);
                            break;
                    }
                    break;
            }
        }

        private void ApplyShiftClick(short slot)
        {
            if (slot < 0 || slot >= InventorySlot.InventorySize) return;
            ref var src = ref ctx.Inventory[slot];
            if (src.IsEmpty) return;

            var (destStart, destEnd) = slot switch
            {
                >= 36 and <= 44 => (9, 35),   // hotbar   → main inventory
                >= 9 and <= 35 => (36, 44),  // main     → hotbar
                _ => (9, 44),   // armor/crafting → main+hotbar
            };

            for (var i = destStart; i <= destEnd && !src.IsEmpty; i++)
            {
                ref var dest = ref ctx.Inventory[i];
                if (dest.IsEmpty || dest.ItemId != src.ItemId || dest.Count >= 64)
                {
                    continue;
                }

                var transfer = Math.Min(64 - dest.Count, src.Count);
                dest = new InventorySlot(dest.ItemId, dest.Count + transfer);
                var remaining = src.Count - transfer;
                src = remaining > 0 ? new InventorySlot(src.ItemId, remaining) : InventorySlot.Empty;
            }

            for (var i = destStart; i <= destEnd && !src.IsEmpty; i++)
            {
                ref var dest = ref ctx.Inventory[i];
                if (!dest.IsEmpty)
                {
                    continue;
                }

                dest = src;
                src = InventorySlot.Empty;
            }
        }

        private void ApplyHotbarSwap(short slot, byte button)
        {
            if (slot is < 0 or >= InventorySlot.InventorySize)
            {
                return;
            }

            if (button > 8)
            {
                return;
            }

            var hotbarSlot = 36 + button;
            (ctx.Inventory[slot], ctx.Inventory[hotbarSlot]) = (ctx.Inventory[hotbarSlot], ctx.Inventory[slot]);
        }

        /// <summary>
        /// Inventory Drag/Paint
        /// <br/>0 = Start-left
        /// <br/>1 = add-left,
        /// <br/>2 = end-left,
        /// <br/>4 = start-right,
        /// <br/>5 = add-right,
        /// <br/>6 = end-right
        /// </summary>
        private void ApplyDrag(short slot, byte button)
        {
            switch (button)
            {
                case 0: // begin left-drag
                case 4: // begin right-drag
                    ctx.DragButton = button == 0 ? 0 : 1;
                    ctx.DragSlots.Clear();
                    break;

                case 1: // add slot to left-drag
                case 5: // add slot to right-drag
                    if (ctx.DragButton < 0)
                    {
                        return;
                    }

                    if (slot is >= 0 and < InventorySlot.InventorySize)
                    {
                        ctx.DragSlots.Add(slot);
                    }

                    break;

                case 2: // commit left-drag — distribute cursor stack evenly
                    {
                        if (ctx.DragButton != 0 || ctx.DragSlots.Count == 0 || ctx.CursorItem.IsEmpty)
                        {
                            break;
                        }

                        Span<int> targets = stackalloc int[ctx.DragSlots.Count];
                        var count = 0;

                        foreach (var s in ctx.DragSlots)
                        {
                            ref var inventorySlot = ref ctx.Inventory[s];

                            if (inventorySlot.IsEmpty || inventorySlot.ItemId == ctx.CursorItem.ItemId)
                            {
                                targets[count++] = s;
                            }
                        }

                        targets = targets[..count];
                        targets.Sort();

                        if (targets.Length == 0)
                        {
                            break;
                        }

                        var perSlot = ctx.CursorItem.Count / targets.Length;
                        if (perSlot < 1)
                        {
                            break; // not enough items to spread
                        }

                        var remaining = ctx.CursorItem.Count;
                        foreach (var s in targets)
                        {
                            ref var inv = ref ctx.Inventory[s];
                            var current = inv.IsEmpty ? 0 : inv.Count;
                            var canAdd = Math.Min(64 - current, perSlot);
                            if (canAdd <= 0) continue;
                            inv = new InventorySlot(ctx.CursorItem.ItemId, current + canAdd);
                            remaining -= canAdd;
                        }

                        ctx.CursorItem = remaining > 0
                            ? new InventorySlot(ctx.CursorItem.ItemId, remaining)
                            : InventorySlot.Empty;

                        ctx.DragButton = -1;
                        ctx.DragSlots.Clear();
                        break;
                    }

                case 6: // commit right-drag — place one item in each targeted slot
                    {
                        if (ctx.DragButton != 1 || ctx.DragSlots.Count == 0 || ctx.CursorItem.IsEmpty)
                        {
                            break;
                        }

                        Span<int> slots = stackalloc int[ctx.DragSlots.Count];
                        var slotCount = 0;

                        foreach (var s in ctx.DragSlots)
                        {
                            slots[slotCount++] = s;
                        }

                        slots = slots[..slotCount];
                        slots.Sort();

                        var remaining = ctx.CursorItem.Count;

                        for (var i = 0; i < slots.Length && remaining > 0; i++)
                        {
                            var s = slots[i];
                            ref var inv = ref ctx.Inventory[s];

                            if (!inv.IsEmpty && inv.ItemId != ctx.CursorItem.ItemId)
                            {
                                continue;
                            }

                            if (inv is { IsEmpty: false, Count: >= 64 })
                            {
                                continue;
                            }

                            var current = inv.IsEmpty ? 0 : inv.Count;
                            inv = new InventorySlot(ctx.CursorItem.ItemId, current + 1);
                            remaining--;
                        }

                        ctx.CursorItem = remaining > 0
                            ? new InventorySlot(ctx.CursorItem.ItemId, remaining)
                            : InventorySlot.Empty;

                        ctx.DragButton = -1;
                        ctx.DragSlots.Clear();
                        break;
                    }
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

            if (!packet.TryRead(ref reader))
            {
                return;
            }

            await Sender.SendAsync(new AcknowledgeBlockChangePacket { SequenceId = packet.Sequence }, ct);

            var held = ctx.HeldItem;
            if (held.IsEmpty)
            {
                return;
            }

            var blockStateId = BlockStatePlacer.Resolve(held.ItemId, packet.Face, ctx.Yaw, packet.CursorY);
            if (blockStateId == 0)
            {
                return; // item does not place a block
            }

            if (packet.Face < 0 || packet.Face >= FaceOffsets.Length)
            {
                return;
            }

            var (dx, dy, dz) = FaceOffsets[packet.Face];
            var placePos = new BlockPosition(packet.Location.X + dx, packet.Location.Y + dy, packet.Location.Z + dz);

            var playerBlockX = (int)Math.Floor(ctx.X);
            var playerBlockY = (int)Math.Floor(ctx.Y);
            var playerBlockZ = (int)Math.Floor(ctx.Z);

            if (placePos.X == playerBlockX && (placePos.Y == playerBlockY || placePos.Y == playerBlockY + 1) && placePos.Z == playerBlockZ)
            {
                return;
            }

            var column = world.GetChunk((int)Math.Floor((double)placePos.X / 16), (int)Math.Floor((double)placePos.Z / 16));
            column.SetBlock(placePos.X, placePos.Y, placePos.Z, new BlockState(blockStateId));

            ctx.HeldItem = ctx.HeldItem.Count > 1 ? new InventorySlot(ctx.HeldItem.ItemId, ctx.HeldItem.Count - 1) : InventorySlot.Empty;

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

            // Trim ASCII whitespace (≤0x20) without allocating.
            var msgSpan = packet.Message.Span;
            var start = 0;
            while (start < msgSpan.Length && msgSpan[start] <= 32) start++;
            var end = msgSpan.Length;
            while (end > start && msgSpan[end - 1] <= 32) end--;
            if (end <= start) return;

            var message = packet.Message[start..(end - start)];

            if (await commands.TryDispatchAsync(message, Respond, Sender, ct)) return;

            // Message starts with '/' but no dispatcher claimed it.
            if (message.Span[0] == (byte)'/')
            {
                await Respond(ConcatBytes("§cUnknown command: "u8, message.Span, MsgUnknownCommandSuffix));
                return;
            }

            // Broadcast "<username> message" — pure span concat, no intermediate string.
            var chatLine = ConcatBytes("\u00a77<\u00a7f"u8, ctx.Username.Span, "\u00a77> "u8, message.Span);
            await registry.BroadcastRawAsync(new SystemChatMessagePacket { Content = chatLine }, Guid.Empty, ct);

            return;

            Task Respond(ReadOnlyMemory<byte> text)
                => Sender.SendAsync(new SystemChatMessagePacket { Content = text }, ct).AsTask();
        }

        private static readonly byte[] MsgUnknownCommandSuffix =
            "§f. Try /help."u8.ToArray();

        private async ValueTask HandleChatCommandAsync(ReadOnlySequence<byte> payload, CancellationToken ct)
        {
            var reader = new SequenceReader<byte>(payload);
            var packet = new ChatCommandPacket();

            if (!packet.TryRead(ref reader))
            {
                return;
            }

            var cmd = packet.Command;
            var withSlash = new byte[1 + cmd.Length];
            withSlash[0] = (byte)'/';
            cmd.Span.CopyTo(withSlash.AsSpan(1));
            var withSlashMem = withSlash.AsMemory();

            if (await _localCommands.TryDispatchAsync(withSlashMem, Respond, Sender, ct))
            {
                return;
            }

            if (await commands.TryDispatchAsync(withSlashMem, Respond, Sender, ct))
            {
                return;
            }

            await Respond(ConcatBytes("§cUnknown command: "u8, withSlash, MsgUnknownCommandSuffix));

            return;

            Task Respond(ReadOnlyMemory<byte> text)
                => Sender.SendAsync(new SystemChatMessagePacket { Content = text }, ct).AsTask();
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
            if (!packet.TryRead(ref reader)) return;

            if (packet.KeepAliveId != _lastKeepAliveId)
            {
                Console.WriteLine($"[PlayHandler] keep-alive mismatch — sent {_lastKeepAliveId}, got {packet.KeepAliveId}");
            }
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

        private static int WorldToChunk(double worldCoord) => (int)Math.Floor(worldCoord) >> 4;

        /// <summary>Allocate one <c>byte[]</c> that is the concatenation of three spans.</summary>
        private static byte[] ConcatBytes(ReadOnlySpan<byte> a, ReadOnlySpan<byte> b, ReadOnlySpan<byte> c)
        {
            var result = new byte[a.Length + b.Length + c.Length];
            a.CopyTo(result);
            b.CopyTo(result.AsSpan(a.Length));
            c.CopyTo(result.AsSpan(a.Length + b.Length));
            return result;
        }

        /// <summary>Allocate one <c>byte[]</c> that is the concatenation of four spans.</summary>
        private static byte[] ConcatBytes(
            ReadOnlySpan<byte> a, ReadOnlySpan<byte> b,
            ReadOnlySpan<byte> c, ReadOnlySpan<byte> d)
        {
            var result = new byte[a.Length + b.Length + c.Length + d.Length];
            a.CopyTo(result);
            b.CopyTo(result.AsSpan(a.Length));
            c.CopyTo(result.AsSpan(a.Length + b.Length));
            d.CopyTo(result.AsSpan(a.Length + b.Length + c.Length));
            return result;
        }
    }
}