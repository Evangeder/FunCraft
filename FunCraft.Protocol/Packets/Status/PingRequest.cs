namespace FunCraft.Protocol.Packets.Status
{
    using IO;

    public class PingRequest : IIncomingPacket
    {
        public const int Id = 0x01;

        public long Payload { get; private set; }

        public bool TryRead(ReadOnlySpan<byte> source, out int bytesRead)
        {
            try
            {
                var reader = new PacketReader(source);
                Payload = reader.ReadLong();
                bytesRead = sizeof(long);
                return true;
            }
            catch (InvalidDataException)
            {
                bytesRead = 0;
                return false;
            }
        }
    }
}
