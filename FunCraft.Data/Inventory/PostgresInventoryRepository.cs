using FunCraft.Data.Inventory;
using Npgsql;
using System.Text.RegularExpressions;

namespace FunCraft.Data.Inventory
{
    /// <summary>
    /// Persists hotbar slots (0–8) to the <c>inventory</c> table.
    /// item_data JSONB format: <c>{"id":28,"count":7}</c>
    /// </summary>
    public sealed partial class PostgresInventoryRepository(NpgsqlDataSource db) : IInventoryRepository
    {
        [GeneratedRegex("""^\{"id":(\d+),"count":(\d+)\}$""")]
        private static partial Regex ItemDataRegex();

        public async Task<HotbarSlot[]?> GetHotbarAsync(Guid uuid, CancellationToken ct = default)
        {
            await using var cmd = db.CreateCommand(
                """
                SELECT slot, item_data
                FROM   inventory
                WHERE  uuid = $1
                  AND  slot BETWEEN 0 AND 8
                ORDER  BY slot
                """);
            cmd.Parameters.AddWithValue(uuid);

            await using var reader = await cmd.ExecuteReaderAsync(ct);

            HotbarSlot[]? hotbar = null;
            while (await reader.ReadAsync(ct))
            {
                hotbar ??= new HotbarSlot[9];
                var slot = reader.GetInt16(0);
                var json = reader.GetString(1);
                var m = ItemDataRegex().Match(json);
                if (m.Success)
                {
                    hotbar[slot] = new HotbarSlot(ItemId: int.Parse(m.Groups[1].ValueSpan), Count: int.Parse(m.Groups[2].ValueSpan));
                    Console.WriteLine($"Readed inventory from database: {hotbar[slot].ItemId}, qty: {hotbar[slot].Count}");
                }
            }


            return hotbar;
        }

        public async Task SaveHotbarAsync(Guid uuid, HotbarSlot[] hotbar, CancellationToken ct = default)
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
                      AND slot BETWEEN 0 AND 8
                    """;
                del.Parameters.AddWithValue(uuid);
                await del.ExecuteNonQueryAsync(ct);
            }

            for (var i = 0; i < hotbar.Length; i++)
            {
                if (hotbar[i].IsEmpty)
                {
                    continue;
                }

                Console.WriteLine($"Saved inventory to database: {hotbar[i].ItemId}, qty: {hotbar[i].Count}");

                var json = $$$"""{"id":{{{hotbar[i].ItemId}}},"count":{{{hotbar[i].Count}}}}""";

                await using var ins = conn.CreateCommand();
                ins.Transaction = tr;
                ins.CommandText =
                    """
                    INSERT INTO inventory (uuid, slot, item_data)
                    VALUES ($1, $2, $3::jsonb)
                    """;
                ins.Parameters.AddWithValue(uuid);
                ins.Parameters.AddWithValue((short)i);
                ins.Parameters.AddWithValue(json);
                await ins.ExecuteNonQueryAsync(ct);
            }

            await tr.CommitAsync(ct);
        }
    }
}