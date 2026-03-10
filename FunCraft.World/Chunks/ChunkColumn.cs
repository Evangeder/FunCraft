namespace FunCraft.World.Chunks
{
    using Blocks;

    /// <summary>
    /// A full 16×384×16 chunk column (24 sections, Y = -64 to +319).
    /// <br/>Section index 0 = Y -64..-49, index 23 = Y 304..319.
    /// </summary>
    public sealed class ChunkColumn
    {
        public const int SectionCount = 24;
        public const int WorldMinY = -64;

        public int ChunkX { get; }
        public int ChunkZ { get; }

        private readonly ChunkSection[] _sections;

        public ChunkColumn(int chunkX, int chunkZ)
        {
            ChunkX = chunkX;
            ChunkZ = chunkZ;
            _sections = new ChunkSection[SectionCount];
            for (var i = 0; i < SectionCount; i++)
                _sections[i] = new ChunkSection();
        }

        public ChunkSection GetSection(int sectionIndex) => _sections[sectionIndex];

        /// <summary>
        /// World-space block access. Y must be in [-64, 319].
        /// </summary>
        public BlockState GetBlock(int x, int y, int z)
        {
            var (si, ly) = SectionCoords(y);
            return _sections[si].Get(x & 15, ly, z & 15);
        }

        /// <summary>
        /// World-space block write.
        /// </summary>
        public void SetBlock(int x, int y, int z, BlockState block)
        {
            var (si, ly) = SectionCoords(y);
            _sections[si].Set(x & 15, ly, z & 15, block);
        }

        private static (int sectionIndex, int localY) SectionCoords(int worldY)
        {
            var adjusted = worldY - WorldMinY;
            return (adjusted >> 4, adjusted & 15);
        }
    }
}