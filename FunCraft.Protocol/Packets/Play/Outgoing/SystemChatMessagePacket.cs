using System.Text;

namespace FunCraft.Protocol.Packets.Play.Outgoing
{
    using IO;

    /// <summary>
    /// 0x77 — System Chat Message (S→C, protocol 773)<br/>
    /// Since 1.20.3 the Content field is a Text Component encoded as network NBT.<br/>
    /// For plain text (no styling) the client accepts a root TAG_String (type 0x08):
    /// [0x08][UInt16BE payload length][UTF-8 bytes][bool overlay]
    /// No root tag name is written in network NBT.
    /// </summary>
    public sealed class SystemChatMessagePacket : IPacket
    {
        public const int Id = 0x77;
        public int PacketId => Id;

        public required string Content { get; init; }

        /// <summary>
        /// True = action bar, false = chat.
        /// </summary>
        public bool Overlay { get; init; }

        private const byte NbtTagString = 0x08;

        private byte[]? _utf8;
        private byte[] Utf8 => _utf8 ??= Encoding.UTF8.GetBytes(Content);

        /// <summary>
        /// // TAG_String: type + UInt16 len + payload
        /// </summary>
        public int GetLength() =>
            1 + 2 + Utf8.Length +
            sizeof(bool);

        public void Write(Span<byte> destination, out int bytesWritten)
        {
            var writer = new PacketWriter(destination);
            writer.WriteByte(NbtTagString);
            writer.WriteUInt16((ushort)Utf8.Length);
            writer.WriteRawBytes(Utf8.AsSpan());
            writer.WriteBoolean(Overlay);
            bytesWritten = writer.BytesWritten;
        }
    }
}