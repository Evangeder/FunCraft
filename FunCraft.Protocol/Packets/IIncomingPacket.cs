namespace FunCraft.Protocol.Packets
{
    public interface IIncomingPacket
    {
        bool TryRead(ReadOnlySpan<byte> source, out int bytesRead);
    }
}
