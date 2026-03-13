using System.Buffers;

namespace FunCraft.Protocol.Packets.Login.Incoming
{
    using IO;

    public class LoginStartPacket : IIncomingPacket
    {
        public const int Id = 0x00;

        public ReadOnlyMemory<byte> PlayerName { get; private set; } = ReadOnlyMemory<byte>.Empty;
        public Guid PlayerGuid { get; private set; }

        public bool TryRead(ref SequenceReader<byte> reader)
        {
            try
            {
                var packetReader = new PacketReader(ref reader);
                PlayerName = packetReader.ReadStringRaw();
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