namespace FunCraft.Protocol.Types
{
    public static class VarLong
    {
        private const byte SegmentBits = 0x7F;
        private const byte ContinueBit = 0x80;
        private const int BitLength = 7;
        private const int MaxSize = 10;

        public static int Write(Span<byte> destination, long value)
        {
            var unsignedValue = (ulong)value;
            var i = 0;

            while (unsignedValue > SegmentBits)
            {
                destination[i++] = (byte)((unsignedValue & SegmentBits) | ContinueBit);
                unsignedValue >>= BitLength;
            }

            destination[i++] = (byte)(unsignedValue & SegmentBits);

            return i;
        }

        public static bool TryRead(ReadOnlySpan<byte> source, out long value, out int bytesRead)
        {
            ulong unsignedValue = 0;
            for (var i = 0; i < MaxSize; i++)
            {
                if (i >= source.Length)
                {
                    value = 0;
                    bytesRead = 0;
                    return false;
                }

                var b = source[i];
                unsignedValue |= (ulong)(b & SegmentBits) << (i * BitLength);

                if ((b & ContinueBit) != 0)
                {
                    continue;
                }

                bytesRead = i + 1;
                value = (long)unsignedValue;
                return true;
            }

            throw new OverflowException($"{nameof(VarLong)} exceeds maximum size.");
        }

        public static int GetSize(long value) => (ulong) value switch
        {
            < 0x80 => 1,
            < 0x4000 => 2,
            < 0x200000 => 3,
            < 0x10000000 => 4,
            < 0x800000000L => 5,
            < 0x40000000000L => 6,
            < 0x2000000000000L => 7,
            < 0x100000000000000L => 8,
            < 0x8000000000000000L => 9,
            _ => 10
        };
    }
}
