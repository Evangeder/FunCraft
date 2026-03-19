namespace FunCraft.Protocol.Packets.Play.Outgoing
{
    /// <summary>
    /// 0x0C — Chunk Batch Start (S→C)
    /// </summary>
    public class ChunkBatchStartPacket : IPacket
    {
        public const int Id = 0x0C;
        public int PacketId => Id;

        public static readonly ReadOnlyMemory<byte> PreFramed = new byte[] { 0x01, 0x0C };

        public int GetLength() => 0;

        public void Write(Span<byte> destination, out int bytesWritten)
            => bytesWritten = 0;
    }
}