namespace FunCraft.Protocol.Packets.Play.Outgoing
{
    using IO;

    /// <summary>
    /// 0x2B — Keep Alive (S→C)
    /// <br/>Sent every ~10 seconds. The client must respond with
    /// <see cref="Incoming.ServerboundKeepAlivePacket"/> containing the same ID
    /// within 15 seconds, or the connection is dropped.
    /// </summary>
    public class ClientboundKeepAlivePacket : IPacket
    {
        public const int Id = 0x2B;
        public int PacketId => Id;

        public required long KeepAliveId { get; init; }

        public int GetLength() => sizeof(long);

        public void Write(Span<byte> destination, out int bytesWritten)
        {
            var writer = new PacketWriter(destination);
            writer.WriteLong(KeepAliveId);
            bytesWritten = writer.BytesWritten;
        }
    }
}