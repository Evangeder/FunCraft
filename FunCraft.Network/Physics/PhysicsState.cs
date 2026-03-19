using System.Runtime.InteropServices;

namespace FunCraft.Network.Physics
{
    [StructLayout(LayoutKind.Sequential, Pack = 8)]
    public struct PhysicsState(Vec3d position, Vec3d velocity)
    {
        public Vec3d Position = position;
        public Vec3d Velocity = velocity;
        public static PhysicsState AtRest(Vec3d position) => new(position, Vec3d.Zero);
    }
}