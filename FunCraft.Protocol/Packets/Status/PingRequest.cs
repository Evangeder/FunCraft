namespace FunCraft.Protocol.Packets.Status
{
    using IO;
    using System.Buffers;

    public class PingRequest : IIncomingPacket
    {
        public const int Id = 0x01;

        public long Payload { get; private set; }

        public bool TryRead(ref SequenceReader<byte> reader)
        {
            try
            {
                var packetReader = new PacketReader(ref reader);
                Payload = packetReader.ReadLong();
                return true;
            }
            catch (InvalidDataException)
            {
                return false;
            }
        }
    }
}
