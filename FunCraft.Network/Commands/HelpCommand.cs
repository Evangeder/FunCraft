namespace FunCraft.Network.Commands
{
    using Connections;

    public sealed class HelpCommand(CommandDispatcher dispatcher) : ICommand
    {
        public ReadOnlySpan<byte> Name => "help"u8;
        public ReadOnlySpan<byte> Description => "Lists all available commands."u8;

        private static readonly byte[] Header = "§6--- Available Commands ---"u8.ToArray();

        public async Task ExecuteAsync(
            ReadOnlyMemory<byte> args,
            Func<ReadOnlyMemory<byte>, Task> respond,
            IPacketSender sender,
            CancellationToken ct)
        {
            await respond(Header);
            foreach (var cmd in dispatcher.All)
                await respond(BuildHelpLine(cmd));
        }

        /// <summary>
        /// Builds "§e/&lt;name&gt; §7— &lt;description&gt;" into a freshly allocated
        /// <c>byte[]</c>.  Returning <see cref="ReadOnlyMemory{T}"/> (not a span)
        /// keeps ownership clear and eliminates any risk of use-after-free.
        /// </summary>
        private static ReadOnlyMemory<byte> BuildHelpLine(ICommand cmd)
        {
            const int prefixLen = 5;
            const int sepLen = 9;

            var totalLen = prefixLen + cmd.Name.Length + sepLen + cmd.Description.Length;
            var buffer = new byte[totalLen];
            var span = buffer.AsSpan();

            "§e/"u8.CopyTo(span);
            var pos = prefixLen;

            cmd.Name.CopyTo(span[pos..]);
            pos += cmd.Name.Length;

            " §7— "u8.CopyTo(span[pos..]);
            pos += sepLen;

            cmd.Description.CopyTo(span[pos..]);

            return buffer;
        }
    }
}