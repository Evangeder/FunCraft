using FunCraft.Protocol.Registry;
using Microsoft.Extensions.Logging;
using System.Buffers;

namespace FunCraft.Network.Handlers
{
    using Commands;
    using Data.Inventory;
    using Data.Players;
    using Entities;
    using FunCraft.Network.Connections;
    using FunCraft.World;
    using FunCraft.World.Blocks;
    using Physics;
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
        IInventoryRepository inventory, IPlayerRegistry registry, CommandDispatcher commands,
        ReadOnlyMemory<byte> welcomeMessage, IEntityManager entities,
        IPhysicsEngine physics) : AsyncHandlerBase
    {
        private const int ViewDistance = 2;
        private const int InitialTeleportId = 1;
        private const long InitialWorldAge = 0;
        private const long NoonTimeOfDay = 6000;
        private const int RespawnGameEvent = 13;
        private const float RespawnGameEventValue = 0f;

        private const double DefaultSpawnX = 0.5;
        private const double DefaultSpawnY = 65.0;
        private const double DefaultSpawnZ = 0.5;

        private const double PickupRadius = 1.5;
        private const double TeleportThreshold = 8.0;

        private static readonly TimeSpan KeepAliveInterval = TimeSpan.FromSeconds(10);

        // Entity type IDs via shared EntityTypeIds class (avoids duplicate Lazy allocations).

        private static readonly byte[] MsgWelcome =
            "Hello, welcome to the FunC#raft server!"u8.ToArray();
        private static readonly byte[] MsgInDev =
            "This server is heavily in development."u8.ToArray();

        private readonly CommandDispatcher _localCommands = new();

        private bool _spawnAcknowledged;
        private long _lastKeepAliveId;
        // All keep-alive IDs sent but not yet acknowledged. The client may
        // respond to several in a burst after any processing pause.
        private readonly HashSet<long> _pendingKeepAliveIds = [];
        private int _lastChunkX = int.MinValue;
        private int _lastChunkZ = int.MinValue;
        private readonly HashSet<(int, int)> _loadedChunks = [];
        private readonly SemaphoreSlim _chunkLock = new(1, 1);

        private double _prevBroadcastX;
        private double _prevBroadcastY;
        private double _prevBroadcastZ;

        // Set in OnEnterAsync; used to feed the movement history for anti-cheat.
        private PlayerPhysicsBody? _playerBody;

        internal async ValueTask OnEnterAsync(CancellationToken ct)
        {
            var record = await players.GetByUuidAsync(ctx.Uuid, ct);
            if (record is not null)
            {
                ctx.X = record.X; ctx.Y = record.Y; ctx.Z = record.Z;
                ctx.Yaw = record.Yaw; ctx.Pitch = record.Pitch;
            }
            else
            {
                ctx.X = DefaultSpawnX; ctx.Y = DefaultSpawnY; ctx.Z = DefaultSpawnZ;
            }

            _prevBroadcastX = ctx.X;
            _prevBroadcastY = ctx.Y;
            _prevBroadcastZ = ctx.Z;

            // Register player tracking body. Records movement history each tick
            // for future anti-cheat validation.
            _playerBody = physics.RegisterPlayer(ctx.EntityId, ctx.X, ctx.Y, ctx.Z);

            await Sender.SendAsync(new LoginPlayPacket
            {
                EntityId = ctx.EntityId,
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
                foreach (var other in existing)
                    await SendSpawnPlayerAsync(Sender, other, ct);
            }

            var self = new ConnectedPlayer(ctx.Uuid, ctx.Username, Sender, ctx);
            registry.Register(self);

            await Sender.SendAsync(BuildInfoUpdate([self]), ct);
            await registry.BroadcastRawAsync(BuildInfoUpdate([self]), excludeUuid: ctx.Uuid, ct);
            foreach (var other in existing)
                await SendSpawnPlayerAsync(other.Sender, self, ct);

            _localCommands.Register(new TestCommand(ctx));
            _localCommands.Register(new RespawnCommand(ctx));
            _localCommands.Register(new GiveCommand(ctx, entities, physics));
            _localCommands.Register(new HelpCommand(_localCommands, commands));

            var rentedInv = ArrayPool<InventorySlot>.Shared.Rent(InventorySlot.InventorySize);
            try
            {
                rentedInv.AsSpan(0, InventorySlot.InventorySize).Clear();
                var hadSaved = await inventory.TryGetInventoryAsync(
                    ctx.Uuid, rentedInv.AsMemory(0, InventorySlot.InventorySize), ct);
                if (hadSaved)
                    rentedInv.AsSpan(0, InventorySlot.InventorySize).CopyTo(ctx.Inventory);
            }
            finally { ArrayPool<InventorySlot>.Shared.Return(rentedInv); }

            var slots = new (int ItemId, int Count, int Damage)[InventorySlot.InventorySize];
            for (var i = 0; i < InventorySlot.InventorySize; i++)
                slots[i] = (ctx.Inventory[i].ItemId, ctx.Inventory[i].Count, SlotDamage(ctx.Inventory[i]));

            await Sender.SendAsync(new SetContainerContentPacket
            {
                WindowId = 0,
                StateId = ctx.NextStateId(),
                Slots = slots,
            }, ct);

            foreach (var item in entities.GetAllItems())
                await SendSpawnItemAsync(Sender, item, ct);

            _ = KeepAliveLoopAsync(ct);

            await registry.BroadcastAsync(new SystemChatMessagePacket
            {
                Content = ConcatBytes("\u00a7e"u8, ctx.Username.Span, "\u00a7f joined the game."u8)
            }, ct);
            await Sender.SendAsync(new SystemChatMessagePacket { Content = MsgWelcome }, ct);
            await Sender.SendAsync(new SystemChatMessagePacket { Content = MsgInDev }, ct);

            var remaining = welcomeMessage;
            while (!remaining.IsEmpty)
            {
                var nl = remaining.Span.IndexOf((byte)'\n');
                ReadOnlyMemory<byte> line;
                if (nl < 0) { line = remaining; remaining = default; }
                else { line = remaining[..nl]; remaining = remaining[(nl + 1)..]; }
                if (!line.IsEmpty)
                    await Sender.SendAsync(new SystemChatMessagePacket { Content = line }, ct);
            }
        }

        internal override async ValueTask<ConnectionState> HandleAsync(
            int packetId, ReadOnlySequence<byte> payload, CancellationToken ct)
        {
            switch (packetId)
            {
                case ConfirmTeleportationPacket.Id:
                    if (!_spawnAcknowledged) { _spawnAcknowledged = true; await SendInitialChunksAsync(ct); }
                    break;
                case ServerboundKeepAlivePacket.Id:
                    HandleKeepAlive(payload);
                    break;
                case ChunkBatchReceivedPacket.Id:
                    HandleChunkBatchReceived(payload);
                    break;
                case SetPlayerPositionPacket.Id:
                    await HandleSetPlayerPositionAsync(payload, ct);
                    break;
                case SetPlayerPositionAndRotationPacket.Id:
                    await HandleSetPlayerPositionAndRotationAsync(payload, ct);
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
                case 0x0C:
                case 0x20:
                case 0x27:
                case 0x29:
                case 0x2A:
                case 0x2B:
                case 0x3C:
                    break;
                case 0x12:
                    HandleCloseContainer();
                    break;
                default:
                    Console.WriteLine($"[PlayHandler] unhandled 0x{packetId:X2}");
                    break;
            }
            return ConnectionState.Play;
        }

        // ─── Chunks ───────────────────────────────────────────────────────────────

        private async ValueTask SendInitialChunksAsync(CancellationToken ct)
        {
            await Sender.SendAsync(new GameEventPacket
            {
                Event = RespawnGameEvent,
                Value = RespawnGameEventValue
            }, ct);

            var cx = WorldToChunk(ctx.X);
            var cz = WorldToChunk(ctx.Z);
            _lastChunkX = cx; _lastChunkZ = cz;

            var batch = ChunksInView(cx, cz);
            await Sender.SendAsync(new ChunkBatchStartPacket(), ct);
            foreach (var (x, z) in batch) { await SendChunkAsync(x, z, ct); _loadedChunks.Add((x, z)); }
            await Sender.SendAsync(new ChunkBatchFinishedPacket { BatchSize = batch.Count }, ct);
        }

        /// <summary>TODO: Remove LINQ</summary>
        private async Task UpdateChunksAsync(int newCx, int newCz, CancellationToken ct)
        {
            if (!await _chunkLock.WaitAsync(0, ct)) return;
            try
            {
                _lastChunkX = newCx; _lastChunkZ = newCz;
                await Sender.SendAsync(new SetCenterChunkPacket { ChunkX = newCx, ChunkZ = newCz }, ct);

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
                    foreach (var (x, z) in toLoad) { await SendChunkAsync(x, z, ct); _loadedChunks.Add((x, z)); }
                    await Sender.SendAsync(new ChunkBatchFinishedPacket { BatchSize = toLoad.Count }, ct);
                }
            }
            finally { _chunkLock.Release(); }
        }

        private async ValueTask SendChunkAsync(int chunkX, int chunkZ, CancellationToken ct)
        {
            var column = world.GetChunk(chunkX, chunkZ);
            var data = ChunkSerializer.Serialize(column);
            await Sender.SendAsync(new ChunkDataPacket { ChunkX = chunkX, ChunkZ = chunkZ, ChunkData = data }, ct);
        }

        private static HashSet<(int, int)> ChunksInView(int cx, int cz)
        {
            var set = new HashSet<(int, int)>((ViewDistance * 2 + 1) * (ViewDistance * 2 + 1));
            for (var dx = -ViewDistance; dx <= ViewDistance; dx++)
                for (var dz = -ViewDistance; dz <= ViewDistance; dz++)
                    set.Add((cx + dx, cz + dz));
            return set;
        }

        // ─── Movement ─────────────────────────────────────────────────────────────

        private async ValueTask HandleSetPlayerPositionAsync(ReadOnlySequence<byte> payload, CancellationToken ct)
        {
            var reader = new SequenceReader<byte>(payload);
            var packet = new SetPlayerPositionPacket();
            if (!packet.TryRead(ref reader)) return;

            ctx.X = packet.X; ctx.Y = packet.Y; ctx.Z = packet.Z;
            _playerBody?.RecordMovement(ctx.X, ctx.Y, ctx.Z);
            BroadcastPositionOnly();
            await CheckPickupsAsync(ct);
            CheckChunkCross(ct);
        }

        private async ValueTask HandleSetPlayerPositionAndRotationAsync(ReadOnlySequence<byte> payload, CancellationToken ct)
        {
            var reader = new SequenceReader<byte>(payload);
            var packet = new SetPlayerPositionAndRotationPacket();
            if (!packet.TryRead(ref reader)) return;

            ctx.X = packet.X; ctx.Y = packet.Y; ctx.Z = packet.Z;
            ctx.Yaw = packet.Yaw; ctx.Pitch = packet.Pitch;
            _playerBody?.RecordMovement(ctx.X, ctx.Y, ctx.Z);
            BroadcastPositionAndRotation();
            await CheckPickupsAsync(ct);
            CheckChunkCross(ct);
        }

        private void HandleSetPlayerRotation(ReadOnlySequence<byte> payload)
        {
            var reader = new SequenceReader<byte>(payload);
            var packet = new SetPlayerRotationPacket();
            if (!packet.TryRead(ref reader)) return;

            ctx.Yaw = packet.Yaw; ctx.Pitch = packet.Pitch;

            _ = registry.BroadcastRawAsync(new UpdateEntityRotationPacket
            {
                EntityId = ctx.EntityId,
                Yaw = ctx.Yaw,
                Pitch = ctx.Pitch,
                OnGround = false,
            }, excludeUuid: ctx.Uuid);

            _ = registry.BroadcastRawAsync(new SetHeadRotationPacket
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

            if (Math.Abs(dx) > TeleportThreshold || Math.Abs(dy) > TeleportThreshold || Math.Abs(dz) > TeleportThreshold)
            {
                _ = registry.BroadcastRawAsync(new TeleportEntityPacket
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
                _ = registry.BroadcastRawAsync(new UpdateEntityPositionPacket
                {
                    EntityId = ctx.EntityId,
                    DeltaX = EncodeDelta(dx),
                    DeltaY = EncodeDelta(dy),
                    DeltaZ = EncodeDelta(dz),
                    OnGround = false,
                }, excludeUuid: ctx.Uuid);
            }

            _prevBroadcastX = ctx.X; _prevBroadcastY = ctx.Y; _prevBroadcastZ = ctx.Z;
        }

        private void BroadcastPositionAndRotation()
        {
            var dx = ctx.X - _prevBroadcastX;
            var dy = ctx.Y - _prevBroadcastY;
            var dz = ctx.Z - _prevBroadcastZ;

            if (Math.Abs(dx) > TeleportThreshold || Math.Abs(dy) > TeleportThreshold || Math.Abs(dz) > TeleportThreshold)
            {
                registry.BroadcastRawAsync(new TeleportEntityPacket
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
                registry.BroadcastRawAsync(new UpdateEntityPositionAndRotationPacket
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

            registry.BroadcastRawAsync(new SetHeadRotationPacket
            {
                EntityId = ctx.EntityId,
                HeadYaw = ctx.Yaw,
            }, excludeUuid: ctx.Uuid);

            _prevBroadcastX = ctx.X; _prevBroadcastY = ctx.Y; _prevBroadcastZ = ctx.Z;
        }

        private static short EncodeDelta(double delta) => (short)(delta * 4096.0);

        private void CheckChunkCross(CancellationToken ct)
        {
            if (!_spawnAcknowledged) return;
            var cx = WorldToChunk(ctx.X);
            var cz = WorldToChunk(ctx.Z);
            if (cx != _lastChunkX || cz != _lastChunkZ)
                _ = UpdateChunksAsync(cx, cz, ct);
        }

        // ─── Pickups ──────────────────────────────────────────────────────────────

        // Items cannot be picked up for this long after spawning.
        // Matches vanilla's 10-tick (500 ms) pickup delay.
        private const long PickupCooldownMs = 500;

        private async Task CheckPickupsAsync(CancellationToken ct)
        {
            var now = Environment.TickCount64;
            var nearby = entities.FindPickups(ctx.X, ctx.Y, ctx.Z, PickupRadius);
            if (nearby.Count == 0) return;

            foreach (var item in nearby)
            {
                // Enforce spawn cooldown — skip items that just appeared.
                // SpawnedAtMs == 0 means instant pickup (e.g. /give); otherwise enforce cooldown.
                if (item.SpawnedAtMs != 0 && now - item.SpawnedAtMs < PickupCooldownMs) continue;

                if (!entities.TryRemove(item.EntityId, out _)) continue;

                // Stop physics simulation for this entity now that it's been collected.
                physics.Unregister(item.EntityId);

                await registry.BroadcastRawAsync(new PickupItemPacket
                {
                    CollectedEntityId = item.EntityId,
                    CollectorEntityId = ctx.EntityId,
                    Count = item.Count,
                }, excludeUuid: Guid.Empty, ct);

                await registry.BroadcastRawAsync(new RemoveEntitiesPacket
                {
                    EntityIds = [item.EntityId],
                }, excludeUuid: Guid.Empty, ct);

                if (TryAddToInventory(item.ItemId, item.Count, out var slotIndex))
                {
                    await Sender.SendAsync(new SetContainerSlotPacket
                    {
                        WindowId = 0,
                        StateId = ctx.NextStateId(),
                        Slot = (short)slotIndex,
                        ItemId = ctx.Inventory[slotIndex].ItemId,
                        Count = ctx.Inventory[slotIndex].Count,
                        Damage = SlotDamage(ctx.Inventory[slotIndex]),
                    }, ct);
                }
            }
        }

        private bool TryAddToInventory(int itemId, int count, out int slotIndex)
        {
            var max = ItemStackTable.GetMaxStack(itemId);

            // Vanilla priority: hotbar partial → main partial → hotbar empty → main empty.

            // Pass 1: merge into partial stacks — hotbar first, then main.
            for (var i = 36; i < InventorySlot.InventorySize; i++)   // hotbar 36–44
            {
                ref var slot = ref ctx.Inventory[i];
                if (slot.IsEmpty || slot.ItemId != itemId || slot.Count >= max) continue;
                slot = new InventorySlot(itemId, slot.Count + Math.Min(max - slot.Count, count), slot.Durability);
                slotIndex = i;
                return true;
            }
            for (var i = 9; i < 36; i++)                              // main 9–35
            {
                ref var slot = ref ctx.Inventory[i];
                if (slot.IsEmpty || slot.ItemId != itemId || slot.Count >= max) continue;
                slot = new InventorySlot(itemId, slot.Count + Math.Min(max - slot.Count, count), slot.Durability);
                slotIndex = i;
                return true;
            }

            // Pass 2: first empty slot — hotbar first, then main.
            for (var i = 36; i < InventorySlot.InventorySize; i++)   // hotbar 36–44
            {
                ref var slot = ref ctx.Inventory[i];
                if (!slot.IsEmpty) continue;
                slot = new InventorySlot(itemId, Math.Min(count, max));
                slotIndex = i;
                return true;
            }
            for (var i = 9; i < 36; i++)                              // main 9–35
            {
                ref var slot = ref ctx.Inventory[i];
                if (!slot.IsEmpty) continue;
                slot = new InventorySlot(itemId, Math.Min(count, max));
                slotIndex = i;
                return true;
            }

            slotIndex = -1;
            return false;
        }

        // ─── Block break / item drop ──────────────────────────────────────────────

        // TODO creative mode when game-mode tracking is added.
        private async ValueTask HandlePlayerActionAsync(ReadOnlySequence<byte> payload, CancellationToken ct)
        {
            var reader = new SequenceReader<byte>(payload);
            var packet = new PlayerActionPacket();
            if (!packet.TryRead(ref reader)) return;

            // ── Item drop (Q key) ────────────────────────────────────────────────
            if (packet.Status is PlayerActionPacket.ActionStatus.DropItem
                              or PlayerActionPacket.ActionStatus.DropItemStack)
            {
                await HandleDropItemAsync(packet.Status == PlayerActionPacket.ActionStatus.DropItemStack, ct);
                return; // no AcknowledgeBlockChange for drops
            }

            // ── Block dig ────────────────────────────────────────────────────────
            var pos = packet.Location;
            var column = world.GetChunk(WorldToChunk(pos.X), WorldToChunk(pos.Z));
            var block = column.GetBlock(pos.X, pos.Y, pos.Z);
            var hardness = RegistryLookup.GetHardness(block);

            if (packet.Status is PlayerActionPacket.ActionStatus.FinishedDigging
                || (packet.Status is PlayerActionPacket.ActionStatus.StartedDigging && hardness == 0))
            {
                column.SetBlock(pos.X, pos.Y, pos.Z, WellKnownBlocks.Air);

                await registry.BroadcastRawAsync(new BlockUpdatePacket
                {
                    Location = pos,
                    BlockState = WellKnownBlocks.Air.Id
                }, Guid.Empty, ct);

                // Damage held tool — always, regardless of whether it's the correct type.
                DamageHeldTool(block, ct);

                // Resolve block → drop item via BlockDropTable, then spawn it.
                var blockName = RegistryLookup.GetBlockName(block);
                if (!blockName.IsEmpty)
                {
                    var dropName = BlockDropTable.GetDrop(blockName.Span);
                    if (!dropName.IsEmpty)
                    {
                        var itemId = RegistryLookup.GetItemId(dropName.Span);
                        if (itemId >= 0)
                        {
                            var vx = (Random.Shared.NextDouble() - 0.5d) * 0.25d;
                            var vy = Random.Shared.NextDouble() * 0.25d;
                            var vz = (Random.Shared.NextDouble() - 0.5d) * 0.25d;

                            var dropped = entities.SpawnItem(itemId, 1,
                                pos.X + 0.5, pos.Y + 0.5, pos.Z + 0.5,
                                vx, vy, vz);

                            var body = physics.RegisterItem(dropped);
                            await SpawnAndScheduleMergeAsync(dropped, body, ct);
                        }
                    }
                }
            }

            await Sender.SendAsync(new AcknowledgeBlockChangePacket { SequenceId = packet.Sequence }, ct);
        }

        /// <summary>
        /// Handles the Q-key drop action. Drops one item (or the whole held stack if
        /// <paramref name="dropStack"/> is true) from the player's held slot as a
        /// world entity, then syncs the inventory slot back to the client.
        /// </summary>
        private async Task HandleDropItemAsync(bool dropStack, CancellationToken ct)
        {
            var heldSlotIndex = 36 + ctx.HeldSlot;
            var held = ctx.Inventory[heldSlotIndex]; // value copy — safe across awaits
            if (held.IsEmpty) return;

            var dropCount = dropStack ? held.Count : 1;

            var (vx, vy, vz) = ThrowVelocity(ctx.Yaw, ctx.Pitch);

            var spawnX = ctx.X - Math.Sin(ctx.Yaw * Math.PI / 180.0) * 0.5;
            var spawnZ = ctx.Z + Math.Cos(ctx.Yaw * Math.PI / 180.0) * 0.5;
            // Spawn at eye level — vanilla item throw origin is feet + 1.62.
            var spawnY = ctx.Y + 1.62;

            var dropped = entities.SpawnItem(held.ItemId, dropCount,
                spawnX, spawnY, spawnZ, vx, vy, vz);

            var body = physics.RegisterItem(dropped);
            await SpawnAndScheduleMergeAsync(dropped, body, ct);

            var remaining = held.Count - dropCount;
            ctx.Inventory[heldSlotIndex] = remaining > 0
                ? new InventorySlot(held.ItemId, remaining, held.Durability)
                : InventorySlot.Empty;

            await Sender.SendAsync(new SetContainerSlotPacket
            {
                WindowId = 0,
                StateId = ctx.NextStateId(),
                Slot = (short)heldSlotIndex,
                ItemId = ctx.Inventory[heldSlotIndex].ItemId,
                Count = ctx.Inventory[heldSlotIndex].Count,
                Damage = SlotDamage(ctx.Inventory[heldSlotIndex]),
            }, ct);
        }

        /// <summary>
        /// Applies one use of damage to the held tool when it has broken a block.
        /// Removes the tool from inventory and syncs to client if durability reaches zero.
        /// Only damages when the tool is the correct type for the block.
        /// </summary>
        /// <summary>
        /// Applies durability damage to the held tool when a block is broken.
        /// Vanilla rules:
        /// <list type="bullet">
        ///   <item>Correct tool for the block: 1 damage.</item>
        ///   <item>Wrong tool type (e.g. pickaxe on dirt): 2 damage.</item>
        ///   <item>Non-tool items or bare hand: no damage.</item>
        /// </list>
        /// When durability reaches zero the item is destroyed and the slot synced.
        /// </summary>
        private void DamageHeldTool(ushort blockStateId, CancellationToken ct)
        {
            ref var held = ref ctx.HeldItem;
            if (held.IsEmpty) return;

            var toolInfo = ToolSpeedTable.Get(held.ItemId);
            if (!toolInfo.HasValue) return; // non-tool item — no durability damage

            // If max durability is 0 the item was not initialised with durability
            // (e.g. given via a path that doesn't call ToolSpeedTable). Treat as
            // if it has full durability so we don't silently ignore the damage.
            var currentDurability = held.Durability > 0
                ? held.Durability
                : toolInfo.Value.Durability;

            if (currentDurability <= 0) return; // truly indestructible (shouldn't happen)

            // Correct tool = 1 damage; wrong tool = 2 damage.
            var blockName = RegistryLookup.GetBlockName(blockStateId);
            var bestTool = blockName.IsEmpty
                ? ToolKind.None
                : BlockToolAffinity.GetBestTool(blockName.Span);

            var damage = (bestTool == toolInfo.Value.Kind) ? 1 : 2;
            var newDurability = currentDurability - damage;

            var heldSlotIndex = (short)(36 + ctx.HeldSlot);

            if (newDurability <= 0)
            {
                held = InventorySlot.Empty;
                _ = Sender.SendAsync(new SetContainerSlotPacket
                {
                    WindowId = 0,
                    StateId = ctx.NextStateId(),
                    Slot = heldSlotIndex,
                    ItemId = 0,
                    Count = 0,
                }, ct);
            }
            else
            {
                held = new InventorySlot(held.ItemId, held.Count, newDurability);
                _ = Sender.SendAsync(new SetContainerSlotPacket
                {
                    WindowId = 0,
                    StateId = ctx.NextStateId(),
                    Slot = heldSlotIndex,
                    ItemId = held.ItemId,
                    Count = held.Count,
                    Damage = SlotDamage(held),
                }, ct);
            }
        }

        // ─── Entity spawn helpers ─────────────────────────────────────────────────

        private static async ValueTask SendSpawnPlayerAsync(
            IPacketSender target, ConnectedPlayer player, CancellationToken ct)
        {
            await target.SendAsync(new BundleDelimiterPacket(), ct);
            await target.SendAsync(new SpawnEntityPacket
            {
                EntityId = player.EntityId,
                EntityUuid = player.Uuid,
                EntityType = EntityTypeIds.Player,
                X = player.Context.X,
                Y = player.Context.Y,
                Z = player.Context.Z,
                Yaw = player.Context.Yaw,
                Pitch = player.Context.Pitch,
                HeadYaw = player.Context.Yaw,
            }, ct);
            await target.SendAsync(new SetHeadRotationPacket
            {
                EntityId = player.EntityId,
                HeadYaw = player.Context.Yaw,
            }, ct);
            await target.SendAsync(new BundleDelimiterPacket(), ct);
        }

        private static async ValueTask SendSpawnItemAsync(
            IPacketSender target, ItemEntity item, CancellationToken ct)
        {
            await target.SendAsync(new BundleDelimiterPacket(), ct);
            await target.SendAsync(new SpawnEntityPacket
            {
                EntityId = item.EntityId,
                EntityUuid = item.Uuid,
                EntityType = EntityTypeIds.Item,
                X = item.X,
                Y = item.Y,
                Z = item.Z,
                VelocityX = item.VelocityX,
                VelocityY = item.VelocityY,
                VelocityZ = item.VelocityZ,
                Yaw = 0,
                Pitch = 0,
                HeadYaw = 0,
            }, ct);
            await target.SendAsync(new SetEntityMetadataPacket
            {
                EntityId = item.EntityId,
                ItemId = item.ItemId,
                Count = item.Count,
            }, ct);
            await target.SendAsync(new BundleDelimiterPacket(), ct);
        }

        // ─── Entity spawn / drop helpers ─────────────────────────────────────────

        /// <summary>
        /// Broadcasts spawn of a new item entity and fires off a background task that
        /// will attempt a merge once the item settles on a surface.
        /// </summary>
        private async Task SpawnAndScheduleMergeAsync(ItemEntity dropped,
            ItemPhysicsBody body, CancellationToken ct)
        {
            await BroadcastSpawnItemAsync(dropped, ct);
            _ = TryMergeOnSettleAsync(dropped, body, ct);
        }

        /// <summary>
        /// Awaits <paramref name="body"/>'s settle signal, then looks for a neighbour
        /// item of the same type within 0.5 blocks. If found, merges them into one entity.
        /// Runs as fire-and-forget from <see cref="SpawnAndScheduleMergeAsync"/>.
        /// </summary>
        private async Task TryMergeOnSettleAsync(ItemEntity dropped,
            ItemPhysicsBody body, CancellationToken ct)
        {
            try
            {
                // Wait for physics to settle OR for the item to be picked up / removed
                // (OnRemoved completes the task either way so this never leaks).
                await body.SettledTask.WaitAsync(ct);
            }
            catch (OperationCanceledException) { return; }

            // Item may have been picked up while falling — verify it still exists.
            if (!entities.TryRemove(dropped.EntityId, out _)) return;

            const double MergeRadius = 0.5;
            var neighbour = entities.FindMergeable(
                dropped.ItemId, dropped.X, dropped.Z, MergeRadius, dropped.EntityId);

            if (neighbour != null && entities.TryRemove(neighbour.EntityId, out _))
            {
                physics.Unregister(neighbour.EntityId);

                await registry.BroadcastRawAsync(new RemoveEntitiesPacket
                {
                    EntityIds = [neighbour.EntityId]
                }, Guid.Empty, ct);

                // Also remove the dropped entity visually — we'll replace with merged.
                await registry.BroadcastRawAsync(new RemoveEntitiesPacket
                {
                    EntityIds = [dropped.EntityId]
                }, Guid.Empty, ct);

                var merged = entities.SpawnItem(
                    dropped.ItemId,
                    neighbour.Count + dropped.Count,
                    neighbour.X, neighbour.Y, neighbour.Z);

                // Merged item is already at rest — no physics needed.
                await BroadcastSpawnItemAsync(merged, ct);
            }
            else
            {
                // No neighbour — re-register the item (we removed it above) so pickup
                // detection keeps working, and broadcast removal+respawn at settled pos.
                entities.ReAdd(dropped);

                // Broadcast a teleport to snap the client-side entity to the final
                // physics-settled position (the client may still show it mid-flight).
                await registry.BroadcastRawAsync(new TeleportEntityPacket
                {
                    EntityId = dropped.EntityId,
                    X = dropped.X,
                    Y = dropped.Y,
                    Z = dropped.Z,
                    Yaw = 0,
                    Pitch = 0,
                }, Guid.Empty, ct);
            }
        }

        private async Task BroadcastSpawnItemAsync(ItemEntity item, CancellationToken ct)
        {
            foreach (var player in registry.GetAll())
            {
                try { await SendSpawnItemAsync(player.Sender, item, ct); }
                catch { /* disconnected mid-broadcast */ }
            }
        }

        // ─── Container ────────────────────────────────────────────────────────────

        private async ValueTask HandleClickContainerAsync(ReadOnlySequence<byte> payload, CancellationToken ct)
        {
            var reader = new SequenceReader<byte>(payload);
            var packet = new ClickContainerPacket();
            if (!packet.TryRead(ref reader)) return;
            if (packet.WindowId != 0) return;

            // ── Outside-window click: drop cursor item into the world ────────────
            // Mode 0, slot -999: left click drops entire cursor stack.
            // Mode 0, slot -999, button 1: right click drops one item.
            if (packet.Mode == 0 && packet.Slot < 0 && !ctx.CursorItem.IsEmpty)
            {
                var dropAll = packet.Button == 0;
                var dropCount = dropAll ? ctx.CursorItem.Count : 1;

                var (vx, vy, vz) = ThrowVelocity(ctx.Yaw, ctx.Pitch);
                var spawnX = ctx.X - Math.Sin(ctx.Yaw * Math.PI / 180.0) * 0.5;
                var spawnZ = ctx.Z + Math.Cos(ctx.Yaw * Math.PI / 180.0) * 0.5;

                var dropped = entities.SpawnItem(ctx.CursorItem.ItemId, dropCount,
                    spawnX, ctx.Y + 1.62, spawnZ, vx, vy, vz);

                var body = physics.RegisterItem(dropped);
                await SpawnAndScheduleMergeAsync(dropped, body, ct);

                var remaining = ctx.CursorItem.Count - dropCount;
                ctx.CursorItem = remaining > 0
                    ? new InventorySlot(ctx.CursorItem.ItemId, remaining, ctx.CursorItem.Durability)
                    : InventorySlot.Empty;

                await SendInventorySync(ct);
                return;
            }

            switch (packet.Mode)
            {
                case 0: ApplyNormalClick(packet.Slot, packet.Button); break;
                case 1: ApplyShiftClick(packet.Slot); break;
                case 2: ApplyHotbarSwap(packet.Slot, packet.Button); break;
                case 5: ApplyDrag(packet.Slot, packet.Button); break;
                case 6: ApplyDoubleClick(); break;
            }

            await SendInventorySync(ct);
        }

        private void ApplyDoubleClick()
        {
            if (ctx.CursorItem.IsEmpty) return;
            var max = ItemStackTable.GetMaxStack(ctx.CursorItem.ItemId);
            if (ctx.CursorItem.Count >= max) return;

            Sweep(fullStacksOnly: false);
            if (ctx.CursorItem.Count < max) Sweep(fullStacksOnly: true);
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
                    ctx.CursorItem = new InventorySlot(ctx.CursorItem.ItemId, ctx.CursorItem.Count + take, ctx.CursorItem.Durability);
                    var left = inv.Count - take;
                    inv = left > 0 ? new InventorySlot(inv.ItemId, left, inv.Durability) : InventorySlot.Empty;
                }
            }
        }

        private ValueTask SendInventorySync(CancellationToken ct)
        {
            var pool = ArrayPool<(int ItemId, int Count, int Damage)>.Shared;
            var slots = pool.Rent(InventorySlot.InventorySize);
            try
            {
                for (var i = 0; i < InventorySlot.InventorySize; i++)
                    slots[i] = (ctx.Inventory[i].ItemId, ctx.Inventory[i].Count, SlotDamage(ctx.Inventory[i]));
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
            finally { pool.Return(slots); }
        }

        private void HandleCloseContainer()
        {
            if (ctx.CursorItem.IsEmpty) return;
            for (var i = 9; i <= 44; i++)
            {
                if (!ctx.Inventory[i].IsEmpty) continue;
                ctx.Inventory[i] = ctx.CursorItem;
                ctx.CursorItem = InventorySlot.Empty;
                return;
            }
            ctx.CursorItem = InventorySlot.Empty;
        }

        private void ApplyNormalClick(short slot, byte button)
        {
            if (slot < 0)
            {
                if (button == 0) ctx.CursorItem = InventorySlot.Empty;
                else if (!ctx.CursorItem.IsEmpty)
                    ctx.CursorItem = ctx.CursorItem.Count > 1
                        ? new InventorySlot(ctx.CursorItem.ItemId, ctx.CursorItem.Count - 1, ctx.CursorItem.Durability)
                        : InventorySlot.Empty;
                return;
            }
            if (slot >= InventorySlot.InventorySize) return;
            ref var inv = ref ctx.Inventory[slot];
            var max = ItemStackTable.GetMaxStack(ctx.CursorItem.IsEmpty ? inv.ItemId : ctx.CursorItem.ItemId);
            switch (button)
            {
                case 0:
                    if (ctx.CursorItem.IsEmpty) { ctx.CursorItem = inv; inv = InventorySlot.Empty; }
                    else if (inv.IsEmpty) { inv = ctx.CursorItem; ctx.CursorItem = InventorySlot.Empty; }
                    else if (ctx.CursorItem.ItemId == inv.ItemId)
                    {
                        var total = ctx.CursorItem.Count + inv.Count;
                        inv = new InventorySlot(inv.ItemId, Math.Min(total, max), inv.Durability);
                        ctx.CursorItem = total > max
                            ? new InventorySlot(ctx.CursorItem.ItemId, total - max, ctx.CursorItem.Durability)
                            : InventorySlot.Empty;
                    }
                    else (ctx.CursorItem, inv) = (inv, ctx.CursorItem);
                    break;
                case 1:
                    switch (ctx.CursorItem, inv)
                    {
                        case ({ IsEmpty: true }, { IsEmpty: false }):
                            var take = (inv.Count + 1) / 2; var leave = inv.Count - take;
                            ctx.CursorItem = new InventorySlot(inv.ItemId, take, inv.Durability);
                            inv = leave > 0 ? new InventorySlot(inv.ItemId, leave, inv.Durability) : InventorySlot.Empty;
                            break;
                        case ({ IsEmpty: false }, { IsEmpty: true }):
                            inv = new InventorySlot(ctx.CursorItem.ItemId, 1, ctx.CursorItem.Durability);
                            ctx.CursorItem = ctx.CursorItem.Count > 1
                                ? new InventorySlot(ctx.CursorItem.ItemId, ctx.CursorItem.Count - 1, ctx.CursorItem.Durability)
                                : InventorySlot.Empty;
                            break;
                        case ({ IsEmpty: false }, _) when ctx.CursorItem.ItemId == inv.ItemId:
                            if (inv.Count < max)
                            {
                                inv = new InventorySlot(inv.ItemId, inv.Count + 1, inv.Durability);
                                ctx.CursorItem = ctx.CursorItem.Count > 1
                                    ? new InventorySlot(ctx.CursorItem.ItemId, ctx.CursorItem.Count - 1, ctx.CursorItem.Durability)
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
            var max = ItemStackTable.GetMaxStack(src.ItemId);
            var (destStart, destEnd) = slot switch
            {
                >= 36 and <= 44 => (9, 35),
                >= 9 and <= 35 => (36, 44),
                _ => (9, 44),
            };
            for (var i = destStart; i <= destEnd && !src.IsEmpty; i++)
            {
                ref var dest = ref ctx.Inventory[i];
                if (dest.IsEmpty || dest.ItemId != src.ItemId || dest.Count >= max) continue;
                var transfer = Math.Min(max - dest.Count, src.Count);
                dest = new InventorySlot(dest.ItemId, dest.Count + transfer, dest.Durability);
                var remaining = src.Count - transfer;
                src = remaining > 0 ? new InventorySlot(src.ItemId, remaining, src.Durability) : InventorySlot.Empty;
            }
            for (var i = destStart; i <= destEnd && !src.IsEmpty; i++)
            {
                ref var dest = ref ctx.Inventory[i];
                if (!dest.IsEmpty) continue;
                dest = src; src = InventorySlot.Empty;
            }
        }

        private void ApplyHotbarSwap(short slot, byte button)
        {
            if (slot is < 0 or >= InventorySlot.InventorySize) return;
            if (button > 8) return;
            var hotbarSlot = 36 + button;
            (ctx.Inventory[slot], ctx.Inventory[hotbarSlot]) = (ctx.Inventory[hotbarSlot], ctx.Inventory[slot]);
        }

        /// <summary>
        /// Inventory Drag/Paint<br/>
        /// 0 = Start-left, 1 = add-left, 2 = end-left,
        /// 4 = start-right, 5 = add-right, 6 = end-right
        /// </summary>
        private void ApplyDrag(short slot, byte button)
        {
            switch (button)
            {
                case 0:
                case 4:
                    ctx.DragButton = button == 0 ? 0 : 1;
                    ctx.DragSlots.Clear();
                    break;
                case 1:
                case 5:
                    if (ctx.DragButton < 0) return;
                    if (slot is >= 0 and < InventorySlot.InventorySize) ctx.DragSlots.Add(slot);
                    break;
                case 2:
                    {
                        if (ctx.DragButton != 0 || ctx.DragSlots.Count == 0 || ctx.CursorItem.IsEmpty) break;
                        var dragMax = ItemStackTable.GetMaxStack(ctx.CursorItem.ItemId);
                        Span<int> targets = stackalloc int[ctx.DragSlots.Count];
                        var count = 0;
                        foreach (var s in ctx.DragSlots)
                        {
                            ref var inventorySlot = ref ctx.Inventory[s];
                            if (inventorySlot.IsEmpty || inventorySlot.ItemId == ctx.CursorItem.ItemId) targets[count++] = s;
                        }
                        targets = targets[..count]; targets.Sort();
                        if (targets.Length == 0) break;
                        var perSlot = ctx.CursorItem.Count / targets.Length;
                        if (perSlot < 1) break;
                        var remaining = ctx.CursorItem.Count;
                        foreach (var s in targets)
                        {
                            ref var inv = ref ctx.Inventory[s];
                            var current = inv.IsEmpty ? 0 : inv.Count;
                            var canAdd = Math.Min(dragMax - current, perSlot);
                            if (canAdd <= 0) continue;
                            inv = new InventorySlot(ctx.CursorItem.ItemId, current + canAdd, ctx.CursorItem.Durability);
                            remaining -= canAdd;
                        }
                        ctx.CursorItem = remaining > 0
                            ? new InventorySlot(ctx.CursorItem.ItemId, remaining, ctx.CursorItem.Durability)
                            : InventorySlot.Empty;
                        ctx.DragButton = -1; ctx.DragSlots.Clear();
                        break;
                    }
                case 6:
                    {
                        if (ctx.DragButton != 1 || ctx.DragSlots.Count == 0 || ctx.CursorItem.IsEmpty) break;
                        var rdragMax = ItemStackTable.GetMaxStack(ctx.CursorItem.ItemId);
                        Span<int> slots = stackalloc int[ctx.DragSlots.Count];
                        var slotCount = 0;
                        foreach (var s in ctx.DragSlots) slots[slotCount++] = s;
                        slots = slots[..slotCount]; slots.Sort();
                        var remaining = ctx.CursorItem.Count;
                        for (var i = 0; i < slots.Length && remaining > 0; i++)
                        {
                            var s = slots[i];
                            ref var inv = ref ctx.Inventory[s];
                            if (!inv.IsEmpty && inv.ItemId != ctx.CursorItem.ItemId) continue;
                            if (!inv.IsEmpty && inv.Count >= rdragMax) continue;
                            var current = inv.IsEmpty ? 0 : inv.Count;
                            inv = new InventorySlot(ctx.CursorItem.ItemId, current + 1, ctx.CursorItem.Durability);
                            remaining--;
                        }
                        ctx.CursorItem = remaining > 0
                            ? new InventorySlot(ctx.CursorItem.ItemId, remaining, ctx.CursorItem.Durability)
                            : InventorySlot.Empty;
                        ctx.DragButton = -1; ctx.DragSlots.Clear();
                        break;
                    }
            }
        }

        // ─── Misc handlers ────────────────────────────────────────────────────────

        private void HandleSetHeldItem(ReadOnlySequence<byte> payload)
        {
            var reader = new SequenceReader<byte>(payload);
            var packet = new SetHeldItemPacket();
            if (!packet.TryRead(ref reader)) return;
            ctx.HeldSlot = Math.Clamp(packet.Slot, (short)0, (short)8);
        }

        private async ValueTask HandleUseItemOnAsync(ReadOnlySequence<byte> payload, CancellationToken ct)
        {
            var reader = new SequenceReader<byte>(payload);
            var packet = new UseItemOnPacket();
            if (!packet.TryRead(ref reader)) return;

            await Sender.SendAsync(new AcknowledgeBlockChangePacket { SequenceId = packet.Sequence }, ct);

            var held = ctx.HeldItem;
            if (held.IsEmpty) return;

            var blockStateId = BlockStatePlacer.Resolve(held.ItemId, packet.Face, ctx.Yaw, packet.CursorY);
            if (blockStateId == 0) return;
            if (packet.Face < 0 || packet.Face >= FaceOffsets.Length) return;

            var (dx, dy, dz) = FaceOffsets[packet.Face];
            var placePos = new BlockPosition(packet.Location.X + dx, packet.Location.Y + dy, packet.Location.Z + dz);

            var playerBlockX = (int)Math.Floor(ctx.X);
            var playerBlockY = (int)Math.Floor(ctx.Y);
            var playerBlockZ = (int)Math.Floor(ctx.Z);

            if (placePos.X == playerBlockX &&
                (placePos.Y == playerBlockY || placePos.Y == playerBlockY + 1) &&
                placePos.Z == playerBlockZ) return;

            var column = world.GetChunk(
                (int)Math.Floor((double)placePos.X / 16),
                (int)Math.Floor((double)placePos.Z / 16));
            column.SetBlock(placePos.X, placePos.Y, placePos.Z, new BlockState(blockStateId));

            ctx.HeldItem = ctx.HeldItem.Count > 1
                ? new InventorySlot(ctx.HeldItem.ItemId, ctx.HeldItem.Count - 1, ctx.HeldItem.Durability)
                : InventorySlot.Empty;

            await registry.BroadcastRawAsync(new BlockUpdatePacket
            {
                Location = placePos,
                BlockState = blockStateId,
            }, Guid.Empty, ct);
        }

        private static readonly (int dx, int dy, int dz)[] FaceOffsets =
        [
            (  0, -1,  0 ),  // 0  -Y  bottom
            (  0, +1,  0 ),  // 1  +Y  top
            (  0,  0, -1 ),  // 2  -Z  north
            (  0,  0, +1 ),  // 3  +Z  south
            ( -1,  0,  0 ),  // 4  -X  west
            ( +1,  0,  0 ),  // 5  +X  east
        ];

        private async ValueTask HandleChatAsync(ReadOnlySequence<byte> payload, CancellationToken ct)
        {
            var reader = new SequenceReader<byte>(payload);
            var packet = new ChatMessagePacket();
            if (!packet.TryRead(ref reader)) return;

            var msgSpan = packet.Message.Span;
            var start = 0;
            while (start < msgSpan.Length && msgSpan[start] <= 32) start++;
            var end = msgSpan.Length;
            while (end > start && msgSpan[end - 1] <= 32) end--;
            if (end <= start) return;

            var message = packet.Message[start..(end - start)];
            if (await commands.TryDispatchAsync(message, Respond, Sender, ct)) return;

            if (message.Span[0] == (byte)'/')
            {
                await Respond(ConcatBytes("§cUnknown command: "u8, message.Span, MsgUnknownCommandSuffix));
                return;
            }

            var chatLine = ConcatBytes("\u00a77<\u00a7f"u8, ctx.Username.Span, "\u00a77> "u8, message.Span);
            await registry.BroadcastRawAsync(new SystemChatMessagePacket { Content = chatLine }, Guid.Empty, ct);
            return;

            Task Respond(ReadOnlyMemory<byte> text)
                => Sender.SendAsync(new SystemChatMessagePacket { Content = text }, ct).AsTask();
        }

        private static readonly byte[] MsgUnknownCommandSuffix = "§f. Try /help."u8.ToArray();

        private async ValueTask HandleChatCommandAsync(ReadOnlySequence<byte> payload, CancellationToken ct)
        {
            var reader = new SequenceReader<byte>(payload);
            var packet = new ChatCommandPacket();
            if (!packet.TryRead(ref reader)) return;

            var cmd = packet.Command;
            var withSlash = new byte[1 + cmd.Length];
            withSlash[0] = (byte)'/';
            cmd.Span.CopyTo(withSlash.AsSpan(1));
            var withSlashMem = withSlash.AsMemory();

            if (await _localCommands.TryDispatchAsync(withSlashMem, Respond, Sender, ct)) return;
            if (await commands.TryDispatchAsync(withSlashMem, Respond, Sender, ct)) return;
            await Respond(ConcatBytes("§cUnknown command: "u8, withSlash, MsgUnknownCommandSuffix));
            return;

            Task Respond(ReadOnlyMemory<byte> text)
                => Sender.SendAsync(new SystemChatMessagePacket { Content = text }, ct).AsTask();
        }

        // ─── TAB list ─────────────────────────────────────────────────────────────

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
            return new PlayerInfoUpdatePacket { Players = entries };
        }

        // ─── Keep-alive ───────────────────────────────────────────────────────────

        private async Task KeepAliveLoopAsync(CancellationToken ct)
        {
            try
            {
                while (!ct.IsCancellationRequested)
                {
                    await Task.Delay(KeepAliveInterval, ct);
                    _lastKeepAliveId = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
                    _pendingKeepAliveIds.Add(_lastKeepAliveId);
                    await Sender.SendAsync(new ClientboundKeepAlivePacket { KeepAliveId = _lastKeepAliveId }, ct);
                }
            }
            catch (OperationCanceledException) { }
        }

        private void HandleKeepAlive(ReadOnlySequence<byte> payload)
        {
            var reader = new SequenceReader<byte>(payload);
            var packet = new ServerboundKeepAlivePacket();
            if (!packet.TryRead(ref reader)) return;

            if (!_pendingKeepAliveIds.Remove(packet.KeepAliveId))
                Console.WriteLine($"[PlayHandler] keep-alive mismatch — unknown ID {packet.KeepAliveId}");
        }

        private static void HandleChunkBatchReceived(ReadOnlySequence<byte> payload)
        {
            var reader = new SequenceReader<byte>(payload);
            var packet = new ChunkBatchReceivedPacket();
            if (packet.TryRead(ref reader))
                Console.WriteLine($"[PlayHandler] client wants {packet.DesiredChunksPerTick:F2} chunks/tick");
        }

        // ─── Utilities ────────────────────────────────────────────────────────────

        private static int WorldToChunk(double worldCoord) => (int)Math.Floor(worldCoord) >> 4;

        /// <summary>
        /// Converts an <see cref="InventorySlot"/> to the <c>minecraft:damage</c>
        /// component value the client expects: damage = maxDurability - remaining.
        /// Returns 0 (pristine) for non-tool items or uninitialised slots.
        /// </summary>
        private static int SlotDamage(InventorySlot slot)
        {
            if (slot.IsEmpty || slot.Durability <= 0) return 0;
            var info = ToolSpeedTable.Get(slot.ItemId);
            if (!info.HasValue || info.Value.Durability <= 0) return 0;
            return info.Value.Durability - slot.Durability;
        }

        /// <summary>
        /// Computes the initial velocity for a player-thrown item.
        /// <para>
        /// Projects forward along the look vector at 0.3 blocks/tick (vanilla throw
        /// speed), then adds ±0.02 random jitter per axis so consecutive drops don't
        /// stack into a single entity before the cooldown expires.
        /// </para>
        /// <para>
        /// Minecraft yaw convention: 0 = south (+Z), 90 = west (−X), 180 = north (−Z),
        /// 270 = east (+X). Pitch: 0 = horizontal, −90 = straight up, +90 = straight down.
        /// Forward vector: dx = −sin(yaw)·cos(pitch), dy = −sin(pitch), dz = cos(yaw)·cos(pitch).
        /// </para>
        /// </summary>
        private static (double vx, double vy, double vz) ThrowVelocity(float yawDeg, float pitchDeg)
        {
            // Vanilla single-item throw: 0.4 blocks/tick along the look vector.
            const double Speed = 0.4;
            const double Jitter = 0.02;

            var yaw = yawDeg * Math.PI / 180.0;
            var pitch = pitchDeg * Math.PI / 180.0;

            var cosPitch = Math.Cos(pitch);
            var dx = -Math.Sin(yaw) * cosPitch;
            var dy = -Math.Sin(pitch);
            var dz = Math.Cos(yaw) * cosPitch;

            var jx = (Random.Shared.NextDouble() - 0.5) * Jitter;
            var jy = (Random.Shared.NextDouble() - 0.5) * Jitter;
            var jz = (Random.Shared.NextDouble() - 0.5) * Jitter;

            return (dx * Speed + jx, dy * Speed + jy, dz * Speed + jz);
        }

        private static byte[] ConcatBytes(ReadOnlySpan<byte> a, ReadOnlySpan<byte> b, ReadOnlySpan<byte> c)
        {
            var result = new byte[a.Length + b.Length + c.Length];
            a.CopyTo(result);
            b.CopyTo(result.AsSpan(a.Length));
            c.CopyTo(result.AsSpan(a.Length + b.Length));
            return result;
        }

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