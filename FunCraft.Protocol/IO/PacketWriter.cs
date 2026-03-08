using System.Buffers.Binary;

namespace FunCraft.Protocol.IO
{
    using Types;

    public ref struct PacketWriter
    {
        private Span<byte> _buffer;
        public int BytesWritten { get; private set; } = 0;

        public PacketWriter(Span<byte> buffer)
        {
            _buffer = buffer;
        }

        public void WriteVarInt(int value)
        {
            BytesWritten += VarInt.Write(_buffer[BytesWritten..], value);
        }

        public void WriteLong(long value)
        {
            BinaryPrimitives.WriteInt64BigEndian(_buffer[BytesWritten..], value);
            BytesWritten += sizeof(long);
        }

        public void WriteVarLong(long value)
        {
            BytesWritten += VarLong.Write(_buffer[BytesWritten..], value);
        }

        public void WriteUInt16(ushort value)
        {
            BinaryPrimitives.WriteUInt16BigEndian(_buffer[BytesWritten..], value);
            BytesWritten += sizeof(ushort);
        }

        public void WriteString(string value)
        {
            BytesWritten += McString.Write(_buffer[BytesWritten..], value);
        }

        public void WriteGuid(Guid value)
        {
            Span<byte> bytes = stackalloc byte[16];
            value.TryWriteBytes(bytes);

            _buffer[BytesWritten + 0] = bytes[3];
            _buffer[BytesWritten + 1] = bytes[2];
            _buffer[BytesWritten + 2] = bytes[1];
            _buffer[BytesWritten + 3] = bytes[0];
            _buffer[BytesWritten + 4] = bytes[5];
            _buffer[BytesWritten + 5] = bytes[4];
            _buffer[BytesWritten + 6] = bytes[7];
            _buffer[BytesWritten + 7] = bytes[6];
            _buffer[BytesWritten + 8] = bytes[8];
            _buffer[BytesWritten + 9] = bytes[9];
            _buffer[BytesWritten + 10] = bytes[10];
            _buffer[BytesWritten + 11] = bytes[11];
            _buffer[BytesWritten + 12] = bytes[12];
            _buffer[BytesWritten + 13] = bytes[13];
            _buffer[BytesWritten + 14] = bytes[14];
            _buffer[BytesWritten + 15] = bytes[15];
            BytesWritten += 16;
        }

        public void WriteBoolean(bool value)
        {
            _buffer[BytesWritten++] = value ? (byte)1 : (byte)0;
        }

        public void WriteRawBytes(byte[] data)
        {
            data.AsSpan().CopyTo(_buffer[BytesWritten..]);
            BytesWritten += data.Length;
        }
    }
}
