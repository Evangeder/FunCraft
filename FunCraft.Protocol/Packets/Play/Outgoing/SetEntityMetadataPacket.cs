namespace FunCraft.Protocol.Packets.Play.Outgoing
{
    using IO;
    using Types;

    /// <summary>
    /// 0x61 — Set Entity Metadata (S→C)<br/>
    /// Updates one or more metadata fields on an existing entity.
    /// This implementation covers only the item-entity Item field
    /// (index 8, type 7 / Slot) which is all we need for dropped items.
    /// </summary>
    public sealed class SetEntityMetadataPacket : IPacket
    {
        public const int Id = 0x61;
        public int PacketId => Id;

        public required int EntityId { get; init; }

        /// <summary>
        /// Protocol item ID. Must be &gt; 0.
        /// </summary>
        public required int ItemId { get; init; }

        /// <summary>
        /// Stack count.
        /// </summary>
        public required int Count { get; init; }

        // Metadata constants
        private const byte ItemIndex = 8;   // index of the Item field on item entities
        private const int TypeSlot = 7;   // metadata value type: Slot
        private const byte Terminator = 0xFF;

        public int GetLength()
        {
            // entity_id(VarInt) + index(1) + type(VarInt) + slot_count(VarInt) +
            // item_id(VarInt) + add_components(VarInt=0) + remove_components(VarInt=0) + terminator(1)
            return VarInt.GetSize(EntityId)
                 + 1
                 + VarInt.GetSize(TypeSlot)
                 + VarInt.GetSize(Count)
                 + VarInt.GetSize(ItemId)
                 + 1 // add components = 0
                 + 1 // remove components = 0
                 + 1; // 0xFF terminator
        }

        public void Write(Span<byte> destination, out int bytesWritten)
        {
            var writer = new PacketWriter(destination);
            writer.WriteVarInt(EntityId);

            // Single metadata entry: item slot
            writer.WriteByte(ItemIndex);
            writer.WriteVarInt(TypeSlot);
            writer.WriteVarInt(Count);      // Slot.Count  (> 0 = present)
            writer.WriteVarInt(ItemId);     // Slot.ItemId
            writer.WriteVarInt(0);          // components to add
            writer.WriteVarInt(0);          // components to remove

            writer.WriteByte(Terminator);
            bytesWritten = writer.BytesWritten;
        }
    }
}