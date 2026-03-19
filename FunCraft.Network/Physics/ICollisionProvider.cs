namespace FunCraft.Network.Physics
{
    /// <summary>
    /// Provides solid-block queries to the physics engine.
    /// Abstracted from <c>IWorldSource</c> so the physics layer has no
    /// compile-time dependency on the world layer, and so test doubles and
    /// future spatial accelerators (BVH, octree) can be swapped in freely.
    /// </summary>
    public interface ICollisionProvider
    {
        /// <summary>
        /// Returns true when the block at world coordinates
        /// (<paramref name="x"/>, <paramref name="y"/>, <paramref name="z"/>) is solid
        /// (i.e. not air and has a full-block collision box for physics purposes).
        /// </summary>
        bool IsSolid(int x, int y, int z);
    }
}