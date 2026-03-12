using System.Buffers;

namespace FunCraft.Protocol.Packets.Play.Incoming
{
    using Types;

    /// <summary>
    /// 0x11 — Click Container (C→S)<br/>
    /// We only read the header fields (windowId, stateId, slot, button, mode).
    /// ChangedSlots are intentionally ignored — the server applies inventory operations
    /// authoritatively from Mode/Button/Slot so we never need to parse item component data,
    /// whose format is type-specific and variable-length in protocol 773.
    /// </summary>
    public sealed class ClickContainerPacket : IIncomingPacket
    {
        public const int Id = 0x11;

        public byte WindowId { get; private set; }
        public int StateId { get; private set; }
        public short Slot { get; private set; }
        public byte Button { get; private set; }
        public int Mode { get; private set; }

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

            WindowId = windowId;
            StateId = stateId;
            Slot = slot;
            Button = button;
            Mode = mode;
            return true;
        }
    }
}