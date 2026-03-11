namespace FunCraft.Data.Inventory
{
    public interface IInventoryRepository
    {
        /// <summary>
        /// Returns the hotbar slots for the given player (indices 0–8).
        /// Empty slots have ItemId == 0. Returns null if the player has no saved inventory.
        /// </summary>
        Task<HotbarSlot[]?> GetHotbarAsync(Guid uuid, CancellationToken ct = default);

        /// <summary>Persists hotbar slots 0–8. Empty slots are deleted.</summary>
        Task SaveHotbarAsync(Guid uuid, HotbarSlot[] hotbar, CancellationToken ct = default);
    }
}