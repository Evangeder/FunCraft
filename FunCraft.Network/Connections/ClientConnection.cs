using System.Buffers;
using System.IO.Pipelines;
using System.Net;
using System.Text;
using System.Net.Sockets;
using System.Threading.Channels;

namespace FunCraft.Network.Connections
{
    using FunCraft.World;
    using Commands;
    using Data.Players;
    using Data.Sessions;
    using Data.Inventory;
    using Entities;
    using Handlers;
    using Physics;
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

        // ── Reliable send channel (bounded) ──────────────────────────────────────
        // For all packets that must be delivered exactly once and in order:
        // chunks, entity spawns/despawns, inventory, chat, keep-alive.
        //
        // Capacity: 8192 frames covers join burst (~30 frames for chunks + spawns) for
        // any reasonable population. When exceeded the connection is actively fallen
        // behind (not reading TCP data) and is disconnected via CloseConnection().
        private const int ReliableChannelCapacity = 8192;

        private readonly Channel<ReadOnlyMemory<byte>> _sendChannel =
            Channel.CreateBounded<ReadOnlyMemory<byte>>(
                new BoundedChannelOptions(ReliableChannelCapacity)
                {
                    SingleReader = true,
                    SingleWriter = false,
                    FullMode = BoundedChannelFullMode.Wait,
                });

        // ── Lock-free movement supersession slot arrays ──────────────────────────
        //
        // At 1000 bots, BroadcastMovementAsync writes to 999 connections per packet.
        // 999 threads simultaneously trying to acquire each connection's SpinLock
        // caused O(N²) lock contention — 32% CPU on Dictionary.set_Item + 13% on
        // SpinLock at 1k bots.
        //
        // Fix: byte[] is a reference type. On all .NET-supported platforms,
        // reference-sized writes are naturally atomic and Volatile.Write adds the
        // required release fence. No lock is needed; concurrent writes to the same
        // slot are safe — supersession semantics mean the latest write wins, and
        // both are valid position updates for the same entity.
        //
        // Key encoding:
        //   movementKey >= 0  → body slot _bodySlots[key]   (pos/rot/teleport)
        //   movementKey <  0  → head slot _headSlots[~key]  (head rotation)
        //
        // Capacity: 4096 covers entity IDs 0..4095 — far more than any real deployment.
        // Entity IDs are sequential from EntityIdSource so no collision is possible.
        private const int MovementSlotCount = 4096;

        private readonly byte[]?[] _bodySlots = new byte[]?[MovementSlotCount];
        private readonly byte[]?[] _headSlots = new byte[]?[MovementSlotCount];

        // High-water marks. DrainAsync only iterates 0..highWater — no wasted
        // null-check iterations beyond the highest entity ID seen this session.
        // Updated with Interlocked.Max so they only grow, never shrink.
        private int _bodyHighWater;
        private int _headHighWater;

        // Interlocked gate for the wake signal.
        // 0 = no signal pending, 1 = signal sent, pending drain.
        // EnqueueMovementFrame calls TryWrite at most ONCE per drain cycle.
        private int _movementPending;

        // Single-item channel. DrainAsync waits here when both the reliable channel
        // and all movement slots are empty. Writers signal it via _movementPending gate.
        private readonly Channel<byte> _movementWake =
            Channel.CreateBounded<byte>(new BoundedChannelOptions(1)
            {
                FullMode = BoundedChannelFullMode.DropWrite,
            });

        // Set to 1 the first time CloseConnection() is called to prevent double-close.
        private int _closed;

        private readonly PlayerContext _ctx;
        private readonly IPlayerRepository _players;
        private readonly IInventoryRepository _inventory;
        private readonly ISessionStore _sessions;
        private readonly IPlayerRegistry _registry;
        private readonly IEntityManager _entities;
        private readonly IPhysicsEngine _physics;

        private readonly HandshakeHandler _handshakeHandler;
        private readonly StatusHandler _statusHandler;
        private readonly LoginHandler _loginHandler;
        private readonly ConfigurationHandler _configurationHandler;
        private readonly PlayHandler _playHandler;

        private ConnectionState _connectionState = ConnectionState.Handshaking;

        public ClientConnection(Socket socket, IWorldSource world, IPlayerRepository players, IInventoryRepository inventory,
            ISessionStore sessions, IPlayerRegistry registry, CommandDispatcher commands,
            ReadOnlyMemory<byte> welcomeMessage, string[] motd, int maxPlayers,
            IEntityManager entities, IPhysicsEngine physics)
        {
            _socket = socket;
            _players = players;
            _inventory = inventory;
            _sessions = sessions;
            _registry = registry;
            _entities = entities;
            _physics = physics;

            _ctx = new PlayerContext
            {
                IpAddress = (socket.RemoteEndPoint as IPEndPoint)?.Address
            };

            _handshakeHandler = new HandshakeHandler { Sender = this };
            _statusHandler = new StatusHandler(motd, maxPlayers, registry) { Sender = this };
            _loginHandler = new LoginHandler(_ctx, sessions) { Sender = this };
            _configurationHandler = new ConfigurationHandler { Sender = this };
            _playHandler = new PlayHandler(world, _ctx, players, inventory, registry, commands, welcomeMessage, entities, physics) { Sender = this };
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

        // 512 segments per writev() syscall. At ~30 bytes/frame, one call sends
        // ~15 KB. For 2000 pending movement slots: 4 scatter-gather sends vs 32
        // at the old MaxCoalesce = 64.
        private const int MaxCoalesce = 512;
        private readonly List<ArraySegment<byte>> _sendBuffers = new(MaxCoalesce);

        private async Task DrainAsync(CancellationToken ct)
        {
            var reliableReader = _sendChannel.Reader;
            var wakeReader = _movementWake.Reader;

            var reliableWaitTask = reliableReader.WaitToReadAsync(ct).AsTask();
            var wakeWaitTask = wakeReader.WaitToReadAsync(ct).AsTask();

            try
            {
                while (!ct.IsCancellationRequested)
                {
                    _sendBuffers.Clear();

                    // ── Movement supersession slots ───────────────────────────────
                    // Reset pending flag BEFORE reading slots so any frame written
                    // after this point either: lands in a slot we haven't passed yet
                    // (processed this cycle), OR lands after we pass it and correctly
                    // re-signals via the Interlocked gate (processed next cycle).
                    Volatile.Write(ref _movementPending, 0);

                    var bodyHigh = Volatile.Read(ref _bodyHighWater);
                    var headHigh = Volatile.Read(ref _headHighWater);

                    for (var i = 0; i < bodyHigh && _sendBuffers.Count < MaxCoalesce; i++)
                    {
                        var frame = Interlocked.Exchange(ref _bodySlots[i], null);

                        if (frame is not null)
                        {
                            _sendBuffers.Add(new ArraySegment<byte>(frame));
                        }
                    }

                    for (var i = 0; i < headHigh && _sendBuffers.Count < MaxCoalesce; i++)
                    {
                        var frame = Interlocked.Exchange(ref _headSlots[i], null);

                        if (frame is not null)
                        {
                            _sendBuffers.Add(new ArraySegment<byte>(frame));
                        }
                    }

                    // ── Reliable frames ───────────────────────────────────────────
                    while (_sendBuffers.Count < MaxCoalesce && reliableReader.TryRead(out var reliableFrame))
                    {
                        if (System.Runtime.InteropServices.MemoryMarshal.TryGetArray(reliableFrame, out var seg))
                        {
                            _sendBuffers.Add(seg);
                        }
                    }

                    if (_sendBuffers.Count > 0)
                    {
                        try
                        {
                            if (_sendBuffers.Count == 1)
                            {
                                var mem = _sendBuffers[0].AsMemory();

                                while (mem.Length > 0)
                                {
                                    var sent = await _socket.SendAsync(mem, ct);
                                    mem = mem[sent..];
                                }
                            }
                            else
                            {
                                // All segments in one writev() syscall.
                                await _socket.SendAsync(_sendBuffers, SocketFlags.None);
                            }
                        }
                        catch
                        {
                            break;
                        }

                        continue;
                    }

                    // ── Idle wait ─────────────────────────────────────────────────
                    var completed = await Task.WhenAny(reliableWaitTask, wakeWaitTask);

                    if (completed == reliableWaitTask)
                    {
                        if (!ct.IsCancellationRequested)
                        {
                            reliableWaitTask = reliableReader.WaitToReadAsync(ct).AsTask();
                        }
                    }

                    if (completed == wakeWaitTask)
                    {
                        while (wakeReader.TryRead(out _)) { }

                        if (!ct.IsCancellationRequested)
                        {
                            wakeWaitTask = wakeReader.WaitToReadAsync(ct).AsTask();
                        }
                    }
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
                // 16 KB hint: fewer BufferSegment objects promoted to Gen2.
                var buffer = _pipe.Writer.GetMemory(16_384);
                var bytesRead = await _socket.ReceiveAsync(buffer, ct);

                if (bytesRead == 0)
                {
                    break;
                }

                _pipe.Writer.Advance(bytesRead);
                var result = await _pipe.Writer.FlushAsync(ct);

                if (result.IsCompleted)
                {
                    break;
                }
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

                if (result.IsCompleted)
                {
                    break;
                }
            }

            await _pipe.Reader.CompleteAsync();
        }

        private static bool TryReadPacket(ref ReadOnlySequence<byte> buffer,
            out int packetId, out ReadOnlySequence<byte> payload)
        {
            if (buffer.Length > 0 && buffer.FirstSpan[0] == LegacyPingPacket)
            {
                buffer = buffer.Slice(buffer.Length);
                packetId = 0;
                payload = default;
                return false;
            }

            packetId = 0;
            payload = default;

            var reader = new SequenceReader<byte>(buffer);

            if (!VarInt.TryRead(ref reader, out var length))
            {
                return false;
            }

            if (reader.Remaining < length)
            {
                return false;
            }

            var beforeId = reader.Consumed;

            if (!VarInt.TryRead(ref reader, out packetId))
            {
                return false;
            }

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
            {
                await OnStateEnteredAsync(_connectionState, ct);
            }
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

                    await _registry.BroadcastRawAsync(new RemoveEntitiesPacket
                    {
                        EntityIds = [_ctx.EntityId]
                    }, _ctx.Uuid);

                    _physics.Unregister(_ctx.EntityId);
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
            _movementWake.Writer.TryComplete();
            await _pipe.Reader.CompleteAsync();
            await _pipe.Writer.CompleteAsync();
        }

        // ─── IPacketSender ────────────────────────────────────────────────────────

        /// <summary>
        /// Closes the socket so all three async loops (FillPipe, ReadPipe, Drain)
        /// exit immediately. Called when the reliable send channel is full — the
        /// client is not reading fast enough to be kept alive.
        /// </summary>
        private void CloseConnection()
        {
            if (Interlocked.Exchange(ref _closed, 1) == 0)
            {
                try
                {
                    _socket.Close();
                }
                catch { }
            }
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

            if (!_sendChannel.Writer.TryWrite(buf.AsMemory()))
            {
                CloseConnection();
            }

            return ValueTask.CompletedTask;
        }

        public ValueTask SendRawAsync(ReadOnlyMemory<byte> framed, CancellationToken ct)
        {
            if (System.Runtime.InteropServices.MemoryMarshal.TryGetArray(framed, out _))
            {
                if (!_sendChannel.Writer.TryWrite(framed))
                {
                    CloseConnection();
                }
            }
            else
            {
                if (!_sendChannel.Writer.TryWrite(framed.ToArray().AsMemory()))
                {
                    CloseConnection();
                }
            }

            return ValueTask.CompletedTask;
        }

        public void EnqueueRaw(ReadOnlyMemory<byte> framed)
        {
            if (!_sendChannel.Writer.TryWrite(framed))
            {
                CloseConnection();
            }
        }

        // Standard CAS-loop max for int — Interlocked.Max(int) does not exist in .NET.
        private static void InterlockedSetIfGreater(ref int location, int value)
        {
            int current;

            do
            {
                current = Volatile.Read(ref location);

                if (current >= value)
                {
                    return;
                }
            }
            while (Interlocked.CompareExchange(ref location, value, current) != current);
        }

        public void EnqueueMovementFrame(int movementKey, byte[] frame)
        {
            // Pure Volatile.Write — no lock, no allocation, no CAS.
            // byte[] is a reference type: reference writes are naturally atomic
            // on all .NET-supported architectures. Volatile.Write adds the release
            // fence so DrainAsync's Interlocked.Exchange (full fence) sees the write.
            //
            // Concurrent writes to the same slot are safe: the latest write wins,
            // which is exactly correct for supersession semantics.
            if (movementKey >= 0)
            {
                if (movementKey >= MovementSlotCount)
                {
                    return;
                }

                Volatile.Write(ref _bodySlots[movementKey], frame);
                InterlockedSetIfGreater(ref _bodyHighWater, movementKey + 1);
            }
            else
            {
                var idx = ~movementKey;

                if (idx >= MovementSlotCount)
                {
                    return;
                }

                Volatile.Write(ref _headSlots[idx], frame);
                InterlockedSetIfGreater(ref _headHighWater, idx + 1);
            }

            // Signal DrainAsync at most once per drain cycle.
            // The Interlocked gate means TryWrite is called ≤1 time between each
            // pair of Volatile.Write(ref _movementPending, 0) calls in DrainAsync.
            if (Interlocked.Exchange(ref _movementPending, 1) == 0)
            {
                _movementWake.Writer.TryWrite(0);
            }
        }
    }
}