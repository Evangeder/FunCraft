namespace FunCraft.Protocol.Types
{
    /// <summary>
    /// Minecraft's packed 64-bit block position:
    /// <br/>X(26 bits) | Z(26 bits) | Y(12 bits).
    /// </summary>
    public readonly struct BlockPosition(int x, int y, int z)
    {
        public readonly int X = x;
        public readonly int Y = y;
        public readonly int Z = z;

        public long Encode() =>
            ((long)(X & 0x3FFFFFF) << 38) |
            ((long)(Z & 0x3FFFFFF) << 12) |
            (long)(Y & 0xFFF);

        public static BlockPosition Decode(long value) => new(
            x: (int)(value >> 38),
            y: (int)(value << 52 >> 52),
            z: (int)(value << 26 >> 38));
    }
}