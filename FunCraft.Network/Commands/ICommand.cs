namespace FunCraft.Network.Commands
{
    using Connections;

    public interface ICommand
    {
        ReadOnlySpan<byte> Name { get; }
        ReadOnlySpan<byte> Description { get; }

        /// <summary>
        /// Execute the command.
        /// <para>
        /// <paramref name="respond"/> sends a System Chat message back to the caller.
        /// It accepts <see cref="ReadOnlyMemory{T}"/> — not <c>ReadOnlySpan</c> — so
        /// that implementations can safely capture it in async continuations.
        /// </para>
        /// <paramref name="sender"/> allows sending arbitrary packets to the caller.
        /// </summary>
        Task ExecuteAsync(ReadOnlyMemory<byte> args, Func<ReadOnlyMemory<byte>, Task> respond,
            IPacketSender sender, CancellationToken ct);
    }
}