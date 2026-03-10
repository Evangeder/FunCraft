namespace FunCraft.Protocol.Packets.Registry.Outgoing
{
    using Packets;
    using IO;
    using Types;

    public class RegistryDataPacket(string registryName, IReadOnlyList<RegistryEntry> entries) : IPacket
    {
        public const int Id = 0x07;
        public int PacketId => Id;

        public readonly string RegistryName = registryName;
        public readonly IReadOnlyList<RegistryEntry> Entries = entries;

        public int GetLength()
        {
            var size = McString.GetSize(RegistryName);
            size += VarInt.GetSize(Entries.Count);
            foreach (var entry in Entries)
            {
                size += McString.GetSize(entry.Name);
                size += 1;
                if (entry.NbtData != null)
                {
                    size += entry.NbtData.Length;
                }
            }
            return size;
        }

        public void Write(Span<byte> destination, out int bytesWritten)
        {
            var writer = new PacketWriter(destination);
            writer.WriteString(RegistryName);
            writer.WriteVarInt(Entries.Count);
            foreach (var entry in Entries)
            {
                writer.WriteString(entry.Name);
                writer.WriteBoolean(entry.NbtData != null);
                if (entry.NbtData != null)
                {
                    writer.WriteRawBytes(entry.NbtData);
                }
            }
            bytesWritten = writer.BytesWritten;
        }
    }

    public record RegistryEntry(string Name, byte[]? NbtData);
}