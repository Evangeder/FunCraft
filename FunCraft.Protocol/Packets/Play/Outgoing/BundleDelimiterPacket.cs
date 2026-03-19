namespace FunCraft.Protocol.Packets.Play.Outgoing
{
    /// <summary>
    /// 0x00 — Bundle Delimiter (S→C)
    /// </summary>
    public sealed class BundleDelimiterPacket : IPacket
    {
        public const int Id = 0x00;
        public int PacketId => Id;
        public int GetLength() => 0;
        public void Write(Span<byte> destination, out int bytesWritten)
            => bytesWritten = 0;
    }
}