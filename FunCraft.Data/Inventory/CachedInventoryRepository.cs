using StackExchange.Redis;
using System.Buffers;

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
            Guid uuid, Memory<InventorySlot> destination, CancellationToken ct = default)
        {
            var key = Key(uuid);
            var entries = await redis.HashGetAllAsync(key);

            if (entries.Length > 0)
            {
                if (entries.Length == 1 && (string?)entries[0].Name == SentinelField)
                {
                    return false;
                }

                var span = destination.Span;

                foreach (var e in entries)
                {
                    var name = (string?)e.Name;

                    if (name is null or SentinelField)
                    {
                        continue;
                    }

                    if (!int.TryParse(name, out var slot) || (uint)slot >= InventorySlot.InventorySize)
                    {
                        continue;
                    }

                    if (TryParseSlotValue(e.Value, out var hs))
                    {
                        span[slot] = hs;
                    }
                }

                return true;
            }

            var found = await db.TryGetInventoryAsync(uuid, destination, ct);
            await PopulateCacheAsync(key, found ? destination : default);
            return found;
        }

        public async ValueTask SaveInventoryAsync(
            Guid uuid, ReadOnlyMemory<InventorySlot> inventory, CancellationToken ct = default)
        {
            await db.SaveInventoryAsync(uuid, inventory, ct);

            var key = Key(uuid);
            await redis.KeyDeleteAsync(key);
            await PopulateCacheAsync(key, inventory);
        }

        private async Task PopulateCacheAsync(RedisKey key, ReadOnlyMemory<InventorySlot> inv)
        {
            var span = inv.Span;

            // Rent a staging buffer to build HashEntry pairs without a List<T> and its
            // internal array. InventorySize is 46 — small enough that a single rent
            // covers all possible non-empty slots.
            var buf = ArrayPool<HashEntry>.Shared.Rent(InventorySlot.InventorySize);
            var count = 0;

            try
            {
                for (var i = 0; i < span.Length; i++)
                {
                    if (!span[i].IsEmpty)
                    {
                        buf[count++] = new HashEntry(i.ToString(), $"{span[i].ItemId}:{span[i].Count}");
                    }
                }

                if (count == 0)
                {
                    await redis.HashSetAsync(key, SentinelField, SentinelValue);
                }
                else
                {
                    // StackExchange.Redis requires a HashEntry[]. Allocate exactly what we
                    // need (at most 46 entries) and copy the staged results in one shot.
                    var entries = new HashEntry[count];
                    buf.AsSpan(0, count).CopyTo(entries.AsSpan());
                    await redis.HashSetAsync(key, entries);
                }
            }
            finally
            {
                ArrayPool<HashEntry>.Shared.Return(buf);
            }

            await redis.KeyExpireAsync(key, CacheTtl);
        }

        private static bool TryParseSlotValue(RedisValue value, out InventorySlot slot)
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

            slot = new InventorySlot(itemId, count);
            return true;
        }

        private static RedisKey Key(Guid uuid) => $"inv:{uuid:N}";

        public async ValueTask<InventorySlot> GetItem(Guid uuid, Memory<InventorySlot> inventory, int slot, CancellationToken ct = default)
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

                        if (!int.TryParse(name, out var itemSlot) || (uint)itemSlot >= InventorySlot.InventorySize)
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