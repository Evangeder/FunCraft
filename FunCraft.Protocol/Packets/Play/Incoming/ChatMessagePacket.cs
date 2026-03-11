using System.Buffers;

namespace FunCraft.Protocol.Packets.Play.Incoming
{
    using Types;

    /// <summary>
    /// 0x05 — Chat Message (C→S)<br/>
    /// Sent when the player sends a chat message. The packet also carries a
    /// timestamp, salt, optional signature, and an acknowledgement bitset —
    /// all of which we read past but don't use in offline mode.
    /// </summary>
    public sealed class ChatMessagePacket : IIncomingPacket
    {
        private const int TimestampSaltLength = 16;
        private const int SignatureLength = 256;

        public const int Id = 0x08;

        /// <summary>
        /// The raw message text, max 256 characters.
        /// </summary>
        public string Message { get; private set; } = string.Empty;

        public bool TryRead(ref SequenceReader<byte> reader)
        {
            if (!McString.TryRead(ref reader, out var message)) return false;
            Message = message;

            if (reader.Remaining < TimestampSaltLength) return false;
            reader.Advance(TimestampSaltLength);

            if (!reader.TryRead(out var hasSig)) return false;
            if (hasSig != 0)
            {
                if (reader.Remaining < SignatureLength) return false;
                reader.Advance(SignatureLength);
            }

            if (!VarInt.TryRead(ref reader, out _)) return false;

            if (reader.Remaining < 3) return false;
            reader.Advance(3);

            return true;
        }
    }
}