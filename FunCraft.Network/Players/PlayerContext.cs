using FunCraft.Data.Inventory;

namespace FunCraft.Network.Players
{
    public sealed class PlayerContext
    {
        public string Username { get; set; } = string.Empty;
        public Guid Uuid { get; set; }
        public string IpAddress { get; set; } = string.Empty;

        public double X { get; set; } = 0.5;
        public double Y { get; set; } = 65.0;
        public double Z { get; set; } = 0.5;
        public float Yaw { get; set; }
        public float Pitch { get; set; }

        /// <summary>Currently selected hotbar slot (0–8).</summary>
        public int HeldSlot { get; set; } = 0;

        /// <summary>
        /// Full window-0 inventory (46 slots).
        /// Wire-slot layout:
        ///   0        crafting output
        ///   1–4      crafting grid
        ///   5–8      armour
        ///   9–35     main inventory
        ///   36–44    hotbar (HeldSlot 0–8)
        ///   45       off-hand
        /// </summary>
        public InventorySlot[] Inventory { get; } = new InventorySlot[InventorySlot.InventorySize];

        /// <summary>Convenience: the item currently held in-hand.</summary>
        public ref InventorySlot HeldItem => ref Inventory[36 + HeldSlot];

        /// <summary>GetItemId attached to the player's cursor during inventory interactions.</summary>
        public InventorySlot CursorItem { get; set; } = InventorySlot.Empty;

        // ── Drag / paint state (mode 5) ──────────────────────────────────────────
        // -1 = not dragging; 0 = left-drag (distribute evenly); 1 = right-drag (place one each)
        public int DragButton { get; set; } = -1;
        public HashSet<int> DragSlots { get; } = [];

        // ── state-ID counter for container sync ─────────────────────────────
        private int _stateId;
        public int NextStateId() => ++_stateId;
        public int CurrentStateId => _stateId;
    }
}