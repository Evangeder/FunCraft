using System.Buffers;

namespace FunCraft.Protocol.Packets.Handshaking
{
    using IO;

    public class HandshakePacket : IIncomingPacket
    {
        public const int PacketId = 0x00;

        public int ProtocolVersion { get; private set; }
        public string ServerAddress { get; private set; } = string.Empty;
        public ushort ServerPort { get; private set; }
        public int NextState { get; private set; }

        public bool TryRead(ref SequenceReader<byte> reader)
        {
            try
            {
                var packetReader = new PacketReader(ref reader);
                ProtocolVersion = packetReader.ReadVarInt();
                ServerAddress = packetReader.ReadString();
                ServerPort = packetReader.ReadUInt16();
                NextState = packetReader.ReadVarInt();
                return true;
            }
            catch (InvalidDataException)
            {
                return false;
            }
        }
    }
}