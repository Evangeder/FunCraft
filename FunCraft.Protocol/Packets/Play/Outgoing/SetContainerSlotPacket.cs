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
        public required short Slot { get; init; }
        public required int ItemId { get; init; }
        public required int Count { get; init; }

        /// <summary>
        /// Current damage on the item (0 = pristine).
        /// Serialised as the <c>minecraft:damage</c> structured component (VarInt value).
        /// </summary>
        public int Damage { get; init; } = 0;

        // minecraft:damage is component type ID 3 per the slot data spec.
        // Type 4 is minecraft:unbreakable (no fields) — do not confuse them.
        private const int DamageComponentTypeId = 3;

        public int GetLength() =>
            VarInt.GetSize(WindowId) +
            VarInt.GetSize(StateId) +
            sizeof(short) +
            SlotWireSize(ItemId, Count, Damage);

        public void Write(Span<byte> destination, out int bytesWritten)
        {
            var writer = new PacketWriter(destination);
            writer.WriteVarInt(WindowId);
            writer.WriteVarInt(StateId);
            writer.WriteShort(Slot);
            WriteSlot(ref writer, ItemId, Count, Damage);
            bytesWritten = writer.BytesWritten;
        }

        internal static void WriteSlot(ref PacketWriter w, int itemId, int count, int damage)
        {
            w.WriteVarInt(count);
            if (count < 1) return;
            w.WriteVarInt(itemId);

            // Both counts must be written BEFORE any component data.
            w.WriteVarInt(damage > 0 ? 1 : 0);  // number of components to add
            w.WriteVarInt(0);                    // number of components to remove

            // Component data follows (only if add count > 0).
            if (damage > 0)
            {
                w.WriteVarInt(DamageComponentTypeId);  // minecraft:damage = 3
                w.WriteVarInt(damage);                 // VarInt value
            }
        }

        internal static int SlotWireSize(int itemId, int count, int damage)
        {
            var s = VarInt.GetSize(count);
            if (count < 1) return s;
            s += VarInt.GetSize(itemId);
            s += 1;  // add-count VarInt (0 or 1, always 1 byte)
            s += 1;  // remove-count VarInt(0) = 1 byte
            if (damage > 0)
                s += VarInt.GetSize(DamageComponentTypeId) + VarInt.GetSize(damage);
            return s;
        }
    }
}