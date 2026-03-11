using StackExchange.Redis;

namespace FunCraft.Data.Inventory
{
    /// <summary>
    /// Cache-aside decorator over <see cref="PostgresInventoryRepository"/>.
    /// </summary>
    public sealed class CachedInventoryRepository(PostgresInventoryRepository db, IDatabase redis) : IInventoryRepository
    {
        private static readonly TimeSpan CacheTtl = TimeSpan.FromHours(24);

        private const string SentinelField = "sentinel";
        private const string SentinelValue = "1";

        private static readonly string[] SlotNames = ["0", "1", "2", "3", "4", "5", "6", "7", "8"];

        public async Task<HotbarSlot[]?> GetHotbarAsync(Guid uuid, CancellationToken ct = default)
        {
            var key = Key(uuid);
            var entries = await redis.HashGetAllAsync(key);

            if (entries.Length > 0)
            {
                if (entries.Length == 1 && (string?) entries[0].Name == SentinelField)
                {
                    return null;
                }

                var hotbar = new HotbarSlot[9];

                foreach (var e in entries)
                {
                    var name = (string?)e.Name;

                    if (name is null or SentinelField)
                    {
                        continue;
                    }

                    if (!int.TryParse(name, out var slot) || slot < 0 || slot > 8)
                    {
                        continue;
                    }

                    if (TryParseSlotValue(e.Value, out var hs))
                    {
                        hotbar[slot] = hs;
                    }
                }
                return hotbar;
            }

            var result = await db.GetHotbarAsync(uuid, ct);
            await PopulateCacheAsync(key, result);

            return result;
        }

        public async Task SaveHotbarAsync(Guid uuid, HotbarSlot[] hotbar, CancellationToken ct = default)
        {
            await db.SaveHotbarAsync(uuid, hotbar, ct);

            var key = Key(uuid);
            await redis.KeyDeleteAsync(key);

            var hasItems = false;

            foreach (var item in hotbar)
            {
                if (!item.IsEmpty)
                {
                    hasItems = true;
                    break;
                }
            }

            await PopulateCacheAsync(key, hasItems ? hotbar : null);
        }

        private async Task PopulateCacheAsync(RedisKey key, HotbarSlot[]? hotbar)
        {
            if (hotbar is null)
            {
                await redis.HashSetAsync(key, SentinelField, SentinelValue);
                await redis.KeyExpireAsync(key, CacheTtl);

                return;
            }

            var count = 0;

            foreach (var item in hotbar)
            {
                if (!item.IsEmpty)
                {
                    count++;
                }
            }

            if (count == 0)
            {
                await redis.HashSetAsync(key, SentinelField, SentinelValue);
                await redis.KeyExpireAsync(key, CacheTtl);

                return;
            }

            var entries = new HashEntry[count];
            var idx = 0;
            for (var i = 0; i < hotbar.Length; i++)
            {
                if (hotbar[i].IsEmpty)
                {
                    continue;
                }

                entries[idx++] = new HashEntry(SlotNames[i], $"{hotbar[i].ItemId}:{hotbar[i].Count}");
            }

            await redis.HashSetAsync(key, entries);
            await redis.KeyExpireAsync(key, CacheTtl);
        }

        private static bool TryParseSlotValue(RedisValue value, out HotbarSlot slot)
        {
            slot = default;
            var str = (string?)value;

            if (str is null)
            {
                return false;
            }

            var sep = str.IndexOf(':');
            if (sep < 1)
            {
                return false;
            }

            if (!int.TryParse(str.AsSpan(0, sep), out var itemId))
            {
                return false;
            }

            if (!int.TryParse(str.AsSpan(sep + 1), out var count))
            {
                return false;
            }

            slot = new HotbarSlot(itemId, count);

            return true;
        }

        private static RedisKey Key(Guid uuid) => $"hotbar:{uuid:N}";
    }
}