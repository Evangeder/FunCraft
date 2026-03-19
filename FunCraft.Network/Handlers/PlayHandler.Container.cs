using System.Buffers;

namespace FunCraft.Network.Handlers
{
    using Data.Inventory;
    using Protocol.Packets.Play.Incoming;
    using Protocol.Packets.Play.Outgoing;
    using Protocol.Registry;

    internal sealed partial class PlayHandler
    {
        private async ValueTask HandleClickContainerAsync(
            ReadOnlySequence<byte> payload, CancellationToken ct)
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

            // Outside-window click: drop cursor item into the world.
            // Mode 0, slot -999: left click drops entire cursor stack.
            // Mode 0, slot -999, button 1: right click drops one item.
            if (packet.Mode == 0 && packet.Slot < 0 && !ctx.CursorItem.IsEmpty)
            {
                var dropAll = packet.Button == 0;
                var dropCount = dropAll ? ctx.CursorItem.Count : 1;
                var (vx, vy, vz) = ThrowVelocity(ctx.Yaw, ctx.Pitch);

                var spawnX = ctx.X - Math.Sin(ctx.Yaw * Math.PI / 180.0) * 0.5;
                var spawnZ = ctx.Z + Math.Cos(ctx.Yaw * Math.PI / 180.0) * 0.5;

                var dropped = entities.SpawnItem(
                    ctx.CursorItem.ItemId, dropCount,
                    spawnX, ctx.Y + 1.62, spawnZ,
                    vx, vy, vz);

                var body = physics.RegisterItem(dropped);
                SpawnAndScheduleMerge(dropped, body, ct);

                var remaining = ctx.CursorItem.Count - dropCount;
                ctx.CursorItem = remaining > 0
                    ? new InventorySlot(ctx.CursorItem.ItemId, remaining, ctx.CursorItem.Durability)
                    : InventorySlot.Empty;

                await SendInventorySync(ct);
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
            }

            await SendInventorySync(ct);
        }

        private void ApplyDoubleClick()
        {
            if (ctx.CursorItem.IsEmpty)
            {
                return;
            }

            var max = ItemStackTable.GetMaxStack(ctx.CursorItem.ItemId);

            if (ctx.CursorItem.Count >= max)
            {
                return;
            }

            // First pass: collect from partial stacks only.
            SweepIntoHand(fullStacksOnly: false, max);

            // Second pass: collect from full stacks if still not complete.
            if (ctx.CursorItem.Count < max)
            {
                SweepIntoHand(fullStacksOnly: true, max);
            }
        }

        private void SweepIntoHand(bool fullStacksOnly, int max)
        {
            for (var i = 0; i < InventorySlot.InventorySize && ctx.CursorItem.Count < max; i++)
            {
                ref var inv = ref ctx.Inventory[i];

                if (inv.IsEmpty || inv.ItemId != ctx.CursorItem.ItemId)
                {
                    continue;
                }

                if (fullStacksOnly && inv.Count < max)
                {
                    continue;
                }

                if (!fullStacksOnly && inv.Count >= max)
                {
                    continue;
                }

                var take = Math.Min(max - ctx.CursorItem.Count, inv.Count);
                ctx.CursorItem = new InventorySlot(
                    ctx.CursorItem.ItemId,
                    ctx.CursorItem.Count + take,
                    ctx.CursorItem.Durability);

                var left = inv.Count - take;
                inv = left > 0
                    ? new InventorySlot(inv.ItemId, left, inv.Durability)
                    : InventorySlot.Empty;
            }
        }

        // Sends the full player inventory to the client without pooling,
        // because SetContainerContentPacket.Slots requires an array reference
        // that must outlive the SendAsync call. Pooling + ToArray was strictly
        // worse (two allocations vs one), so we allocate directly.
        private ValueTask SendInventorySync(CancellationToken ct)
        {
            var slots = new (int ItemId, int Count, int Damage)[InventorySlot.InventorySize];

            for (var i = 0; i < InventorySlot.InventorySize; i++)
            {
                slots[i] = (ctx.Inventory[i].ItemId, ctx.Inventory[i].Count, SlotDamage(ctx.Inventory[i]));
            }

            return Sender.SendAsync(new SetContainerContentPacket
            {
                WindowId = 0,
                StateId = ctx.NextStateId(),
                Slots = slots,
                CarriedItemId = ctx.CursorItem.ItemId,
                CarriedItemCount = ctx.CursorItem.Count,
            }, ct);
        }

        private void HandleCloseContainer()
        {
            if (ctx.CursorItem.IsEmpty)
            {
                return;
            }

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
                        ? new InventorySlot(
                            ctx.CursorItem.ItemId,
                            ctx.CursorItem.Count - 1,
                            ctx.CursorItem.Durability)
                        : InventorySlot.Empty;
                }

                return;
            }

            if (slot >= InventorySlot.InventorySize)
            {
                return;
            }

            ref var inv = ref ctx.Inventory[slot];
            var max = ItemStackTable.GetMaxStack(
                ctx.CursorItem.IsEmpty ? inv.ItemId : ctx.CursorItem.ItemId);

            switch (button)
            {
                case 0:
                    ApplyNormalLeftClick(ref inv, max);
                    break;
                case 1:
                    ApplyNormalRightClick(ref inv, max);
                    break;
            }
        }

        private void ApplyNormalLeftClick(ref InventorySlot inv, int max)
        {
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
                inv = new InventorySlot(inv.ItemId, Math.Min(total, max), inv.Durability);
                ctx.CursorItem = total > max
                    ? new InventorySlot(ctx.CursorItem.ItemId, total - max, ctx.CursorItem.Durability)
                    : InventorySlot.Empty;
            }
            else
            {
                (ctx.CursorItem, inv) = (inv, ctx.CursorItem);
            }
        }

        private void ApplyNormalRightClick(ref InventorySlot inv, int max)
        {
            if (ctx.CursorItem.IsEmpty && !inv.IsEmpty)
            {
                var take = (inv.Count + 1) / 2;
                var leave = inv.Count - take;
                ctx.CursorItem = new InventorySlot(inv.ItemId, take, inv.Durability);
                inv = leave > 0
                    ? new InventorySlot(inv.ItemId, leave, inv.Durability)
                    : InventorySlot.Empty;
            }
            else if (!ctx.CursorItem.IsEmpty && inv.IsEmpty)
            {
                inv = new InventorySlot(ctx.CursorItem.ItemId, 1, ctx.CursorItem.Durability);
                ctx.CursorItem = ctx.CursorItem.Count > 1
                    ? new InventorySlot(ctx.CursorItem.ItemId, ctx.CursorItem.Count - 1, ctx.CursorItem.Durability)
                    : InventorySlot.Empty;
            }
            else if (!ctx.CursorItem.IsEmpty && ctx.CursorItem.ItemId == inv.ItemId)
            {
                if (inv.Count < max)
                {
                    inv = new InventorySlot(inv.ItemId, inv.Count + 1, inv.Durability);
                    ctx.CursorItem = ctx.CursorItem.Count > 1
                        ? new InventorySlot(ctx.CursorItem.ItemId, ctx.CursorItem.Count - 1, ctx.CursorItem.Durability)
                        : InventorySlot.Empty;
                }
            }
            else if (!ctx.CursorItem.IsEmpty && !inv.IsEmpty)
            {
                (ctx.CursorItem, inv) = (inv, ctx.CursorItem);
            }
        }

        private void ApplyShiftClick(short slot)
        {
            if (slot < 0 || slot >= InventorySlot.InventorySize)
            {
                return;
            }

            ref var src = ref ctx.Inventory[slot];

            if (src.IsEmpty)
            {
                return;
            }

            var max = ItemStackTable.GetMaxStack(src.ItemId);
            var (destStart, destEnd) = slot switch
            {
                >= 36 and <= 44 => (9, 35),
                >= 9 and <= 35 => (36, 44),
                _ => (9, 44),
            };

            // Pass 1: fill partial stacks of the same item type.
            for (var i = destStart; i <= destEnd && !src.IsEmpty; i++)
            {
                ref var dest = ref ctx.Inventory[i];

                if (dest.IsEmpty || dest.ItemId != src.ItemId || dest.Count >= max)
                {
                    continue;
                }

                var transfer = Math.Min(max - dest.Count, src.Count);
                dest = new InventorySlot(dest.ItemId, dest.Count + transfer, dest.Durability);
                var remaining = src.Count - transfer;
                src = remaining > 0
                    ? new InventorySlot(src.ItemId, remaining, src.Durability)
                    : InventorySlot.Empty;
            }

            // Pass 2: fill empty slots.
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
                    if (ctx.DragButton < 0)
                    {
                        return;
                    }

                    if (slot is >= 0 and < InventorySlot.InventorySize)
                    {
                        ctx.DragSlots.Add(slot);
                    }

                    break;

                case 2:
                    ApplyLeftDragEnd();
                    break;

                case 6:
                    ApplyRightDragEnd();
                    break;
            }
        }

        private void ApplyLeftDragEnd()
        {
            if (ctx.DragButton != 0 || ctx.DragSlots.Count == 0 || ctx.CursorItem.IsEmpty)
            {
                return;
            }

            var dragMax = ItemStackTable.GetMaxStack(ctx.CursorItem.ItemId);

            // Collect valid target slots into a stackalloc'd buffer — drag slot
            // count is bounded by inventory size (46), well within stack budget.
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
                return;
            }

            var perSlot = ctx.CursorItem.Count / targets.Length;

            if (perSlot < 1)
            {
                return;
            }

            var remaining = ctx.CursorItem.Count;

            foreach (var s in targets)
            {
                ref var inv = ref ctx.Inventory[s];
                var current = inv.IsEmpty ? 0 : inv.Count;
                var canAdd = Math.Min(dragMax - current, perSlot);

                if (canAdd <= 0)
                {
                    continue;
                }

                inv = new InventorySlot(ctx.CursorItem.ItemId, current + canAdd, ctx.CursorItem.Durability);
                remaining -= canAdd;
            }

            ctx.CursorItem = remaining > 0
                ? new InventorySlot(ctx.CursorItem.ItemId, remaining, ctx.CursorItem.Durability)
                : InventorySlot.Empty;

            ctx.DragButton = -1;
            ctx.DragSlots.Clear();
        }

        private void ApplyRightDragEnd()
        {
            if (ctx.DragButton != 1 || ctx.DragSlots.Count == 0 || ctx.CursorItem.IsEmpty)
            {
                return;
            }

            var rdragMax = ItemStackTable.GetMaxStack(ctx.CursorItem.ItemId);

            // Collect and sort target slots into a stackalloc'd buffer.
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

                if (!inv.IsEmpty && inv.Count >= rdragMax)
                {
                    continue;
                }

                var current = inv.IsEmpty ? 0 : inv.Count;
                inv = new InventorySlot(ctx.CursorItem.ItemId, current + 1, ctx.CursorItem.Durability);
                remaining--;
            }

            ctx.CursorItem = remaining > 0
                ? new InventorySlot(ctx.CursorItem.ItemId, remaining, ctx.CursorItem.Durability)
                : InventorySlot.Empty;

            ctx.DragButton = -1;
            ctx.DragSlots.Clear();
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
    }
}