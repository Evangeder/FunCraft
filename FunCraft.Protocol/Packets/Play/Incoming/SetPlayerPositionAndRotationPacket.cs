using System.Buffers;
using System.Buffers.Binary;

namespace FunCraft.Protocol.Packets.Play.Incoming
{
    /// <summary>
    /// 0x1E — Set Player Position and Rotation (C→S)
    /// <br/>Sent when the player moves and/or changes look direction in the same tick.
    /// </summary>
    public class SetPlayerPositionAndRotationPacket : IIncomingPacket
    {
        public const int Id = 0x1E;

        /// <summary>
        /// Payload size: X + Y + Z (3 × 8) + Yaw (4) + Pitch (4) + Flags (1) = 33 bytes.
        /// </summary>
        public const int MinPayloadSize = 33;

        private const int OffsetX = 0;
        private const int OffsetY = 8;
        private const int OffsetZ = 16;
        private const int OffsetYaw = 24;
        private const int OffsetPitch = 28;

        public double X { get; private set; }
        public double Y { get; private set; }
        public double Z { get; private set; }
        public float Yaw { get; private set; }
        public float Pitch { get; private set; }

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
            Yaw = BinaryPrimitives.ReadSingleBigEndian(buf[OffsetYaw..]);
            Pitch = BinaryPrimitives.ReadSingleBigEndian(buf[OffsetPitch..]);
            Flags = buf[32];
            return true;
        }
    }
}