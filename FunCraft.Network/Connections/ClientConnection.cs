using System.Buffers;
using System.IO.Pipelines;
using System.Net.Sockets;
using Microsoft.Extensions.Logging;

namespace FunCraft.Network.Connections
{
    using Server;
    using Protocol.Packets;
    using Protocol.Types;
    using Protocol.IO;
    using Handlers;

    public sealed class ClientConnection : IPacketSender, IAsyncDisposable
    {
        private readonly Socket _socket;
        private readonly Pipe _pipe = new Pipe();
        private readonly ILogger _logger;

        private readonly HandshakeHandler _handshakeHandler;
        private readonly StatusHandler _statusHandler;
        private readonly LoginHandler _loginHandler;

        private ConnectionState _connectionState = ConnectionState.Handshaking;

        public ClientConnection(Socket socket, ILogger<ClientConnection> logger)
        {
            _socket = socket;
            _logger = logger;
            _handshakeHandler = new HandshakeHandler();
            _statusHandler = new StatusHandler { Sender = this };
            _loginHandler = new LoginHandler { Sender = this };
        }

        public async Task RunAsync(CancellationToken ct)
        {
            try
            {
                await Task.WhenAll(FillPipeAsync(ct), ReadPipeAsync(ct));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Connection error");
            }
        }

        private async Task FillPipeAsync(CancellationToken ct)
        {
            try
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
            catch (Exception ex)
            {
                _logger.LogError(ex, "FillPipeAsync error");
            }
        }

        private async Task ReadPipeAsync(CancellationToken ct)
        {
            try
            {
                while (!ct.IsCancellationRequested)
                {
                    var result = await _pipe.Reader.ReadAsync(ct);
                    _logger.LogInformation("Pipe read - bytes available: {Bytes}", result.Buffer.Length);
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
            catch (Exception ex)
            {
                _logger.LogError(ex, "ReadPipeAsync error");
            }
        }

        private bool TryReadPacket(ref ReadOnlySequence<byte> buffer, out int packetId,
            out ReadOnlySequence<byte> payload)
        {
            if (buffer.Length > 0 && buffer.FirstSpan[0] == 0xFE)
            {
                buffer = buffer.Slice(buffer.Length);
                packetId = 0;
                payload = default;
                return false;
            }

            _logger.LogInformation("TryReadPacket - buffer length: {Len}", buffer.Length);
            packetId = 0;
            payload = default;

            var reader = new SequenceReader<byte>(buffer);

            var firstBytes = buffer.Slice(0, Math.Min(4, buffer.Length)).ToArray();
            _logger.LogInformation("First bytes: {Bytes}", BitConverter.ToString(firstBytes));

            if (!VarInt.TryRead(ref reader, out var length))
            {
                _logger.LogInformation("Failed to read length VarInt");
                return false;
            }
            _logger.LogInformation("Packet length: {Len}, remaining: {Rem}", length, reader.Remaining);

            if (reader.Remaining < length)
            {
                _logger.LogInformation("Not enough data, need {Need} have {Have}", length, reader.Remaining);
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
            _logger.LogInformation("Packet received - State: {State}, PacketId: {Id}", _connectionState, packetId);
            return _connectionState switch
            {
                ConnectionState.Handshaking => HandleHandshakingAsync(packetId, payload),
                ConnectionState.Status => HandleStatusAsync(packetId, payload, ct),
                ConnectionState.Login => HandleLoginAsync(packetId, payload, ct),
                ConnectionState.Play => HandlePlayAsync(packetId, payload, ct),
                _ => ValueTask.CompletedTask
            };
        }

        private ValueTask HandleHandshakingAsync(int packetId, ReadOnlySequence<byte> payload)
        {
            _connectionState = _handshakeHandler.Handle(packetId, payload);
            return ValueTask.CompletedTask;
        }

        private async ValueTask HandleStatusAsync(int packetId, ReadOnlySequence<byte> payload, CancellationToken ct)
        {
            _connectionState = await _statusHandler.HandleAsync(packetId, payload, ct);
        }

        private async ValueTask HandleLoginAsync(int packetId, ReadOnlySequence<byte> payload, CancellationToken ct)
        {
            _connectionState = await _loginHandler.HandleAsync(packetId, payload, ct);
        }

        private ValueTask HandlePlayAsync(int packetId, ReadOnlySequence<byte> payload, CancellationToken ct)
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

            _logger.LogInformation("SendAsync - packetId:{Id} payloadLen:{Payload} totalLen:{Total} frameLen:{Frame}",
                packet.PacketId, payloadLength, totalLength, frameLength);

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
