namespace FunCraft.Network.Commands
{
    using Connections;

    public sealed class HelpCommand(params CommandDispatcher[] dispatchers) : ICommand
    {
        public ReadOnlySpan<byte> Name => "help"u8;
        public ReadOnlySpan<byte> Description => "Lists all available commands."u8;

        private static readonly byte[] Header = "§6--- Available Commands ---"u8.ToArray();
        private static readonly byte[] Prefix = "§e/"u8.ToArray();
        private static readonly byte[] Separator = " §7\u2014 "u8.ToArray();

        public async Task ExecuteAsync(
            ReadOnlyMemory<byte> args,
            Func<ReadOnlyMemory<byte>, Task> respond,
            IPacketSender sender,
            CancellationToken ct)
        {
            await respond(Header);
            foreach (var dispatcher in dispatchers)
            foreach (var cmd in dispatcher.All)
                await respond(BuildHelpLine(cmd));
        }

        private static ReadOnlyMemory<byte> BuildHelpLine(ICommand cmd)
        {
            var totalLen = Prefix.Length + cmd.Name.Length + Separator.Length + cmd.Description.Length;
            var buffer = new byte[totalLen];
            var pos = 0;

            Prefix.CopyTo(buffer.AsSpan(pos)); pos += Prefix.Length;
            cmd.Name.CopyTo(buffer.AsSpan(pos)); pos += cmd.Name.Length;
            Separator.CopyTo(buffer.AsSpan(pos)); pos += Separator.Length;
            cmd.Description.CopyTo(buffer.AsSpan(pos));

            return buffer;
        }
    }
}