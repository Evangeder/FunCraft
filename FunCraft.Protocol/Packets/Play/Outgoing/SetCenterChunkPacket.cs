namespace FunCraft.Protocol.Packets.Play.Outgoing
{
    using IO;
    using Types;

    /// <summary>
    /// 0x5C — Set Center Chunk (S→C)
    /// </summary>
    public class SetCenterChunkPacket : IPacket
    {
        public const int Id = 0x5C;
        public int PacketId => Id;

        public required int ChunkX { get; init; }
        public required int ChunkZ { get; init; }

        public int GetLength() =>
            VarInt.GetSize(ChunkX) + VarInt.GetSize(ChunkZ);

        public void Write(Span<byte> destination, out int bytesWritten)
        {
            var writer = new PacketWriter(destination);
            writer.WriteVarInt(ChunkX);
            writer.WriteVarInt(ChunkZ);
            bytesWritten = writer.BytesWritten;
        }
    }
}