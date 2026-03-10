using System.Text.Json;

namespace FunCraft.RegistryBuilder
{
    internal enum NbtTagType : byte
    {
        End = 0,
        Byte = 1,
        Short = 2,
        Int = 3,
        Long = 4,
        Float = 5,
        Double = 6,
        ByteArray = 7,
        String = 8,
        List = 9,
        Compound = 10,
        IntArray = 11,
        LongArray = 12
    }

    internal abstract class SchemaNode { }
    internal sealed class DynamicSchemaNode : SchemaNode { }

    internal sealed class ScalarSchemaNode(NbtTagType type) : SchemaNode
    {
        public NbtTagType Type = type;
    }

    internal sealed class CompoundSchemaNode : SchemaNode
    {
        public Dictionary<string, SchemaNode> Fields = [];
    }

    internal sealed class ListSchemaNode : SchemaNode
    {
        public SchemaNode ElementNode = new ScalarSchemaNode(NbtTagType.End);
    }

    internal class SchemaAnalyzer
    {
        private static readonly HashSet<string> ForceDouble = 
            [
                "coordinate_scale"
            ];

        private static readonly HashSet<string> ForceInt =
            [
                "fog_color", "sky_color", "water_color", "water_fog_color",
                "foliage_color", "grass_color", "tick_delay", "block_search_extent",
                "weight", "minCount", "maxCount", "min_inclusive", "max_inclusive",
                "cloud_height", "width", "height", "min_y", "logical_height",
                "monster_spawn_block_light_limit", "monster_spawn_light_level"
            ];

        private static readonly HashSet<string> ForceString =
            [
                "title", "author",
            ];

        public CompoundSchemaNode Analyze(string directory)
        {
            var root = new CompoundSchemaNode();
            foreach (var file in Directory.EnumerateFiles(directory, "*.json", SearchOption.TopDirectoryOnly))
            {
                using var doc = JsonDocument.Parse(File.ReadAllBytes(file));
                MergeIntoCompound(root, doc.RootElement);
            }

            return root;
        }

        private void MergeIntoCompound(CompoundSchemaNode node, JsonElement element)
        {
            foreach (var prop in element.EnumerateObject())
            {
                var child = InferNode(prop.Value, prop.Name);
                if (!node.Fields.TryGetValue(prop.Name, out var existing))
                    node.Fields[prop.Name] = child;
                else
                    node.Fields[prop.Name] = Merge(existing, child, prop.Name);
            }
        }

        internal SchemaNode InferNode(JsonElement el, string fieldName) => el.ValueKind switch
        {
            JsonValueKind.True or JsonValueKind.False
                => new ScalarSchemaNode(NbtTagType.Byte),

            JsonValueKind.String
                => new ScalarSchemaNode(NbtTagType.String),

            JsonValueKind.Number when ForceDouble.Contains(fieldName)
                => new ScalarSchemaNode(NbtTagType.Double),

            JsonValueKind.Number when el.TryGetInt64(out _)
                => InferNumber(el, fieldName),

            JsonValueKind.Number
                => InferNumber(el, fieldName),

            JsonValueKind.Object when ForceString.Contains(fieldName)
                => new ScalarSchemaNode(NbtTagType.String),

            JsonValueKind.Object => InferCompound(el),

            JsonValueKind.Array => InferList(el, fieldName),

            _ => new ScalarSchemaNode(NbtTagType.End)
        };

        private static ScalarSchemaNode InferNumber(JsonElement el, string fieldName)
        {
            var raw = el.GetRawText();
            var isDecimal = raw.Contains('.') || raw.Contains('e') || raw.Contains('E');

            if (isDecimal)
                return new ScalarSchemaNode(NbtTagType.Float);

            var iv = el.GetInt64();
            return new ScalarSchemaNode(ForceInt.Contains(fieldName) || iv < -128 || iv > 127
                ? NbtTagType.Int
                : NbtTagType.Byte);
        }

        internal CompoundSchemaNode InferCompound(JsonElement el)
        {
            var node = new CompoundSchemaNode();
            MergeIntoCompound(node, el);
            return node;
        }

        internal ListSchemaNode InferList(JsonElement el, string fieldName)
        {
            var node = new ListSchemaNode();
            foreach (var item in el.EnumerateArray())
                node.ElementNode = Merge(node.ElementNode, InferNode(item, fieldName), fieldName);
            return node;
        }

        private static SchemaNode Merge(SchemaNode a, SchemaNode b, string fieldName)
        {
            if (a is ScalarSchemaNode {Type: NbtTagType.End}) return b;
            if (b is ScalarSchemaNode {Type: NbtTagType.End}) return a;

            if (a is DynamicSchemaNode || b is DynamicSchemaNode)
                return new DynamicSchemaNode();

            if (a is ScalarSchemaNode sa && b is ScalarSchemaNode sb)
            {
                if (sa.Type == sb.Type) return a;

                if (sa.Type is NbtTagType.Byte or NbtTagType.Int &&
                    sb.Type is NbtTagType.Byte or NbtTagType.Int)
                    return new ScalarSchemaNode(NbtTagType.Int);

                if (sa.Type is NbtTagType.Float or NbtTagType.Double &&
                    sb.Type is NbtTagType.Float or NbtTagType.Double)
                    return new ScalarSchemaNode(NbtTagType.Double);

                Console.WriteLine($"  [dynamic] scalar conflict on '{fieldName}': {sa.Type} vs {sb.Type}");
                return new DynamicSchemaNode();
            }

            if (a is CompoundSchemaNode ca && b is CompoundSchemaNode cb)
            {
                foreach (var (key, val) in cb.Fields)
                {
                    if (!ca.Fields.TryGetValue(key, out var existing))
                        ca.Fields[key] = val;
                    else
                        ca.Fields[key] = Merge(existing, val, key);
                }

                return ca;
            }

            if (a is ListSchemaNode la && b is ListSchemaNode lb)
            {
                la.ElementNode = Merge(la.ElementNode, lb.ElementNode, fieldName);
                return la;
            }

            Console.WriteLine(
                $"  [dynamic] structural conflict on '{fieldName}': {a.GetType().Name} vs {b.GetType().Name}");
            return new DynamicSchemaNode();
        }
    }
}