using StackExchange.Redis;

namespace FunCraft.Data.Inventory
{
    /// <summary>
    /// Cache-aside decorator over <see cref="PostgresInventoryRepository"/>.
    /// No heap allocations on the hot (cache-hit) path — the caller owns the buffer.
    /// </summary>
    public sealed class CachedInventoryRepository(PostgresInventoryRepository db, IDatabase redis) : IInventoryRepository
    {
        private static readonly TimeSpan CacheTtl = TimeSpan.FromHours(24);

        private const string SentinelField = "sentinel";
        private const string SentinelValue = "1";

        public async ValueTask<bool> TryGetInventoryAsync(
            Guid uuid, Memory<HotbarSlot> destination, CancellationToken ct = default)
        {
            var key = Key(uuid);
            var entries = await redis.HashGetAllAsync(key);

            if (entries.Length > 0)
            {
                if (entries.Length == 1 && (string?)entries[0].Name == SentinelField)
                    return false;

                var span = destination.Span;
                foreach (var e in entries)
                {
                    var name = (string?)e.Name;
                    if (name is null or SentinelField) continue;
                    if (!int.TryParse(name, out var slot) || (uint)slot >= HotbarSlot.InventorySize) continue;
                    if (TryParseSlotValue(e.Value, out var hs))
                        span[slot] = hs;
                }

                return true;
            }

            var found = await db.TryGetInventoryAsync(uuid, destination, ct);
            await PopulateCacheAsync(key, found ? destination : default);
            return found;
        }

        public async ValueTask SaveInventoryAsync(
            Guid uuid, ReadOnlyMemory<HotbarSlot> inventory, CancellationToken ct = default)
        {
            await db.SaveInventoryAsync(uuid, inventory, ct);

            var key = Key(uuid);
            await redis.KeyDeleteAsync(key);
            await PopulateCacheAsync(key, inventory);
        }

        private async Task PopulateCacheAsync(RedisKey key, ReadOnlyMemory<HotbarSlot> inv)
        {
            var span = inv.Span;
            var entries = new List<HashEntry>(HotbarSlot.InventorySize);

            for (var i = 0; i < span.Length; i++)
            {
                if (!span[i].IsEmpty)
                    entries.Add(new HashEntry(i.ToString(), $"{span[i].ItemId}:{span[i].Count}"));
            }

            if (entries.Count == 0)
                await redis.HashSetAsync(key, SentinelField, SentinelValue);
            else
                await redis.HashSetAsync(key, [.. entries]);

            await redis.KeyExpireAsync(key, CacheTtl);
        }

        private static bool TryParseSlotValue(RedisValue value, out HotbarSlot slot)
        {
            slot = default;
            var str = (string?)value;
            if (str is null) return false;

            var sep = str.IndexOf(':');
            if (sep < 1) return false;
            if (!int.TryParse(str.AsSpan(0, sep), out var itemId)) return false;
            if (!int.TryParse(str.AsSpan(sep + 1), out var count)) return false;

            slot = new HotbarSlot(itemId, count);
            return true;
        }

        private static RedisKey Key(Guid uuid) => $"inv:{uuid:N}";

        public async ValueTask<HotbarSlot> GetItem(Guid uuid, Memory<HotbarSlot> inventory, int slot, CancellationToken ct = default)
        {
            var key = Key(uuid);
            var entries = await redis.HashGetAllAsync(key);

            switch (entries.Length)
            {
                case 0:
                case 1 when (string?)entries[0].Name == SentinelField:
                    return default;

                default:
                    foreach (var e in entries)
                    {
                        var name = (string?)e.Name;
                        if (name is null or SentinelField)
                        {
                            continue;
                        }

                        if (!int.TryParse(name, out var itemSlot) || (uint)itemSlot >= HotbarSlot.InventorySize)
                        {
                            continue;
                        }

                        if (itemSlot == slot && TryParseSlotValue(e.Value, out var hs))
                        {
                            return hs;
                        }
                    }

                    return default;
            }
        }
    }
}