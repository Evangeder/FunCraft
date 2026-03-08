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

        public bool TryRead(ReadOnlySpan<byte> source, out int bytesRead)
        {
            try
            {
                var reader = new PacketReader(source);
                ProtocolVersion = reader.ReadVarInt();
                ServerAddress = reader.ReadString();
                ServerPort = reader.ReadUInt16();
                NextState = reader.ReadVarInt();

                bytesRead = reader.BytesRead;
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