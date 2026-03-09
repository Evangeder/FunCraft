using System.Collections;
using System.IO.Compression;
using System.Text;
using System.Text.Json;

namespace FunCraft.Protocol.NBT
{
    public sealed class NbtJsonDecoder
    {
        public static string DecodeNbt(byte[] data)
        {
            ArgumentNullException.ThrowIfNull(data);

            if (data.Length == 0)
                return "{}";

            data = TryDecompressGzip(data);

            var reader = new NbtReader(data);
            object result;

            if (reader.PeekByte() == 10)
            {
                var rootType = reader.ReadByte();
                var rootName = reader.ReadString();
                var rootValue = reader.ReadPayload(rootType);

                if (reader.Remaining != 0)
                    throw new InvalidDataException($"Trailing data after NBT root: {reader.Remaining} bytes.");

                result = string.IsNullOrEmpty(rootName)
                    ? rootValue
                    : new Dictionary<string, object?> {[rootName] = rootValue};
            }
            else
            {
                result = reader.ReadCompoundPayload();

                if (reader.Remaining != 0)
                    throw new InvalidDataException($"Trailing data after compound payload: {reader.Remaining} bytes.");
            }

            return SerializeJson(result);
        }

        private static string SerializeJson(object? value)
        {
            using var stream = new MemoryStream();
            using (var writer = new Utf8JsonWriter(stream, new JsonWriterOptions
                   {
                       Indented = true
                   }))
            {
                WriteJsonValue(writer, value);
            }

            return Encoding.UTF8.GetString(stream.ToArray());
        }

        private static void WriteJsonValue(Utf8JsonWriter writer, object? value)
        {
            if (value == null)
            {
                writer.WriteNullValue();
                return;
            }

            switch (value)
            {
                case string s:
                    writer.WriteStringValue(s);
                    return;

                case bool b:
                    writer.WriteBooleanValue(b);
                    return;

                case sbyte sb:
                    writer.WriteNumberValue(sb);
                    return;

                case byte by:
                    writer.WriteNumberValue(by);
                    return;

                case short sh:
                    writer.WriteNumberValue(sh);
                    return;

                case ushort ush:
                    writer.WriteNumberValue(ush);
                    return;

                case int i:
                    writer.WriteNumberValue(i);
                    return;

                case uint ui:
                    writer.WriteNumberValue(ui);
                    return;

                case long l:
                    writer.WriteNumberValue(l);
                    return;

                case ulong ul:
                    writer.WriteNumberValue(ul);
                    return;

                case float f:
                    writer.WriteNumberValue(f);
                    return;

                case double d:
                    writer.WriteNumberValue(d);
                    return;

                case decimal dec:
                    writer.WriteNumberValue(dec);
                    return;

                case Dictionary<string, object?> dict:
                    writer.WriteStartObject();
                    foreach (var kv in dict)
                    {
                        writer.WritePropertyName(kv.Key);
                        WriteJsonValue(writer, kv.Value);
                    }

                    writer.WriteEndObject();
                    return;

                case IDictionary<string, object?> genericDict:
                    writer.WriteStartObject();
                    foreach (var kv in genericDict)
                    {
                        writer.WritePropertyName(kv.Key);
                        WriteJsonValue(writer, kv.Value);
                    }

                    writer.WriteEndObject();
                    return;

                case IEnumerable enumerable when value is not string:
                    writer.WriteStartArray();
                    foreach (var item in enumerable)
                        WriteJsonValue(writer, item);
                    writer.WriteEndArray();
                    return;

                default:
                    throw new NotSupportedException(
                        $"Unsupported JSON value type: {value.GetType().FullName}");
            }
        }

        private static byte[] TryDecompressGzip(byte[] data)
        {
            if (data.Length < 2 || data[0] != 0x1F || data[1] != 0x8B)
                return data;

            using var input = new MemoryStream(data);
            using var gzip = new GZipStream(input, CompressionMode.Decompress);
            using var output = new MemoryStream();
            gzip.CopyTo(output);
            return output.ToArray();
        }

        private sealed class NbtReader(byte[] data)
        {
            private int _pos;

            public int Remaining => data.Length - _pos;

            public byte PeekByte()
            {
                EnsureAvailable(1);
                return data[_pos];
            }

            public byte ReadByte()
            {
                EnsureAvailable(1);
                return data[_pos++];
            }

            public sbyte ReadSByte()
            {
                return unchecked((sbyte) ReadByte());
            }

            public ushort ReadUInt16()
            {
                EnsureAvailable(2);
                var value = (ushort) ((data[_pos] << 8) | data[_pos + 1]);
                _pos += 2;
                return value;
            }

            public short ReadInt16()
            {
                return unchecked((short) ReadUInt16());
            }

            public int ReadInt32()
            {
                EnsureAvailable(4);
                var value =
                    (data[_pos] << 24) |
                    (data[_pos + 1] << 16) |
                    (data[_pos + 2] << 8) |
                    data[_pos + 3];
                _pos += 4;
                return value;
            }

            public long ReadInt64()
            {
                EnsureAvailable(8);
                var value =
                    ((ulong) data[_pos] << 56) |
                    ((ulong) data[_pos + 1] << 48) |
                    ((ulong) data[_pos + 2] << 40) |
                    ((ulong) data[_pos + 3] << 32) |
                    ((ulong) data[_pos + 4] << 24) |
                    ((ulong) data[_pos + 5] << 16) |
                    ((ulong) data[_pos + 6] << 8) |
                    data[_pos + 7];
                _pos += 8;
                return unchecked((long) value);
            }

            public float ReadFloat()
            {
                return BitConverter.Int32BitsToSingle(ReadInt32());
            }

            public double ReadDouble()
            {
                return BitConverter.Int64BitsToDouble(ReadInt64());
            }

            public string ReadString()
            {
                var length = ReadUInt16();
                if (length == 0)
                    return string.Empty;

                var bytes = ReadBytes(length);
                return Encoding.UTF8.GetString(bytes);
            }

            public byte[] ReadBytes(int count)
            {
                EnsureAvailable(count);
                var result = new byte[count];
                Buffer.BlockCopy(data, _pos, result, 0, count);
                _pos += count;
                return result;
            }

            public object ReadPayload(byte tagType) => tagType switch
            {
                1 => ReadSByte(),
                2 => ReadInt16(),
                3 => ReadInt32(),
                4 => ReadInt64(),
                5 => ReadFloat(),
                6 => ReadDouble(),
                7 => ReadByteArray(),
                8 => ReadString(),
                9 => ReadList(),
                10 => ReadCompoundPayload(),
                11 => ReadIntArray(),
                12 => ReadLongArray(),
                _ => throw new InvalidDataException($"Unsupported or invalid NBT tag type: {tagType}"),
            };

            public Dictionary<string, object?> ReadCompoundPayload()
            {
                var compound = new Dictionary<string, object?>();

                while (true)
                {
                    var tagType = ReadByte();
                    if (tagType == 0)
                        break;

                    var name = ReadString();
                    compound[name] = ReadPayload(tagType);
                }

                return compound;
            }

            private List<object?> ReadList()
            {
                byte elementType = ReadByte();
                var length = ReadInt32();

                if (length < 0)
                    throw new InvalidDataException($"Negative list length: {length}");

                var list = new List<object?>(length);

                if (length == 0)
                    return list;

                if (elementType == 0)
                    throw new InvalidDataException("Non-empty TAG_List cannot have TAG_End as element type.");

                for (var i = 0; i < length; i++)
                    list.Add(ReadPayload(elementType));

                return list;
            }

            private int[] ReadByteArray()
            {
                var length = ReadInt32();
                if (length < 0)
                    throw new InvalidDataException($"Negative byte array length: {length}");

                var array = new int[length];
                for (var i = 0; i < length; i++)
                    array[i] = ReadSByte();

                return array;
            }

            private int[] ReadIntArray()
            {
                var length = ReadInt32();
                if (length < 0)
                    throw new InvalidDataException($"Negative int array length: {length}");

                var array = new int[length];
                for (var i = 0; i < length; i++)
                    array[i] = ReadInt32();

                return array;
            }

            private long[] ReadLongArray()
            {
                var length = ReadInt32();
                if (length < 0)
                    throw new InvalidDataException($"Negative long array length: {length}");

                var array = new long[length];
                for (var i = 0; i < length; i++)
                    array[i] = ReadInt64();

                return array;
            }

            private void EnsureAvailable(int count)
            {
                if (_pos + count > data.Length)
                    throw new EndOfStreamException(
                        $"Unexpected end of data at offset {_pos}, needed {count} more bytes.");
            }
        }
    }
}