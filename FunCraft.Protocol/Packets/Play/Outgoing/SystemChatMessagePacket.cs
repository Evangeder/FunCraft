namespace FunCraft.Protocol.Packets.Play.Outgoing
{
    using IO;

    /// <summary>
    /// 0x77 — System Chat Message (S→C, protocol 773)<br/>
    /// Content is a Text Component encoded as network NBT.
    /// For plain text the client accepts a root TAG_String (type 0x08):
    ///   [0x08][UInt16BE length][UTF-8 bytes][bool overlay]
    /// No root tag name is written in network NBT.
    /// </summary>
    public sealed class SystemChatMessagePacket : IPacket
    {
        public const int Id = 0x77;
        public int PacketId => Id;

        /// <summary>
        /// Pre-encoded UTF-8 bytes of the text content.
        /// </summary>
        public required ReadOnlyMemory<byte> Content { get; init; }

        /// <summary>
        /// True = action bar, false = chat.
        /// </summary>
        public bool Overlay { get; init; }

        private const byte NbtTagString = 0x08;

        /// <summary>
        /// TAG_String wire size: type(1) + UInt16(2) + payload + bool(1)
        /// </summary>
        public int GetLength() => 1 + 2 + Content.Length + sizeof(bool);

        public void Write(Span<byte> destination, out int bytesWritten)
        {
            var writer = new PacketWriter(destination);
            writer.WriteByte(NbtTagString);
            writer.WriteUInt16((ushort)Content.Length);
            writer.WriteRawBytes(Content.Span);
            writer.WriteBoolean(Overlay);
            bytesWritten = writer.BytesWritten;
        }
    }
}