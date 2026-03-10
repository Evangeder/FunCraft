namespace FunCraft.Protocol.Packets.Play
{
    using IO;
    using Types;

    /// <summary>
    /// 0x46 — Synchronize Player Position (S→C)
    /// </summary>
    public class SynchronizePlayerPositionPacket : IPacket
    {
        public const int Id = 0x46;
        public int PacketId => Id;

        public required int TeleportId { get; init; }
        public required double X { get; init; }
        public required double Y { get; init; }
        public required double Z { get; init; }
        public required double VelocityX { get; init; }
        public required double VelocityY { get; init; }
        public required double VelocityZ { get; init; }
        public required float Yaw { get; init; }
        public required float Pitch { get; init; }

        /// <summary>
        /// Teleport Flags bitmask. 0 = all coordinates are absolute.
        /// </summary>
        public required int Flags { get; init; }

        public int GetLength() => VarInt.GetSize(TeleportId)
           + sizeof(double) * 6
           + sizeof(float) * 2
           + sizeof(int);

        public void Write(Span<byte> destination, out int bytesWritten)
        {
            var writer = new PacketWriter(destination);
            writer.WriteVarInt(TeleportId);
            writer.WriteDouble(X);
            writer.WriteDouble(Y);
            writer.WriteDouble(Z);
            writer.WriteDouble(VelocityX);
            writer.WriteDouble(VelocityY);
            writer.WriteDouble(VelocityZ);
            writer.WriteFloat(Yaw);
            writer.WriteFloat(Pitch);
            writer.WriteInt(Flags);
            bytesWritten = writer.BytesWritten;
        }
    }
}