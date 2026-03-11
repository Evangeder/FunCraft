using System.Linq;
using FunCraft.World;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System.Net;
using System.Net.Sockets;

namespace FunCraft.Network.Server
{
    using Commands;
    using Connections;
    using Data.Players;
    using Data.Sessions;
    using FunCraft.Data.Inventory;
    using Players;

    public class MinecraftServer(IConfiguration config, ILogger<MinecraftServer> logger, IWorldSource world, IPlayerRepository players,
        IInventoryRepository inventory, ISessionStore sessions, IPlayerRegistry registry, CommandDispatcher commands) : BackgroundService
    {
        private readonly int _port = int.Parse(config["Server:Port"] ?? "25565");
        private readonly string _serverName = config["Server:Name"] ?? "FunCraft";
        private readonly int _maxPlayers = int.Parse(config["Server:MaxPlayers"] ?? "20");
        private readonly string _motd = BuildMotd(config);

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
                socket, world, players, inventory, sessions, registry, commands, _serverName, _motd, _maxPlayers);
            await connection.RunAsync(ct);
        }

        private static string BuildMotd(IConfiguration config)
        {
            // GetChildren() is on IConfiguration directly — no Binder package needed.
            var lines = config.GetSection("Server:Motd")
                              .GetChildren()
                              .Select(c => c.Value ?? string.Empty)
                              .Where(v => v.Length > 0)
                              .ToArray();

            return lines.Length > 0
                ? string.Join('\n', lines)
                : config["Server:Motd"] ?? "A FunC#raft Server";
        }
    }
}