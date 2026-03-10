namespace FunCraft.Protocol.Packets.Play.Outgoing
{
    using IO;
    using Types;

    /// <summary>
    /// 0x5D — Set Chunk Cache Radius (S→C)
    /// </summary>
    public class SetChunkCacheRadiusPacket : IPacket
    {
        public const int Id = 0x5D;
        public int PacketId => Id;

        public required int ViewDistance { get; init; }

        public int GetLength() => VarInt.GetSize(ViewDistance);

        public void Write(Span<byte> destination, out int bytesWritten)
        {
            var writer = new PacketWriter(destination);
            writer.WriteVarInt(ViewDistance);
            bytesWritten = writer.BytesWritten;
        }
    }
}