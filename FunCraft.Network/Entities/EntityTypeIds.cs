using FunCraft.Protocol.Registry;

namespace FunCraft.Network.Entities
{
    /// <summary>
    /// Cached entity type IDs from the <c>minecraft:entity_type</c> registry.
    /// Resolved lazily at first use, after <see cref="RegistryLookup.Build"/> has run.
    /// </summary>
    public static class EntityTypeIds
    {
        public static int Player => _player.Value;
        public static int Item => _item.Value;

        private static readonly Lazy<int> _player =
            new(() => RegistryLookup.GetId("minecraft:entity_type"u8, "minecraft:player"u8));
        private static readonly Lazy<int> _item =
            new(() => RegistryLookup.GetId("minecraft:entity_type"u8, "minecraft:item"u8));
    }
}