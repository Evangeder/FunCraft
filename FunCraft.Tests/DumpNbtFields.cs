using System.Text;
using Xunit.Abstractions;

namespace FunCraft.Tests
{
    using Protocol.Registry;

    public class DumpNbtFields(ITestOutputHelper output)
    {
        [Fact]
        public void DumpAllNbtRegistryFields()
        {
            RegistryLoader.Load();

            var nbtRegistries = new[]
            {
                "minecraft:damage_type",
                "minecraft:chat_type",
                "minecraft:banner_pattern",
                "minecraft:wolf_variant",
                "minecraft:painting_variant",
                "minecraft:trim_material",
                "minecraft:trim_pattern",
                "minecraft:cat_variant",
            };

            foreach (var regName in nbtRegistries)
            {
                var packet = RegistryLoader.Packets.FirstOrDefault(p => p.RegistryName == regName);
                if (packet == null) { output.WriteLine($"MISSING: {regName}"); continue; }

                output.WriteLine($"=== {regName} ===");
                var first = packet.Entries.First();
                output.WriteLine($"  [{first.Name}]");
                var data = first.NbtData!;
                int pos = 3;
                DumpCompound(data, ref pos, 2);
                output.WriteLine("");
            }
        }

        [Fact]
        public void DumpRegistryDimensionTypeNbtFields()
        {
            RegistryLoader.Load();

            var packet = RegistryLoader.Packets.First(p => p.RegistryName == "minecraft:dimension_type");

            foreach (var entry in packet.Entries)
            {
                output.WriteLine($"=== {entry.Name} ({entry.NbtData!.Length} bytes) ===");
                var data = entry.NbtData!;
                var pos = 3; // skip 0x0A 0x00 0x00
                DumpCompound(data, ref pos, 0);
                output.WriteLine("");
            }
        }

        private void DumpCompound(byte[] data, ref int pos, int depth)
        {
            var indent = new string(' ', depth * 2);
            while (pos < data.Length)
            {
                var tagType = data[pos++];
                if (tagType == 0x00)
                {
                    return;
                }

                var nameLen = (data[pos] << 8) | data[pos + 1];
                pos += 2;
                var name = Encoding.UTF8.GetString(data, pos, nameLen);
                pos += nameLen;

                switch (tagType)
                {
                    case 1: output.WriteLine($"{indent}{name} = TAG_Byte({(sbyte)data[pos++]})"); break;
                    case 2: output.WriteLine($"{indent}{name} = TAG_Short({ReadI16(data, ref pos)})"); break;
                    case 3: output.WriteLine($"{indent}{name} = TAG_Int({ReadI32(data, ref pos)})"); break;
                    case 4: output.WriteLine($"{indent}{name} = TAG_Long({ReadI64(data, ref pos)})"); break;
                    case 5: output.WriteLine($"{indent}{name} = TAG_Float({ReadF32(data, ref pos)})"); break;
                    case 6: output.WriteLine($"{indent}{name} = TAG_Double({ReadF64(data, ref pos)})"); break;
                    case 8:
                        var slen = (data[pos] << 8) | data[pos + 1]; pos += 2;
                        var s = Encoding.UTF8.GetString(data, pos, slen); pos += slen;
                        output.WriteLine($"{indent}{name} = TAG_String(\"{s}\")");
                        break;
                    case 9:
                        var elType = data[pos++];
                        var count = ReadI32(data, ref pos);
                        output.WriteLine($"{indent}{name} = TAG_List<{elType}>({count} items)");
                        SkipList(data, ref pos, elType, count);
                        break;
                    case 10:
                        output.WriteLine($"{indent}{name} = TAG_Compound {{");
                        DumpCompound(data, ref pos, depth + 1);
                        output.WriteLine($"{indent}}}");
                        break;
                    default:
                        output.WriteLine($"{indent}{name} = UNKNOWN(0x{tagType:X2}) — cannot continue");
                        return;
                }
            }
        }

        private void SkipList(byte[] data, ref int pos, byte elType, int count)
        {
            for (var i = 0; i < count; i++)
            {
                SkipPayload(data, ref pos, elType);
            }
        }

        private void SkipPayload(byte[] data, ref int pos, byte tagType)
        {
            switch (tagType)
            {
                case 1:
                    pos += 1;
                    break;

                case 2:
                    pos += 2;
                    break;

                case 3:
                    pos += 4;
                    break;

                case 4:
                    pos += 8;
                    break;

                case 5:
                    pos += 4;
                    break;

                case 6:
                    pos += 8;
                    break;

                case 8:
                    var slen = (data[pos] << 8) | data[pos + 1];
                    pos += 2 + slen;
                    break;

                case 9:
                    var el = data[pos++];
                    var c = ReadI32(data, ref pos);
                    SkipList(data, ref pos, el, c);
                    break;

                case 10:
                    while (true)
                    {
                        var t = data[pos++];
                        if (t == 0)
                        {
                            break;
                        }

                        var nl = (data[pos] << 8) | data[pos + 1];
                        pos += 2 + nl;
                        SkipPayload(data, ref pos, t);
                    }
                    break;
            }
        }

        private static short ReadI16(byte[] d, ref int p)
        {

            var v = (short)((d[p] << 8) | d[p + 1]);
            p += 2;
            return v;
        }

        private static int ReadI32(byte[] d, ref int p)
        {
            var v = (d[p] << 24)
                    | (d[p + 1] << 16)
                    | (d[p + 2] << 8)
                    | d[p + 3];
            p += 4;
            return v;
        }

        private static long ReadI64(byte[] d, ref int p)
        {
            var v = ((long)d[p] << 56)
                    | ((long)d[p + 1] << 48)
                    | ((long)d[p + 2] << 40)
                    | ((long)d[p + 3] << 32)
                    | ((long)d[p + 4] << 24)
                    | ((long)d[p + 5] << 16)
                    | ((long)d[p + 6] << 8)
                    | d[p + 7];
            p += 8;
            return v;
        }

        private static float ReadF32(byte[] d, ref int p)
        {
            var b = new byte[]
            {
                d[p + 3],
                d[p + 2],
                d[p + 1],
                d[p]
            };
            p += 4;
            return BitConverter.ToSingle(b);
        }

        private static double ReadF64(byte[] d, ref int p)
        {
            var b = new byte[]
            {
                d[p + 7],
                d[p + 6],
                d[p + 5],
                d[p + 4],
                d[p + 3],
                d[p + 2],
                d[p + 1],
                d[p]
            };
            p += 8;
            return BitConverter.ToDouble(b);
        }

    }
}
