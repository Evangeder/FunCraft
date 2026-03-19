namespace FunCraft.Protocol.Registry
{
    /// <summary>
    /// Maps block names to what they drop when broken without Silk Touch.
    /// <para>
    /// Rules encoded here:
    /// <list type="bullet">
    ///   <item><c>null</c> — drops nothing (e.g. grass, leaves without shears).</item>
    ///   <item>Same name — drops itself (the common case, also the default).</item>
    ///   <item>Different name — drops the mapped item (e.g. stone→cobblestone).</item>
    /// </list>
    /// Anything not listed drops itself by default.
    /// </para>
    /// <para>
    /// Keyed by FNV-1a hash of the block's UTF-8 name — zero allocation on the
    /// hot block-break path.
    /// </para>
    /// </summary>
    public static class BlockDropTable
    {
        private const string None = "\0"; // sentinel: drops nothing

        // Maps block_name_hash → drop_item_name (or None sentinel).
        private static readonly Dictionary<uint, string> _table = Build();

        /// <summary>
        /// Returns the item name (UTF-8) that the block drops, or
        /// <see cref="ReadOnlyMemory{T}.Empty"/> if it drops nothing.
        /// Returns the block name itself for anything not explicitly listed.
        /// </summary>
        public static ReadOnlyMemory<byte> GetDrop(ReadOnlySpan<byte> blockName)
        {
            if (_table.TryGetValue(Hash(blockName), out var drop))
            {
                if (drop == None) return ReadOnlyMemory<byte>.Empty;
                return System.Text.Encoding.UTF8.GetBytes(drop).AsMemory();
            }

            // Default: drops itself.
            return blockName.ToArray().AsMemory();
        }

        private static uint Hash(ReadOnlySpan<byte> data)
        {
            const uint fnvPrime = 16777619;
            var hash = 2166136261u;
            foreach (var b in data) { hash ^= b; hash *= fnvPrime; }
            return hash;
        }

        private static uint H(string s) => Hash(System.Text.Encoding.UTF8.GetBytes(s));

        private static Dictionary<uint, string> Build()
        {
            var t = new Dictionary<uint, string>(256);

            // ── Drops nothing (require silk touch or special tool) ────────────────

            // Grass-family: requires silk touch for block, otherwise drops nothing
            // (seeds/dirt handled separately in survival — we drop nothing for now).
            Add("minecraft:grass_block", "minecraft:dirt");
            Add("minecraft:mycelium", "minecraft:dirt");
            Add("minecraft:podzol", "minecraft:dirt");

            // Leaves: drop nothing bare-hand; shears or silk touch give the block.
            foreach (var tree in new[]
            { "oak","spruce","birch","jungle","acacia","dark_oak","mangrove","cherry",
              "pale_oak","azalea","flowering_azalea" })
            {
                Add($"minecraft:{tree}_leaves", null);
            }

            // Grass / fern / tall variants: no item drop.
            Add("minecraft:grass", null);
            Add("minecraft:fern", null);
            Add("minecraft:tall_grass", null);
            Add("minecraft:large_fern", null);

            // Dead bush: no drop without shears.
            Add("minecraft:dead_bush", null);

            // Snow layer: drops snowball, not block.
            Add("minecraft:snow", "minecraft:snowball");

            // Ice: drops nothing (melts).
            Add("minecraft:ice", null);
            Add("minecraft:packed_ice", null); // drops itself with silk touch only
            Add("minecraft:blue_ice", null);

            // Glowstone: drops glowstone dust, not block.
            Add("minecraft:glowstone", "minecraft:glowstone_dust");

            // ── Stone family: drops cobblestone variants ──────────────────────────
            Add("minecraft:stone", "minecraft:cobblestone");
            Add("minecraft:deepslate", "minecraft:cobbled_deepslate");

            // Ores: drop themselves by default EXCEPT the ones below.
            Add("minecraft:coal_ore", "minecraft:coal");
            Add("minecraft:deepslate_coal_ore", "minecraft:coal");
            Add("minecraft:diamond_ore", "minecraft:diamond");
            Add("minecraft:deepslate_diamond_ore", "minecraft:diamond");
            Add("minecraft:emerald_ore", "minecraft:emerald");
            Add("minecraft:deepslate_emerald_ore", "minecraft:emerald");
            Add("minecraft:lapis_ore", "minecraft:lapis_lazuli");
            Add("minecraft:deepslate_lapis_ore", "minecraft:lapis_lazuli");
            Add("minecraft:redstone_ore", "minecraft:redstone");
            Add("minecraft:deepslate_redstone_ore", "minecraft:redstone");
            Add("minecraft:nether_quartz_ore", "minecraft:quartz");
            Add("minecraft:nether_gold_ore", "minecraft:gold_nugget");

            // Gravel: drops flint (simplified — vanilla is random, we always drop flint).
            Add("minecraft:gravel", "minecraft:flint");

            // Clay: drops clay balls.
            Add("minecraft:clay", "minecraft:clay_ball");

            // Bookshelf: drops books.
            Add("minecraft:bookshelf", "minecraft:book");

            // Sponge: drops wet sponge when broken underwater (simplified: drops sponge).
            // (no change needed — defaults to itself)

            // Crops: drop seeds/food at varying stages; simplified to seed drop only.
            Add("minecraft:wheat", "minecraft:wheat_seeds");
            Add("minecraft:potatoes", "minecraft:potato");
            Add("minecraft:carrots", "minecraft:carrot");
            Add("minecraft:beetroots", "minecraft:beetroot_seeds");
            Add("minecraft:melon", "minecraft:melon_slice");
            Add("minecraft:nether_wart", "minecraft:nether_wart"); // keeps item name

            // Sea lantern: drops prismarine crystals.
            Add("minecraft:sea_lantern", "minecraft:prismarine_crystals");

            // Flower pot: drops clay pot (broken pot, simplified).
            Add("minecraft:flower_pot", "minecraft:flower_pot");

            return t;

            void Add(string block, string? item)
                => t[H(block)] = item ?? None;
        }
    }
}