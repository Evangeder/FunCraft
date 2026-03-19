namespace FunCraft.Protocol.Packets.Play.Outgoing
{
    using IO;
    using Types;

    /// <summary>
    /// 0x23 — Teleport Entity (S→C, resource <c>entity_position_sync</c>)
    /// </summary>
    public sealed class TeleportEntityPacket : IPacket
    {
        public const int Id = 0x23;
        public int PacketId => Id;

        public required int EntityId { get; init; }
        public required double X { get; init; }
        public required double Y { get; init; }
        public required double Z { get; init; }
        public double VelocityX { get; init; } = 0;
        public double VelocityY { get; init; } = 0;
        public double VelocityZ { get; init; } = 0;
        public required float Yaw { get; init; }
        public required float Pitch { get; init; }
        public bool OnGround { get; init; } = false;

        public int GetLength() =>
            VarInt.GetSize(EntityId) +
            sizeof(double) * 6 +    // X, Y, Z, VelocityX/Y/Z
            sizeof(float) * 2 +     // Yaw, Pitch (full floats, not angle bytes)
            1;                      // OnGround

        public void Write(Span<byte> destination, out int bytesWritten)
        {
            var writer = new PacketWriter(destination);
            writer.WriteVarInt(EntityId);
            writer.WriteDouble(X);
            writer.WriteDouble(Y);
            writer.WriteDouble(Z);
            writer.WriteDouble(VelocityX);
            writer.WriteDouble(VelocityY);
            writer.WriteDouble(VelocityZ);
            writer.WriteFloat(Yaw);
            writer.WriteFloat(Pitch);
            writer.WriteBoolean(OnGround);
            bytesWritten = writer.BytesWritten;
        }
    }
}