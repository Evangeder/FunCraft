namespace FunCraft.Network.Commands
{
    using Connections;

    public interface ICommand
    {
        string Name { get; }
        string Description { get; }

        /// <summary>
        /// Execute the command.
        /// <paramref name="respond"/> sends a chat message back to the caller.
        /// <paramref name="sender"/> allows sending arbitrary packets to the caller.
        /// </summary>
        Task ExecuteAsync(string[] args, Func<string, Task> respond, IPacketSender sender, CancellationToken ct);
    }
}