namespace FunCraft.Protocol.Packets.Play.Outgoing
{
    using IO;
    using Types;

    /// <summary>
    /// 0x14 — Set Container Slot (S→C)<br/>
    /// Places an item into a specific inventory slot.
    /// Window ID 0 = player inventory; slots 36–44 = hotbar slots 0–8.
    /// </summary>
    public sealed class SetContainerSlotPacket : IPacket
    {
        public const int Id = 0x14;
        public int PacketId => Id;

        public required int WindowId { get; init; }
        public required int StateId { get; init; }

        /// <summary>
        /// 0 = empty slot.
        /// </summary>
        public required short Slot { get; init; }
        public required int ItemId { get; init; }
        public required int Count { get; init; }

        public int GetLength() =>
            VarInt.GetSize(WindowId) +
            VarInt.GetSize(StateId) +
            sizeof(short) +
            SlotSize();

        public void Write(Span<byte> destination, out int bytesWritten)
        {
            var writer = new PacketWriter(destination);
            writer.WriteVarInt(WindowId);
            writer.WriteVarInt(StateId);
            writer.WriteShort(Slot);
            WriteSlot(ref writer);
            bytesWritten = writer.BytesWritten;
        }

        private void WriteSlot(ref PacketWriter writer)
        {
            writer.WriteVarInt(Count);
            if (Count < 1)
            {
                return;
            }

            writer.WriteVarInt(ItemId);
            writer.WriteVarInt(0);
            writer.WriteVarInt(0);
        }

        private int SlotSize()
        {
            var size = VarInt.GetSize(Count);
            if (Count > 0)
            {
                size += VarInt.GetSize(ItemId) + 1 + 1;
            }

            return size;
        }
    }
}