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

        /// <summary>Write a <see cref="string"/> as a length-prefixed MC string (encodes to UTF-8).</summary>
        public void WriteString(string value)
        {
            BytesWritten += McString.Write(_buffer[BytesWritten..], value);
        }

        /// <summary>
        /// Write pre-encoded UTF-8 bytes as a length-prefixed MC string.
        /// No encoding step — bytes are copied verbatim.
        /// </summary>
        public void WriteString(ReadOnlySpan<byte> utf8Value)
        {
            BytesWritten += McString.Write(_buffer[BytesWritten..], utf8Value);
        }

        public void WriteGuid(Guid value)
        {
            Span<byte> bytes = stackalloc byte[16];
            value.TryWriteBytes(bytes);
            _buffer[BytesWritten + 0] = bytes[3]; _buffer[BytesWritten + 1] = bytes[2];
            _buffer[BytesWritten + 2] = bytes[1]; _buffer[BytesWritten + 3] = bytes[0];
            _buffer[BytesWritten + 4] = bytes[5]; _buffer[BytesWritten + 5] = bytes[4];
            _buffer[BytesWritten + 6] = bytes[7]; _buffer[BytesWritten + 7] = bytes[6];
            _buffer[BytesWritten + 8] = bytes[8]; _buffer[BytesWritten + 9] = bytes[9];
            _buffer[BytesWritten + 10] = bytes[10]; _buffer[BytesWritten + 11] = bytes[11];
            _buffer[BytesWritten + 12] = bytes[12]; _buffer[BytesWritten + 13] = bytes[13];
            _buffer[BytesWritten + 14] = bytes[14]; _buffer[BytesWritten + 15] = bytes[15];
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

        public void WriteRawBytes(ReadOnlySpan<byte> data)
        {
            data.CopyTo(_buffer[BytesWritten..]);
            BytesWritten += data.Length;
        }

        public void WriteInt(int value)
        {
            BinaryPrimitives.WriteInt32BigEndian(_buffer[BytesWritten..], value);
            BytesWritten += sizeof(int);
        }

        public void WriteByte(byte value)
        {
            _buffer[BytesWritten++] = value;
        }

        public void WriteFloat(float value)
        {
            BinaryPrimitives.WriteSingleBigEndian(_buffer[BytesWritten..], value);
            BytesWritten += sizeof(float);
        }

        public void WriteDouble(double value)
        {
            BinaryPrimitives.WriteDoubleBigEndian(_buffer[BytesWritten..], value);
            BytesWritten += sizeof(double);
        }

        public void WriteShort(short value)
        {
            BinaryPrimitives.WriteInt16BigEndian(_buffer[BytesWritten..], value);
            BytesWritten += sizeof(short);
        }

        /// <summary>
        /// Writes a velocity vector using the LpVec3 format introduced in 1.21.2.
        /// Zero-velocity fast path: 1 byte. Normal path: 6 bytes + optional VarInt continuation.
        /// </summary>
        public void WriteLpVec3(double vx, double vy, double vz)
        {
            const double threshold = 3.051944088384301e-5;
            const double maxQuantized = 32766.0;

            var maxCoordinate = Math.Max(Math.Abs(vx), Math.Max(Math.Abs(vy), Math.Abs(vz)));

            if (maxCoordinate < threshold)
            {
                _buffer[BytesWritten++] = 0;
                return;
            }

            var maxCoordinateI = (long)maxCoordinate;
            var scaleFactor = maxCoordinate > (double)maxCoordinateI
                ? maxCoordinateI + 1L
                : maxCoordinateI;

            var needContinuation = (scaleFactor & 3L) != scaleFactor;
            var packedScale = needContinuation ? (scaleFactor & 3L) | 4L : scaleFactor;

            var packedX = Pack(vx / (double)scaleFactor) << 3;
            var packedY = Pack(vy / (double)scaleFactor) << 18;
            var packedZ = Pack(vz / (double)scaleFactor) << 33;
            var packed = packedZ | packedY | packedX | packedScale;

            _buffer[BytesWritten++] = (byte)packed;
            _buffer[BytesWritten++] = (byte)(packed >> 8);

            BinaryPrimitives.WriteInt32BigEndian(_buffer[BytesWritten..], (int)(packed >> 16));
            BytesWritten += sizeof(int);

            if (needContinuation)
            {
                BytesWritten += VarInt.Write(_buffer[BytesWritten..], (int)(scaleFactor >> 2));
            }

            return;

            static long Pack(double v) =>
                (long)Math.Round((v * 0.5 + 0.5) * maxQuantized);
        }

        /// <summary>
        /// Returns the wire size in bytes that <see cref="WriteLpVec3"/> will produce
        /// for the given velocity vector.
        /// </summary>
        public static int LpVec3Size(double vx, double vy, double vz)
        {
            const double threshold = 3.051944088384301e-5;
            var maxCoordinate = Math.Max(Math.Abs(vx), Math.Max(Math.Abs(vy), Math.Abs(vz)));
            if (maxCoordinate < threshold)
            {
                return 1;
            }

            var maxCoordinateI = (long)maxCoordinate;
            var scaleFactor = maxCoordinate > (double)maxCoordinateI
                ? maxCoordinateI + 1L
                : maxCoordinateI;

            var needContinuation = (scaleFactor & 3L) != scaleFactor;
            return needContinuation
                ? 6 + VarInt.GetSize((int)(scaleFactor >> 2))
                : 6;
        }
    }
}