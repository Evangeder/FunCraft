using System.Buffers;

namespace FunCraft.Protocol.Packets.Play.Incoming
{
    using Types;

    /// <summary>
    /// 0x11 — Click Container (C→S)<br/>
    /// Sent whenever the player interacts with a container slot (inventory, chest, etc.).
    /// We read the full packet so we can mirror slot changes server-side.
    /// </summary>
    public sealed class ClickContainerPacket : IIncomingPacket
    {
        public const int Id = 0x11;

        public byte WindowId { get; private set; }
        public int StateId { get; private set; }
        public short Slot { get; private set; }
        public byte Button { get; private set; }
        public int Mode { get; private set; }

        /// <summary>
        /// All slots whose contents changed as a result of this click.
        /// Key = wire slot index, Value = (ItemId, Count) — ItemId 0 means the slot is now empty.
        /// </summary>
        public (short Slot, int ItemId, int Count)[] ChangedSlots { get; private set; } = [];

        public bool TryRead(ref SequenceReader<byte> reader)
        {
            if (!reader.TryRead(out var windowId))
            {
                return false;
            }

            if (!VarInt.TryRead(ref reader, out var stateId))
            {
                return false;
            }

            if (!reader.TryReadBigEndian(out short slot))
            {
                return false;
            }

            if (!reader.TryRead(out var button))
            {
                return false;
            }

            if (!VarInt.TryRead(ref reader, out var mode))
            {
                return false;
            }

            if (!VarInt.TryRead(ref reader, out var count))
            {
                return false;
            }

            var changed = new (short, int, int)[count];
            for (var i = 0; i < count; i++)
            {
                if (!reader.TryReadBigEndian(out short slotIdx))
                {
                    return false;
                }

                if (!TryReadSlotData(ref reader, out var itemId, out var itemCount))
                {
                    return false;
                }

                changed[i] = (slotIdx, itemId, itemCount);
            }

            TryReadSlotData(ref reader, out _, out _);

            WindowId = windowId;
            StateId = stateId;
            Slot = slot;
            Button = button;
            Mode = mode;
            ChangedSlots = changed;
            return true;
        }

        /// <summary>
        /// Reads one Slot data structure.<br/>
        /// Wire format: VarInt count — if &gt; 0: VarInt itemId, VarInt numAdds (skip adds), VarInt numRemoves (skip removes).
        /// </summary>
        private static bool TryReadSlotData(
            ref SequenceReader<byte> reader, out int itemId, out int count)
        {
            itemId = 0; count = 0;
            if (!VarInt.TryRead(ref reader, out var itemCount))
            {
                return false;
            }

            if (itemCount <= 0)
            {
                return true;
            }

            count = itemCount;
            if (!VarInt.TryRead(ref reader, out itemId))
            {
                return false;
            }

            if (!VarInt.TryRead(ref reader, out var numAdds))
            {
                return false;
            }
            for (var i = 0; i < numAdds; i++)
            {
                if (!VarInt.TryRead(ref reader, out _))
                {
                    return false;
                }
            }

            if (!VarInt.TryRead(ref reader, out var numRemoves))
            {
                return false;
            }

            for (var i = 0; i < numRemoves; i++)
            {
                if (!VarInt.TryRead(ref reader, out _))
                {
                    return false;
                }
            }

            return true;
        }
    }
}