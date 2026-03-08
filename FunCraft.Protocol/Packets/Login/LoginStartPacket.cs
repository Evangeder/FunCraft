namespace FunCraft.Protocol.Packets.Login
{
    using IO;

    public class LoginStartPacket : IIncomingPacket
    {
        public const int Id = 0x00;

        public string PlayerName { get; private set; } = string.Empty;
        public Guid PlayerGuid { get; private set; }

        public bool TryRead(ReadOnlySpan<byte> source, out int bytesRead)
        {
            try
            {
                var reader = new PacketReader(source);
                PlayerName = reader.ReadString();
                PlayerGuid = reader.ReadGuid();
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
