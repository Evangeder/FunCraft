using System.Collections.Concurrent;
using FunCraft.World.Blocks;

namespace FunCraft.WorldGen
{
    using Protocol.Registry;
    using World;
    using World.Chunks;

    /// <summary>
    /// Classic superflat world generator with a bounded LRU-evicting column cache.
    /// Evicted columns are disposed so their pooled arrays are returned immediately.
    /// </summary>
    public sealed class FlatWorldGenerator : IWorldSource, IDisposable
    {
        private static readonly byte[] BlockBedrock = "minecraft:bedrock"u8.ToArray();
        private static readonly byte[] BlockStone = "minecraft:stone"u8.ToArray();
        private static readonly byte[] BlockDirt = "minecraft:dirt"u8.ToArray();
        private static readonly byte[] BlockGrass = "minecraft:grass_block"u8.ToArray();

        private static ushort _bedrockId;
        private static ushort _stoneId;
        private static ushort _dirtId;
        private static ushort _grassId;

        public FlatWorldGenerator()
        {
            _bedrockId = RegistryLookup.GetBlockId(BlockBedrock);
            _stoneId = RegistryLookup.GetBlockId(BlockStone);
            _dirtId = RegistryLookup.GetBlockId(BlockDirt);
            _grassId = RegistryLookup.GetBlockId(BlockGrass);
        }

        private const int BedrockY = -64;
        private const int StoneY1 = -63;
        private const int StoneY2 = -11;
        private const int DirtY1 = -10;
        private const int DirtY2 = -1;
        private const int GrassY = 0;

        private const int MaxCachedColumns = 2048;

        private readonly ConcurrentDictionary<(int, int), ChunkColumn> _cache = new();

        private readonly ConcurrentQueue<(int, int)> _evictionQueue = new();

        public ChunkColumn GetChunk(int chunkX, int chunkZ)
        {
            if (_cache.TryGetValue((chunkX, chunkZ), out var existing))
                return existing;

            var column = Generate(chunkX, chunkZ);
            if (_cache.TryAdd((chunkX, chunkZ), column))
            {
                _evictionQueue.Enqueue((chunkX, chunkZ));
                TrimCache();
            }
            else
            {
                column.Dispose();
                column = _cache[(chunkX, chunkZ)];
            }
            return column;
        }

        private void TrimCache()
        {
            while (_cache.Count > MaxCachedColumns &&
                   _evictionQueue.TryDequeue(out var key))
            {
                if (_cache.TryRemove(key, out var evicted))
                    evicted.Dispose();
            }
        }

        private static ChunkColumn Generate(int chunkX, int chunkZ)
        {
#if DEBUG
            Console.WriteLine($"Generating chunk at {chunkX}/{chunkZ}");
#endif
            var column = new ChunkColumn(chunkX, chunkZ);

            for (var x = 0; x < 16; x++)
                for (var z = 0; z < 16; z++)
                {
                    column.SetBlock(x, BedrockY, z, _bedrockId);

                    for (var y = StoneY1; y <= StoneY2; y++)
                        column.SetBlock(x, y, z, _stoneId);

                    for (var y = DirtY1; y <= DirtY2; y++)
                        column.SetBlock(x, y, z, _dirtId);

                    column.SetBlock(x, GrassY, z, _grassId);
                }

            return column;
        }

        public void Dispose()
        {
            foreach (var col in _cache.Values)
            {
                col.Dispose();
            }

            _cache.Clear();
        }
    }
}