using System.Buffers.Binary;
using System.Text;

namespace FunCraft.Protocol.NBT
{
    public ref struct NbtWriter(Span<byte> buffer)
    {
        private Span<byte> _buffer = buffer;

        public int BytesWritten { get; private set; } = 0;

        private void WriteNbtString(string value)
        {
            var byteCount = Encoding.UTF8.GetByteCount(value);
            BinaryPrimitives.WriteUInt16BigEndian(_buffer[BytesWritten..], (ushort)byteCount);
            BytesWritten += sizeof(ushort);
            Encoding.UTF8.GetBytes(value, _buffer[BytesWritten..]);
            BytesWritten += byteCount;
        }

        public void WriteCompoundStart(string name)
        {
            _buffer[BytesWritten++] = NbtTag.Compound;
            WriteNbtString(name);
        }

        public void WriteCompoundEnd()
        {
            _buffer[BytesWritten++] = NbtTag.End;
        }

        public void WriteString(string name, string value)
        {
            _buffer[BytesWritten++] = NbtTag.String;
            WriteNbtString(name);
            WriteNbtString(value);
        }

        public void WriteInt(string name, int value)
        {
            _buffer[BytesWritten++] = NbtTag.Int;
            WriteNbtString(name);
            BinaryPrimitives.WriteInt32BigEndian(_buffer[BytesWritten..], value);
            BytesWritten += sizeof(int);
        }

        public void WriteByte(string name, byte value)
        {
            _buffer[BytesWritten++] = NbtTag.Byte;
            WriteNbtString(name);
            _buffer[BytesWritten++] = value;
        }

        public void WriteShort(string name, short value)
        {
            _buffer[BytesWritten++] = NbtTag.Short;
            WriteNbtString(name);
            BinaryPrimitives.WriteInt16BigEndian(_buffer[BytesWritten..], value);
            BytesWritten += sizeof(short);
        }

        public void WriteFloat(string name, float value)
        {
            _buffer[BytesWritten++] = NbtTag.Float;
            WriteNbtString(name);
            BinaryPrimitives.WriteSingleBigEndian(_buffer[BytesWritten..], value);
            BytesWritten += sizeof(float);
        }

        public void WriteDouble(string name, double value)
        {
            _buffer[BytesWritten++] = NbtTag.Double;
            WriteNbtString(name);
            BinaryPrimitives.WriteDoubleBigEndian(_buffer[BytesWritten..], value);
            BytesWritten += sizeof(double);
        }

        public void WriteLong(string name, long value)
        {
            _buffer[BytesWritten++] = NbtTag.Long;
            WriteNbtString(name);
            BinaryPrimitives.WriteInt64BigEndian(_buffer[BytesWritten..], value);
            BytesWritten += sizeof(long);
        }

        public void WriteListStart(string name, byte elementType, int count)
        {
            _buffer[BytesWritten++] = NbtTag.List;
            WriteNbtString(name);
            _buffer[BytesWritten++] = elementType;
            BinaryPrimitives.WriteInt32BigEndian(_buffer[BytesWritten..], count);
            BytesWritten += sizeof(int);
        }

        public void WriteRootCompoundStart()
        {
            _buffer[BytesWritten++] = NbtTag.Compound;
            BinaryPrimitives.WriteUInt16BigEndian(_buffer[BytesWritten..], 0);
            BytesWritten += sizeof(ushort);
        }
    }
}