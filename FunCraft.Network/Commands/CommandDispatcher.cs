using System.Text;

namespace FunCraft.Network.Commands
{
    using Connections;

    public sealed class CommandDispatcher
    {
        // Command names are ASCII, so a string key is cheap to produce at
        // Register() time and zero-cost to look up per dispatch.
        private readonly Dictionary<string, ICommand> _commands =
            new(StringComparer.OrdinalIgnoreCase);

        public IReadOnlyCollection<ICommand> All => _commands.Values;

        /// <summary>
        /// Register a command. Called once at startup — the one-time UTF-8→string
        /// decode of <see cref="ICommand.Name"/> is intentional and cheap.
        /// </summary>
        public CommandDispatcher Register(ICommand command)
        {
            _commands[Encoding.UTF8.GetString(command.Name)] = command;
            return this;
        }

        /// <summary>
        /// Attempt to dispatch <paramref name="rawMessage"/> as a command.
        /// Returns <c>true</c> if the message starts with <c>/</c> (whether or not a
        /// matching command was found), <c>false</c> if it should be treated as chat.
        /// </summary>
        /// <param name="rawMessage">
        /// Raw UTF-8 bytes of the full message, <b>including</b> the leading slash.
        /// </param>
        /// <param name="respond">Callback that sends a chat reply to the caller.</param>
        public async Task<bool> TryDispatchAsync(
            ReadOnlyMemory<byte> rawMessage,
            Func<ReadOnlyMemory<byte>, Task> respond,
            IPacketSender sender,
            CancellationToken ct)
        {
            var span = rawMessage.Span;
            if (span.IsEmpty || span[0] != (byte)'/') return false;

            // Split "/<name> <args>" on the first space.
            var rest = span[1..];
            var spaceIdx = rest.IndexOf((byte)' ');
            ReadOnlySpan<byte> nameSpan = spaceIdx < 0 ? rest : rest[..spaceIdx];

            // Slice args from the original Memory so it stays alive through await.
            // rawMessage layout: '/' (1) + name (spaceIdx) + ' ' (1) + args
            ReadOnlyMemory<byte> args = spaceIdx < 0
                ? ReadOnlyMemory<byte>.Empty
                : rawMessage.Slice(1 + spaceIdx + 1);

            // One decode per dispatch — names are short ASCII strings.
            var nameStr = Encoding.UTF8.GetString(nameSpan);
            if (!_commands.TryGetValue(nameStr, out var command))
            {
                await respond(Encoding.UTF8.GetBytes($"§cUnknown command: /{nameStr}. Try /help."));
                return true;
            }

            await command.ExecuteAsync(args, respond, sender, ct);
            return true;
        }
    }
}