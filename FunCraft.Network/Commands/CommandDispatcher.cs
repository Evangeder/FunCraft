namespace FunCraft.Network.Commands
{
    using Connections;

    public sealed class CommandDispatcher
    {
        private readonly Dictionary<string, ICommand> _commands = new(StringComparer.OrdinalIgnoreCase);

        public IReadOnlyCollection<ICommand> All => _commands.Values;

        public CommandDispatcher Register(ICommand command)
        {
            _commands[command.Name] = command;
            return this;
        }

        public async Task<bool> TryDispatchAsync(string rawMessage, Func<string, Task> respond, IPacketSender sender, CancellationToken ct)
        {
            if (!rawMessage.StartsWith('/')) return false;

            var parts = rawMessage[1..].Split(' ', StringSplitOptions.RemoveEmptyEntries);
            var name = parts.Length > 0 ? parts[0] : string.Empty;
            var args = parts.Length > 1 ? parts[1..] : [];

            if (!_commands.TryGetValue(name, out var command))
            {
                await respond($"§cUnknown command: /{name}. Try /help.");
                return true;
            }

            await command.ExecuteAsync(args, respond, sender, ct);
            return true;
        }
    }
}