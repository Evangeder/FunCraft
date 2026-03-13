using System.Text;

namespace FunCraft.Network.Commands
{
    using Connections;

    public sealed class CommandDispatcher
    {
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
        /// Returns <c>true</c> if a matching command was found and executed.
        /// <br/>Returns <c>false</c> if the message doesn't start with <c>/</c> OR if no
        /// matching command is registered — so the caller can try another dispatcher
        /// or send its own "unknown command" reply.
        /// </summary>
        public async Task<bool> TryDispatchAsync(ReadOnlyMemory<byte> rawMessage, Func<ReadOnlyMemory<byte>,
                Task> respond, IPacketSender sender, CancellationToken ct)
        {
            var span = rawMessage.Span;
            if (span.IsEmpty || span[0] != (byte) '/')
            {
                return false;
            }

            var rest = span[1..];
            var spaceIdx = rest.IndexOf((byte)' ');
            var nameSpan = spaceIdx < 0 ? rest : rest[..spaceIdx];

            var args = spaceIdx < 0
                ? ReadOnlyMemory<byte>.Empty
                : rawMessage[(1 + spaceIdx + 1)..];

            var nameStr = Encoding.UTF8.GetString(nameSpan);
            if (!_commands.TryGetValue(nameStr, out var command))
            {
                return false;
            }

            await command.ExecuteAsync(args, respond, sender, ct);

            return true;
        }
    }
}