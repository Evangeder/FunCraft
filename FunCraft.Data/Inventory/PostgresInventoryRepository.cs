using Npgsql;

namespace FunCraft.Data.Inventory
{
    /// <summary>
    /// Persists all 46 window-0 inventory slots to the <c>inventory</c> table.
    /// Schema: (uuid, slot SMALLINT, item_id INT, item_count SMALLINT).
    /// </summary>
    public sealed class PostgresInventoryRepository(NpgsqlDataSource db) : IInventoryRepository
    {
        public async ValueTask<bool> TryGetInventoryAsync(
            Guid uuid, Memory<HotbarSlot> destination, CancellationToken ct = default)
        {
            await using var cmd = db.CreateCommand(
                """
                SELECT slot, item_id, item_count
                FROM   inventory
                WHERE  uuid = $1
                  AND  slot BETWEEN 0 AND 45
                ORDER  BY slot
                """);
            cmd.Parameters.AddWithValue(uuid);

            await using var reader = await cmd.ExecuteReaderAsync(ct);

            var found = false;

            while (await reader.ReadAsync(ct))
            {
                var slot = reader.GetInt16(0);
                if ((uint)slot >= HotbarSlot.InventorySize) continue;

                destination.Span[slot] = new HotbarSlot(
                    ItemId: reader.GetInt32(1),
                    Count: reader.GetInt16(2));
                found = true;
            }

            return found;
        }

        public async ValueTask<HotbarSlot> GetItem(Guid uuid, Memory<HotbarSlot> inventory,
            int slot, CancellationToken ct = default)
        {
            await using var cmd = db.CreateCommand(
                """
                SELECT item_id, item_count
                FROM   inventory
                WHERE  uuid = $1, slot = $2
                  AND  slot BETWEEN 0 AND 45
                ORDER  BY slot
                """);
            cmd.Parameters.AddWithValue(uuid);
            cmd.Parameters.AddWithValue(slot);

            await using var reader = await cmd.ExecuteReaderAsync(ct);

            if (await reader.ReadAsync(ct))
            {
                return new HotbarSlot(reader.GetInt16(0), reader.GetInt16(1));
            }

            return default;
        }

        public async ValueTask SaveInventoryAsync(
            Guid uuid, ReadOnlyMemory<HotbarSlot> inventory, CancellationToken ct = default)
        {
            await using var conn = await db.OpenConnectionAsync(ct);
            await using var tr = await conn.BeginTransactionAsync(ct);

            await using (var del = conn.CreateCommand())
            {
                del.Transaction = tr;
                del.CommandText =
                    """
                    DELETE FROM inventory
                    WHERE uuid = $1
                      AND slot BETWEEN 0 AND 45
                    """;
                del.Parameters.AddWithValue(uuid);
                await del.ExecuteNonQueryAsync(ct);
            }

            for (var i = 0; i < inventory.Length; i++)
            {
                var s = inventory.Span[i];
                if (s.IsEmpty)
                {
                    continue;
                }

                await using var ins = conn.CreateCommand();
                ins.Transaction = tr;
                ins.CommandText =
                    """
                    INSERT INTO inventory (uuid, slot, item_id, item_count)
                    VALUES ($1, $2, $3, $4)
                    """;
                ins.Parameters.AddWithValue(uuid);
                ins.Parameters.AddWithValue((short)i);
                ins.Parameters.AddWithValue(s.ItemId);
                ins.Parameters.AddWithValue((short)s.Count);
                await ins.ExecuteNonQueryAsync(ct);
            }

            await tr.CommitAsync(ct);
        }
    }
}