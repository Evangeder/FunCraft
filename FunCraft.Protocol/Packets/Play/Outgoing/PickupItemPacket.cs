namespace FunCraft.Protocol.Packets.Play.Outgoing
{
    using IO;
    using Types;

    /// <summary>
    /// 0x7A — Pickup Item (S→C)
    /// </summary>
    public sealed class PickupItemPacket : IPacket
    {
        public const int Id = 0x7A;
        public int PacketId => Id;

        public required int CollectedEntityId { get; init; }
        public required int CollectorEntityId { get; init; }

        /// <summary>
        /// Stack count that was picked up. Use 1 for XP orbs;
        /// otherwise the actual count of items collected.
        /// </summary>
        public required int Count { get; init; }

        public int GetLength() =>
            VarInt.GetSize(CollectedEntityId) +
            VarInt.GetSize(CollectorEntityId) +
            VarInt.GetSize(Count);

        public void Write(Span<byte> destination, out int bytesWritten)
        {
            var writer = new PacketWriter(destination);
            writer.WriteVarInt(CollectedEntityId);
            writer.WriteVarInt(CollectorEntityId);
            writer.WriteVarInt(Count);
            bytesWritten = writer.BytesWritten;
        }
    }
}