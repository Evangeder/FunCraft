using System.Buffers;

namespace FunCraft.Network.Handlers
{
    using Protocol.Packets.Play.Incoming;
    using Protocol.Packets.Play.Outgoing;

    internal sealed partial class PlayHandler
    {
        private async Task KeepAliveLoopAsync(CancellationToken ct)
        {
            try
            {
                while (!ct.IsCancellationRequested)
                {
                    await Task.Delay(KeepAliveInterval, ct);
                    _lastKeepAliveId = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
                    _pendingKeepAliveIds.Add(_lastKeepAliveId);
                    await Sender.SendAsync(
                        new ClientboundKeepAlivePacket { KeepAliveId = _lastKeepAliveId }, ct);
                }
            }
            catch (OperationCanceledException)
            {
            }
        }

        private void HandleKeepAlive(ReadOnlySequence<byte> payload)
        {
            var reader = new SequenceReader<byte>(payload);
            var packet = new ServerboundKeepAlivePacket();

            if (!packet.TryRead(ref reader))
            {
                return;
            }

            if (!_pendingKeepAliveIds.Remove(packet.KeepAliveId))
            {
                Console.WriteLine($"[PlayHandler] keep-alive mismatch — unknown ID {packet.KeepAliveId}");
            }
        }

        private static void HandleChunkBatchReceived(ReadOnlySequence<byte> payload)
        {
            var reader = new SequenceReader<byte>(payload);
            var packet = new ChunkBatchReceivedPacket();

            if (packet.TryRead(ref reader))
            {
                Console.WriteLine($"[PlayHandler] client wants {packet.DesiredChunksPerTick:F2} chunks/tick");
            }
        }
    }
}