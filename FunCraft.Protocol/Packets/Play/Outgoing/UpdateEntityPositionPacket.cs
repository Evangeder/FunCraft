namespace FunCraft.Protocol.Packets.Play.Outgoing
{
    using IO;
    using Types;

    /// <summary>
    /// 0x33 — Update Entity Position (S→C)
    /// </summary>
    public sealed class UpdateEntityPositionPacket : IPacket
    {
        public const int Id = 0x33;
        public int PacketId => Id;

        public required int EntityId { get; init; }
        public required short DeltaX { get; init; }
        public required short DeltaY { get; init; }
        public required short DeltaZ { get; init; }
        public required bool OnGround { get; init; }

        public int GetLength() =>
            VarInt.GetSize(EntityId) +
            sizeof(short) * 3 +
            1; // OnGround

        public void Write(Span<byte> destination, out int bytesWritten)
        {
            var writer = new PacketWriter(destination);
            writer.WriteVarInt(EntityId);
            writer.WriteShort(DeltaX);
            writer.WriteShort(DeltaY);
            writer.WriteShort(DeltaZ);
            writer.WriteBoolean(OnGround);
            bytesWritten = writer.BytesWritten;
        }
    }
}