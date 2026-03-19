using System.Collections.Concurrent;

namespace FunCraft.Network.Entities
{
    public sealed class EntityManager : IEntityManager
    {
        private readonly ConcurrentDictionary<int, ItemEntity> _items = new();
        private readonly object _snapshotLock = new();

        // Copy-on-write snapshot rebuilt under _snapshotLock on every add/remove.
        // GetAllItems() returns this array directly — zero allocation on the read path.
        private ItemEntity[] _snapshot = [];

        public ItemEntity SpawnItem(int itemId, int count,
            double x, double y, double z,
            double vx = 0, double vy = 0, double vz = 0,
            bool instantPickup = false)
        {
            var entity = new ItemEntity(itemId, count, x, y, z, vx, vy, vz, instantPickup);
            _items[entity.EntityId] = entity;
            RebuildSnapshot();
            return entity;
        }

        public void ReAdd(ItemEntity item)
        {
            _items[item.EntityId] = item;
            RebuildSnapshot();
        }

        public bool TryRemove(int entityId, out ItemEntity? entity)
        {
            if (_items.TryRemove(entityId, out var found))
            {
                entity = found;
                RebuildSnapshot();
                return true;
            }

            entity = null;
            return false;
        }

        private void RebuildSnapshot()
        {
            lock (_snapshotLock)
            {
                var values = _items.Values;
                var next = new ItemEntity[values.Count];
                var i = 0;

                foreach (var e in values)
                {
                    next[i++] = e;
                }

                Volatile.Write(ref _snapshot, next);
            }
        }

        // Returns the cached snapshot — zero allocation on the read path.
        public IReadOnlyList<ItemEntity> GetAllItems() =>
            Volatile.Read(ref _snapshot);

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
                if (item.EntityId == excludeEntityId)
                {
                    continue;
                }

                if (item.ItemId != itemId)
                {
                    continue;
                }

                if (!item.IsSettled)
                {
                    continue;
                }

                var dx = item.X - x;
                var dz2 = item.Z - z;

                if (dx * dx + dz2 * dz2 <= radiusSq)
                {
                    return item;
                }
            }

            return null;
        }
    }
}