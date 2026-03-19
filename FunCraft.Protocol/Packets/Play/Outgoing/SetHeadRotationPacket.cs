namespace FunCraft.Protocol.Packets.Play.Outgoing
{
    using IO;
    using Types;

    /// <summary>
    /// 0x51 — Set Head Rotation (S→C)
    /// </summary>
    public sealed class SetHeadRotationPacket : IPacket
    {
        public const int Id = 0x51;
        public int PacketId => Id;

        public required int EntityId { get; init; }

        /// <summary>
        /// Head yaw in degrees.
        /// </summary>
        public required float HeadYaw { get; init; }

        public int GetLength() => VarInt.GetSize(EntityId) + 1;

        public void Write(Span<byte> destination, out int bytesWritten)
        {
            var writer = new PacketWriter(destination);
            writer.WriteVarInt(EntityId);
            writer.WriteByte((byte)(int)(HeadYaw / 360.0f * 256.0f));
            bytesWritten = writer.BytesWritten;
        }
    }
}