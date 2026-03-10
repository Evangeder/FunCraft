using System.Reflection;
using Npgsql;

namespace FunCraft.Data.Migrations
{
    /// <summary>
    /// Lightweight migration runner. Reads SQL files embedded in this assembly,
    /// ordered by their numeric prefix (001_, 002_, …), and applies any that
    /// haven't been recorded in the <c>schema_migrations</c> table yet.
    /// </summary>
    public sealed class DbMigrator(NpgsqlDataSource db)
    {
        private const int FileExtensionLength = 4;

        public async Task MigrateAsync(CancellationToken ct = default)
        {
            await EnsureMigrationsTableAsync(ct);

            var applied = await GetAppliedVersionsAsync(ct);

            var pending = GetEmbeddedMigrations()
                .Where(m => !applied.Contains(m.Version))
                .OrderBy(m => m.Version)
                .ToList();

            foreach (var migration in pending)
            {
                await ApplyAsync(migration, ct);
            }
        }

        private async Task EnsureMigrationsTableAsync(CancellationToken ct)
        {
            await using var cmd = db.CreateCommand(
                """
                CREATE TABLE IF NOT EXISTS schema_migrations (
                    version    INT         PRIMARY KEY,
                    name       TEXT        NOT NULL,
                    applied_at TIMESTAMPTZ NOT NULL DEFAULT NOW()
                )
                """);
            await cmd.ExecuteNonQueryAsync(ct);
        }

        private async Task<HashSet<int>> GetAppliedVersionsAsync(CancellationToken ct)
        {
            await using var cmd = db.CreateCommand("SELECT version FROM schema_migrations");
            await using var reader = await cmd.ExecuteReaderAsync(ct);

            var versions = new HashSet<int>();
            while (await reader.ReadAsync(ct))
                versions.Add(reader.GetInt32(0));
            return versions;
        }

        private static List<(int Version, string Name, string Sql)> GetEmbeddedMigrations()
        {
            var assembly = Assembly.GetExecutingAssembly();
            var prefix = $"{assembly.GetName().Name}.Migrations.SQL.";

            var results = new List<(int, string, string)>();

            foreach (var resourceName in assembly.GetManifestResourceNames())
            {
                if (!resourceName.StartsWith(prefix) || !resourceName.EndsWith(".sql"))
                    continue;

                var fileName = resourceName[prefix.Length..];
                var digits = new string([.. fileName.TakeWhile(char.IsDigit)]);
                if (digits.Length == 0)
                {
                    throw new InvalidOperationException($"Migration resource '{resourceName}' has no numeric version prefix.");
                }

                var version = int.Parse(digits);
                var name = fileName[..^FileExtensionLength];

                using var stream = assembly.GetManifestResourceStream(resourceName)!;
                using var reader = new StreamReader(stream);
                var sql = reader.ReadToEnd();

                results.Add((version, name, sql));
            }

            return results;
        }

        private async Task ApplyAsync((int Version, string Name, string Sql) migration, CancellationToken ct)
        {
            await using var conn = await db.OpenConnectionAsync(ct);
            await using var tr = await conn.BeginTransactionAsync(ct);

            await using (var cmd = conn.CreateCommand())
            {
                cmd.Transaction = tr;
                cmd.CommandText = migration.Sql;
                await cmd.ExecuteNonQueryAsync(ct);
            }

            await using (var cmd = conn.CreateCommand())
            {
                cmd.Transaction = tr;
                cmd.CommandText = "INSERT INTO schema_migrations (version, name) VALUES ($1, $2)";
                cmd.Parameters.AddWithValue(migration.Version);
                cmd.Parameters.AddWithValue(migration.Name);
                await cmd.ExecuteNonQueryAsync(ct);
            }

            await tr.CommitAsync(ct);
        }
    }
}