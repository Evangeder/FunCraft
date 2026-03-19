using System.Runtime.InteropServices;

namespace FunCraft.Data.Inventory
{
    /// <summary>
    /// One inventory slot: item protocol ID, stack count, and remaining durability.
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
    /// <param name="Durability">
    /// Remaining uses. 0 means either the item is not a tool (indestructible) or it
    /// has broken and should be removed. Use <see cref="MaxDurability"/> to
    /// distinguish — if that is also 0 the item simply has no durability model.
    /// </param>
    [StructLayout(LayoutKind.Sequential, Pack = 4)]
    public readonly record struct InventorySlot(int ItemId, int Count, int Durability = 0)
    {
        /// <summary>Total number of window-0 slots tracked server-side.</summary>
        public const int InventorySize = 46;

        public static readonly InventorySlot Empty = default;
        public bool IsEmpty => ItemId == 0;
    }
}