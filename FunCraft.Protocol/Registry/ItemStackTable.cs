using System.Collections.Frozen;

namespace FunCraft.Protocol.Registry
{
    /// <summary>
    /// Max stack sizes for every Minecraft item.
    /// </summary>
    public static class ItemStackTable
    {
        private static readonly string[] StacksOfOne =
        [
            // Swords
            "minecraft:wooden_sword", "minecraft:stone_sword", "minecraft:iron_sword",
            "minecraft:golden_sword", "minecraft:diamond_sword", "minecraft:netherite_sword",
            // Pickaxes
            "minecraft:wooden_pickaxe", "minecraft:stone_pickaxe", "minecraft:iron_pickaxe",
            "minecraft:golden_pickaxe", "minecraft:diamond_pickaxe", "minecraft:netherite_pickaxe",
            // Axes
            "minecraft:wooden_axe", "minecraft:stone_axe", "minecraft:iron_axe",
            "minecraft:golden_axe", "minecraft:diamond_axe", "minecraft:netherite_axe",
            // Shovels
            "minecraft:wooden_shovel", "minecraft:stone_shovel", "minecraft:iron_shovel",
            "minecraft:golden_shovel", "minecraft:diamond_shovel", "minecraft:netherite_shovel",
            // Hoes
            "minecraft:wooden_hoe", "minecraft:stone_hoe", "minecraft:iron_hoe",
            "minecraft:golden_hoe", "minecraft:diamond_hoe", "minecraft:netherite_hoe",
            // Helmets
            "minecraft:leather_helmet", "minecraft:chainmail_helmet", "minecraft:iron_helmet",
            "minecraft:golden_helmet", "minecraft:diamond_helmet", "minecraft:netherite_helmet",
            "minecraft:turtle_helmet",
            // Chestplates
            "minecraft:leather_chestplate", "minecraft:chainmail_chestplate",
            "minecraft:iron_chestplate", "minecraft:golden_chestplate",
            "minecraft:diamond_chestplate", "minecraft:netherite_chestplate",
            // Leggings
            "minecraft:leather_leggings", "minecraft:chainmail_leggings",
            "minecraft:iron_leggings", "minecraft:golden_leggings",
            "minecraft:diamond_leggings", "minecraft:netherite_leggings",
            // Boots
            "minecraft:leather_boots", "minecraft:chainmail_boots", "minecraft:iron_boots",
            "minecraft:golden_boots", "minecraft:diamond_boots", "minecraft:netherite_boots",
            // Ranged / misc tools
            "minecraft:bow", "minecraft:crossbow", "minecraft:trident", "minecraft:fishing_rod",
            "minecraft:flint_and_steel", "minecraft:shears", "minecraft:shield",
            "minecraft:elytra", "minecraft:carrot_on_a_stick", "minecraft:warped_fungus_on_a_stick",
            "minecraft:brush",
            // Special single-stack items
            "minecraft:totem_of_undying", "minecraft:enchanted_book",
            "minecraft:goat_horn", "minecraft:music_disc_13", "minecraft:music_disc_cat",
            "minecraft:music_disc_blocks", "minecraft:music_disc_chirp",
            "minecraft:music_disc_far", "minecraft:music_disc_mall",
            "minecraft:music_disc_mellohi", "minecraft:music_disc_stal",
            "minecraft:music_disc_strad", "minecraft:music_disc_ward",
            "minecraft:music_disc_11", "minecraft:music_disc_wait",
            "minecraft:music_disc_otherside", "minecraft:music_disc_pigstep",
            "minecraft:music_disc_5", "minecraft:music_disc_relic",
            "minecraft:music_disc_creator", "minecraft:music_disc_creator_music_box",
            "minecraft:music_disc_precipice",
            // Buckets
            "minecraft:water_bucket", "minecraft:lava_bucket", "minecraft:milk_bucket",
            "minecraft:powder_snow_bucket", "minecraft:axolotl_bucket", "minecraft:cod_bucket",
            "minecraft:salmon_bucket", "minecraft:tropical_fish_bucket", "minecraft:pufferfish_bucket",
            "minecraft:tadpole_bucket",
            // Boats
            "minecraft:oak_boat", "minecraft:spruce_boat", "minecraft:birch_boat",
            "minecraft:jungle_boat", "minecraft:acacia_boat", "minecraft:dark_oak_boat",
            "minecraft:mangrove_boat", "minecraft:cherry_boat", "minecraft:bamboo_raft",
            "minecraft:oak_chest_boat", "minecraft:spruce_chest_boat", "minecraft:birch_chest_boat",
            "minecraft:jungle_chest_boat", "minecraft:acacia_chest_boat",
            "minecraft:dark_oak_chest_boat", "minecraft:mangrove_chest_boat",
            "minecraft:cherry_chest_boat", "minecraft:bamboo_chest_raft",
            // Misc
            "minecraft:written_book", "minecraft:bundle",
        ];
        private static readonly string[] StacksOfSixteen =
        [
            "minecraft:ender_pearl", "minecraft:snowball", "minecraft:egg",
            "minecraft:ender_eye", "minecraft:honey_bottle", "minecraft:potion",
            "minecraft:splash_potion", "minecraft:lingering_potion",
        ];

        // uint → max stack size.  Built once at class-init, read-only thereafter.
        private static readonly FrozenDictionary<uint, int> Table = Build().ToFrozenDictionary();

        /// <summary>
        /// Returns the maximum stack size for the item with the given UTF-8 name.
        /// Returns 64 for anything not in the table.
        /// </summary>
        public static int GetMaxStack(ReadOnlySpan<byte> itemName)
            => Table.GetValueOrDefault(Hash(itemName), 64);

        /// <summary>
        /// Returns the maximum stack size for the item with the given protocol item ID,
        /// resolved via <see cref="RegistryLookup.GetItemName"/>.
        /// Returns 64 for unknown IDs.
        /// </summary>
        public static int GetMaxStack(int itemId)
        {
            var name = RegistryLookup.GetItemName(itemId);
            return name.IsEmpty ? 64 : GetMaxStack(name.Span);
        }

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

        private static Dictionary<uint, int> Build()
        {
            var t = new Dictionary<uint, int>(512);

            foreach (var item in StacksOfOne)
            {
                Add(item, 1);
            }

            foreach (var item in StacksOfSixteen)
            {
                Add(item, 16);
            }

            return t;

            void Add(string name, int size)
                => t[Hash(System.Text.Encoding.UTF8.GetBytes(name))] = size;
        }
    }
}