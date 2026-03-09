namespace FunCraft.Protocol.Packets.Registry
{
    using IO;
    using Types;
    public class KnownPacksPacket : IPacket
    {
        public const int Id = 0x0E;
        public int PacketId => Id;

        public int GetLength() =>
            VarInt.GetSize(1) +
            McString.GetSize("minecraft:core") +
            McString.GetSize("") +
            McString.GetSize("1.21.10");

        public void Write(Span<byte> destination, out int bytesWritten)
        {
            var writer = new PacketWriter(destination);
            writer.WriteVarInt(1); // 1 pack
            writer.WriteString("minecraft:core");
            writer.WriteString(""); // id
            writer.WriteString("1.21.10");
            bytesWritten = writer.BytesWritten;
        }
    }
}
