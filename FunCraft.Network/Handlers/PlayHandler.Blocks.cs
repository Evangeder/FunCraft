using System.Buffers;

namespace FunCraft.Network.Handlers
{
    using Data.Inventory;
    using FunCraft.Network.World;
    using FunCraft.World.Blocks;
    using Protocol.Packets.Play.Incoming;
    using Protocol.Packets.Play.Outgoing;
    using Protocol.Registry;
    using Protocol.Types;

    internal sealed partial class PlayHandler
    {
        private static readonly (int dx, int dy, int dz)[] FaceOffsets =
        [
            (  0, -1,  0 ),  // 0  -Y  bottom
            (  0, +1,  0 ),  // 1  +Y  top
            (  0,  0, -1 ),  // 2  -Z  north
            (  0,  0, +1 ),  // 3  +Z  south
            ( -1,  0,  0 ),  // 4  -X  west
            ( +1,  0,  0 ),  // 5  +X  east
        ];

        // TODO creative mode when game-mode tracking is added.
        private async ValueTask HandlePlayerActionAsync(
            ReadOnlySequence<byte> payload, CancellationToken ct)
        {
            var reader = new SequenceReader<byte>(payload);
            var packet = new PlayerActionPacket();

            if (!packet.TryRead(ref reader))
            {
                return;
            }

            // Item drop (Q key) — no AcknowledgeBlockChange for drops.
            if (packet.Status is PlayerActionPacket.ActionStatus.DropItem
                               or PlayerActionPacket.ActionStatus.DropItemStack)
            {
                await HandleDropItemAsync(
                    packet.Status == PlayerActionPacket.ActionStatus.DropItemStack, ct);
                return;
            }

            // Acknowledge the sequence IMMEDIATELY — before any world mutation or
            // broadcast. The client freezes the block in a "pending" state until this
            // arrives; delaying it behind a full N-player spawn fan-out caused the
            // visible rubber-band / lag on block break under load.
            await Sender.SendAsync(
                new AcknowledgeBlockChangePacket { SequenceId = packet.Sequence }, ct);

            // Block dig
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

                // Resolve block -> drop item via BlockDropTable, then spawn it.
                var blockName = RegistryLookup.GetBlockName(block);

                if (!blockName.IsEmpty)
                {
                    var dropName = BlockDropTable.GetDrop(blockName);

                    if (!dropName.IsEmpty)
                    {
                        var itemId = RegistryLookup.GetItemId(dropName.Span);

                        if (itemId >= 0)
                        {
                            var vx = (Random.Shared.NextDouble() - 0.5d) * 0.25d;
                            var vy = Random.Shared.NextDouble() * 0.25d;
                            var vz = (Random.Shared.NextDouble() - 0.5d) * 0.25d;

                            var dropped = entities.SpawnItem(
                                itemId, 1,
                                pos.X + 0.5, pos.Y + 0.5, pos.Z + 0.5,
                                vx, vy, vz);

                            var body = physics.RegisterItem(dropped);

                            // Fire-and-forget: broadcasting the spawn to N players does
                            // not need to block the breaking player's read loop.
                            // The acknowledge was already sent above; the client's block
                            // break is fully confirmed. The item spawn can arrive shortly
                            // after without affecting perceived responsiveness.
                            SpawnAndScheduleMerge(dropped, body, ct);
                        }
                    }
                }
            }
        }

        /// <summary>
        /// Handles the Q-key drop action. Drops one item (or the whole held stack if
        /// <paramref name="dropStack"/> is true) from the player's held slot as a
        /// world entity, then syncs the inventory slot back to the client.
        /// </summary>
        private async Task HandleDropItemAsync(bool dropStack, CancellationToken ct)
        {
            var heldSlotIndex = 36 + ctx.HeldSlot;
            var held = ctx.Inventory[heldSlotIndex];

            if (held.IsEmpty)
            {
                return;
            }

            var dropCount = dropStack ? held.Count : 1;
            var (vx, vy, vz) = ThrowVelocity(ctx.Yaw, ctx.Pitch);

            var spawnX = ctx.X - Math.Sin(ctx.Yaw * Math.PI / 180.0) * 0.5;
            var spawnZ = ctx.Z + Math.Cos(ctx.Yaw * Math.PI / 180.0) * 0.5;

            // Spawn at eye level — vanilla item throw origin is feet + 1.62.
            var spawnY = ctx.Y + 1.62;

            var dropped = entities.SpawnItem(held.ItemId, dropCount, spawnX, spawnY, spawnZ, vx, vy, vz);
            var body = physics.RegisterItem(dropped);

            // Fire-and-forget: spawn broadcast does not need to block the slot sync.
            SpawnAndScheduleMerge(dropped, body, ct);

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

            if (held.IsEmpty)
            {
                return;
            }

            var toolInfo = ToolSpeedTable.Get(held.ItemId);

            if (!toolInfo.HasValue)
            {
                return;
            }

            // If max durability is 0 the item was not initialised with durability
            // (e.g. given via a path that doesn't call ToolSpeedTable). Treat as
            // if it has full durability so we don't silently ignore the damage.
            var currentDurability = held.Durability > 0
                ? held.Durability
                : toolInfo.Value.Durability;

            if (currentDurability <= 0)
            {
                return;
            }

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

        private async ValueTask HandleUseItemOnAsync(
            ReadOnlySequence<byte> payload, CancellationToken ct)
        {
            var reader = new SequenceReader<byte>(payload);
            var packet = new UseItemOnPacket();

            if (!packet.TryRead(ref reader))
            {
                return;
            }

            await Sender.SendAsync(
                new AcknowledgeBlockChangePacket { SequenceId = packet.Sequence }, ct);

            var held = ctx.HeldItem;

            if (held.IsEmpty)
            {
                return;
            }

            var blockStateId = BlockStatePlacer.Resolve(held.ItemId, packet.Face, ctx.Yaw, packet.CursorY);

            if (blockStateId == 0)
            {
                return;
            }

            if (packet.Face < 0 || packet.Face >= FaceOffsets.Length)
            {
                return;
            }

            var (dx, dy, dz) = FaceOffsets[packet.Face];
            var placePos = new BlockPosition(
                packet.Location.X + dx,
                packet.Location.Y + dy,
                packet.Location.Z + dz);

            var playerBlockX = (int)Math.Floor(ctx.X);
            var playerBlockY = (int)Math.Floor(ctx.Y);
            var playerBlockZ = (int)Math.Floor(ctx.Z);

            if (placePos.X == playerBlockX
                && (placePos.Y == playerBlockY || placePos.Y == playerBlockY + 1)
                && placePos.Z == playerBlockZ)
            {
                return;
            }

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
    }
}