namespace FunCraft.Network.Handlers
{
    using Data.Inventory;
    using Protocol.Packets.Play.Outgoing;
    using Protocol.Registry;

    internal sealed partial class PlayHandler
    {
        private async Task CheckPickupsAsync(CancellationToken ct)
        {
            var now = Environment.TickCount64;
            var nearby = entities.FindPickups(ctx.X, ctx.Y, ctx.Z, PickupRadius);

            if (nearby.Count == 0)
            {
                return;
            }

            foreach (var item in nearby)
            {
                // Enforce spawn cooldown — skip items that just appeared.
                // SpawnedAtMs == 0 means instant pickup (e.g. /give); otherwise enforce cooldown.
                if (item.SpawnedAtMs != 0 && now - item.SpawnedAtMs < PickupCooldownMs)
                {
                    continue;
                }

                if (!entities.TryRemove(item.EntityId, out _))
                {
                    continue;
                }

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

            // Vanilla priority: hotbar partial -> main partial -> hotbar empty -> main empty.

            // Pass 1: merge into partial stacks — hotbar first, then main.

            // hotbar 36-44
            for (var i = 36; i < InventorySlot.InventorySize; i++)
            {
                ref var slot = ref ctx.Inventory[i];

                if (slot.IsEmpty || slot.ItemId != itemId || slot.Count >= max)
                {
                    continue;
                }

                slot = new InventorySlot(itemId, slot.Count + Math.Min(max - slot.Count, count), slot.Durability);
                slotIndex = i;
                return true;
            }

            // main 9-35
            for (var i = 9; i < 36; i++)
            {
                ref var slot = ref ctx.Inventory[i];

                if (slot.IsEmpty || slot.ItemId != itemId || slot.Count >= max)
                {
                    continue;
                }

                slot = new InventorySlot(itemId, slot.Count + Math.Min(max - slot.Count, count), slot.Durability);
                slotIndex = i;
                return true;
            }

            // Pass 2: first empty slot — hotbar first, then main.

            // hotbar 36-44
            for (var i = 36; i < InventorySlot.InventorySize; i++)
            {
                ref var slot = ref ctx.Inventory[i];

                if (!slot.IsEmpty)
                {
                    continue;
                }

                slot = new InventorySlot(itemId, Math.Min(count, max));
                slotIndex = i;
                return true;
            }

            // main 9-35
            for (var i = 9; i < 36; i++)
            {
                ref var slot = ref ctx.Inventory[i];

                if (!slot.IsEmpty)
                {
                    continue;
                }

                slot = new InventorySlot(itemId, Math.Min(count, max));
                slotIndex = i;
                return true;
            }

            slotIndex = -1;
            return false;
        }
    }
}