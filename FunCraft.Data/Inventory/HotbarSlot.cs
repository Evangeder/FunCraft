namespace FunCraft.Data.Inventory
{
    /// <summary>
    /// One inventory slot: an item protocol ID and a stack count.
    /// <br/>Wire slot indices for window 0 (player inventory):
    /// <br/> * 0        crafting output
    /// <br/> * 1–4      crafting grid (2×2)
    /// <br/> * 5–8      armour (helmet, chestplate, leggings, boots)
    /// <br/> * 9–35     main inventory
    /// <br/> * 36–44    hotbar (0–8)
    /// <br/> * 45       off-hand
    /// <br/>Total = 46 slots (indices 0–45).
    /// </summary>
    /// <param name="ItemId">Protocol ID of the item (0 = empty).</param>
    /// <param name="Count">Stack size (1–99). Ignored when ItemId is 0.</param>
    public readonly record struct HotbarSlot(int ItemId, int Count)
    {
        /// <summary>
        /// Total number of window-0 slots tracked server-side.
        /// </summary>
        public const int InventorySize = 46;

        public static readonly HotbarSlot Empty = default;
        public bool IsEmpty => ItemId == 0;
    }
}