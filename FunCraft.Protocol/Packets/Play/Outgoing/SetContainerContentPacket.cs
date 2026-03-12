namespace FunCraft.Protocol.Packets.Play.Outgoing
{
    using IO;
    using Types;

    /// <summary>
    /// 0x12 — Set Container Content (S→C)<br/>
    /// <br/>Replaces the entire contents of a container window.
    /// <br/>WindowId 0 = player inventory (46 slots: craft/armour/main/hotbar/offhand).
    /// </summary>
    public sealed class SetContainerContentPacket : IPacket
    {
        public const int Id = 0x12;
        public int PacketId => Id;

        public required int WindowId { get; init; }
        public required int StateId { get; init; }

        /// <summary>
        /// Exactly <c>SlotCount</c> entries. An entry with Count ≤ 0 is an empty slot.
        /// </summary>
        public required (int ItemId, int Count)[] Slots { get; init; }

        /// <summary>
        /// Carried item (item being dragged with the mouse) — almost always empty.
        /// </summary>
        public int CarriedItemId { get; init; }
        public int CarriedItemCount { get; init; }

        public int GetLength()
        {
            var size = VarInt.GetSize(WindowId)
                     + VarInt.GetSize(StateId)
                     + VarInt.GetSize(Slots.Length);

            foreach (var (itemId, count) in Slots)
                size += SlotSize(itemId, count);

            size += SlotSize(CarriedItemId, CarriedItemCount);
            return size;
        }

        public void Write(Span<byte> destination, out int bytesWritten)
        {
            var writer = new PacketWriter(destination);
            writer.WriteVarInt(WindowId);
            writer.WriteVarInt(StateId);
            writer.WriteVarInt(Slots.Length);

            foreach (var (itemId, count) in Slots)
                WriteSlot(ref writer, itemId, count);

            WriteSlot(ref writer, CarriedItemId, CarriedItemCount);
            bytesWritten = writer.BytesWritten;
        }

        private static void WriteSlot(ref PacketWriter w, int itemId, int count)
        {
            w.WriteVarInt(count);
            if (count < 1) return;
            w.WriteVarInt(itemId);
            w.WriteVarInt(0); // number of components added
            w.WriteVarInt(0); // number of components removed
        }

        private static int SlotSize(int itemId, int count)
        {
            var s = VarInt.GetSize(count);
            if (count > 0) s += VarInt.GetSize(itemId) + 1 + 1; // +1+1 for the two 0-varint component counts
            return s;
        }
    }
}