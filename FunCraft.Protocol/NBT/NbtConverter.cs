using System.Buffers;
using System.Text.Json;

namespace FunCraft.Protocol.NBT
{
    /// <summary>
    /// Converts a <see cref="JsonElement"/> into network NBT bytes.
    /// </summary>
    public static class NbtConverter
    {
        private const int InitialBufferSize = 4096;

        /// <summary>
        /// Converts a JsonElement to NBT bytes.
        /// <br/>The root element must be a JSON object → written as unnamed root TAG_Compound.
        /// </summary>
        public static byte[] Convert(JsonElement root)
        {
            if (root.ValueKind != JsonValueKind.Object)
                throw new ArgumentException("Root NBT element must be a JSON object.");

            var buffer = ArrayPool<byte>.Shared.Rent(InitialBufferSize);
            try
            {
                int written = 0;
                WriteRootCompound(root, ref buffer, ref written);
                return buffer.AsSpan(0, written).ToArray();
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(buffer);
            }
        }

        private static void EnsureCapacity(ref byte[] buffer, int offset, int needed)
        {
            if (offset + needed <= buffer.Length) return;

            var newSize = Math.Max(buffer.Length * 2, offset + needed);
            var newBuffer = ArrayPool<byte>.Shared.Rent(newSize);
            buffer.AsSpan(0, offset).CopyTo(newBuffer);
            ArrayPool<byte>.Shared.Return(buffer);
            buffer = newBuffer;
        }

        private static void WriteRootCompound(JsonElement obj, ref byte[] buffer, ref int offset)
        {
            foreach (var prop in obj.EnumerateObject())
            {
                WriteNamedTag(prop.Name, prop.Value, ref buffer, ref offset);
            }

            EnsureCapacity(ref buffer, offset, 1);
            buffer[offset++] = NbtTag.End;
        }

        private static void WriteNamedTag(string name, JsonElement value, ref byte[] buffer, ref int offset)
        {
            var tagType = GetTagType(value);

            EnsureCapacity(ref buffer, offset, 1);
            buffer[offset++] = tagType;
            WriteNbtString(name, ref buffer, ref offset);
            WriteTagPayload(value, tagType, ref buffer, ref offset);
        }

        private static void WriteTagPayload(JsonElement value, byte tagType, ref byte[] buffer, ref int offset)
        {
            switch (tagType)
            {
                case NbtTag.Byte:
                    EnsureCapacity(ref buffer, offset, 1);
                    buffer[offset++] = value.ValueKind == JsonValueKind.True ? (byte)1 : (byte)0;
                    break;

                case NbtTag.Int:
                    EnsureCapacity(ref buffer, offset, 4);
                    WriteInt32BigEndian(buffer, offset, value.GetInt32());
                    offset += 4;
                    break;

                case NbtTag.Double:
                    EnsureCapacity(ref buffer, offset, 8);
                    WriteDoubleBigEndian(buffer, offset, value.GetDouble());
                    offset += 8;
                    break;

                case NbtTag.String:
                    WriteNbtString(value.GetString()!, ref buffer, ref offset);
                    break;

                case NbtTag.List:
                    WriteList(value, ref buffer, ref offset);
                    break;

                case NbtTag.Compound:
                    foreach (var prop in value.EnumerateObject())
                    {
                        WriteNamedTag(prop.Name, prop.Value, ref buffer, ref offset);
                    }
                    EnsureCapacity(ref buffer, offset, 1);
                    buffer[offset++] = NbtTag.End;
                    break;
            }
        }

        private static void WriteList(JsonElement array, ref byte[] buffer, ref int offset)
        {
            var items = array.EnumerateArray().ToList();

            var elementType = (byte)(items.Count > 0 ? GetTagType(items[0]) : NbtTag.Byte);

            EnsureCapacity(ref buffer, offset, 5);
            buffer[offset++] = elementType;
            WriteInt32BigEndian(buffer, offset, items.Count);
            offset += 4;

            foreach (var item in items)
            {
                WriteTagPayload(item, elementType, ref buffer, ref offset);
            }
        }

        private static void WriteNbtString(string value, ref byte[] buffer, ref int offset)
        {
            var byteCount = System.Text.Encoding.UTF8.GetByteCount(value);
            EnsureCapacity(ref buffer, offset, 2 + byteCount);

            buffer[offset++] = (byte)(byteCount >> 8);
            buffer[offset++] = (byte)(byteCount & 0xFF);

            System.Text.Encoding.UTF8.GetBytes(value, buffer.AsSpan(offset));
            offset += byteCount;
        }

        private static byte GetTagType(JsonElement value) => value.ValueKind switch
        {
            JsonValueKind.True or JsonValueKind.False => NbtTag.Byte,
            JsonValueKind.Number when IsInteger(value) => NbtTag.Int,
            JsonValueKind.Number => NbtTag.Double,
            JsonValueKind.String => NbtTag.String,
            JsonValueKind.Array => NbtTag.List,
            JsonValueKind.Object => NbtTag.Compound,
            _ => throw new NotSupportedException($"Unsupported JSON value kind: {value.ValueKind}")
        };

        private static bool IsInteger(JsonElement value)
        {
            // If the raw text has no decimal point or exponent, treat as integer
            var raw = value.GetRawText();
            return !raw.Contains('.') && !raw.Contains('e') && !raw.Contains('E');
        }

        private static void WriteInt32BigEndian(byte[] buffer, int offset, int value)
        {
            buffer[offset] = (byte)(value >> 24);
            buffer[offset + 1] = (byte)(value >> 16);
            buffer[offset + 2] = (byte)(value >> 8);
            buffer[offset + 3] = (byte)(value);
        }

        private static void WriteDoubleBigEndian(byte[] buffer, int offset, double value)
        {
            var bits = BitConverter.DoubleToInt64Bits(value);
            buffer[offset] = (byte)(bits >> 56);
            buffer[offset + 1] = (byte)(bits >> 48);
            buffer[offset + 2] = (byte)(bits >> 40);
            buffer[offset + 3] = (byte)(bits >> 32);
            buffer[offset + 4] = (byte)(bits >> 24);
            buffer[offset + 5] = (byte)(bits >> 16);
            buffer[offset + 6] = (byte)(bits >> 8);
            buffer[offset + 7] = (byte)(bits);
        }
    }
}