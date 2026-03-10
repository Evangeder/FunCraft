namespace FunCraft.Protocol.Packets.Play.Outgoing
{
    using IO;

    /// <summary>
    /// <b>0x2C</b> — Chunk Data and Update Light (S→C, protocol 773)
    /// </summary>
    public class ChunkDataPacket : IPacket
    {
        public const int Id = 0x2C;
        public int PacketId => Id;

        public required int ChunkX { get; init; }
        public required int ChunkZ { get; init; }
        public required byte[] ChunkData { get; init; }

        public int GetLength() => sizeof(int) + sizeof(int) + ChunkData.Length;

        public void Write(Span<byte> destination, out int bytesWritten)
        {
            var writer = new PacketWriter(destination);
            writer.WriteInt(ChunkX);
            writer.WriteInt(ChunkZ);
            writer.WriteRawBytes(ChunkData);
            bytesWritten = writer.BytesWritten;
        }
    }
}