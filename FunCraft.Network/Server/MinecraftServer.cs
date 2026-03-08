using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System.Net;
using System.Net.Sockets;

namespace FunCraft.Network.Server
{
    using Connections;

    public class MinecraftServer(IConfiguration config, ILogger<MinecraftServer> logger, ILoggerFactory loggerFactory) : BackgroundService
    {
        private readonly int _port = int.Parse(config["Server:Port"] ?? "25565");

        protected override async Task ExecuteAsync(CancellationToken ct)
        {
            using var listener = new Socket(
                AddressFamily.InterNetworkV6,
                SocketType.Stream,
                ProtocolType.Tcp);

            listener.DualMode = true;
            listener.Bind(new IPEndPoint(IPAddress.IPv6Any, _port));
            listener.Listen(backlog: 128);

            logger.LogInformation("Server listening on port {Port}", _port);

            while (!ct.IsCancellationRequested)
            {
                try
                {
                    var clientSocket = await listener.AcceptAsync(ct);
                    clientSocket.NoDelay = true;
                    _ = HandleConnectionAsync(clientSocket, ct);
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "Error accepting connection");
                }
            }
        }

        private async Task HandleConnectionAsync(Socket socket, CancellationToken ct)
        {
            await using var connection = new ClientConnection(socket, loggerFactory.CreateLogger<ClientConnection>());
            await connection.RunAsync(ct);
        }
    }
}
