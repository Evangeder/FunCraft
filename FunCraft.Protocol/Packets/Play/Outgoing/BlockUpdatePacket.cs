namespace FunCraft.Protocol.Packets.Play.Outgoing
{
    using IO;
    using Types;

    /// <summary>
    /// 0x08 — Block Update (S→C)
    /// <br/>Sets a single block in the world on the client.
    /// </summary>
    public sealed class BlockUpdatePacket : IPacket
    {
        public const int Id = 0x08;
        public int PacketId => Id;

        public required BlockPosition Location { get; init; }

        /// <summary>
        /// Global block state ID.
        /// </summary>
        public required int BlockState { get; init; }

        public int GetLength() => sizeof(long) + VarInt.GetSize(BlockState);

        public void Write(Span<byte> destination, out int bytesWritten)
        {
            var writer = new PacketWriter(destination);
            writer.WriteLong(Location.Encode());
            writer.WriteVarInt(BlockState);
            bytesWritten = writer.BytesWritten;
        }
    }
}