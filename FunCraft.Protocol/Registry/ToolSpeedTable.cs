using System.Collections.Frozen;

namespace FunCraft.Protocol.Registry
{
    /// <summary>
    /// Mining speed multipliers and durability for every tool.
    /// </summary>
    public static class ToolSpeedTable
    {
        /// <summary>
        /// The speed multiplier a tool applies to <em>compatible</em> blocks
        /// (those whose material matches the tool's effective set).
        /// </summary>
        public readonly struct ToolInfo(float speed, int durability, ToolKind kind)
        {
            /// <summary>
            /// Mining speed multiplier against compatible blocks.
            /// </summary>
            public readonly float Speed = speed;

            /// <summary>
            /// Total uses before the item breaks. 0 = indestructible.
            /// </summary>
            public readonly int Durability = durability;

            /// <summary>
            /// Which category of block this tool is effective against.
            /// </summary>
            public readonly ToolKind Kind = kind;
        }

        /// <summary>
        /// Returns the tool info for the given UTF-8 item name, or null if
        /// the item is not a tool (generic items deal no speed bonus).
        /// </summary>
        public static ToolInfo? Get(ReadOnlySpan<byte> itemName)
            => Table.TryGetValue(Hash(itemName), out var v) ? v : null;

        /// <summary>
        /// Returns the tool info for the given protocol item ID, or null.
        /// </summary>
        public static ToolInfo? Get(int itemId)
        {
            var name = RegistryLookup.GetItemName(itemId);
            return name.IsEmpty ? null : Get(name.Span);
        }

        private const float Wood = 2f;
        private const float Stone = 4f;
        private const float Iron = 6f;
        private const float Gold = 12f;
        private const float Diamond = 8f;
        private const float Netherite = 9f;

        private const int DurabilityWood = 59;
        private const int DurabilityStone = 131;
        private const int DurabilityIron = 250;
        private const int DurabilityGold = 32;
        private const int DurabilityDiamond = 1561;
        private const int DurabilityNetherite = 2031;

        private const int DurabilityShears = 238;

        private static readonly FrozenDictionary<uint, ToolInfo> Table = BuildTable().ToFrozenDictionary();

        private static uint Hash(ReadOnlySpan<byte> data)
        {
            const uint fnvPrime = 16777619;
            var hash = 2166136261u;

            foreach (var b in data)
            { 
                hash ^= b;
                hash *= fnvPrime;
            }

            return hash;
        }

        private static Dictionary<uint, ToolInfo> BuildTable()
        {
            var t = new Dictionary<uint, ToolInfo>(128);

            Add("minecraft:wooden_pickaxe", Wood, DurabilityWood, ToolKind.Pickaxe);
            Add("minecraft:stone_pickaxe", Stone, DurabilityStone, ToolKind.Pickaxe);
            Add("minecraft:iron_pickaxe", Iron, DurabilityIron, ToolKind.Pickaxe);
            Add("minecraft:golden_pickaxe", Gold, DurabilityGold, ToolKind.Pickaxe);
            Add("minecraft:diamond_pickaxe", Diamond, DurabilityDiamond, ToolKind.Pickaxe);
            Add("minecraft:netherite_pickaxe", Netherite, DurabilityNetherite, ToolKind.Pickaxe);

            Add("minecraft:wooden_shovel", Wood, DurabilityWood, ToolKind.Shovel);
            Add("minecraft:stone_shovel", Stone, DurabilityStone, ToolKind.Shovel);
            Add("minecraft:iron_shovel", Iron, DurabilityIron, ToolKind.Shovel);
            Add("minecraft:golden_shovel", Gold, DurabilityGold, ToolKind.Shovel);
            Add("minecraft:diamond_shovel", Diamond, DurabilityDiamond, ToolKind.Shovel);
            Add("minecraft:netherite_shovel", Netherite, DurabilityNetherite, ToolKind.Shovel);
            
            Add("minecraft:wooden_axe", Wood, DurabilityWood, ToolKind.Axe);
            Add("minecraft:stone_axe", Stone, DurabilityStone, ToolKind.Axe);
            Add("minecraft:iron_axe", Iron, DurabilityIron, ToolKind.Axe);
            Add("minecraft:golden_axe", Gold, DurabilityGold, ToolKind.Axe);
            Add("minecraft:diamond_axe", Diamond, DurabilityDiamond, ToolKind.Axe);
            Add("minecraft:netherite_axe", Netherite, DurabilityNetherite, ToolKind.Axe);
            
            Add("minecraft:wooden_hoe", Wood, DurabilityWood, ToolKind.Hoe);
            Add("minecraft:stone_hoe", Stone, DurabilityStone, ToolKind.Hoe);
            Add("minecraft:iron_hoe", Iron, DurabilityIron, ToolKind.Hoe);
            Add("minecraft:golden_hoe", Gold, DurabilityGold, ToolKind.Hoe);
            Add("minecraft:diamond_hoe", Diamond, DurabilityDiamond, ToolKind.Hoe);
            Add("minecraft:netherite_hoe", Netherite, DurabilityNetherite, ToolKind.Hoe);
            
            Add("minecraft:wooden_sword", Wood, DurabilityWood, ToolKind.Sword);
            Add("minecraft:stone_sword", Stone, DurabilityStone, ToolKind.Sword);
            Add("minecraft:iron_sword", Iron, DurabilityIron, ToolKind.Sword);
            Add("minecraft:golden_sword", Gold, DurabilityGold, ToolKind.Sword);
            Add("minecraft:diamond_sword", Diamond, DurabilityDiamond, ToolKind.Sword);
            Add("minecraft:netherite_sword", Netherite, DurabilityNetherite, ToolKind.Sword);
            
            Add("minecraft:shears", 1.5f, DurabilityShears, ToolKind.Shears);

            return t;

            void Add(string name, float speed, int dur, ToolKind kind)
                => t[Hash(System.Text.Encoding.UTF8.GetBytes(name))] =
                    new ToolInfo(speed, dur, kind);
        }
    }

    /// <summary>
    /// The broad category a tool belongs to.
    /// </summary>
    public enum ToolKind : byte
    {
        None = 0,
        Pickaxe = 1,
        Shovel = 2,
        Axe = 3,
        Hoe = 4,
        Sword = 5,
        Shears = 6,
    }
}