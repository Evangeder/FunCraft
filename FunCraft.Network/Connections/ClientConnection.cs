using System.Buffers;
using System.IO.Pipelines;
using System.Net;
using System.Text;
using System.Net.Sockets;
using System.Threading.Channels;

namespace FunCraft.Network.Connections
{
    using Commands;
    using Data.Players;
    using Data.Sessions;
    using FunCraft.Data.Inventory;
    using global::FunCraft.World;
    using Handlers;
    using Players;
    using Protocol.IO;
    using Protocol.Packets;
    using Protocol.Packets.Play.Outgoing;
    using Protocol.Types;

    public sealed class ClientConnection : IPacketSender, IAsyncDisposable
    {
        private const byte LegacyPingPacket = 0xFE;

        private readonly Socket _socket;
        private readonly Pipe _pipe = new();

        // Limits concurrent Postgres persist operations on disconnect.
        // Postgres default max_connections = 100; keep headroom for reads.
        private static readonly SemaphoreSlim _persistGate = new(20, 20);

        // Unbounded channel — single reader (DrainAsync), multiple writers.
        // Each item is a self-contained byte[] frame; the drain loop sends them
        // sequentially without any lock contention.
        private readonly Channel<ReadOnlyMemory<byte>> _sendChannel =
            Channel.CreateUnbounded<ReadOnlyMemory<byte>>(
                new UnboundedChannelOptions { SingleReader = true, SingleWriter = false });

        private readonly PlayerContext _ctx;
        private readonly IPlayerRepository _players;
        private readonly IInventoryRepository _inventory;
        private readonly ISessionStore _sessions;
        private readonly IPlayerRegistry _registry;

        private readonly HandshakeHandler _handshakeHandler;
        private readonly StatusHandler _statusHandler;
        private readonly LoginHandler _loginHandler;
        private readonly ConfigurationHandler _configurationHandler;
        private readonly PlayHandler _playHandler;

        private ConnectionState _connectionState = ConnectionState.Handshaking;

        public ClientConnection(Socket socket, IWorldSource world, IPlayerRepository players, IInventoryRepository inventory,
            ISessionStore sessions, IPlayerRegistry registry, CommandDispatcher commands, string serverName, string motd, int maxPlayers)
        {
            _socket = socket;
            _players = players;
            _inventory = inventory;
            _sessions = sessions;
            _registry = registry;

            _ctx = new PlayerContext
            {
                IpAddress = (socket.RemoteEndPoint as IPEndPoint)?.Address
            };

            _handshakeHandler = new HandshakeHandler { Sender = this };
            _statusHandler = new StatusHandler(motd, maxPlayers, registry) { Sender = this };
            _loginHandler = new LoginHandler(_ctx, sessions) { Sender = this };
            _configurationHandler = new ConfigurationHandler { Sender = this };
            _playHandler = new PlayHandler(world, _ctx, players, inventory, registry, commands) { Sender = this };
        }

        public async Task RunAsync(CancellationToken ct)
        {
            // Per-connection CTS: when Fill or Read finishes (socket dead), cancel Drain
            // so WaitToReadAsync unblocks and the connection can be disposed.
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            var token = cts.Token;
            try
            {
                await Task.WhenAll(
                    CancelOnComplete(FillPipeAsync(token), cts),
                    CancelOnComplete(ReadPipeAsync(token), cts),
                    DrainAsync(token));
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested) { }
            catch (Exception) { }
        }

        // Cancels the CTS when the wrapped task finishes (for any reason).
        private static async Task CancelOnComplete(Task task, CancellationTokenSource cts)
        {
            try
            {
                await task;
            }
            catch { }
            finally
            {
                cts.Cancel();
            }
        }

        // ─── Send drain ──────────────────────────────────────────────────────────

        // Reused across iterations — cleared each time, never grows unboundedly
        // because we cap at MaxCoalesce frames per send.
        private const int MaxCoalesce = 64;
        private readonly List<ArraySegment<byte>> _sendBuffers = new(MaxCoalesce);

        private async Task DrainAsync(CancellationToken ct)
        {
            var reader = _sendChannel.Reader;
            try
            {
                while (await reader.WaitToReadAsync(ct))
                {
                    // Coalesce all queued frames (up to MaxCoalesce) into one scatter-gather send.
                    // Socket.SendAsync(IList<ArraySegment<byte>>) maps to a single writev() syscall
                    // regardless of how many segments are in the list.
                    _sendBuffers.Clear();
                    while (_sendBuffers.Count < MaxCoalesce && reader.TryRead(out var framed))
                    {
                        if (System.Runtime.InteropServices.MemoryMarshal.TryGetArray(framed, out var seg))
                            _sendBuffers.Add(seg);
                    }

                    if (_sendBuffers.Count == 0) continue;

                    try
                    {
                        if (_sendBuffers.Count == 1)
                        {
                            // Fast path — single frame, no list overhead.
                            var mem = _sendBuffers[0].AsMemory();
                            while (mem.Length > 0)
                            {
                                var sent = await _socket.SendAsync(mem, ct);
                                mem = mem[sent..];
                            }
                        }
                        else
                        {
                            // Scatter-gather — all frames in one syscall.
                            await _socket.SendAsync(_sendBuffers, SocketFlags.None);
                        }
                    }
                    catch
                    {
                        break;
                    } // socket dead — exit drain
                }
            }
            catch (OperationCanceledException)
            {
                // connection closed
            }
        }

        // ─── Pipe ────────────────────────────────────────────────────────────────

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
                    await _registry.BroadcastRawAsync(new PlayerInfoRemovePacket
                    {
                        Uuids = [_ctx.Uuid]
                    }, _ctx.Uuid);
                }
                catch
                {
                     /* server may be shutting down */
                }

                await _persistGate.WaitAsync();
                try
                {
                    await _players.SaveAsync(new global::FunCraft.Data.Players.PlayerRecord
                    {
                        Uuid = _ctx.Uuid,
                        Username = Encoding.UTF8.GetString(_ctx.Username.Span),
                        X = _ctx.X,
                        Y = _ctx.Y,
                        Z = _ctx.Z,
                        Yaw = _ctx.Yaw,
                        Pitch = _ctx.Pitch,
                    });
                    await _inventory.SaveInventoryAsync(_ctx.Uuid, _ctx.Inventory.AsMemory());
                    await _sessions.EndAsync(_ctx.Uuid, DateTimeOffset.UtcNow);
                }
                catch (Exception ex)
                {
                    //Console.WriteLine($"[ClientConnection] persist failed: {ex}");
                }
                finally
                {
                    _persistGate.Release();
                }
            }

            _socket.Shutdown(SocketShutdown.Both);
            _socket.Dispose();
            _sendChannel.Writer.TryComplete();
            await _pipe.Reader.CompleteAsync();
            await _pipe.Writer.CompleteAsync();
        }

        public ValueTask SendAsync(IPacket packet, CancellationToken ct)
        {
            var payloadLength = packet.GetLength();
            var packetIdLength = VarInt.GetSize(packet.PacketId);
            var totalLength = payloadLength + packetIdLength;
            var frameLength = VarInt.GetSize(totalLength) + totalLength;

            var buf = new byte[frameLength];
            var writer = new PacketWriter(buf);
            writer.WriteVarInt(totalLength);
            writer.WriteVarInt(packet.PacketId);
            packet.Write(buf.AsSpan(writer.BytesWritten), out _);

            _sendChannel.Writer.TryWrite(buf.AsMemory());
            return ValueTask.CompletedTask;
        }

        public ValueTask SendRawAsync(ReadOnlyMemory<byte> framed, CancellationToken ct)
        {
            var copy = framed.ToArray();
            _sendChannel.Writer.TryWrite(copy.AsMemory());
            return ValueTask.CompletedTask;
        }

        public void EnqueueRaw(ReadOnlyMemory<byte> framed)
        {
            _sendChannel.Writer.TryWrite(framed);
        }
    }
}