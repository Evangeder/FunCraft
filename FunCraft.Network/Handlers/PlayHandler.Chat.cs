using System.Buffers;

namespace FunCraft.Network.Handlers
{
    using Players;
    using Protocol.Packets.Play.Incoming;
    using Protocol.Packets.Play.Outgoing;

    internal sealed partial class PlayHandler
    {
        private static readonly byte[] MsgUnknownCommandSuffix = "\u00a7f. Try /help."u8.ToArray();

        private async ValueTask HandleChatAsync(
            ReadOnlySequence<byte> payload, CancellationToken ct)
        {
            var reader = new SequenceReader<byte>(payload);
            var packet = new ChatMessagePacket();

            if (!packet.TryRead(ref reader))
            {
                return;
            }

            var msgSpan = packet.Message.Span;
            var start = 0;

            while (start < msgSpan.Length && msgSpan[start] <= 32)
            {
                start++;
            }

            var end = msgSpan.Length;

            while (end > start && msgSpan[end - 1] <= 32)
            {
                end--;
            }

            if (end <= start)
            {
                return;
            }

            var message = packet.Message[start..(end - start)];

            if (await commands.TryDispatchAsync(message, Respond, Sender, ct))
            {
                return;
            }

            if (message.Span[0] == (byte)'/')
            {
                await Respond(ConcatBytes("\u00a7cUnknown command: "u8, message.Span, MsgUnknownCommandSuffix));
                return;
            }

            var chatLine = ConcatBytes("\u00a77<\u00a7f"u8, ctx.Username.Span, "\u00a77> "u8, message.Span);
            await registry.BroadcastRawAsync(new SystemChatMessagePacket { Content = chatLine }, Guid.Empty, ct);
            return;

            Task Respond(ReadOnlyMemory<byte> text)
                => Sender.SendAsync(new SystemChatMessagePacket { Content = text }, ct).AsTask();
        }

        private async ValueTask HandleChatCommandAsync(
            ReadOnlySequence<byte> payload, CancellationToken ct)
        {
            var reader = new SequenceReader<byte>(payload);
            var packet = new ChatCommandPacket();

            if (!packet.TryRead(ref reader))
            {
                return;
            }

            var cmd = packet.Command;
            var withSlash = new byte[1 + cmd.Length];
            withSlash[0] = (byte)'/';
            cmd.Span.CopyTo(withSlash.AsSpan(1));
            var withSlashMem = withSlash.AsMemory();

            if (await _localCommands.TryDispatchAsync(withSlashMem, Respond, Sender, ct))
            {
                return;
            }

            if (await commands.TryDispatchAsync(withSlashMem, Respond, Sender, ct))
            {
                return;
            }

            await Respond(ConcatBytes("\u00a7cUnknown command: "u8, withSlash, MsgUnknownCommandSuffix));
            return;

            Task Respond(ReadOnlyMemory<byte> text)
                => Sender.SendAsync(new SystemChatMessagePacket { Content = text }, ct).AsTask();
        }

        private static PlayerInfoUpdatePacket BuildInfoUpdate(IReadOnlyList<ConnectedPlayer> players)
        {
            var entries = new PlayerInfoUpdatePacket.PlayerInfoEntry[players.Count];

            for (var i = 0; i < players.Count; i++)
            {
                var p = players[i];
                entries[i] = new PlayerInfoUpdatePacket.PlayerInfoEntry
                {
                    Uuid = p.Uuid,
                    Username = p.Username,
                    Listed = true,
                    Latency = 0,
                };
            }

            return new PlayerInfoUpdatePacket { Players = entries };
        }
    }
}