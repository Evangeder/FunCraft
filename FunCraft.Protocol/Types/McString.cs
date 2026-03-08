using System.Buffers;
using System.Text;

namespace FunCraft.Protocol.Types
{
    public static class McString
    {
        public static int Write(Span<byte> destination, string value)
        {
            var byteCount = Encoding.UTF8.GetByteCount(value);
            var varIntSize = VarInt.Write(destination, byteCount);
            Encoding.UTF8.GetBytes(value, destination[varIntSize..]);
            return varIntSize + byteCount;
        }

        public static bool TryRead(ref SequenceReader<byte> reader, out string value)
        {
            value = string.Empty;

            if (!VarInt.TryRead(ref reader, out var byteCount))
            {
                return false;
            }

            if (reader.Remaining < byteCount)
            {
                return false;
            }

            if (reader.UnreadSpan.Length >= byteCount)
            {
                value = Encoding.UTF8.GetString(reader.UnreadSpan[..byteCount]);
                reader.Advance(byteCount);
            }
            else
            {
                var buffer = ArrayPool<byte>.Shared.Rent(byteCount);
                try
                {
                    reader.TryCopyTo(buffer.AsSpan(0, byteCount));
                    reader.Advance(byteCount);
                    value = Encoding.UTF8.GetString(buffer, 0, byteCount);
                }
                finally
                {
                    ArrayPool<byte>.Shared.Return(buffer);
                }
            }

            return true;
        }

        public static int GetSize(string value)
        {
            var byteCount = Encoding.UTF8.GetByteCount(value);
            return VarInt.GetSize(byteCount) + byteCount;
        }
    }
}
