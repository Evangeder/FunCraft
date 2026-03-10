using Npgsql;

namespace FunCraft.Data.Players
{
    public sealed class PostgresPlayerRepository(NpgsqlDataSource db) : IPlayerRepository
    {
        public async Task<PlayerRecord?> GetByUuidAsync(Guid uuid, CancellationToken ct = default)
        {
            await using var cmd = db.CreateCommand(
                """
                SELECT uuid, username, x, y, z, yaw, pitch, last_seen
                FROM   players
                WHERE  uuid = $1
                """);

            cmd.Parameters.AddWithValue(uuid);

            await using var reader = await cmd.ExecuteReaderAsync(ct);
            if (!await reader.ReadAsync(ct))
            {
                return null;
            }

            return new PlayerRecord
            {
                Uuid = reader.GetGuid(0),
                Username = reader.GetString(1),
                X = reader.GetDouble(2),
                Y = reader.GetDouble(3),
                Z = reader.GetDouble(4),
                Yaw = reader.GetFloat(5),
                Pitch = reader.GetFloat(6),
                LastSeen = reader.GetDateTime(7),
            };
        }

        public async Task SaveAsync(PlayerRecord player, CancellationToken ct = default)
        {
            await using var cmd = db.CreateCommand(
                """
                INSERT INTO players (uuid, username, x, y, z, yaw, pitch, last_seen)
                VALUES ($1, $2, $3, $4, $5, $6, $7, NOW())
                ON CONFLICT (uuid) DO UPDATE SET
                    username  = EXCLUDED.username,
                    x         = EXCLUDED.x,
                    y         = EXCLUDED.y,
                    z         = EXCLUDED.z,
                    yaw       = EXCLUDED.yaw,
                    pitch     = EXCLUDED.pitch,
                    last_seen = EXCLUDED.last_seen
                """);

            cmd.Parameters.AddWithValue(player.Uuid);
            cmd.Parameters.AddWithValue(player.Username);
            cmd.Parameters.AddWithValue(player.X);
            cmd.Parameters.AddWithValue(player.Y);
            cmd.Parameters.AddWithValue(player.Z);
            cmd.Parameters.AddWithValue(player.Yaw);
            cmd.Parameters.AddWithValue(player.Pitch);

            await cmd.ExecuteNonQueryAsync(ct);
        }
    }
}