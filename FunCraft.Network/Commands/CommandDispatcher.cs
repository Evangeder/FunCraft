namespace FunCraft.Network.Commands
{
    using Connections;

    /// <summary>
    /// Routes incoming command messages to registered <see cref="ICommand"/> handlers.
    ///
    /// <para>
    /// Dispatch is intentionally allocation-free on the hot path: commands are stored
    /// as a plain array and matched with <see cref="ReadOnlySpan{T}.SequenceEqual"/>
    /// on the raw UTF-8 name bytes, avoiding any managed string allocation.
    /// With typical command counts (4-20 entries) a linear scan is faster than a
    /// hash-map lookup due to cache locality and the elimination of string decoding.
    /// </para>
    /// </summary>
    public sealed class CommandDispatcher
    {
        // Snapshot rebuilt on each Register() call. Written under _lock,
        // read lock-free via Volatile (array reference reads are atomic on .NET).
        private readonly object _lock = new();
        private ICommand[] _snapshot = [];

        /// <summary>All currently registered commands.</summary>
        public IReadOnlyList<ICommand> All => _snapshot;

        /// <summary>
        /// Register a command. Safe to call concurrently with other <see cref="Register"/>
        /// calls; dispatch does not need to be suspended.
        /// </summary>
        public CommandDispatcher Register(ICommand command)
        {
            lock (_lock)
            {
                // Guard against double-registration by name.
                foreach (var existing in _snapshot)
                {
                    if (existing.Name.SequenceEqual(command.Name))
                    {
                        return this;
                    }
                }

                var next = new ICommand[_snapshot.Length + 1];
                _snapshot.AsSpan().CopyTo(next);
                next[_snapshot.Length] = command;
                Volatile.Write(ref _snapshot, next);
            }

            return this;
        }

        /// <summary>
        /// Returns <c>true</c> if a matching command was found and executed.
        /// Returns <c>false</c> if the message does not start with <c>/</c> or if no
        /// matching command is registered — so the caller can try another dispatcher
        /// or send its own "unknown command" reply.
        /// <para>Zero heap allocation on this path.</para>
        /// </summary>
        public async Task<bool> TryDispatchAsync(
            ReadOnlyMemory<byte> rawMessage,
            Func<ReadOnlyMemory<byte>, Task> respond,
            IPacketSender sender,
            CancellationToken ct)
        {
            var span = rawMessage.Span;

            if (span.IsEmpty || span[0] != (byte)'/')
            {
                return false;
            }

            var rest = span[1..];
            var spaceIdx = rest.IndexOf((byte)' ');
            var nameSpan = spaceIdx < 0 ? rest : rest[..spaceIdx];

            var args = spaceIdx < 0
                ? ReadOnlyMemory<byte>.Empty
                : rawMessage[(1 + spaceIdx + 1)..];

            // Linear byte-span search — no string decode, no Dictionary lookup.
            var commands = Volatile.Read(ref _snapshot);

            foreach (var command in commands)
            {
                if (!command.Name.SequenceEqual(nameSpan))
                {
                    continue;
                }

                await command.ExecuteAsync(args, respond, sender, ct);
                return true;
            }

            return false;
        }
    }
}