using System.Buffers;
using System.Buffers.Binary;

namespace FunCraft.Protocol.Packets.Play.Incoming
{
    /// <summary>
    /// 0x1D — Set Player Position (C→S)
    /// <br/>Sent when the player moves without changing their look direction.
    /// </summary>
    public class SetPlayerPositionPacket : IIncomingPacket
    {
        public const int Id = 0x1D;

        /// <summary>
        /// Payload size: X + Y + Z (3 × 8 bytes) + Flags (1 byte) = 25 bytes.
        /// </summary>
        public const int MinPayloadSize = 25;

        private const int OffsetX = 0;
        private const int OffsetY = 8;
        private const int OffsetZ = 16;

        public double X { get; private set; }
        public double Y { get; private set; }
        public double Z { get; private set; }

        /// <summary>
        /// Bit mask: 0x01 = on ground.
        /// </summary>
        public byte Flags { get; private set; }

        public bool TryRead(ref SequenceReader<byte> reader)
        {
            if (reader.Remaining < MinPayloadSize) return false;

            Span<byte> buf = stackalloc byte[MinPayloadSize];
            if (!reader.TryCopyTo(buf)) return false;
            reader.Advance(MinPayloadSize);

            X = BinaryPrimitives.ReadDoubleBigEndian(buf[OffsetX..]);
            Y = BinaryPrimitives.ReadDoubleBigEndian(buf[OffsetY..]);
            Z = BinaryPrimitives.ReadDoubleBigEndian(buf[OffsetZ..]);
            Flags = buf[24];
            return true;
        }
    }
}