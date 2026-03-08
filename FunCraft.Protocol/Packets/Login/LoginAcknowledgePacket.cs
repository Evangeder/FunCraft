namespace FunCraft.Protocol.Packets.Login
{
    public class LoginAcknowledgePacket : IIncomingPacket
    {
        public const int Id = 0x03;

        public bool TryRead(ReadOnlySpan<byte> source, out int bytesRead)
        {
            throw new NotImplementedException();
        }
    }
}
