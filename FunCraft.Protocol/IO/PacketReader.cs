using System.Buffers;

namespace FunCraft.Protocol.IO
{
    using Types;

    public ref struct PacketReader
    {
        private SequenceReader<byte> _reader;

        public PacketReader(ref SequenceReader<byte> reader)
        {
            _reader = reader;
        }

        public int ReadVarInt()
        {
            if (!VarInt.TryRead(ref _reader, out var value))
            {
                throw new InvalidDataException(nameof(VarInt));
            }
            return value;
        }

        public long ReadLong()
        {
            if (!_reader.TryReadBigEndian(out long value))
            {
                throw new InvalidDataException(nameof(Int64));
            }
            return value;
        }

        public long ReadVarLong()
        {
            if (!VarLong.TryRead(ref _reader, out var value))
            {
                throw new InvalidDataException(nameof(VarLong));
            }
            return value;
        }

        public ushort ReadUInt16()
        {
            if (!_reader.TryReadBigEndian(out short value))
            {
                throw new InvalidDataException(nameof(UInt16));
            }
            return (ushort)value;
        }

        public string ReadString()
        {
            if (!McString.TryRead(ref _reader, out var value))
            {
                throw new InvalidDataException(nameof(String));
            }
            return value;
        }

        public Guid ReadGuid()
        {
            if (_reader.Remaining < 16)
            {
                throw new InvalidDataException(nameof(Guid));
            }
            Span<byte> bytes = stackalloc byte[16];
            _reader.TryCopyTo(bytes);
            _reader.Advance(16);
            return new Guid(bytes);
        }

        public ReadOnlySpan<byte> ReadBytes(int length)
        {
            if (_reader.Remaining < length)
            {
                throw new InvalidDataException("Not enough bytes");
            }
            var span = _reader.CurrentSpan[..length];
            _reader.Advance(length);
            return span;
        }
    }
}
