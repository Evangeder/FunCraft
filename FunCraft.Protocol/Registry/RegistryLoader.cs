using System.Text;

namespace FunCraft.Protocol.Registry
{
    using Packets.Registry.Outgoing;
    using Properties;
    using Types;

    public static class RegistryLoader
    {
        public static IReadOnlyList<RegistryDataPacket> Packets { get; private set; } = [];
        public static IReadOnlyList<ReadOnlyMemory<byte>> FramedPackets { get; private set; } = [];

        public static void Load()
        {
            var data = (byte[])Resources.ResourceManager.GetObject("registries")!;
            var pos = 0;

            var packets = new List<RegistryDataPacket>();
            var registryCount = ReadVarInt(data, ref pos);

            for (var r = 0; r < registryCount; r++)
            {
                var registryId = ReadString(data, ref pos);
                var entryCount = ReadVarInt(data, ref pos);

                var entries = new List<RegistryEntry>(entryCount);
                for (var e = 0; e < entryCount; e++)
                {
                    var entryId = ReadString(data, ref pos);
                    var hasNbt = data[pos++] != 0;
                    byte[]? nbt = null;
                    if (hasNbt)
                    {
                        var nbtLen = ReadVarInt(data, ref pos);
                        nbt = new byte[nbtLen];
                        data.AsSpan(pos, nbtLen).CopyTo(nbt);
                        pos += nbtLen;
                    }

                    entries.Add(new RegistryEntry(entryId, nbt));
                }

                packets.Add(new RegistryDataPacket(registryId, entries));
            }

            Packets = packets;

            var framed = new List<ReadOnlyMemory<byte>>(packets.Count);
            foreach (var p in packets)
            {
                framed.Add(FramePacket(p));
            }

            FramedPackets = framed;

            Packets = [];
        }

        private static ReadOnlyMemory<byte> FramePacket(RegistryDataPacket p)
        {
            var payloadLen = p.GetLength();
            var idLen = VarInt.GetSize(p.PacketId);
            var totalLen = payloadLen + idLen;
            var frameLen = VarInt.GetSize(totalLen) + totalLen;
            var buf = new byte[frameLen];
            var writer = new IO.PacketWriter(buf);
            writer.WriteVarInt(totalLen);
            writer.WriteVarInt(p.PacketId);
            p.Write(buf.AsSpan(writer.BytesWritten), out _);

            return buf.AsMemory();
        }

        private static int ReadVarInt(byte[] data, ref int pos)
        {
            int value = 0, shift = 0;
            while (true)
            {
                var b = data[pos++];
                value |= (b & 0x7F) << shift;
                if ((b & 0x80) == 0) return value;
                shift += 7;
            }
        }

        private static string ReadString(byte[] data, ref int pos)
        {
            var len = ReadVarInt(data, ref pos);
            var str = Encoding.UTF8.GetString(data, pos, len);
            pos += len;
            return str;
        }
    }
}