using System.Buffers;

namespace FunCraft.Protocol.Packets.Play.Incoming
{
    using Types;

    /// <summary>
    /// 0x08 — Chat Message (C→S)<br/>
    /// The raw message bytes (max 256 chars) without decoding to string.
    /// Timestamp, salt, optional signature and the acknowledgement bitset are
    /// read past but discarded — we run in offline mode.
    /// </summary>
    public sealed class ChatMessagePacket : IIncomingPacket
    {
        private const int TimestampSaltLength = 16;
        private const int SignatureLength = 256;

        public const int Id = 0x08;

        /// <summary>
        /// Raw UTF-8 bytes of the chat message, max 256 chars.
        /// </summary>
        public ReadOnlyMemory<byte> Message { get; private set; } = ReadOnlyMemory<byte>.Empty;

        public bool TryRead(ref SequenceReader<byte> reader)
        {
            if (!McString.TryReadRaw(ref reader, out var message)) return false;
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