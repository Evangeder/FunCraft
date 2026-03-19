namespace FunCraft.Network.Entities
{
    /// <summary>
    /// Monotonically-increasing entity ID source shared across all connections.
    /// Entity IDs must be unique server-wide for the lifetime of the server process.
    /// </summary>
    public static class EntityIdSource
    {
        private static int _next = 1;

        /// <summary>
        /// Atomically allocates and returns the next entity ID.
        /// </summary>
        public static int Next() => Interlocked.Increment(ref _next);
    }
}