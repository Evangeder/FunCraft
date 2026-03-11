using System.Buffers;
using System.IO.Pipelines;
using System.Net;
using System.Net.Sockets;

namespace FunCraft.Network.Connections
{
    using Commands;
    using Data.Players;
    using Data.Sessions;
    using Players;
    using Protocol.Packets;
    using Protocol.Packets.Play.Outgoing;
    using Protocol.Types;
    using Protocol.IO;
    using Handlers;

    using global::FunCraft.World;
    using FunCraft.Data.Inventory;

    public sealed class ClientConnection : IPacketSender, IAsyncDisposable
    {
        private const byte LegacyPingPacket = 0xFE;

        private readonly Socket _socket;
        private readonly Pipe _pipe = new();
        private readonly SemaphoreSlim _sendLock = new(1, 1);

        private readonly PlayerContext _ctx;
        private readonly IPlayerRepository _players;
        private readonly ISessionStore _sessions;
        private readonly IPlayerRegistry _registry;

        private readonly HandshakeHandler _handshakeHandler;
        private readonly StatusHandler _statusHandler;
        private readonly LoginHandler _loginHandler;
        private readonly ConfigurationHandler _configurationHandler;
        private readonly PlayHandler _playHandler;

        private ConnectionState _connectionState = ConnectionState.Handshaking;

        public ClientConnection(Socket socket, IWorldSource world, IPlayerRepository players, IInventoryRepository inventory,
            ISessionStore sessions, IPlayerRegistry registry, CommandDispatcher commands, string motd, int maxPlayers)
        {
            _socket = socket;
            _players = players;
            _sessions = sessions;
            _registry = registry;

            _ctx = new PlayerContext
            {
                IpAddress = (socket.RemoteEndPoint as IPEndPoint)?.Address.ToString() ?? "unknown"
            };

            _handshakeHandler = new HandshakeHandler { Sender = this };
            _statusHandler = new StatusHandler(motd, maxPlayers, registry) { Sender = this };
            _loginHandler = new LoginHandler(_ctx, sessions) { Sender = this };
            _configurationHandler = new ConfigurationHandler { Sender = this };
            _playHandler = new PlayHandler(world, _ctx, players, inventory, registry, commands) { Sender = this };
        }

        public async Task RunAsync(CancellationToken ct)
        {
            await Task.WhenAll(FillPipeAsync(ct), ReadPipeAsync(ct));
        }

        private async Task FillPipeAsync(CancellationToken ct)
        {
            while (!ct.IsCancellationRequested)
            {
                var buffer = _pipe.Writer.GetMemory(4096);
                var bytesRead = await _socket.ReceiveAsync(buffer, ct);
                if (bytesRead == 0) break;

                _pipe.Writer.Advance(bytesRead);
                var result = await _pipe.Writer.FlushAsync(ct);
                if (result.IsCompleted) break;
            }
            await _pipe.Writer.CompleteAsync();
        }

        private async Task ReadPipeAsync(CancellationToken ct)
        {
            while (!ct.IsCancellationRequested)
            {
                var result = await _pipe.Reader.ReadAsync(ct);
                var buffer = result.Buffer;
                var consumed = buffer.Start;
                var examined = buffer.End;

                while (TryReadPacket(ref buffer, out var packetId, out var payload))
                {
                    await HandlePacketAsync(packetId, payload, ct);
                    consumed = buffer.Start;
                }

                _pipe.Reader.AdvanceTo(consumed, examined);
                if (result.IsCompleted) break;
            }
            await _pipe.Reader.CompleteAsync();
        }

        private static bool TryReadPacket(ref ReadOnlySequence<byte> buffer,
            out int packetId, out ReadOnlySequence<byte> payload)
        {
            if (buffer.Length > 0 && buffer.FirstSpan[0] == LegacyPingPacket)
            {
                buffer = buffer.Slice(buffer.Length);
                packetId = 0; payload = default;
                return false;
            }

            packetId = 0; payload = default;

            var reader = new SequenceReader<byte>(buffer);
            if (!VarInt.TryRead(ref reader, out var length)) return false;
            if (reader.Remaining < length) return false;

            var beforeId = reader.Consumed;
            if (!VarInt.TryRead(ref reader, out packetId)) return false;

            var idLength = reader.Consumed - beforeId;
            var payloadLength = length - idLength;
            payload = buffer.Slice(reader.Position, payloadLength);
            buffer = buffer.Slice(reader.Position).Slice(payloadLength);
            return true;
        }

        private async ValueTask HandlePacketAsync(int packetId,
            ReadOnlySequence<byte> payload, CancellationToken ct)
        {
            var previous = _connectionState;

            _connectionState = _connectionState switch
            {
                ConnectionState.Handshaking => _handshakeHandler.Handle(packetId, payload),
                ConnectionState.Status => await _statusHandler.HandleAsync(packetId, payload, ct),
                ConnectionState.Login => await _loginHandler.HandleAsync(packetId, payload, ct),
                ConnectionState.Configuration => await _configurationHandler.HandleAsync(packetId, payload, ct),
                ConnectionState.Play => await _playHandler.HandleAsync(packetId, payload, ct),
                _ => _connectionState
            };

            if (_connectionState != previous)
                await OnStateEnteredAsync(_connectionState, ct);
        }

        private async ValueTask OnStateEnteredAsync(ConnectionState state, CancellationToken ct)
        {
            switch (state)
            {
                case ConnectionState.Play:
                    await _playHandler.OnEnterAsync(ct);
                    break;
                case ConnectionState.Configuration:
                    await _configurationHandler.OnEnterAsync(ct);
                    break;
            }
        }

        public async ValueTask DisposeAsync()
        {
            if (_ctx.Uuid != Guid.Empty)
            {
                _registry.Unregister(_ctx.Uuid);
                try
                {
                    await _registry.BroadcastAsync(new PlayerInfoRemovePacket
                    {
                        Uuids = [_ctx.Uuid]
                    });
                }
                catch
                {
                     /* server may be shutting down */
                }

                try
                {
                    await _players.SaveAsync(new global::FunCraft.Data.Players.PlayerRecord
                    {
                        Uuid = _ctx.Uuid,
                        Username = _ctx.Username,
                        X = _ctx.X,
                        Y = _ctx.Y,
                        Z = _ctx.Z,
                        Yaw = _ctx.Yaw,
                        Pitch = _ctx.Pitch,
                    });
                    await _sessions.EndAsync(_ctx.Uuid, DateTimeOffset.UtcNow);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[ClientConnection] persist failed: {ex.Message}");
                }
            }

            _socket.Shutdown(SocketShutdown.Both);
            _socket.Dispose();
            _sendLock.Dispose();
            await _pipe.Reader.CompleteAsync();
            await _pipe.Writer.CompleteAsync();
        }

        public async ValueTask SendAsync(IPacket packet, CancellationToken ct)
        {
            var payloadLength = packet.GetLength();
            var packetIdLength = VarInt.GetSize(packet.PacketId);
            var totalLength = payloadLength + packetIdLength;
            var frameLength = VarInt.GetSize(totalLength) + totalLength;

            var buffer = ArrayPool<byte>.Shared.Rent(frameLength);
            try
            {
                var writer = new PacketWriter(buffer);
                writer.WriteVarInt(totalLength);
                writer.WriteVarInt(packet.PacketId);
                packet.Write(buffer.AsSpan(writer.BytesWritten), out var payloadWritten);

                var memory = buffer.AsMemory(0, writer.BytesWritten + payloadWritten);

                await _sendLock.WaitAsync(ct);
                try
                {
                    while (memory.Length > 0)
                    {
                        var sent = await _socket.SendAsync(memory, ct);
                        memory = memory[sent..];
                    }
                }
                finally
                {
                    _sendLock.Release();
                }
            }
            finally { ArrayPool<byte>.Shared.Return(buffer); }
        }
    }
}