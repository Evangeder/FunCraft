namespace FunCraft.Protocol.Packets.Play
{
    using IO;

    /// <summary>
    /// 0x26 — Game Event (S→C)
    /// </summary>
    public class GameEventPacket : IPacket
    {
        public const int Id = 0x26;
        public int PacketId => Id;

        public required byte Event { get; init; }

        public required float Value { get; init; }

        public int GetLength() => sizeof(byte) + sizeof(float);

        public void Write(Span<byte> destination, out int bytesWritten)
        {
            var writer = new PacketWriter(destination);
            writer.WriteByte(Event);
            writer.WriteFloat(Value);
            bytesWritten = writer.BytesWritten;
        }
    }
}