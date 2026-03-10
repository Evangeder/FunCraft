using System.Buffers;
using System.Buffers.Binary;

namespace FunCraft.Protocol.Packets.Play.Incoming
{
    /// <summary>
    /// 0x1F — Set Player Rotation (C→S)
    /// <br/>Sent when the player changes look direction without moving.
    /// </summary>
    public class SetPlayerRotationPacket : IIncomingPacket
    {
        public const int Id = 0x1F;

        /// <summary>
        /// Payload size: Yaw (4) + Pitch (4) + Flags (1) = 9 bytes.
        /// </summary>
        public const int MinPayloadSize = 9;

        private const int OffsetYaw = 0;
        private const int OffsetPitch = 4;

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

            Yaw = BinaryPrimitives.ReadSingleBigEndian(buf[OffsetYaw..]);
            Pitch = BinaryPrimitives.ReadSingleBigEndian(buf[OffsetPitch..]);
            Flags = buf[8];
            return true;
        }
    }
}