using Xunit.Abstractions;
using System.Buffers;
using System.Text;

namespace FunCraft.Tests
{
    using Protocol.Registry;
    using Protocol.Types;

    public class CompoundHeaderTests(ITestOutputHelper output)
    {
        [Fact]
        public void AllNbtRegistries_EachEntry_StartsWithCompoundHeader()
        {
            RegistryLoader.Load();

            var failed = new List<string>();

            foreach (var packet in RegistryLoader.Packets)
            {
                output.WriteLine($"Analyzing: {packet.RegistryName}");
                foreach (var entry in packet.Entries)
                {
                    if (entry.NbtData == null)
                    {
                        // Entries with no NBT — if the registry requires NBT this will surface below
                        continue;
                    }

                    if (entry.NbtData[0] != 0x0A)
                        failed.Add($"{packet.RegistryName} / {entry.Name}: first byte=0x{entry.NbtData[0]:X2} expected 0x0A");
                }

                // Check that no entry is null in registries that should always have NBT
                var nbtRegistries = new HashSet<string>
                {
                    "minecraft:dimension_type", "minecraft:worldgen/biome",
                    "minecraft:damage_type", "minecraft:chat_type",
                    "minecraft:wolf_variant", "minecraft:painting_variant",
                    "minecraft:trim_material", "minecraft:trim_pattern",
                    "minecraft:banner_pattern", "minecraft:cat_variant",
                    "minecraft:chicken_variant", "minecraft:cow_variant",
                    "minecraft:frog_variant", "minecraft:pig_variant",
                };

                if (nbtRegistries.Contains(packet.RegistryName))
                {
                    foreach (var entry in packet.Entries.Where(e => e.NbtData == null))
                        failed.Add($"{packet.RegistryName} / {entry.Name}: NbtData is null but registry requires NBT");
                }
            }

            foreach (var f in failed)
                output.WriteLine($"FAIL: {f}");

            Assert.Empty(failed);
        }

        [Fact]
        public void AllNbtRegistries_DumpNbtCounts()
        {
            RegistryLoader.Load();

            foreach (var packet in RegistryLoader.Packets)
            {
                var withNbt = packet.Entries.Count(e => e.NbtData != null);
                var withoutNbt = packet.Entries.Count(e => e.NbtData == null);

                if (withNbt > 0 || withoutNbt > 0)
                    output.WriteLine($"{packet.RegistryName}: {withNbt} with NBT, {withoutNbt} without NBT");

                foreach (var entry in packet.Entries.Where(e => e.NbtData != null && e.NbtData[0] != 0x0A))
                    output.WriteLine($"  BAD HEADER: {entry.Name} first=0x{entry.NbtData[0]:X2}");
            }
        }

        [Fact]
        public void AllRegistryPackets_WireBytes_DecodeClean()
        {
            RegistryLoader.Load();

            foreach (var packet in RegistryLoader.Packets)
            {
                if (packet.Entries.All(e => e.NbtData == null)) continue;

                var declaredLen = packet.GetLength();
                var buf = new byte[declaredLen];
                packet.Write(buf.AsSpan(), out var written);

                Assert.Equal(declaredLen, written);

                var seq = new ReadOnlySequence<byte>(buf);
                var reader = new SequenceReader<byte>(seq);

                Assert.True(McString.TryRead(ref reader, out var regName));
                Assert.True(VarInt.TryRead(ref reader, out var count));

                for (var i = 0; i < count; i++)
                {
                    Assert.True(McString.TryRead(ref reader, out var entryName),
                        $"{packet.RegistryName}[{i}]: failed reading entry name");

                    var entry = packet.Entries[i];
                    if (entry.NbtData == null)
                    {
                        Assert.Equal(0x00, buf[^(int)reader.Remaining]);
                        reader.Advance(1);
                        continue;
                    }

                    var nbt = new byte[entry.NbtData.Length];
                    Assert.True(reader.TryCopyTo(nbt),
                        $"{packet.RegistryName} / {entryName}: failed copying {entry.NbtData.Length} NBT bytes, only {reader.Remaining} remaining");
                    reader.Advance(entry.NbtData.Length);

                    Assert.Equal(0x0A, nbt[0]);
                    Assert.Equal(0x00, nbt[^1]);
                }

                Assert.Equal(0, reader.Remaining);
            }
        }

        [Fact]
        public void DumpDimensionTypeNbtFields()
        {
            RegistryLoader.Load();

            var packet = RegistryLoader.Packets.First(p => p.RegistryName == "minecraft:dimension_type");
            var entry = packet.Entries.First();

            output.WriteLine($"Entry: {entry.Name} ({entry.NbtData!.Length} bytes)");
            output.WriteLine("");

            var data = entry.NbtData!;
            var pos = 3; // skip 0x0A 0x00 0x00 root compound header

            while (pos < data.Length)
            {
                var tagType = data[pos++];
                if (tagType == 0x00) break;

                var nameLen = (data[pos] << 8) | data[pos + 1];
                pos += 2;
                var name = Encoding.UTF8.GetString(data, pos, nameLen);
                pos += nameLen;

                var typeName = tagType switch
                {
                    1 => "TAG_Byte",
                    2 => "TAG_Short",
                    3 => "TAG_Int",
                    4 => "TAG_Long",
                    5 => "TAG_Float",
                    6 => "TAG_Double",
                    7 => "TAG_ByteArray",
                    8 => "TAG_String",
                    9 => "TAG_List",
                    10 => "TAG_Compound",
                    11 => "TAG_IntArray",
                    12 => "TAG_LongArray",
                    _ => $"UNKNOWN(0x{tagType:X2})"
                };

                output.WriteLine($"{name} = {typeName}");

                // We can't easily skip variable-length payloads without a full parser
                // so just break after listing — the first compound level is enough
                if (tagType is 10 or 9) break;
            }
        }
    }
}
