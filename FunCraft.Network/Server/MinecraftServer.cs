using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System.Net;
using System.Net.Sockets;
using System.Text;

namespace FunCraft.Network.Server
{
    using Commands;
    using Connections;
    using Data.Inventory;
    using Data.Players;
    using Data.Sessions;
    using Entities;
    using FunCraft.World;
    using Physics;
    using Players;

    public class MinecraftServer(IConfiguration config, ILogger<MinecraftServer> logger, IWorldSource world, IPlayerRepository players,
        IInventoryRepository inventory, ISessionStore sessions, IPlayerRegistry registry, CommandDispatcher commands,
        IEntityManager entities, IPhysicsEngine physics) : BackgroundService
    {
        private readonly int _port = int.Parse(config["Server:Port"] ?? "25565");
        private readonly string _serverName = config["Server:Name"] ?? "FunCraft";
        private readonly int _maxPlayers = int.Parse(config["Server:MaxPlayers"] ?? "20");
        private readonly string[] _motd = BuildMotd(config);
        private readonly ReadOnlyMemory<byte> _welcomeMessage = BuildWelcomeMessage(config);

        protected override async Task ExecuteAsync(CancellationToken ct)
        {
            using var listener = new Socket(
                AddressFamily.InterNetworkV6, SocketType.Stream, ProtocolType.Tcp);

            listener.DualMode = true;
            listener.Bind(new IPEndPoint(IPAddress.IPv6Any, _port));
            listener.Listen(backlog: 128);

            logger.LogInformation("Listening on port {Port} — \"{Name}\"", _port, _serverName);

            while (!ct.IsCancellationRequested)
            {
                try
                {
                    var socket = await listener.AcceptAsync(ct);
                    socket.NoDelay = true;
                    _ = HandleConnectionAsync(socket, ct);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    logger.LogError(ex, "Error accepting connection");
                }
            }
        }

        private async Task HandleConnectionAsync(Socket socket, CancellationToken ct)
        {
            await using var connection = new ClientConnection(
                socket, world, players, inventory, sessions, registry, commands, _welcomeMessage, _motd, _maxPlayers, entities, physics);
            await connection.RunAsync(ct);
        }

        private static string[] BuildMotd(IConfiguration config)
        {
            var lines = config.GetSection("Server:Motd")
                              .GetChildren()
                              .Select(c => c.Value ?? string.Empty)
                              .Where(v => v.Length > 0)
                              .ToArray();

            return lines.Length > 0 ? lines : ["A FunC#raft Server"];
        }

        private static ReadOnlyMemory<byte> BuildWelcomeMessage(IConfiguration config)
        {
            var lines = config.GetSection("Server:WelcomeMessage")
                .GetChildren()
                .Select(c => c.Value ?? string.Empty)
                .Where(v => v.Length > 0)
                .ToArray();

            return lines.Length > 0
                ? Encoding.UTF8.GetBytes(string.Join('\n', lines)).AsMemory()
                : ReadOnlyMemory<byte>.Empty;
        }
    }
}