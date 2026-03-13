using System.Buffers;
using System.Buffers.Binary;

namespace FunCraft.Protocol.Packets.Play.Incoming
{
    using Types;

    /// <summary>
    /// 0x3F — Use Item On (C→S)<br/>
    /// Sent when the player right-clicks a block face (block placement, interaction).
    /// </summary>
    public sealed class UseItemOnPacket : IIncomingPacket
    {
        public const int Id = 0x3F;

        public int Hand { get; private set; }
        public BlockPosition Location { get; private set; }

        /// <summary>
        /// The face of the target block that was clicked:
        /// 0 = bottom (-Y), 1 = top (+Y), 2 = north (-Z), 3 = south (+Z),
        /// 4 = west (-X), 5 = east (+X).
        /// </summary>
        public int Face { get; private set; }

        /// <summary>
        /// Cursor X position within the clicked block face (0.0 – 1.0).
        /// </summary>
        public float CursorX { get; private set; }
        /// <summary>
        /// Cursor Y position within the clicked block face (0.0 – 1.0).
        /// </summary>
        public float CursorY { get; private set; }
        /// <summary>
        /// Cursor Z position within the clicked block face (0.0 – 1.0).
        /// </summary>
        public float CursorZ { get; private set; }

        /// <summary>
        /// True if the player's head is inside a block.
        /// </summary>
        public bool InsideBlock { get; private set; }

        public int Sequence { get; private set; }

        public bool TryRead(ref SequenceReader<byte> reader)
        {
            if (!VarInt.TryRead(ref reader, out var hand))
            {
                return false;
            }

            if (!reader.TryReadBigEndian(out long encodedPos))
            {
                return false;
            }

            if (!VarInt.TryRead(ref reader, out var face))
            {
                return false;
            }

            if (reader.Remaining < 14)
            {
                return false; // 3x float + bool + bool
            }

            Span<byte> floatBuf = stackalloc byte[4];

            reader.TryCopyTo(floatBuf); reader.Advance(4);
            var cursorX = BinaryPrimitives.ReadSingleBigEndian(floatBuf);

            reader.TryCopyTo(floatBuf); reader.Advance(4);
            var cursorY = BinaryPrimitives.ReadSingleBigEndian(floatBuf);

            reader.TryCopyTo(floatBuf); reader.Advance(4);
            var cursorZ = BinaryPrimitives.ReadSingleBigEndian(floatBuf);

            if (!reader.TryRead(out var insideBlock))
            {
                return false;
            }

            if (!reader.TryRead(out _))
            {
                return false; // world border hit — unused
            }

            if (!VarInt.TryRead(ref reader, out var sequence)) return false;

            Hand = hand;
            Location = BlockPosition.Decode(encodedPos);
            Face = face;
            CursorX = cursorX;
            CursorY = cursorY;
            CursorZ = cursorZ;
            InsideBlock = insideBlock != 0;
            Sequence = sequence;
            return true;
        }
    }
}