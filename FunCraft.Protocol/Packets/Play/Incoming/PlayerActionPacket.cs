using System.Buffers;

namespace FunCraft.Protocol.Packets.Play.Incoming
{
    using Types;

    /// <summary>
    /// 0x28 — Player Action (C→S)<br/>
    /// Sent for digging, item interactions and other player actions.
    /// </summary>
    public sealed class PlayerActionPacket : IIncomingPacket
    {
        public const int Id = 0x28;

        public enum ActionStatus
        {
            StartedDigging = 0,
            CancelledDigging = 1,
            FinishedDigging = 2,
            DropItemStack = 3,
            DropItem = 4,
            ShootArrow = 5,
            SwapItemInHand = 6,
        }

        public ActionStatus Status { get; private set; }
        public BlockPosition Location { get; private set; }
        public byte Face { get; private set; }
        public int Sequence { get; private set; }

        public bool TryRead(ref SequenceReader<byte> reader)
        {
            if (!VarInt.TryRead(ref reader, out var status)) return false;
            if (!reader.TryReadBigEndian(out long encodedPos)) return false;
            if (!reader.TryRead(out var face)) return false;
            if (!VarInt.TryRead(ref reader, out var sequence)) return false;

            Status = (ActionStatus)status;
            Location = BlockPosition.Decode(encodedPos);
            Face = face;
            Sequence = sequence;
            return true;
        }
    }
}