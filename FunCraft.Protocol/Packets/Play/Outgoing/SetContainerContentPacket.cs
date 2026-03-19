namespace FunCraft.Protocol.Packets.Play.Outgoing
{
    using IO;
    using Types;

    /// <summary>
    /// 0x12 — Set Container Content (S→C)
    /// </summary>
    public sealed class SetContainerContentPacket : IPacket
    {
        public const int Id = 0x12;
        public int PacketId => Id;
        public required int WindowId { get; init; }
        public required int StateId { get; init; }
        public required (int ItemId, int Count, int Damage)[] Slots { get; init; }
        public int CarriedItemId { get; init; }
        public int CarriedItemCount { get; init; }
        public int CarriedDamage { get; init; }

        public int GetLength()
        {
            var size = VarInt.GetSize(WindowId)
                     + VarInt.GetSize(StateId)
                     + VarInt.GetSize(Slots.Length);

            foreach (var (itemId, count, damage) in Slots)
                size += SetContainerSlotPacket.SlotWireSize(itemId, count, damage);

            size += SetContainerSlotPacket.SlotWireSize(CarriedItemId, CarriedItemCount, CarriedDamage);
            return size;
        }

        public void Write(Span<byte> destination, out int bytesWritten)
        {
            var writer = new PacketWriter(destination);
            writer.WriteVarInt(WindowId);
            writer.WriteVarInt(StateId);
            writer.WriteVarInt(Slots.Length);

            foreach (var (itemId, count, damage) in Slots)
                SetContainerSlotPacket.WriteSlot(ref writer, itemId, count, damage);

            SetContainerSlotPacket.WriteSlot(ref writer, CarriedItemId, CarriedItemCount, CarriedDamage);
            bytesWritten = writer.BytesWritten;
        }
    }
}