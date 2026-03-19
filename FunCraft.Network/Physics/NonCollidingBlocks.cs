namespace FunCraft.Network.Physics
{
    /// <summary>
    /// Set of block names that exist in the world but have no full-block collision
    /// box — item entities pass through them without bouncing or stopping.
    ///
    /// <para>
    /// Keyed by FNV-1a hash of the UTF-8 block name (same algorithm as
    /// <c>RegistryLookup</c>) so <see cref="Contains"/> is a single dictionary
    /// probe with no string allocation on the 20 Hz physics path.
    /// </para>
    ///
    /// <para>
    /// The list covers every vanilla block that decompiles to a
    /// <c>VoxelShape.empty()</c> or partial-shape collision box. Full-block shapes
    /// that are logically "passable" (e.g. snow layer 1–7, slabs) are intentionally
    /// excluded because item physics still collides with them in vanilla.
    /// </para>
    /// </summary>
    public static class NonCollidingBlocks
    {
        private static readonly HashSet<uint> _hashes = Build();

        /// <summary>Returns true when the named block has no item-entity collision.</summary>
        public static bool Contains(ReadOnlySpan<byte> blockName) =>
            _hashes.Contains(Hash(blockName));

        private static uint Hash(ReadOnlySpan<byte> data)
        {
            const uint fnvPrime = 16777619;
            var hash = 2166136261u;
            foreach (var b in data) { hash ^= b; hash *= fnvPrime; }
            return hash;
        }

        private static HashSet<uint> Build()
        {
            var h = new HashSet<uint>(512);

            // Torches
            Add("minecraft:torch"); Add("minecraft:wall_torch");
            Add("minecraft:soul_torch"); Add("minecraft:soul_wall_torch");
            Add("minecraft:redstone_torch"); Add("minecraft:redstone_wall_torch");

            // Flowers / plants / saplings 
            Add("minecraft:dandelion"); Add("minecraft:poppy");
            Add("minecraft:blue_orchid"); Add("minecraft:allium");
            Add("minecraft:azure_bluet"); Add("minecraft:red_tulip");
            Add("minecraft:orange_tulip"); Add("minecraft:white_tulip");
            Add("minecraft:pink_tulip"); Add("minecraft:oxeye_daisy");
            Add("minecraft:cornflower"); Add("minecraft:lily_of_the_valley");
            Add("minecraft:wither_rose"); Add("minecraft:sunflower");
            Add("minecraft:lilac"); Add("minecraft:rose_bush");
            Add("minecraft:peony"); Add("minecraft:tall_grass");
            Add("minecraft:large_fern"); Add("minecraft:grass");
            Add("minecraft:fern"); Add("minecraft:dead_bush");
            Add("minecraft:seagrass"); Add("minecraft:tall_seagrass");
            Add("minecraft:kelp"); Add("minecraft:kelp_plant");
            Add("minecraft:bamboo_sapling");
            Add("minecraft:sweet_berry_bush"); Add("minecraft:cave_vines");
            Add("minecraft:cave_vines_plant"); Add("minecraft:spore_blossom");
            Add("minecraft:hanging_roots"); Add("minecraft:pitcher_plant");
            Add("minecraft:torchflower"); Add("minecraft:pink_petals");
            Add("minecraft:wildflowers"); Add("minecraft:leaf_litter");
            Add("minecraft:short_dry_grass"); Add("minecraft:tall_dry_grass");

            foreach (var tree in new[]
            { "oak","spruce","birch","jungle","acacia","dark_oak","mangrove","cherry",
              "pale_oak","azalea","flowering_azalea" })
            {
                Add($"minecraft:{tree}_sapling");
            }

            // Vines / weeping / twisting
            Add("minecraft:vine");
            Add("minecraft:weeping_vines"); Add("minecraft:weeping_vines_plant");
            Add("minecraft:twisting_vines"); Add("minecraft:twisting_vines_plant");
            Add("minecraft:glow_lichen"); Add("minecraft:resin_clump");

            // Mushrooms
            Add("minecraft:brown_mushroom"); Add("minecraft:red_mushroom");
            Add("minecraft:crimson_fungus"); Add("minecraft:warped_fungus");
            Add("minecraft:crimson_roots"); Add("minecraft:warped_roots");
            Add("minecraft:nether_sprouts");

            // Rails
            Add("minecraft:rail");
            Add("minecraft:powered_rail"); Add("minecraft:detector_rail");
            Add("minecraft:activator_rail");

            // Buttons
            foreach (var mat in new[]
            { "oak","spruce","birch","jungle","acacia","dark_oak","mangrove","cherry",
              "pale_oak","bamboo","crimson","warped","stone","polished_blackstone" })
            {
                Add($"minecraft:{mat}_button");
            }

            // Pressure plates
            foreach (var mat in new[]
            { "oak","spruce","birch","jungle","acacia","dark_oak","mangrove","cherry",
              "pale_oak","bamboo","crimson","warped","stone","polished_blackstone",
              "light_weighted","heavy_weighted" })
            {
                Add($"minecraft:{mat}_pressure_plate");
            }

            // Signs / hanging signs
            foreach (var mat in new[]
            { "oak","spruce","birch","jungle","acacia","dark_oak","mangrove","cherry",
              "pale_oak","bamboo","crimson","warped" })
            {
                Add($"minecraft:{mat}_sign");
                Add($"minecraft:{mat}_wall_sign");
                Add($"minecraft:{mat}_hanging_sign");
                Add($"minecraft:{mat}_wall_hanging_sign");
            }

            // Carpets
            foreach (var color in new[]
            { "white","orange","magenta","light_blue","yellow","lime","pink","gray",
              "light_gray","cyan","purple","blue","brown","green","red","black" })
            {
                Add($"minecraft:{color}_carpet");
                Add($"minecraft:{color}_banner");
                Add($"minecraft:{color}_wall_banner");
            }
            Add("minecraft:moss_carpet");

            // Redstone wiring / components
            Add("minecraft:redstone_wire");
            Add("minecraft:lever");
            Add("minecraft:tripwire_hook"); Add("minecraft:tripwire");
            Add("minecraft:comparator"); Add("minecraft:repeater");
            Add("minecraft:daylight_detector");

            // Misc non-colliding
            Add("minecraft:ladder");
            Add("minecraft:sugar_cane");
            Add("minecraft:cobweb");
            Add("minecraft:wheat"); Add("minecraft:carrots");
            Add("minecraft:potatoes"); Add("minecraft:beetroots");
            Add("minecraft:melon_stem"); Add("minecraft:pumpkin_stem");
            Add("minecraft:attached_melon_stem"); Add("minecraft:attached_pumpkin_stem");
            Add("minecraft:nether_wart");
            Add("minecraft:cocoa");
            Add("minecraft:flower_pot");
            Add("minecraft:lily_pad");
            Add("minecraft:snow");
            Add("minecraft:fire"); Add("minecraft:soul_fire");
            Add("minecraft:end_rod");
            Add("minecraft:chorus_plant"); Add("minecraft:chorus_flower");
            Add("minecraft:structure_void");
            Add("minecraft:amethyst_cluster");
            Add("minecraft:small_amethyst_bud"); Add("minecraft:medium_amethyst_bud");
            Add("minecraft:large_amethyst_bud");
            Add("minecraft:sea_pickle");
            Add("minecraft:turtle_egg"); Add("minecraft:sniffer_egg");
            Add("minecraft:pointed_dripstone");
            Add("minecraft:sculk_sensor"); Add("minecraft:calibrated_sculk_sensor");
            Add("minecraft:sculk_vein");
            Add("minecraft:candle");

            foreach (var color in new[]
            { "white","orange","magenta","light_blue","yellow","lime","pink","gray",
              "light_gray","cyan","purple","blue","brown","green","red","black" })
            {
                Add($"minecraft:{color}_candle");
            }

            return h;

            void Add(string name)
                => h.Add(Hash(System.Text.Encoding.UTF8.GetBytes(name)));
        }
    }
}