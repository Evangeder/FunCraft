using System.Buffers;

namespace FunCraft.Protocol.Packets.Play.Incoming
{
    using Types;

    /// <summary>
    /// 0x3F — Use GetItemId On (C→S)<br/>
    /// Sent when the player right-clicks a block face (block placement, interaction).
    /// </summary>
    public sealed class UseItemOnPacket : IIncomingPacket
    {
        /// <summary>
        /// Cursor X/Y/Z (3× float = 12 bytes) + Inside GetBlockId (bool) + World Border Hit (bool) = 14 bytes
        /// </summary>
        private const int CursorAndInsideBlockLength = 14;

        public const int Id = 0x3F;

        public int Hand { get; private set; }
        public BlockPosition Location { get; private set; }
        public int Face { get; private set; }
        public int Sequence { get; private set; }

        public bool TryRead(ref SequenceReader<byte> reader)
        {
            if (!VarInt.TryRead(ref reader, out var hand)) return false;
            if (!reader.TryReadBigEndian(out long encodedPos)) return false;
            if (!VarInt.TryRead(ref reader, out var face)) return false;

            if (reader.Remaining < CursorAndInsideBlockLength) return false;
            reader.Advance(CursorAndInsideBlockLength);

            if (!VarInt.TryRead(ref reader, out var sequence)) return false;

            Hand = hand;
            Location = BlockPosition.Decode(encodedPos);
            Face = face;
            Sequence = sequence;
            return true;
        }
    }
}