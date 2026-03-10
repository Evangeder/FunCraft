namespace FunCraft.WorldGen
{
    using System.Collections.Concurrent;
    using World;
    using World.Blocks;
    using World.Chunks;

    /// <summary>
    /// Classic superflat layout:
    /// <list type="bullet">
    /// <item>Y -64 — bedrock</item>
    /// <item>Y -63..-62 — stone (2 layers)</item>
    /// <item>Y -61..-58 — dirt (4 layers, matching vanilla superflat default)</item>
    /// <item>Y -57 — grass_block</item>
    /// </list>
    /// All generated columns are cached so each (x,z) is only built once.
    /// </summary>
    public sealed class FlatWorldGenerator : IWorldSource
    {
        // Absolute Y coords for each layer.
        private const int BedrockY = -64;
        private const int StoneY1 = -63;
        private const int StoneY2 = -11;
        private const int DirtY1 = -10;
        private const int DirtY2 = -1; // inclusive — 4 layers: -61, -60, -59, -58
        private const int GrassY = 0;

        private readonly ConcurrentDictionary<(int, int), ChunkColumn> _cache = new();

        public ChunkColumn GetChunk(int chunkX, int chunkZ)
            => _cache.GetOrAdd((chunkX, chunkZ), static key =>
            {
                var (cx, cz) = key;
                return Generate(cx, cz);
            });

        private static ChunkColumn Generate(int chunkX, int chunkZ)
        {
            var column = new ChunkColumn(chunkX, chunkZ);

            for (var x = 0; x < 16; x++)
                for (var z = 0; z < 16; z++)
                {
                    column.SetBlock(x, BedrockY, z, WellKnownBlocks.Bedrock);

                    for (var y = StoneY1; y <= StoneY2; y++)
                        column.SetBlock(x, y, z, WellKnownBlocks.Stone);

                    for (var y = DirtY1; y <= DirtY2; y++)
                        column.SetBlock(x, y, z, WellKnownBlocks.Dirt);

                    column.SetBlock(x, GrassY, z, WellKnownBlocks.GrassBlock);
                }

            return column;
        }
    }
}