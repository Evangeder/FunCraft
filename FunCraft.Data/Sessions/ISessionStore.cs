namespace FunCraft.Data.Sessions
{
    public interface ISessionStore
    {
        /// <summary>
        /// Writes the active session to Redis. TTL = 24 h in case disconnect is never called.
        /// </summary>
        Task SetAsync(PlayerSession session, CancellationToken ct = default);

        /// <summary>
        /// Marks the session ended. Removes the live key and persists end time to Postgres.
        /// </summary>
        Task EndAsync(Guid uuid, DateTimeOffset disconnectedAt, CancellationToken ct = default);
    }
}