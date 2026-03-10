using System.Text;
using System.Text.Json;

namespace FunCraft.RegistryBuilder
{
    internal static class NbtBinaryWriter
    {
        /// <summary>
        /// Writes a JsonElement compound as network NBT.
        /// Network NBT (1.20.2+): no root type byte, no root name — fields + TAG_End.
        /// </summary>
        public static byte[] WriteNetworkNbt(JsonElement root, CompoundSchemaNode schema, SchemaAnalyzer analyzer)
        {
            using var ms = new MemoryStream();
            ms.WriteByte(0x0A);
            WriteCompoundBody(ms, root, schema, analyzer);
            return ms.ToArray();
        }

        private static void WriteCompoundBody(Stream s, JsonElement element, CompoundSchemaNode schema,
            SchemaAnalyzer analyzer)
        {
            if (element.ValueKind != JsonValueKind.Object)
            {
                s.WriteByte((byte) NbtTagType.End);
                return;
            }

            foreach (var prop in element.EnumerateObject())
            {
                if (prop.Value.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
                    continue;

                if (!schema.Fields.TryGetValue(prop.Name, out var fieldSchema))
                    continue;

                if (fieldSchema is DynamicSchemaNode)
                    fieldSchema = analyzer.InferNode(prop.Value, prop.Name);

                var tagType = TagTypeOf(fieldSchema);
                if (tagType == NbtTagType.End)
                    continue;

                s.WriteByte((byte) tagType);
                WriteNbtString(s, prop.Name);
                WriteValue(s, prop.Value, fieldSchema, analyzer);
            }

            s.WriteByte((byte) NbtTagType.End);
        }

        private static NbtTagType TagTypeOf(SchemaNode node) => node switch
        {
            ScalarSchemaNode scalar => scalar.Type,
            CompoundSchemaNode => NbtTagType.Compound,
            ListSchemaNode => NbtTagType.List,
            _ => NbtTagType.End
        };

        private static void WriteValue(Stream s, JsonElement el, SchemaNode schema, SchemaAnalyzer analyzer)
        {
            if (schema is DynamicSchemaNode)
                schema = analyzer.InferNode(el, string.Empty);

            if (schema is CompoundSchemaNode && el.ValueKind != JsonValueKind.Object) return;
            if (schema is ListSchemaNode && el.ValueKind != JsonValueKind.Array) return;

            switch (schema)
            {
                case ScalarSchemaNode {Type: NbtTagType.Byte}:
                    s.WriteByte(el.ValueKind == JsonValueKind.True ? (byte) 1 :
                        el.ValueKind == JsonValueKind.False ? (byte) 0 :
                        (byte) (sbyte) el.GetSByte());
                    break;

                case ScalarSchemaNode {Type: NbtTagType.Short}:
                    WriteI16(s, el.GetInt16());
                    break;

                case ScalarSchemaNode {Type: NbtTagType.Int}:
                    WriteI32(s, el.GetInt32());
                    break;

                case ScalarSchemaNode {Type: NbtTagType.Long}:
                    WriteI64(s, el.GetInt64());
                    break;

                case ScalarSchemaNode {Type: NbtTagType.Float}:
                    WriteF32(s, el.GetSingle());
                    break;

                case ScalarSchemaNode {Type: NbtTagType.Double}:
                    WriteF64(s, el.GetDouble());
                    break;

                case ScalarSchemaNode {Type: NbtTagType.String}:
                    var strVal = el.ValueKind == JsonValueKind.String
                        ? el.GetString()!
                        : el.GetRawText();
                    WriteNbtString(s, strVal);
                    break;

                case CompoundSchemaNode compound:
                    WriteCompoundBody(s, el, compound, analyzer);
                    break;

                case ListSchemaNode list:
                    WriteList(s, el, list, analyzer);
                    break;
            }
        }

        private static void WriteList(Stream s, JsonElement el, ListSchemaNode schema, SchemaAnalyzer analyzer)
        {
            var items = el.EnumerateArray().ToArray();

            if (items.Length == 0)
            {
                s.WriteByte((byte) NbtTagType.End);
                WriteI32(s, 0);
                return;
            }

            var elementSchema = schema.ElementNode is DynamicSchemaNode
                ? analyzer.InferNode(items[0], string.Empty)
                : schema.ElementNode;

            s.WriteByte((byte) TagTypeOf(elementSchema));
            WriteI32(s, items.Length);

            foreach (var item in items)
            {
                var itemSchema = elementSchema is DynamicSchemaNode
                    ? analyzer.InferNode(item, string.Empty)
                    : elementSchema;
                WriteValue(s, item, itemSchema, analyzer);
            }
        }

        private static void WriteNbtString(Stream s, string value)
        {
            var bytes = Encoding.UTF8.GetBytes(value);
            s.WriteByte((byte) (bytes.Length >> 8));
            s.WriteByte((byte) bytes.Length);
            s.Write(bytes);
        }

        private static void WriteI16(Stream s, short v)
        {
            s.WriteByte((byte) (v >> 8));
            s.WriteByte((byte) v);
        }

        private static void WriteI32(Stream s, int v)
        {
            s.WriteByte((byte) (v >> 24));
            s.WriteByte((byte) (v >> 16));
            s.WriteByte((byte) (v >> 8));
            s.WriteByte((byte) v);
        }

        private static void WriteI64(Stream s, long v)
        {
            s.WriteByte((byte) (v >> 56));
            s.WriteByte((byte) (v >> 48));
            s.WriteByte((byte) (v >> 40));
            s.WriteByte((byte) (v >> 32));
            s.WriteByte((byte) (v >> 24));
            s.WriteByte((byte) (v >> 16));
            s.WriteByte((byte) (v >> 8));
            s.WriteByte((byte) v);
        }

        private static void WriteF32(Stream s, float v)
        {
            var b = BitConverter.GetBytes(v);
            if (BitConverter.IsLittleEndian) Array.Reverse(b);
            s.Write(b);
        }

        private static void WriteF64(Stream s, double v)
        {
            var b = BitConverter.GetBytes(v);
            if (BitConverter.IsLittleEndian) Array.Reverse(b);
            s.Write(b);
        }
    }
}