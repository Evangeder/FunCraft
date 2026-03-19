using System.Collections.Concurrent;

namespace FunCraft.Network.Entities
{
    public sealed class EntityManager : IEntityManager
    {
        private readonly ConcurrentDictionary<int, ItemEntity> _items = new();

        public ItemEntity SpawnItem(int itemId, int count,
            double x, double y, double z,
            double vx = 0, double vy = 0, double vz = 0,
            bool instantPickup = false)
        {
            var entity = new ItemEntity(itemId, count, x, y, z, vx, vy, vz, instantPickup);
            _items[entity.EntityId] = entity;
            return entity;
        }

        public void ReAdd(ItemEntity item) => _items[item.EntityId] = item;

        public bool TryRemove(int entityId, out ItemEntity? entity)
        {
            if (_items.TryRemove(entityId, out var found))
            {
                entity = found;
                return true;
            }

            entity = null;
            return false;
        }

        public IReadOnlyList<ItemEntity> GetAllItems() => [.. _items.Values];

        public IReadOnlyList<ItemEntity> FindPickups(double x, double y, double z, double radius)
        {
            var radiusSq = radius * radius;
            List<ItemEntity>? result = null;

            foreach (var item in _items.Values)
            {
                var dx = item.X - x;
                var dy = item.Y - y;
                var dz = item.Z - z;

                if (dx * dx + dy * dy + dz * dz <= radiusSq)
                {
                    result ??= [];
                    result.Add(item);
                }
            }

            return result ?? (IReadOnlyList<ItemEntity>)[];
        }

        public ItemEntity? FindMergeable(int itemId, double x, double z, double radius, int excludeEntityId)
        {
            var radiusSq = radius * radius;

            foreach (var item in _items.Values)
            {
                if (item.EntityId == excludeEntityId) continue;
                if (item.ItemId != itemId) continue;
                if (!item.IsSettled) continue;   // only merge items at rest

                var dx = item.X - x;
                var dz2 = item.Z - z;

                if (dx * dx + dz2 * dz2 <= radiusSq)
                    return item;
            }

            return null;
        }
    }
}