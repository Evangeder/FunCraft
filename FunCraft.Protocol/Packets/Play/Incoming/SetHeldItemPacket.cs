using System.Buffers;

namespace FunCraft.Protocol.Packets.Play.Incoming
{
    /// <summary>
    /// 0x34 — Set Held GetItemId (C→S)<br/>
    /// Sent when the player changes the selected hotbar slot (0–8).
    /// </summary>
    public sealed class SetHeldItemPacket : IIncomingPacket
    {
        public const int Id = 0x34;

        public short Slot { get; private set; }

        public bool TryRead(ref SequenceReader<byte> reader)
        {
            if (!reader.TryReadBigEndian(out short slot)) return false;
            Slot = slot;
            return true;
        }
    }
}