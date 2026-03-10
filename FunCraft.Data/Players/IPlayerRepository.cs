namespace FunCraft.Data.Players
{
    public interface IPlayerRepository
    {
        /// <summary>
        /// Returns the player's persisted record, or null if they have never joined.
        /// </summary>
        Task<PlayerRecord?> GetByUuidAsync(Guid uuid, CancellationToken ct = default);

        /// <summary>
        /// Inserts or updates the player record (upsert on uuid).
        /// </summary>
        Task SaveAsync(PlayerRecord player, CancellationToken ct = default);
    }
}