namespace FunCraft.World.Chunks
{
    using Blocks;

    /// <summary>
    /// One 16×16×16 section of a chunk column.
    /// Index = (y &amp; 15) * 256 + z * 16 + x
    /// </summary>
    public sealed class ChunkSection
    {
        public const int Width = 16;
        public const int Height = 16;
        public const int Depth = 16;
        public const int Volume = Width * Height * Depth;

        private readonly BlockState[] _blocks = new BlockState[Volume];
        private int _solidCount;

        /// <summary>
        /// Number of non-air blocks — written into the chunk section header.
        /// </summary>
        public short BlockCount => (short)_solidCount;

        public BlockState Get(int x, int y, int z) => _blocks[Index(x, y, z)];

        public void Set(int x, int y, int z, BlockState block)
        {
            ref var slot = ref _blocks[Index(x, y, z)];
            if (slot.IsAir && !block.IsAir) _solidCount++;
            else if (!slot.IsAir && block.IsAir) _solidCount--;
            slot = block;
        }

        /// <summary>
        /// Fills every block in this section with the given state.
        /// </summary>
        public void Fill(BlockState block)
        {
            Array.Fill(_blocks, block);
            _solidCount = block.IsAir ? 0 : Volume;
        }

        /// <summary>
        /// True when every block is air — enables single-valued palette optimisation.
        /// </summary>
        public bool IsAllAir => _solidCount == 0;

        /// <summary>
        /// True when every block is the same state — enables single-valued palette.
        /// </summary>
        public bool IsUniform(out BlockState uniform)
        {
            uniform = _blocks[0];
            if (_solidCount == 0) { uniform = BlockState.Air; return true; }
            foreach (var b in _blocks.AsSpan())
                if (b != uniform) return false;
            return true;
        }

        /// <summary>
        /// Raw span for the serializer — iterates in index order.
        /// </summary>
        public ReadOnlySpan<BlockState> Blocks => _blocks;

        private static int Index(int x, int y, int z)
            => (y & 15) * 256 + z * 16 + x;
    }
}