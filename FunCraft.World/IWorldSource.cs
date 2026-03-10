namespace FunCraft.World
{
    using Chunks;

    /// <summary>
    /// Provides chunk data to the network layer.
    /// <br/>Implementations include procedural generators, saved-world loaders, etc.
    /// </summary>
    public interface IWorldSource
    {
        /// <summary>
        /// Returns the chunk column at the given chunk coordinates.
        /// <br/>Generates or loads it if not already cached.
        /// <br/>This method must be thread-safe.
        /// </summary>
        ChunkColumn GetChunk(int chunkX, int chunkZ);
    }
}