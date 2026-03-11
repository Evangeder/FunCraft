namespace FunCraft.Network.Commands
{
    using Connections;

    public sealed class HelpCommand(CommandDispatcher dispatcher) : ICommand
    {
        public string Name => "help";
        public string Description => "Lists all available commands.";

        public async Task ExecuteAsync(
            string[] args, Func<string, Task> respond, IPacketSender sender, CancellationToken ct)
        {
            await respond("§6--- Available Commands ---");
            foreach (var cmd in dispatcher.All.OrderBy(c => c.Name))
                await respond($"§e/{cmd.Name} §7— {cmd.Description}");
        }
    }
}