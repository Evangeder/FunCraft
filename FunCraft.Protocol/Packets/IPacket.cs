namespace FunCraft.Protocol.Packets
{
    public interface IPacket
    {
        int PacketId { get; }
        int GetLength();
        void Write(Span<byte> destination, out int bytesWritten);
    }
}
