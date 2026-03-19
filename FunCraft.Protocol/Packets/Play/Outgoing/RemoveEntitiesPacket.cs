namespace FunCraft.Protocol.Packets.Play.Outgoing
{
    using IO;
    using Types;

    /// <summary>
    /// 0x4B — Remove Entities (S→C)
    /// </summary>
    public sealed class RemoveEntitiesPacket : IPacket
    {
        public const int Id = 0x4B;
        public int PacketId => Id;
        public required IReadOnlyList<int> EntityIds { get; init; }

        public int GetLength()
        {
            var size = VarInt.GetSize(EntityIds.Count);
            foreach (var id in EntityIds)
            {
                size += VarInt.GetSize(id);
            }

            return size;
        }

        public void Write(Span<byte> destination, out int bytesWritten)
        {
            var writer = new PacketWriter(destination);
            writer.WriteVarInt(EntityIds.Count);
            foreach (var id in EntityIds)
            {
                writer.WriteVarInt(id);
            }

            bytesWritten = writer.BytesWritten;
        }
    }
}