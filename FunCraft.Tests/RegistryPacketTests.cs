using System.Buffers;
using Xunit.Abstractions;

namespace FunCraft.Tests
{
    using Protocol.Packets.Registry;
    using Protocol.Registry;
    using Protocol.Types;

    public class RegistryPacketTests(ITestOutputHelper output)
    {
        // Replicates exactly what ClientConnection.SendAsync frames on the wire:
        // [VarInt: totalLength][VarInt: packetId][payload bytes]
        private static byte[] FramePacket(RegistryDataPacket packet)
        {
            var payloadLen = VarInt.GetSize(packet.PacketId) + packet.GetLength();
            var frameLen = VarInt.GetSize(payloadLen) + payloadLen;

            var buf = new byte[frameLen];
            var pos = 0;

            pos += VarInt.Write(buf.AsSpan(pos), payloadLen);
            pos += VarInt.Write(buf.AsSpan(pos), packet.PacketId);
            packet.Write(buf.AsSpan(pos), out var written);
            pos += written;

            Assert.Equal(frameLen, pos);
            return buf;
        }

        [Fact]
        public void BiomeRegistry_FramedBytes_DecodesCorrectly()
        {
            RegistryLoader.Load();
            // Find the biome registry packet
            var biomePacket = RegistryLoader.Packets
                .FirstOrDefault(p => p.RegistryName == "minecraft:dimension_type");

            Assert.NotNull(biomePacket);
            output.WriteLine($"Registry: {biomePacket.RegistryName}");
            output.WriteLine($"Entries:  {biomePacket.Entries.Count}");

            // Frame it exactly as SendAsync would
            var framed = FramePacket(biomePacket);
            output.WriteLine($"Total framed bytes: {framed.Length}");

            // Decode
            var seq = new ReadOnlySequence<byte>(framed);
            var reader = new SequenceReader<byte>(seq);

            // Read outer length prefix
            Assert.True(VarInt.TryRead(ref reader, out var outerLen));
            output.WriteLine($"Outer length field: {outerLen}");
            Assert.Equal(framed.Length - VarInt.GetSize(outerLen), outerLen);

            // Read packet ID
            Assert.True(VarInt.TryRead(ref reader, out var packetId));
            Assert.Equal(RegistryDataPacket.Id, packetId);
            output.WriteLine($"Packet ID: 0x{packetId:X2}");

            // Read registry name
            Assert.True(McString.TryRead(ref reader, out var registryName));
            Assert.Equal("minecraft:dimension_type", registryName);
            output.WriteLine($"Registry name: {registryName}");

            // Read entry count
            Assert.True(VarInt.TryRead(ref reader, out var entryCount));
            output.WriteLine($"Entry count: {entryCount}");
            Assert.True(entryCount > 0);

            var entriesWithNbt = 0;
            var entriesWithoutNbt = 0;

            for (int i = 0; i < entryCount; i++)
            {
                Assert.True(McString.TryRead(ref reader, out var entryName),
                    $"Failed to read entry name at index {i}");

                var entry = biomePacket.Entries[i];

                if (entry.NbtData == null)
                {
                    // should be a single 0x00
                    Assert.True(reader.TryRead(out var b));
                    Assert.Equal(0x00, b);
                    entriesWithoutNbt++;
                    continue;
                }

                entriesWithNbt++;
                var nbtSpan = new byte[entry.NbtData.Length];
                Assert.True(reader.TryCopyTo(nbtSpan),
                    $"Failed to read {entry.NbtData.Length} NBT bytes for {entryName}, only {reader.Remaining} remaining");
                reader.Advance(entry.NbtData.Length);

                Assert.True(nbtSpan[0] == 0x0A, $"{entryName}: expected 0x0A header, got 0x{nbtSpan[0]:X2}");
                Assert.True(nbtSpan[^1] == 0x00, $"{entryName}: expected TAG_End, got 0x{nbtSpan[^1]:X2}");

                output.WriteLine($"  [{i}] {entryName}: {entry.NbtData.Length} bytes");
            }

            output.WriteLine($"Entries with NBT:    {entriesWithNbt}");
            output.WriteLine($"Entries without NBT: {entriesWithoutNbt}");

            output.WriteLine($"Data types for the NBT:");


            // All remaining bytes should be consumed
            Assert.True(reader.Remaining == 0, $"{reader.Remaining} bytes left over after decoding all entries");
        }

        [Fact]
        public void AllRegistryPackets_GetLength_MatchesActualWrite()
        {
            RegistryLoader.Load();

            var totalMismatches = 0;

            foreach (var packet in RegistryLoader.Packets)
            {
                var declared = packet.GetLength();
                var buf = new byte[declared + 16]; // slight overalloc to catch overruns
                packet.Write(buf.AsSpan(), out var actual);

                if (declared == actual)
                {
                    continue;
                }

                output.WriteLine($"MISMATCH: {packet.RegistryName} declared={declared} actual={actual}");
                totalMismatches++;
            }

            Assert.Equal(0, totalMismatches);
        }

        [Fact]
        public void BiomeRegistry_EachEntry_HasValidNbt()
        {
            RegistryLoader.Load();

            var biomePacket = RegistryLoader.Packets
                .First(p => p.RegistryName == "minecraft:dimension_type");

            var failed = new List<string>();

            foreach (var entry in biomePacket.Entries)
            {
                if (entry.NbtData == null) continue;

                // Ends with TAG_End
                if (entry.NbtData[^1] != 0x00)
                    failed.Add($"{entry.Name}: missing TAG_End");

                // First byte is valid tag type
                var firstTag = entry.NbtData[0];
                if (firstTag is < 1 or > 12)
                    failed.Add($"{entry.Name}: bad first tag={firstTag}");
            }

            if (failed.Count > 0)
            {
                foreach (var f in failed)
                    output.WriteLine($"FAIL: {f}");
            }

            Assert.Empty(failed);
        }
    }
}