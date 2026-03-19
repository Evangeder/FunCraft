namespace FunCraft.Protocol.Packets.Play.Outgoing
{
    using IO;
    using Types;

    /// <summary>
    /// 0x01 — Spawn Entity (S→C)<br/>
    /// Creates any entity on the client. Do NOT use for the local player —
    /// the client auto-creates that one from the Login packet.<br/>
    /// <para>
    /// Velocity is encoded as LpVec3 (variable length, 1–10 bytes).
    /// Zero velocity (the default) encodes as a single byte 0x00.
    /// </para>
    /// </summary>
    public sealed class SpawnEntityPacket : IPacket
    {
        public const int Id = 0x01;
        public int PacketId => Id;

        public required int EntityId { get; init; }
        public required Guid EntityUuid { get; init; }

        /// <summary>ID in the <c>minecraft:entity_type</c> registry.</summary>
        public required int EntityType { get; init; }

        public required double X { get; init; }
        public required double Y { get; init; }
        public required double Z { get; init; }

        public double VelocityX { get; init; } = 0;
        public double VelocityY { get; init; } = 0;
        public double VelocityZ { get; init; } = 0;

        public required float Pitch { get; init; }
        public required float Yaw { get; init; }

        /// <summary>Head rotation — only meaningful for living entities.</summary>
        public required float HeadYaw { get; init; }

        /// <summary>
        /// Entity-type-specific data field. 0 for most entities.
        /// </summary>
        public int Data { get; init; } = 0;

        public int GetLength() =>
            VarInt.GetSize(EntityId) +
            16 +                                        // UUID (128-bit)
            VarInt.GetSize(EntityType) +
            sizeof(double) * 3 +                        // X, Y, Z
            PacketWriter.LpVec3Size(VelocityX, VelocityY, VelocityZ) +
            3 +                                         // Pitch, Yaw, HeadYaw (1 byte each)
            VarInt.GetSize(Data);

        public void Write(Span<byte> destination, out int bytesWritten)
        {
            var writer = new PacketWriter(destination);
            writer.WriteVarInt(EntityId);
            writer.WriteGuid(EntityUuid);
            writer.WriteVarInt(EntityType);
            writer.WriteDouble(X);
            writer.WriteDouble(Y);
            writer.WriteDouble(Z);
            writer.WriteLpVec3(VelocityX, VelocityY, VelocityZ);
            writer.WriteByte(ToAngle(Pitch));
            writer.WriteByte(ToAngle(Yaw));
            writer.WriteByte(ToAngle(HeadYaw));
            writer.WriteVarInt(Data);
            bytesWritten = writer.BytesWritten;
        }

        /// <summary>Converts a degree angle to the Minecraft Angle byte encoding.</summary>
        private static byte ToAngle(float degrees)
            => (byte)(int)(degrees / 360.0f * 256.0f);
    }
}