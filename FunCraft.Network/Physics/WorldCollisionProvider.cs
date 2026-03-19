using FunCraft.Protocol.Registry;
using FunCraft.World;

namespace FunCraft.Network.Physics
{
    /// <summary>
    /// Adapts <see cref="IWorldSource"/> to <see cref="ICollisionProvider"/>.
    /// A block is considered solid for item-entity physics when ALL of the
    /// following are true:
    /// <list type="bullet">
    ///   <item>It is not air.</item>
    ///   <item>Its name is not in <see cref="NonCollidingBlocks"/>.</item>
    /// </list>
    /// Chunk access is already thread-safe in <see cref="IWorldSource"/>.
    /// </summary>
    public sealed class WorldCollisionProvider(IWorldSource world) : ICollisionProvider
    {
        public bool IsSolid(int x, int y, int z)
        {
            var block = world.GetChunk(x >> 4, z >> 4).GetBlock(x, y, z);
            if (block.IsAir)
            {
                return false;
            }

            var name = RegistryLookup.GetBlockName(block.Id);
            if (name.IsEmpty)
            {
                return true;
            }

            return !NonCollidingBlocks.Contains(name.Span);
        }
    }
}