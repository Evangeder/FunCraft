namespace FunCraft.Protocol.Packets.Play.Outgoing
{
    using IO;
    using Types;

    /// <summary>
    /// 0x34 — Update Entity Position and Rotation (S→C)
    /// </summary>
    public sealed class UpdateEntityPositionAndRotationPacket : IPacket
    {
        public const int Id = 0x34;
        public int PacketId => Id;

        public required int EntityId { get; init; }
        public required short DeltaX { get; init; }
        public required short DeltaY { get; init; }
        public required short DeltaZ { get; init; }

        /// <summary>
        /// Body yaw in degrees.
        /// </summary>
        public required float Yaw { get; init; }

        /// <summary>
        /// Pitch in degrees.
        /// </summary>
        public required float Pitch { get; init; }
        public required bool OnGround { get; init; }

        public int GetLength() =>
            VarInt.GetSize(EntityId) +
            sizeof(short) * 3 +
            2 +     // Yaw + Pitch angle bytes
            1;      // OnGround

        public void Write(Span<byte> destination, out int bytesWritten)
        {
            var writer = new PacketWriter(destination);
            writer.WriteVarInt(EntityId);
            writer.WriteShort(DeltaX);
            writer.WriteShort(DeltaY);
            writer.WriteShort(DeltaZ);
            writer.WriteByte(ToAngle(Yaw));
            writer.WriteByte(ToAngle(Pitch));
            writer.WriteBoolean(OnGround);
            bytesWritten = writer.BytesWritten;
        }

        private static byte ToAngle(float degrees)
            => (byte)(int)(degrees / 360.0f * 256.0f);
    }
}