namespace FunCraft.Network.Entities
{
    public interface IEntityManager
    {
        /// <summary>
        /// Creates an item entity at the given position with an optional initial
        /// velocity and registers it. Caller broadcasts the spawn to clients.
        /// Set <paramref name="instantPickup"/> to bypass the spawn cooldown (e.g. /give).
        /// </summary>
        ItemEntity SpawnItem(int itemId, int count,
            double x, double y, double z,
            double vx = 0, double vy = 0, double vz = 0,
            bool instantPickup = false);

        /// <summary>
        /// Removes the item entity with <paramref name="entityId"/> and returns it.
        /// Returns false if it no longer exists (already picked up by another player).
        /// </summary>
        bool TryRemove(int entityId, out ItemEntity? entity);

        /// <summary>All currently live item entities. Used to sync newly connecting players.</summary>
        IReadOnlyList<ItemEntity> GetAllItems();

        /// <summary>
        /// Returns all item entities within <paramref name="radius"/> blocks (Euclidean)
        /// of the given position.
        /// </summary>
        IReadOnlyList<ItemEntity> FindPickups(double x, double y, double z, double radius);

        /// <summary>
        /// Re-registers an item entity that was temporarily removed during merge
        /// evaluation but ultimately had no neighbour to merge with.
        /// </summary>
        void ReAdd(ItemEntity item);

        /// <summary>
        /// Returns the first item entity of <paramref name="itemId"/> within
        /// <paramref name="radius"/> blocks horizontally (XZ only) of the given
        /// position, excluding <paramref name="excludeEntityId"/>.
        /// Used to merge freshly-dropped items with nearby stacks of the same type.
        /// </summary>
        ItemEntity? FindMergeable(int itemId, double x, double z, double radius, int excludeEntityId);
    }
}