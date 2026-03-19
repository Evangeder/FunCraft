namespace FunCraft.Protocol.Packets.Play.Outgoing
{
    using IO;
    using Types;

    /// <summary>
    /// 0x36 — Update Entity Rotation (S→C)
    /// </summary>
    public sealed class UpdateEntityRotationPacket : IPacket
    {
        public const int Id = 0x36;
        public int PacketId => Id;

        public required int EntityId { get; init; }
        public required float Yaw { get; init; }
        public required float Pitch { get; init; }
        public required bool OnGround { get; init; }

        public int GetLength() =>
            VarInt.GetSize(EntityId) +
            2 +     // Yaw + Pitch angle bytes
            1;      // OnGround

        public void Write(Span<byte> destination, out int bytesWritten)
        {
            var writer = new PacketWriter(destination);
            writer.WriteVarInt(EntityId);
            writer.WriteByte(ToAngle(Yaw));
            writer.WriteByte(ToAngle(Pitch));
            writer.WriteBoolean(OnGround);
            bytesWritten = writer.BytesWritten;
        }

        private static byte ToAngle(float degrees)
            => (byte)(int)(degrees / 360.0f * 256.0f);
    }
}