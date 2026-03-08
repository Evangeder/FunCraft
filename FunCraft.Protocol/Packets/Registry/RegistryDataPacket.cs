namespace FunCraft.Protocol.Packets.Registry
{
    using IO;
    using Packets;
    using Types;

    /// <summary>
    /// <br/>Packet 0x07 — Registry Data (Clientbound, Configuration state)
    /// <br/>Sends one registry worth of data to the client.
    /// <br/>
    /// <br/>Wire format:
    /// <br/>  [McString] Registry name (e.g. "minecraft:dimension_type")
    /// <br/>  [VarInt]   Entry count
    /// <br/>  Per entry:
    /// <br/>    [McString] Entry name
    /// <br/>    [Boolean]  Has NBT
    /// <br/>    [NBT]      Entry data (only if Has NBT = true)
    /// </summary>
    public class RegistryDataPacket : IPacket
    {
        public const int Id = 0x07;
        public int PacketId => Id;

        private readonly string _registryName;
        private readonly IReadOnlyList<RegistryEntry> _entries;

        public RegistryDataPacket(string registryName, IReadOnlyList<RegistryEntry> entries)
        {
            _registryName = registryName;
            _entries = entries;
        }

        public int GetLength()
        {
            var size = McString.GetSize(_registryName);
            size += VarInt.GetSize(_entries.Count);

            foreach (var entry in _entries)
            {
                size += McString.GetSize(entry.Name);
                size += 1; // has NBT boolean
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
            writer.WriteString(_registryName);
            writer.WriteVarInt(_entries.Count);

            foreach (var entry in _entries)
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