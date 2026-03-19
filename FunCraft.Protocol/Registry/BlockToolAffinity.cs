namespace FunCraft.Protocol.Registry
{
    /// <summary>
    /// Maps block names to the <see cref="ToolKind"/> that mines them most efficiently.
    /// </summary>
    public static class BlockToolAffinity
    {
        /// <summary>
        /// Returns the tool kind most effective against the given block name
        /// (e.g. <c>"minecraft:stone"</c>).
        /// </summary>
        public static ToolKind GetBestTool(ReadOnlySpan<byte> blockName)
        {
            var name = blockName;
            if (name.StartsWith("minecraft:"u8))
                name = name["minecraft:".Length..];

            if (name.SequenceEqual("dirt"u8) ||
                name.SequenceEqual("grass_block"u8) ||
                name.SequenceEqual("podzol"u8) ||
                name.SequenceEqual("coarse_dirt"u8) ||
                name.SequenceEqual("rooted_dirt"u8) ||
                name.SequenceEqual("mycelium"u8) ||
                name.SequenceEqual("gravel"u8) ||
                name.SequenceEqual("sand"u8) ||
                name.SequenceEqual("red_sand"u8) ||
                name.SequenceEqual("snow"u8) ||
                name.SequenceEqual("snow_block"u8) ||
                name.SequenceEqual("clay"u8) ||
                name.SequenceEqual("farmland"u8) ||
                name.SequenceEqual("dirt_path"u8) ||
                name.SequenceEqual("soul_sand"u8) ||
                name.SequenceEqual("soul_soil"u8) ||
                name.SequenceEqual("mud"u8) ||
                name.SequenceEqual("muddy_mangrove_roots"u8) ||
                name.EndsWith("_concrete_powder"u8) ||
                name.EndsWith("_sand"u8))
                return ToolKind.Shovel;

            if (name.EndsWith("_wool"u8) ||
                name.EndsWith("_leaves"u8) ||
                name.EndsWith("_carpet"u8) ||
                name.SequenceEqual("cobweb"u8))
                return ToolKind.Shears;

            if (name.EndsWith("_log"u8) ||
                name.EndsWith("_wood"u8) ||
                name.EndsWith("_planks"u8) ||
                name.EndsWith("_slab"u8) ||
                name.EndsWith("_stairs"u8) ||
                name.EndsWith("_fence"u8) ||
                name.EndsWith("_fence_gate"u8) ||
                name.EndsWith("_door"u8) ||
                name.EndsWith("_trapdoor"u8) ||
                name.EndsWith("_sign"u8) ||
                name.EndsWith("_wall_sign"u8) ||
                name.EndsWith("_hanging_sign"u8) ||
                name.EndsWith("_wall_hanging_sign"u8) ||
                name.EndsWith("_button"u8) ||
                name.EndsWith("_pressure_plate"u8) ||
                name.SequenceEqual("bookshelf"u8) ||
                name.SequenceEqual("chest"u8) ||
                name.SequenceEqual("trapped_chest"u8) ||
                name.SequenceEqual("crafting_table"u8) ||
                name.SequenceEqual("jukebox"u8) ||
                name.SequenceEqual("note_block"u8) ||
                name.SequenceEqual("pumpkin"u8) ||
                name.SequenceEqual("carved_pumpkin"u8) ||
                name.SequenceEqual("melon"u8) ||
                name.SequenceEqual("brown_mushroom_block"u8) ||
                name.SequenceEqual("red_mushroom_block"u8) ||
                name.SequenceEqual("mushroom_stem"u8) ||
                name.SequenceEqual("ladder"u8) ||
                name.SequenceEqual("barrel"u8) ||
                name.SequenceEqual("beehive"u8) ||
                name.SequenceEqual("bee_nest"u8) ||
                name.SequenceEqual("bamboo_block"u8) ||
                name.SequenceEqual("bamboo"u8) ||
                name.SequenceEqual("scaffolding"u8))
                return ToolKind.Axe;

            if (name.EndsWith("_leaves"u8) ||
                name.EndsWith("_nylium"u8) ||
                name.SequenceEqual("shroomlight"u8) ||
                name.SequenceEqual("sponge"u8) ||
                name.SequenceEqual("wet_sponge"u8) ||
                name.SequenceEqual("hay_block"u8) ||
                name.SequenceEqual("dried_kelp_block"u8) ||
                name.SequenceEqual("target"u8))
                return ToolKind.Hoe;

            if (name.SequenceEqual("stone"u8) ||
                name.SequenceEqual("cobblestone"u8) ||
                name.SequenceEqual("stone_bricks"u8) ||
                name.SequenceEqual("mossy_cobblestone"u8) ||
                name.SequenceEqual("mossy_stone_bricks"u8) ||
                name.SequenceEqual("cracked_stone_bricks"u8) ||
                name.SequenceEqual("chiseled_stone_bricks"u8) ||
                name.SequenceEqual("granite"u8) ||
                name.SequenceEqual("polished_granite"u8) ||
                name.SequenceEqual("diorite"u8) ||
                name.SequenceEqual("polished_diorite"u8) ||
                name.SequenceEqual("andesite"u8) ||
                name.SequenceEqual("polished_andesite"u8) ||
                name.SequenceEqual("deepslate"u8) ||
                name.SequenceEqual("cobbled_deepslate"u8) ||
                name.SequenceEqual("sandstone"u8) ||
                name.SequenceEqual("red_sandstone"u8) ||
                name.SequenceEqual("netherrack"u8) ||
                name.SequenceEqual("nether_bricks"u8) ||
                name.SequenceEqual("basalt"u8) ||
                name.SequenceEqual("blackstone"u8) ||
                name.SequenceEqual("bedrock"u8) ||
                name.SequenceEqual("obsidian"u8) ||
                name.SequenceEqual("crying_obsidian"u8) ||
                name.SequenceEqual("ancient_debris"u8) ||
                name.EndsWith("_ore"u8) ||
                name.EndsWith("_block"u8) && (
                    name.StartsWith("iron"u8) ||
                    name.StartsWith("gold"u8) ||
                    name.StartsWith("diamond"u8) ||
                    name.StartsWith("emerald"u8) ||
                    name.StartsWith("copper"u8) ||
                    name.StartsWith("lapis"u8) ||
                    name.StartsWith("redstone"u8) ||
                    name.StartsWith("coal"u8) ||
                    name.StartsWith("netherite"u8)) ||
                name.EndsWith("_slab"u8) && (
                    name.StartsWith("stone"u8) ||
                    name.StartsWith("cobble"u8) ||
                    name.StartsWith("brick"u8) ||
                    name.StartsWith("nether"u8) ||
                    name.StartsWith("deepslate"u8)) ||
                name.EndsWith("_stairs"u8) && (
                    name.StartsWith("stone"u8) ||
                    name.StartsWith("cobble"u8) ||
                    name.StartsWith("brick"u8) ||
                    name.StartsWith("nether"u8) ||
                    name.StartsWith("deepslate"u8)) ||
                name.EndsWith("_wall"u8) ||
                name.SequenceEqual("grindstone"u8) ||
                name.SequenceEqual("stonecutter"u8) ||
                name.SequenceEqual("blast_furnace"u8) ||
                name.SequenceEqual("furnace"u8) ||
                name.SequenceEqual("smoker"u8) ||
                name.SequenceEqual("dispenser"u8) ||
                name.SequenceEqual("dropper"u8) ||
                name.SequenceEqual("piston"u8) ||
                name.SequenceEqual("sticky_piston"u8) ||
                name.SequenceEqual("anvil"u8) ||
                name.SequenceEqual("chipped_anvil"u8) ||
                name.SequenceEqual("damaged_anvil"u8) ||
                name.SequenceEqual("brewing_stand"u8) ||
                name.SequenceEqual("cauldron"u8) ||
                name.SequenceEqual("hopper"u8) ||
                name.SequenceEqual("iron_bars"u8) ||
                name.SequenceEqual("iron_door"u8) ||
                name.SequenceEqual("iron_trapdoor"u8) ||
                name.SequenceEqual("heavy_weighted_pressure_plate"u8) ||
                name.SequenceEqual("light_weighted_pressure_plate"u8) ||
                name.SequenceEqual("chain"u8) ||
                name.SequenceEqual("lantern"u8) ||
                name.SequenceEqual("soul_lantern"u8) ||
                name.SequenceEqual("bell"u8) ||
                name.SequenceEqual("lodestone"u8) ||
                name.SequenceEqual("respawn_anchor"u8))
                return ToolKind.Pickaxe;

            return ToolKind.None;
        }
    }
}