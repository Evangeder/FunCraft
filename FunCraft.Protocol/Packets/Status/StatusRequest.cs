namespace FunCraft.Protocol.Packets.Status
{
    public class StatusRequest : IIncomingPacket
    {
        public const int Id = 0x00;

        public bool TryRead(ReadOnlySpan<byte> source, out int bytesRead)
        {
            bytesRead = 0;
            return true;
        }
    }
}
