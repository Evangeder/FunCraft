using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using StackExchange.Redis;

namespace FunCraft.Data
{
    using Migrations;
    using Players;
    using Sessions;

    public static class DataServicesExtensions
    {
        /// <summary>
        /// Registers Postgres, Redis, the migrator, and all repositories.
        /// </summary>
        /// <param name="postgresConnectionString">
        ///   e.g. <c>Host=localhost;Database=funcraft;Username=funcraft;Password=funcraft</c>
        /// </param>
        /// <param name="redisConnectionString">
        ///   e.g. <c>localhost:6379</c>
        /// </param>
        public static IServiceCollection AddFunCraftData(
            this IServiceCollection services,
            string postgresConnectionString,
            string redisConnectionString)
        {
            // Npgsql data source — pooled, reused for the process lifetime.
            var dataSource = NpgsqlDataSource.Create(postgresConnectionString);
            services.AddSingleton(dataSource);

            // Redis multiplexer — also process-lifetime singleton.
            var muxer = ConnectionMultiplexer.Connect(redisConnectionString);
            services.AddSingleton<IConnectionMultiplexer>(muxer);
            services.AddSingleton(muxer.GetDatabase());

            services.AddSingleton<DbMigrator>();
            services.AddSingleton<IPlayerRepository, PostgresPlayerRepository>();
            services.AddSingleton<ISessionStore, RedisSessionStore>();

            return services;
        }
    }
}