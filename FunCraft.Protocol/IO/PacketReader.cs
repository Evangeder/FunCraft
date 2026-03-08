using FunCraft.Protocol.Types;
using System.Buffers.Binary;

namespace FunCraft.Protocol.IO
{
    public ref struct PacketReader
    {
        private ReadOnlySpan<byte> _source;
        public int BytesRead { get; private set; } = 0;

        public PacketReader(ReadOnlySpan<byte> source)
        {
            _source = source;
            BytesRead = 0;
        }

        public int ReadVarInt()
        {
            if (!VarInt.TryRead(_source[BytesRead..], out var value, out var bytesRead))
            {
                throw new InvalidDataException(nameof(VarInt));
            }

            BytesRead += bytesRead;
            return value;
        }

        public long ReadLong()
        {
            if (_source.Length - BytesRead < sizeof(long))
            {
                throw new InvalidDataException(nameof(Int64));
            }

            var value = BinaryPrimitives.ReadInt64BigEndian(_source[BytesRead..]);
            BytesRead += sizeof(long);
            return value;
        }

        public long ReadVarLong()
        {
            if (!VarLong.TryRead(_source[BytesRead..], out var value, out var bytesRead))
            {
                throw new InvalidDataException(nameof(VarLong));
            }

            BytesRead += bytesRead;
            return value;
        }

        public ushort ReadUInt16()
        {
            if (_source.Length - BytesRead < sizeof(ushort))
            {
                throw new InvalidDataException(nameof(UInt16));
            }

            var value = BinaryPrimitives.ReadUInt16BigEndian(_source[BytesRead..]);
            BytesRead += sizeof(ushort);
            return value;
        }

        public string ReadString()
        {
            if (!McString.TryRead(_source[BytesRead..], out var value, out var bytesRead))
            {
                throw new InvalidDataException(nameof(String));
            }

            BytesRead += bytesRead;
            return value;
        }

        public Guid ReadGuid()
        {
            if (_source.Length - BytesRead < 16)
            {
                throw new InvalidDataException(nameof(Guid));
            }

            var value = new Guid(_source[BytesRead..(BytesRead + 16)]);
            BytesRead += 16;
            return value;
        }
    }
}
