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

        /// <summary>
        /// Currently selected hotbar slot (0–8).
        /// </summary>
        public int HeldSlot { get; set; } = 0;

        /// <summary>
        /// Server-side hotbar: item ID + count per slot (index 0–8).
        /// </summary>
        public HotbarSlot[] Hotbar { get; } = new HotbarSlot[9];
    }
}