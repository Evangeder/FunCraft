namespace FunCraft.Protocol.Packets.Play.Outgoing
{
    using IO;
    using Types;

    /// <summary>
    /// 0x0B — Chunk Batch Finished (S→C)
    /// <br/>Marks the end of a chunk batch. The client uses the elapsed time since
    /// <see cref="ChunkBatchStartPacket"/> and this batch size to estimate
    /// milliseconds-per-chunk, then reports its desired rate via
    /// <see cref="Incoming.ChunkBatchReceivedPacket"/>.
    /// </summary>
    public class ChunkBatchFinishedPacket : IPacket
    {
        public const int Id = 0x0B;
        public int PacketId => Id;

        public required int BatchSize { get; init; }

        public int GetLength() => VarInt.GetSize(BatchSize);

        public void Write(Span<byte> destination, out int bytesWritten)
        {
            var writer = new PacketWriter(destination);
            writer.WriteVarInt(BatchSize);
            bytesWritten = writer.BytesWritten;
        }
    }
}