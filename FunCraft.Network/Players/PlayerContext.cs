using FunCraft.Data.Inventory;
using System.Net;

namespace FunCraft.Network.Players
{
    public sealed class PlayerContext
    {
        public ReadOnlyMemory<byte> Username { get; set; } = ReadOnlyMemory<byte>.Empty;
        public Guid Uuid { get; set; }
        public IPAddress? IpAddress { get; set; }
        public double X { get; set; } = 0.5;
        public double Y { get; set; } = 65.0;
        public double Z { get; set; } = 0.5;
        public float Yaw { get; set; }
        public float Pitch { get; set; }
        public int HeldSlot { get; set; } = 0;
        public InventorySlot[] Inventory { get; } = new InventorySlot[InventorySlot.InventorySize];
        public ref InventorySlot HeldItem => ref Inventory[36 + HeldSlot];
        public InventorySlot CursorItem { get; set; } = InventorySlot.Empty;
        public int DragButton { get; set; } = -1;
        public HashSet<int> DragSlots { get; } = [];
        private int _stateId;
        public int NextStateId() => ++_stateId;
        public int CurrentStateId => _stateId;
    }
}