using Npgsql;
using StackExchange.Redis;

namespace FunCraft.Data.Sessions
{
    /// <summary>
    /// Stores the "currently online" state in Redis (fast presence checks) and
    /// writes a completed session row to Postgres when the player disconnects.
    /// Redis key: <c>session:{uuid}</c>  — hash with fields: username, ip, connected_at.
    /// </summary>
    public sealed class RedisSessionStore(IDatabase redis, NpgsqlDataSource db) : ISessionStore
    {
        private static readonly TimeSpan SessionTtl = TimeSpan.FromHours(24);

        public async Task SetAsync(PlayerSession session, CancellationToken ct = default)
        {
            var key = Key(session.Uuid);

            var entries = new HashEntry[]
            {
                new("username", session.Username),
                new("ip", session.IpAddress),
                new("connected_at", session.ConnectedAt.ToUnixTimeMilliseconds()),
            };

            await redis.HashSetAsync(key, entries);
            await redis.KeyExpireAsync(key, SessionTtl);
        }

        public async Task EndAsync(Guid uuid, DateTimeOffset disconnectedAt, CancellationToken ct = default)
        {
            var key = Key(uuid);
            var entries = await redis.HashGetAllAsync(key);

            if (entries.Length == 0)
            {
                return;
            }

            // Walk the flat entry array without LINQ or Dictionary allocation.
            // The hash has exactly 3 fields: username, ip, connected_at.
            string username = "";
            string ip = "";
            DateTimeOffset connectedAt = disconnectedAt;

            foreach (var entry in entries)
            {
                var name = (string?)entry.Name;

                if (name == "username")
                {
                    username = (string?)entry.Value ?? "";
                }
                else if (name == "ip")
                {
                    ip = (string?)entry.Value ?? "";
                }
                else if (name == "connected_at")
                {
                    if ((long?)entry.Value is long ms)
                    {
                        connectedAt = DateTimeOffset.FromUnixTimeMilliseconds(ms);
                    }
                }
            }

            await using var cmd = db.CreateCommand(
                """
                INSERT INTO sessions (uuid, username, ip_address, connected_at, disconnected_at)
                VALUES ($1, $2, $3, $4, $5)
                """);

            cmd.Parameters.AddWithValue(uuid);
            cmd.Parameters.AddWithValue(username);
            cmd.Parameters.AddWithValue(ip);
            cmd.Parameters.AddWithValue(connectedAt.UtcDateTime);
            cmd.Parameters.AddWithValue(disconnectedAt.UtcDateTime);

            await cmd.ExecuteNonQueryAsync(ct);
            await redis.KeyDeleteAsync(key);
        }

        private static RedisKey Key(Guid uuid) => $"session:{uuid:N}";
    }
}