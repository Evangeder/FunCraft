namespace FunCraft.Protocol.NBT
{
    /// <summary>
    /// NBT tag type identifiers as defined in the NBT specification.
    /// </summary>
    public static class NbtTag
    {
        public const byte End = 0x00;
        public const byte Byte = 0x01;
        public const byte Short = 0x02;
        public const byte Int = 0x03;
        public const byte Long = 0x04;
        public const byte Float = 0x05;
        public const byte Double = 0x06;
        public const byte ByteArray = 0x07;
        public const byte String = 0x08;
        public const byte List = 0x09;
        public const byte Compound = 0x0A;
        public const byte IntArray = 0x0B;
        public const byte LongArray = 0x0C;
    }
}
