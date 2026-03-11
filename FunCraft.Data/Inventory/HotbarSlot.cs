namespace FunCraft.Data.Inventory
{
    /// <summary>One hotbar slot: an item protocol ID and a stack count.</summary>
    /// <param name="ItemId">Protocol ID of the item (0 = empty).</param>
    /// <param name="Count">Stack size (1–99). Ignored when ItemId is 0.</param>
    public readonly record struct HotbarSlot(int ItemId, int Count)
    {
        public static readonly HotbarSlot Empty = default;
        public bool IsEmpty => ItemId == 0;
    }
}