using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace FunCraft.Server
{
    using Network.Server;
    using Protocol.Registry;
    using World;
    using WorldGen;

    internal class Program
    {
        private static async Task Main(string[] args)
        {
            var host = Host.CreateDefaultBuilder(args)
                .ConfigureServices(services =>
                {
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

            RegistryLoader.Load();
            await host.RunAsync();
        }
    }
}