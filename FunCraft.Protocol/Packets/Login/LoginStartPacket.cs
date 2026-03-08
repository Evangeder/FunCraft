using System.Buffers;

namespace FunCraft.Protocol.Packets.Login
{
    using IO;

    public class LoginStartPacket : IIncomingPacket
    {
        public const int Id = 0x00;

        public string PlayerName { get; private set; } = string.Empty;
        public Guid PlayerGuid { get; private set; }

        public bool TryRead(ref SequenceReader<byte> reader)
        {
            try
            {
                var packetReader = new PacketReader(ref reader);
                PlayerName = packetReader.ReadString();
                PlayerGuid = packetReader.ReadGuid();
                return true;
            }
            catch (InvalidDataException)
            {
                return false;
            }
        }
    }
}
