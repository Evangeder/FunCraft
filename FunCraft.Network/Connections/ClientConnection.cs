using System.Buffers;
using System.IO.Pipelines;
using System.Net;
using System.Runtime.InteropServices;
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

        // Back-pressure: pause socket reads once the pipe holds 64 KB of unprocessed
        // data. Without this a slow handler stalls ReadPipeAsync while FillPipeAsync
        // keeps allocating BufferSegments indefinitely.
        private static readonly PipeOptions PipeOpts = new PipeOptions(
            pauseWriterThreshold: 64 * 1024,
            resumeWriterThreshold: 16 * 1024,
            useSynchronizationContext: false);

        private readonly Pipe _pipe = new(PipeOpts);

        private static readonly SemaphoreSlim _persistGate = new(20, 20);

        // ── Reliable send channel ─────────────────────────────────────────────────
        private const int ReliableChannelCapacity = 65_536;

        private readonly Channel<ReadOnlyMemory<byte>> _sendChannel =
            Channel.CreateBounded<ReadOnlyMemory<byte>>(
                new BoundedChannelOptions(ReliableChannelCapacity)
                {
                    SingleReader = true,
                    SingleWriter = false,
                    FullMode = BoundedChannelFullMode.DropWrite,
                });

        // ── Per-connection packet processing queue ────────────────────────────────
        //
        // Previously: ReadPipeAsync called await HandlePacketAsync inline.
        // Problem: if any handler is slow (join storm, chunk serialization, Redis),
        // the pipe cannot advance and FillPipeAsync allocates BufferSegments without
        // bound → 6 GB of pipe segments with 1000 bots.
        //
        // Fix: ReadPipeAsync only reads bytes and posts (packetId, payload) to this
        // channel, then advances the pipe immediately. The pipe always drains fast
        // regardless of handler duration. ProcessPacketsAsync handles packets
        // sequentially in order (no concurrency issues on per-connection state).
        //
        // Payload bytes are rented from ArrayPool, copied from the pipe buffer
        // before advancing, and returned after handling. One rent/return per packet.
        private const int PacketQueueCapacity = 4096;

        private readonly Channel<(int PacketId, byte[] Payload, int Length)> _packetQueue =
            Channel.CreateBounded<(int, byte[], int)>(
                new BoundedChannelOptions(PacketQueueCapacity)
                {
                    SingleReader = true,
                    SingleWriter = true,
                    FullMode = BoundedChannelFullMode.Wait,
                });

        // ── Movement supersession slot arrays ────────────────────────────────────
        //
        // object[] (not object?[]) so that Interlocked.Exchange(ref _bodySlots[i], null)
        // resolves to the non-generic Exchange(ref object, object) JIT intrinsic.
        // With object?[], the generic Exchange<object?> path was invoking
        // CastHelpers.LdelemaRef + ChkCastAny on every iteration — 2.6% wasted CPU.
        private const int MovementSlotCount = 1024;
        private readonly object[] _bodySlots = new object[MovementSlotCount];
        private readonly object[] _headSlots = new object[MovementSlotCount];
        private volatile int _movementActive;

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

        public ClientConnection(Socket socket, IWorldSource world, IPlayerRepository players,
            IInventoryRepository inventory, ISessionStore sessions, IPlayerRegistry registry,
            CommandDispatcher commands, ReadOnlyMemory<byte> welcomeMessage, string[] motd,
            int maxPlayers, IEntityManager entities, IPhysicsEngine physics)
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
            _playHandler = new PlayHandler(world, _ctx, players, inventory, registry, commands,
                welcomeMessage, entities, physics)
            { Sender = this };
        }

        public async Task RunAsync(CancellationToken ct)
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            var token = cts.Token;

            try
            {
                await Task.WhenAll(
                    CancelOnComplete(FillPipeAsync(token), cts),
                    CancelOnComplete(ReadPipeAsync(token), cts),
                    CancelOnComplete(ProcessPacketsAsync(token), cts),
                    DrainAsync(token));
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested) { }
            catch (Exception) { }
        }

        private static async Task CancelOnComplete(Task task, CancellationTokenSource cts)
        {
            try { await task; }
            catch { }
            finally { cts.Cancel(); }
        }

        // ─── Pipe I/O ─────────────────────────────────────────────────────────────

        private async Task FillPipeAsync(CancellationToken ct)
        {
            while (!ct.IsCancellationRequested)
            {
                var buffer = _pipe.Writer.GetMemory(4096);
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

        /// <summary>
        /// Reads complete packets from the pipe, copies payloads to pooled arrays,
        /// and posts them to <see cref="_packetQueue"/>. The pipe is ALWAYS advanced
        /// regardless of whether the queue write succeeds — this prevents the pipe from
        /// accumulating unbounded BufferSegments when the packet processor falls behind.
        ///
        /// If the queue is full, the packet is dropped (rented buffer returned to pool).
        /// Under normal load the queue is never full; under extreme overload dropping
        /// a position packet is preferable to 4+ GB of pipe segment accumulation.
        /// </summary>
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
                    // Advance consumed BEFORE the queue write — the payload has been
                    // parsed and the position recorded. Whether or not the write succeeds
                    // the pipe segment is no longer needed.
                    consumed = buffer.Start;

                    var len = (int)payload.Length;
                    var rented = ArrayPool<byte>.Shared.Rent(len);

                    if (len > 0)
                    {
                        payload.CopyTo(rented);
                    }

                    // TryWrite never blocks. If the queue is full the packet is dropped
                    // and the rented buffer is returned immediately.
                    if (!_packetQueue.Writer.TryWrite((packetId, rented, len)))
                    {
                        ArrayPool<byte>.Shared.Return(rented);
                    }
                }

                _pipe.Reader.AdvanceTo(consumed, examined);

                if (result.IsCompleted)
                {
                    break;
                }
            }

            _packetQueue.Writer.TryComplete();
            await _pipe.Reader.CompleteAsync();
        }

        /// <summary>
        /// Processes packets from <see cref="_packetQueue"/> sequentially, preserving
        /// per-connection packet ordering. Runs independently of the pipe I/O tasks
        /// so slow handlers (chunk serialization, Redis, DB) never stall socket reading.
        /// </summary>
        private async Task ProcessPacketsAsync(CancellationToken ct)
        {
            var reader = _packetQueue.Reader;

            try
            {
                while (await reader.WaitToReadAsync(ct))
                {
                    while (reader.TryRead(out var item))
                    {
                        var (packetId, payload, length) = item;

                        try
                        {
                            var sequence = new ReadOnlySequence<byte>(payload, 0, length);
                            await HandlePacketAsync(packetId, sequence, ct);
                        }
                        finally
                        {
                            ArrayPool<byte>.Shared.Return(payload);
                        }
                    }
                }
            }
            catch (OperationCanceledException) { }
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

        // ─── Send drain ──────────────────────────────────────────────────────────

        private const int MaxCoalesce = 512;
        private readonly List<ArraySegment<byte>> _sendBuffers = new(MaxCoalesce);

        private async Task DrainAsync(CancellationToken ct)
        {
            var reliableReader = _sendChannel.Reader;

            using var movementTimer = new PeriodicTimer(TimeSpan.FromMilliseconds(50));
            var reliableWait = reliableReader.WaitToReadAsync(ct).AsTask();
            var movementTick = movementTimer.WaitForNextTickAsync(ct).AsTask();

            try
            {
                while (!ct.IsCancellationRequested)
                {
                    _sendBuffers.Clear();

                    // ── Movement supersession slots ───────────────────────────────
                    if (_movementActive != 0)
                    {
                        _movementActive = 0;

                        for (var i = 0; i < MovementSlotCount && _sendBuffers.Count < MaxCoalesce; i++)
                        {
                            var frame = (byte[]?)Interlocked.Exchange(ref _bodySlots[i], null);

                            if (frame is not null)
                            {
                                _sendBuffers.Add(new ArraySegment<byte>(frame));
                            }
                        }

                        for (var i = 0; i < MovementSlotCount && _sendBuffers.Count < MaxCoalesce; i++)
                        {
                            var frame = (byte[]?)Interlocked.Exchange(ref _headSlots[i], null);

                            if (frame is not null)
                            {
                                _sendBuffers.Add(new ArraySegment<byte>(frame));
                            }
                        }
                    }

                    // ── Reliable frames ───────────────────────────────────────────
                    while (_sendBuffers.Count < MaxCoalesce && reliableReader.TryRead(out var reliableFrame))
                    {
                        if (MemoryMarshal.TryGetArray(reliableFrame, out var seg))
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
                    if (reliableWait.IsCompleted)
                    {
                        reliableWait = reliableReader.WaitToReadAsync(ct).AsTask();
                    }

                    if (movementTick.IsCompleted)
                    {
                        movementTick = movementTimer.WaitForNextTickAsync(ct).AsTask();
                    }

                    await Task.WhenAny(reliableWait, movementTick);
                }
            }
            catch (OperationCanceledException) { }
        }

        // ─── Dispose ─────────────────────────────────────────────────────────────

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
            _packetQueue.Writer.TryComplete();
            await _pipe.Reader.CompleteAsync();
            await _pipe.Writer.CompleteAsync();
        }

        // ─── IPacketSender ────────────────────────────────────────────────────────

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
            if (MemoryMarshal.TryGetArray(framed, out _))
            {
                _sendChannel.Writer.TryWrite(framed);
            }
            else
            {
                _sendChannel.Writer.TryWrite(framed.ToArray().AsMemory());
            }

            return ValueTask.CompletedTask;
        }

        public void EnqueueRaw(ReadOnlyMemory<byte> framed)
        {
            _sendChannel.Writer.TryWrite(framed);
        }

        public void EnqueueMovementFrame(int movementKey, byte[] frame)
        {
            // Volatile.Write<object>(ref array[index], frame) — store-release, no lock.
            // object[] (not object?[]) means Interlocked.Exchange(ref _bodySlots[i], null)
            // in DrainAsync resolves to the non-generic JIT intrinsic (lock xchg),
            // eliminating the CastHelpers.LdelemaRef + ChkCastAny overhead that appeared
            // in the profiler at 2.6% CPU with the previous object?[] declaration.
            if (movementKey >= 0)
            {
                if (movementKey >= MovementSlotCount)
                {
                    return;
                }

                Volatile.Write(ref _bodySlots[movementKey], (object)frame);
            }
            else
            {
                var idx = ~movementKey;

                if (idx >= MovementSlotCount)
                {
                    return;
                }

                Volatile.Write(ref _headSlots[idx], (object)frame);
            }

            _movementActive = 1;
        }
    }
}