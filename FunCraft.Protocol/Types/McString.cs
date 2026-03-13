using System.Buffers;
using System.Text;

namespace FunCraft.Protocol.Types
{
    public readonly struct McString : IDataType<McString, string>
    {
        public static int Write(Span<byte> destination, string value)
        {
            var byteCount = Encoding.UTF8.GetByteCount(value);
            var varIntSize = VarInt.Write(destination, byteCount);
            Encoding.UTF8.GetBytes(value, destination[varIntSize..]);
            return varIntSize + byteCount;
        }

        public static int GetSize(string value)
        {
            var byteCount = Encoding.UTF8.GetByteCount(value);
            return VarInt.GetSize(byteCount) + byteCount;
        }

        /// <summary>
        /// Write a pre-encoded UTF-8 span as a length-prefixed MC string.
        /// </summary>
        public static int Write(Span<byte> destination, ReadOnlySpan<byte> utf8Value)
        {
            var varIntSize = VarInt.Write(destination, utf8Value.Length);
            utf8Value.CopyTo(destination[varIntSize..]);
            return varIntSize + utf8Value.Length;
        }

        public static int GetSize(ReadOnlySpan<byte> utf8Value) =>
            VarInt.GetSize(utf8Value.Length) + utf8Value.Length;

        public static bool TryRead(ref SequenceReader<byte> reader, out string value)
        {
            value = string.Empty;

            if (!VarInt.TryRead(ref reader, out var byteCount))
                return false;

            if (reader.Remaining < byteCount)
                return false;

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

        /// <summary>
        /// Read a length-prefixed MC string and return the raw UTF-8 bytes without
        /// decoding them to <see cref="string"/>.  The memory is backed by a freshly
        /// allocated <c>byte[]</c> so it outlives the pipeline buffer.
        /// </summary>
        public static bool TryReadRaw(ref SequenceReader<byte> reader, out ReadOnlyMemory<byte> value)
        {
            value = ReadOnlyMemory<byte>.Empty;

            if (!VarInt.TryRead(ref reader, out var byteCount))
                return false;

            if (reader.Remaining < byteCount)
                return false;

            var bytes = new byte[byteCount];

            if (reader.UnreadSpan.Length >= byteCount)
            {
                reader.UnreadSpan[..byteCount].CopyTo(bytes);
            }
            else
            {
                reader.TryCopyTo(bytes);
            }

            reader.Advance(byteCount);

            value = bytes;
            return true;
        }
    }
}