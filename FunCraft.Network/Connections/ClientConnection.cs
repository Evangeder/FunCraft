using System.Buffers;
using System.IO.Pipelines;
using System.Net.Sockets;

namespace FunCraft.Network.Connections
{
    using Protocol.Packets;
    using Protocol.Types;
    using Protocol.IO;
    using Handlers;

    public sealed class ClientConnection : IPacketSender, IAsyncDisposable
    {
        private const byte LegacyPingPacket = 0xFE;

        private readonly Socket _socket;
        private readonly Pipe _pipe = new Pipe();

        private readonly HandshakeHandler _handshakeHandler;
        private readonly StatusHandler _statusHandler;
        private readonly LoginHandler _loginHandler;
        private readonly ConfigurationHandler _configurationHandler;

        private ConnectionState _connectionState = ConnectionState.Handshaking;

        public ClientConnection(Socket socket)
        {
            _socket = socket;
            _handshakeHandler = new HandshakeHandler();
            _statusHandler = new StatusHandler { Sender = this };
            _loginHandler = new LoginHandler { Sender = this };
            _configurationHandler = new ConfigurationHandler { Sender = this };
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

        private static bool TryReadPacket(ref ReadOnlySequence<byte> buffer, out int packetId,
            out ReadOnlySequence<byte> payload)
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

        private ValueTask HandlePacketAsync(int packetId, ReadOnlySequence<byte> payload, CancellationToken ct)
        {
            return _connectionState switch
            {
                ConnectionState.Handshaking => HandleHandshaking(packetId, payload),
                ConnectionState.Status => HandleStatusAsync(packetId, payload, ct),
                ConnectionState.Login => HandleLoginAsync(packetId, payload, ct),
                ConnectionState.Play => HandlePlay(packetId, payload, ct),
                _ => ValueTask.CompletedTask
            };
        }

        private ValueTask HandleHandshaking(int packetId, ReadOnlySequence<byte> payload)
        {
            _connectionState = HandshakeHandler.Handle(packetId, payload);
            return ValueTask.CompletedTask;
        }

        private async ValueTask HandleStatusAsync(int packetId, ReadOnlySequence<byte> payload, CancellationToken ct)
        {
            _connectionState = await _statusHandler.HandleAsync(packetId, payload, ct);
        }

        private async ValueTask HandleLoginAsync(int packetId, ReadOnlySequence<byte> payload, CancellationToken ct)
        {
            var newState = await _loginHandler.HandleAsync(packetId, payload, ct);
            if (newState != _connectionState)
            {
                _connectionState = newState;
                await OnStateEntered(newState, ct);
            }
        }

        private ValueTask OnStateEntered(ConnectionState state, CancellationToken ct) => state switch
        {
            ConnectionState.Configuration => _configurationHandler.OnEnterAsync(ct),
            _ => ValueTask.CompletedTask
        };

        private ValueTask HandlePlay(int packetId, ReadOnlySequence<byte> payload, CancellationToken ct)
        {
            return ValueTask.CompletedTask;
        }

        public async ValueTask DisposeAsync()
        {
            _socket.Shutdown(SocketShutdown.Both);
            _socket.Dispose();
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
                packet.Write(buffer.AsSpan(writer.BytesWritten), out _);

                await _socket.SendAsync(buffer.AsMemory(0, frameLength), ct);
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(buffer);
            }
        }
    }
}
