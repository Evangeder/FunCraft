using System.Buffers.Binary;
using System.Text;

namespace FunCraft.Protocol.NBT
{
    public ref struct NbtWriter
    {
        private Span<byte> _buffer;
        public int BytesWritten { get; private set; } = 0;

        private const byte TagEnd = 0x00;
        private const byte TagByte = 0x01;
        private const byte TagShort = 0x02;
        private const byte TagInt = 0x03;
        private const byte TagLong = 0x04;
        private const byte TagFloat = 0x05;
        private const byte TagDouble = 0x06;
        private const byte TagString = 0x08;
        private const byte TagList = 0x09;
        private const byte TagCompound = 0x0A;

        public NbtWriter(Span<byte> buffer)
        {
            _buffer = buffer;
        }

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
            _buffer[BytesWritten++] = TagCompound;
            WriteNbtString(name);
        }

        public void WriteCompoundEnd()
        {
            _buffer[BytesWritten++] = TagEnd;
        }

        public void WriteString(string name, string value)
        {
            _buffer[BytesWritten++] = TagString;
            WriteNbtString(name);
            WriteNbtString(value);
        }

        public void WriteInt(string name, int value)
        {
            _buffer[BytesWritten++] = TagInt;
            WriteNbtString(name);
            BinaryPrimitives.WriteInt32BigEndian(_buffer[BytesWritten..], value);
            BytesWritten += sizeof(int);
        }

        public void WriteByte(string name, byte value)
        {
            _buffer[BytesWritten++] = TagByte;
            WriteNbtString(name);
            _buffer[BytesWritten++] = value;
        }

        public void WriteShort(string name, short value)
        {
            _buffer[BytesWritten++] = TagShort;
            WriteNbtString(name);
            BinaryPrimitives.WriteInt16BigEndian(_buffer[BytesWritten..], value);
            BytesWritten += sizeof(short);
        }

        public void WriteFloat(string name, float value)
        {
            _buffer[BytesWritten++] = TagFloat;
            WriteNbtString(name);
            BinaryPrimitives.WriteSingleBigEndian(_buffer[BytesWritten..], value);
            BytesWritten += sizeof(float);
        }

        public void WriteDouble(string name, double value)
        {
            _buffer[BytesWritten++] = TagDouble;
            WriteNbtString(name);
            BinaryPrimitives.WriteDoubleBigEndian(_buffer[BytesWritten..], value);
            BytesWritten += sizeof(double);
        }

        public void WriteLong(string name, long value)
        {
            _buffer[BytesWritten++] = TagLong;
            WriteNbtString(name);
            BinaryPrimitives.WriteInt64BigEndian(_buffer[BytesWritten..], value);
            BytesWritten += sizeof(long);
        }

        public void WriteListStart(string name, byte elementType, int count)
        {
            _buffer[BytesWritten++] = TagList;
            WriteNbtString(name);
            _buffer[BytesWritten++] = elementType;
            BinaryPrimitives.WriteInt32BigEndian(_buffer[BytesWritten..], count);
            BytesWritten += sizeof(int);
        }

        public void WriteRootCompoundStart()
        {
            // Root compound has empty name in modern NBT
            _buffer[BytesWritten++] = TagCompound;
            BinaryPrimitives.WriteUInt16BigEndian(_buffer[BytesWritten..], 0);
            BytesWritten += sizeof(ushort);
        }
    }
}