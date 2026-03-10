using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace FunCraft.Server
{
    using Data;
    using Network.Server;
    using Protocol.Registry;
    using World;
    using WorldGen;

    internal class Program
    {
        private static async Task Main(string[] args)
        {
            var host = Host.CreateDefaultBuilder(args)
                .ConfigureServices((ctx, services) =>
                {
                    var cfg = ctx.Configuration;

                    // Storage — Postgres + Redis.
                    services.AddFunCraftData(
                        postgresConnectionString: cfg["ConnectionStrings:Postgres"]
                                                  ?? "Host=localhost;Database=funcraft;Username=funcraft;Password=funcraft",
                        redisConnectionString: cfg["ConnectionStrings:Redis"]
                                               ?? "localhost:6379");

                    services.AddSingleton<IWorldSource, FlatWorldGenerator>();
                    services.AddHostedService<MinecraftServer>();
                })
                .ConfigureLogging(logging =>
                {
                    logging.AddConsole(options =>
                    {
                        options.LogToStandardErrorThreshold = LogLevel.Trace;
                    });
                })
                .Build();

            // Run DB migrations before accepting connections.
            await host.Services
                .GetRequiredService<global::FunCraft.Data.Migrations.DbMigrator>()
                .MigrateAsync();

            RegistryLoader.Load();
            await host.RunAsync();
        }
    }
}