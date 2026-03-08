using System.Buffers;

namespace FunCraft.Protocol.Types
{
    public static class VarInt
    {
        private const byte SegmentBits = 0x7F;
        private const byte ContinueBit = 0x80;
        private const int BitLength = 7;
        private const int MaxSize = 5;

        public static int Write(Span<byte> destination, int value)
        {
            var unsignedValue = (uint)value;
            var i = 0;

            while (unsignedValue > SegmentBits)
            {
                destination[i++] = (byte)((unsignedValue & SegmentBits) | ContinueBit);
                unsignedValue >>= BitLength;
            }

            destination[i++] = (byte)(unsignedValue & SegmentBits);

            return i;
        }

        public static bool TryRead(ref SequenceReader<byte> reader, out int value)
        {
            value = 0;
            var shift = 0;

            while (shift < MaxSize * BitLength)
            {
                if (!reader.TryRead(out var b))
                {
                    return false;
                }

                value |= (b & SegmentBits) << shift;

                if ((b & ContinueBit) == 0)
                {
                    return true;
                }

                shift += BitLength;
            }

            throw new OverflowException($"{nameof(VarInt)} exceeds maximum size.");
        }

        public static int GetSize(int value) => (uint)value switch
        {
            < 0x80 => 1,
            < 0x4000 => 2,
            < 0x200000 => 3,
            < 0x10000000 => 4,
            _ => 5
        };
    }
}
