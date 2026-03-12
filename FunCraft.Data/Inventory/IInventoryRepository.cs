namespace FunCraft.Data.Inventory
{
    public interface IInventoryRepository
    {
        /// <summary>
        /// Fills <paramref name="destination"/> (must be at least <see cref="HotbarSlot.InventorySize"/>
        /// elements) with the saved inventory for the player.
        /// Returns <c>true</c> if the player had saved data; <c>false</c> if they are new
        /// (destination is left untouched in that case).
        /// </summary>
        /// <remarks>
        /// The caller is responsible for renting/returning the buffer, e.g. via
        /// <c>ArrayPool&lt;HotbarSlot&gt;.Shared</c>.
        /// </remarks>
        ValueTask<bool> TryGetInventoryAsync(Guid uuid, Memory<HotbarSlot> destination, CancellationToken ct = default);

        /// <summary>
        /// Persists all <see cref="HotbarSlot.InventorySize"/> slots from
        /// <paramref name="inventory"/>. Empty slots are deleted.
        /// </summary>
        ValueTask SaveInventoryAsync(Guid uuid, ReadOnlyMemory<HotbarSlot> inventory, CancellationToken ct = default);

        /// <summary>
        /// Gets inventory item from given slot.
        /// </summary>
        ValueTask<HotbarSlot> GetItem(Guid uuid, Memory<HotbarSlot> inventory,
            int slot, CancellationToken ct = default);
    }
}