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

        public static bool TryRead(ReadOnlySpan<byte> source, out string value, out int bytesRead)
        {
            if (!VarInt.TryRead(source, out var valueLength, out var varIntSize))
            {
                value = string.Empty;
                bytesRead = 0;
                return false;
            }

            value = Encoding.UTF8.GetString(source[varIntSize..(varIntSize + valueLength)]);
            bytesRead = varIntSize + valueLength;
            return true;
        }

        public static int GetSize(string value)
        {
            var byteCount = Encoding.UTF8.GetByteCount(value);
            return VarInt.GetSize(byteCount) + byteCount;
        }
    }
}
