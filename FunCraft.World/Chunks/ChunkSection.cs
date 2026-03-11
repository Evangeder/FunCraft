using System.Buffers;

namespace FunCraft.World.Chunks
{
    using Blocks;
    using System.Collections.Concurrent;

    /// <summary>
    /// One 16×16×16 section of a chunk column.
    /// Index = (y &amp; 15) * 256 + z * 16 + x
    /// <para>
    /// Uniform (all-one-block) sections share a static sentinel and allocate no array.
    /// As soon as a block is set that breaks uniformity, the section "wakes up" and
    /// rents a <see cref="BlockState"/> array from <see cref="ArrayPool{T}"/>.
    /// </para>
    /// </summary>
    public sealed class ChunkSection : IDisposable
    {
        public const int Width = 16;
        public const int Height = 16;
        public const int Depth = 16;
        public const int Volume = Width * Height * Depth;

        private static readonly ConcurrentDictionary<BlockState, ChunkSection> Sentinels = new();

        public static ChunkSection GetOrCreateShared(BlockState fill)
            => Sentinels.GetOrAdd(fill, static b => new ChunkSection(b));

        private BlockState[]? _blocks;
        private BlockState _uniform;
        private int _solidCount;
        private bool _disposed;
        public bool IsSharedSentinel { get; }

        public ChunkSection() { _uniform = BlockState.Air; }

        internal ChunkSection(BlockState fill, bool mutable)
        {
            _uniform = fill;
            _solidCount = fill.IsAir ? 0 : Volume;
        }

        private ChunkSection(BlockState fill)
        {
            _uniform = fill;
            _solidCount = fill.IsAir ? 0 : Volume;
            IsSharedSentinel = true;
        }

        public short BlockCount => (short)_solidCount;

        public BlockState Get(int x, int y, int z)
            => _blocks is null ? _uniform : _blocks[Index(x, y, z)];

        public void Set(int x, int y, int z, BlockState block)
        {
            if (IsSharedSentinel)
            {
                throw new InvalidOperationException(
                    "Cannot mutate a shared sentinel ChunkSection. Replace it with a new mutable section first.");
            }

            if (_blocks is null)
            {
                _blocks = ArrayPool<BlockState>.Shared.Rent(Volume);
                Array.Fill(_blocks, _uniform, 0, Volume);
            }

            ref var slot = ref _blocks[Index(x, y, z)];

            if (slot.IsAir && !block.IsAir)
            {
                _solidCount++;
            }

            if (!slot.IsAir && block.IsAir)
            {
                _solidCount--;
            }

            slot = block;
        }

        public void Fill(BlockState block)
        {
            if (IsSharedSentinel)
            {
                throw new InvalidOperationException("Cannot mutate a shared sentinel.");
            }

            if (_blocks is not null)
            {
                ArrayPool<BlockState>.Shared.Return(_blocks);
                _blocks = null;
            }

            _uniform = block;
            _solidCount = block.IsAir ? 0 : Volume;
        }

        public bool IsAllAir => _solidCount == 0;

        public bool IsUniform(out BlockState uniform)
        {
            if (_blocks is null) { uniform = _uniform; return true; }
            uniform = _blocks[0];

            foreach (var b in _blocks.AsSpan(0, Volume))
            {
                if (b != uniform)
                {
                    return false;
                }
            }

            return true;
        }

        /// <summary>
        /// Raw span for the serializer.
        /// If the section is uniform, the span points into a shared static array —
        /// callers must not hold onto it across yields.
        /// </summary>
        public ReadOnlySpan<BlockState> Blocks
            => _blocks is not null
                ? _blocks.AsSpan(0, Volume)
                : UniformSpan(_uniform);

        [ThreadStatic] private static BlockState[]? _uniformBuf;

        private static ReadOnlySpan<BlockState> UniformSpan(BlockState fill)
        {
            _uniformBuf ??= new BlockState[Volume];
            _uniformBuf.AsSpan(0, Volume).Fill(fill);
            return _uniformBuf.AsSpan(0, Volume);
        }

        public void Dispose()
        {
            if (_disposed || IsSharedSentinel)
            {
                return;
            }
            _disposed = true;

            if (_blocks is null)
            {
                return;
            }

            ArrayPool<BlockState>.Shared.Return(_blocks);
            _blocks = null;
        }

        private static int Index(int x, int y, int z) => (y & 15) * 256 + z * 16 + x;
    }
}