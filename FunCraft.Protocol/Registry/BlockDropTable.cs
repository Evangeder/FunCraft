using System.Collections.Frozen;
using System.Text;

namespace FunCraft.Protocol.Registry
{
    /// <summary>
    /// Maps block names to what they drop when broken without Silk Touch.
    /// <para>
    /// Rules encoded here:
    /// <list type="bullet">
    ///   <item><c>Empty</c> — drops nothing (e.g. grass, leaves without shears).</item>
    ///   <item>Same name — drops itself (the common case, also the default).</item>
    ///   <item>Different name — drops the mapped item (e.g. stone→cobblestone).</item>
    /// </list>
    /// Anything not listed drops itself by default.
    /// </para>
    /// <para>
    /// Keys are FNV-1a hashes of the block's UTF-8 name; values are pre-encoded
    /// UTF-8 byte arrays. Zero allocation on the hot block-break path.
    /// <c>ReadOnlyMemory.Empty</c> is the sentinel for "drops nothing".
    /// </para>
    /// </summary>
    public static class BlockDropTable
    {
        // Maps block_name_hash -> pre-encoded UTF-8 drop item name.
        // ReadOnlyMemory<byte>.Empty means "drops nothing".
        private static readonly FrozenDictionary<uint, ReadOnlyMemory<byte>> _table = Build();

        /// <summary>
        /// Returns the UTF-8 item name that the block drops, or
        /// <see cref="ReadOnlyMemory{T}.Empty"/> if it drops nothing.
        /// Returns <paramref name="blockName"/> itself (zero allocation) for
        /// anything not explicitly listed in the drop table.
        /// </summary>
        public static ReadOnlyMemory<byte> GetDrop(ReadOnlyMemory<byte> blockName)
        {
            if (_table.TryGetValue(Hash(blockName.Span), out var drop))
            {
                return drop;
            }

            // Default: block drops itself — return the caller's memory, no allocation.
            return blockName;
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

        private static uint H(string s) => Hash(Encoding.UTF8.GetBytes(s));

        private static ReadOnlyMemory<byte> Encode(string s) => Encoding.UTF8.GetBytes(s);

        private static FrozenDictionary<uint, ReadOnlyMemory<byte>> Build()
        {
            var t = new Dictionary<uint, ReadOnlyMemory<byte>>(256);

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
                AddNone($"minecraft:{tree}_leaves");
            }

            // Grass / fern / tall variants: no item drop.
            AddNone("minecraft:grass");
            AddNone("minecraft:fern");
            AddNone("minecraft:tall_grass");
            AddNone("minecraft:large_fern");

            // Dead bush: no drop without shears.
            AddNone("minecraft:dead_bush");

            // Snow layer: drops snowball, not block.
            Add("minecraft:snow", "minecraft:snowball");

            // Ice: drops nothing (melts).
            AddNone("minecraft:ice");
            AddNone("minecraft:packed_ice");
            AddNone("minecraft:blue_ice");

            // Glowstone: drops glowstone dust, not block.
            Add("minecraft:glowstone", "minecraft:glowstone_dust");

            // Stone family: drops cobblestone variants.
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

            // Crops: drop seeds/food at varying stages; simplified to seed drop only.
            Add("minecraft:wheat", "minecraft:wheat_seeds");
            Add("minecraft:potatoes", "minecraft:potato");
            Add("minecraft:carrots", "minecraft:carrot");
            Add("minecraft:beetroots", "minecraft:beetroot_seeds");
            Add("minecraft:melon", "minecraft:melon_slice");
            Add("minecraft:nether_wart", "minecraft:nether_wart");

            // Sea lantern: drops prismarine crystals.
            Add("minecraft:sea_lantern", "minecraft:prismarine_crystals");

            // Flower pot: drops clay pot (broken pot, simplified).
            Add("minecraft:flower_pot", "minecraft:flower_pot");

            return t.ToFrozenDictionary();

            void Add(string block, string item)
                => t[H(block)] = Encode(item);

            void AddNone(string block)
                => t[H(block)] = ReadOnlyMemory<byte>.Empty;
        }
    }
}