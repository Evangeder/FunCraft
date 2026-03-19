using System.Runtime.InteropServices;

namespace FunCraft.Network.Physics
{
    [StructLayout(LayoutKind.Sequential, Pack = 8)]
    public readonly struct Vec3d(double x, double y, double z)
    {
        public readonly double X = x;
        public readonly double Y = y;
        public readonly double Z = z;

        public static readonly Vec3d Zero = default;

        public static Vec3d operator +(Vec3d a, Vec3d b) => new(a.X + b.X, a.Y + b.Y, a.Z + b.Z);
        public static Vec3d operator -(Vec3d a, Vec3d b) => new(a.X - b.X, a.Y - b.Y, a.Z - b.Z);
        public static Vec3d operator *(Vec3d v, double s) => new(v.X * s, v.Y * s, v.Z * s);

        public double LengthSquared => X * X + Y * Y + Z * Z;
        public double HorizontalLengthSquared => X * X + Z * Z;

        public override string ToString() => $"({X:F3}, {Y:F3}, {Z:F3})";
    }
}