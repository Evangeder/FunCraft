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

            if (entries.Length > 0)
            {
                var map = entries.ToDictionary(e => (string)e.Name!, e => e.Value);
                var username = (string?)map.GetValueOrDefault("username") ?? "";
                var ip = (string?)map.GetValueOrDefault("ip") ?? "";
                var connectedAt = map.TryGetValue("connected_at", out var ms)
                    ? DateTimeOffset.FromUnixTimeMilliseconds((long)ms)
                    : disconnectedAt;

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
        }

        private static RedisKey Key(Guid uuid) => $"session:{uuid:N}";
    }
}