namespace FunCraft.Protocol.Packets.Play.Outgoing
{
    using IO;

    /// <summary>
    /// 0x25 — Unload Chunk (S→C)
    /// <br/>Tells the client to drop a chunk column from memory.
    /// <br/>Note: the protocol sends Z before X.
    /// </summary>
    public sealed class UnloadChunkPacket : IPacket
    {
        public const int Id = 0x25;
        public int PacketId => Id;

        public required int ChunkX { get; init; }
        public required int ChunkZ { get; init; }

        public int GetLength() => sizeof(int) + sizeof(int);

        public void Write(Span<byte> destination, out int bytesWritten)
        {
            var writer = new PacketWriter(destination);
            writer.WriteInt(ChunkZ); // protocol sends Z first
            writer.WriteInt(ChunkX);
            bytesWritten = writer.BytesWritten;
        }
    }
}