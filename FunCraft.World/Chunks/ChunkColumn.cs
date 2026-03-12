namespace FunCraft.World.Chunks
{
    using Blocks;

    /// <summary>
    /// A full 16×384×16 chunk column (24 sections, Y = -64 to +319).
    /// Section index 0 = Y -64..-49, index 23 = Y 304..319.
    /// <para>
    /// Sections that were obtained from <see cref="ChunkSection.GetOrCreateShared"/>
    /// are never disposed here — they are shared flyweights.
    /// </para>
    /// </summary>
    public sealed class ChunkColumn : IDisposable
    {
        public const int SectionCount = 24;
        public const int WorldMinY = -64;

        public int ChunkX { get; }
        public int ChunkZ { get; }

        private readonly ChunkSection[] _sections;
        private bool _disposed;

        public ChunkColumn(int chunkX, int chunkZ)
        {
            ChunkX = chunkX;
            ChunkZ = chunkZ;
            _sections = new ChunkSection[SectionCount];
            var air = ChunkSection.GetOrCreateShared(BlockState.Air);
            Array.Fill(_sections, air);
        }

        public ChunkSection GetSection(int sectionIndex) => _sections[sectionIndex];

        public BlockState GetBlock(int x, int y, int z)
        {
            var (si, ly) = SectionCoords(y);
            return _sections[si].Get(x & 15, ly, z & 15);
        }

        public void SetBlock(int x, int y, int z, BlockState block)
        {
            var (si, ly) = SectionCoords(y);
            var section = _sections[si];

            if (section.IsSharedSentinel)
            {
                section.IsUniform(out var fill);
                var mutable = new ChunkSection(fill, mutable: true);
                _sections[si] = mutable;
                section = mutable;
            }

            section.Set(x & 15, ly, z & 15, block);
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            foreach (var s in _sections)
            {
                s.Dispose();
            }
        }

        private static (int sectionIndex, int localY) SectionCoords(int worldY)
        {
            var adjusted = worldY - WorldMinY;
            return (adjusted >> 4, adjusted & 15);
        }
    }
}