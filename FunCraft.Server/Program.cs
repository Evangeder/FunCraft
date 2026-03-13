using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace FunCraft.Server
{
    using Data;
    using Network.Commands;
    using Network.Players;
    using Network.Server;
    using Protocol.Registry;
    using World;
    using WorldGen;

    internal class Program
    {
        private static async Task Main(string[] args)
        {
            var host = Host.CreateDefaultBuilder(args)
                .ConfigureAppConfiguration((_, cfg) =>
                {
                    cfg.AddJsonFile("server.json", optional: true, reloadOnChange: false);
                })
                .ConfigureServices((ctx, services) =>
                {
                    var cfg = ctx.Configuration;

                    services.AddFunCraftData(
                        postgresConnectionString: cfg["ConnectionStrings:Postgres"]
                            ?? "Host=localhost;Database=funcraft;Username=funcraft;Password=funcraft",
                        redisConnectionString: cfg["ConnectionStrings:Redis"]
                            ?? "localhost:6379");

                    services.AddSingleton<IWorldSource, FlatWorldGenerator>();
                    services.AddSingleton<IPlayerRegistry, PlayerRegistry>();
                    services.AddSingleton(sp => new CommandDispatcher());

                    services.AddHostedService<MinecraftServer>();
                })
                .ConfigureLogging(logging =>
                {
                    logging.AddConsole(options =>
                        options.LogToStandardErrorThreshold = LogLevel.Information);
                })
                .Build();

            await host.Services
                .GetRequiredService<Data.Migrations.DbMigrator>()
                .MigrateAsync();

            RegistryLoader.Load();
            await host.RunAsync();
        }
    }
}