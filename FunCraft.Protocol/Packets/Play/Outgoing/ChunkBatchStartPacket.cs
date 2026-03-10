namespace FunCraft.Protocol.Packets.Play.Outgoing
{
    /// <summary>
    /// 0x0C — Chunk Batch Start (S→C)
    /// <br/>Marks the beginning of a chunk batch. The client records the timestamp
    /// it receives this packet and uses it to calculate milliseconds-per-chunk
    /// after receiving <see cref="ChunkBatchFinishedPacket"/>.
    /// </summary>
    public class ChunkBatchStartPacket : IPacket
    {
        public const int Id = 0x0C;
        public int PacketId => Id;

        public int GetLength() => 0;

        public void Write(Span<byte> destination, out int bytesWritten)
            => bytesWritten = 0;
    }
}