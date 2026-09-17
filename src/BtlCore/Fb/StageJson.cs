/*
 * StageJson.cs
 *
 * 关卡 JSON（给人看、对照、再打开），不是内存里的 BtlFront 文档格式。
 *
 * 导出两种：
 *   字段名 — 键用 battle.fbs 的字段名（map_terrain、tiles…）
 *   字段 ID — 键用 vtable 槽号（"0"、"1"…）
 * 都是普通 JSON：标量直接写数，表/结构是对象，向量是数组。不写 t/f/elem。
 *
 * 导入：上述两种、旧版别名（playable_flag、country_ai_bt…）、以及 Dump 的 schema JSON。
 * 打开后仍变成 BtlFront，编辑器内存格式不变。
 */
using System.Globalization;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using BtlCore.Front;

namespace BtlCore.Fb
{
    public static class StageJson
    {
        static readonly JsonSerializerOptions JsonOpts = new JsonSerializerOptions(BtlFrontJson.JsonOptions)
        {
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
        };

        public static string ToNamed(BtlFrontDocument doc, FbsSchema schema) =>
            WriteDocument(doc, schema, named: true);

        public static string ToFieldIds(BtlFrontDocument doc, FbsSchema schema) =>
            WriteDocument(doc, schema, named: false);

        public static BtlFrontDocument LoadFile(string path, FbsSchema schema = null, List<string> notes = null) =>
            LoadJson(File.ReadAllText(path), schema, notes);

        public static BtlFrontDocument LoadJson(string json, FbsSchema schema = null, List<string> notes = null)
        {
            notes ??= new List<string>();
            var node = JsonNode.Parse(json, documentOptions: new JsonDocumentOptions { MaxDepth = 1024 });
            if (node is not JsonObject obj)
                throw new InvalidDataException("关卡 JSON 根必须是对象");

            string format = obj["format"]?.GetValue<string>();
            if (string.Equals(format, "btlfront", StringComparison.OrdinalIgnoreCase))
                return BtlFrontJson.Parse(json);

            schema ??= SoftSchema.Schema;
            if (schema == null)
                throw new InvalidDataException("打开 JSON 需要 battle.fbs");

            return SchemaDumpToFront.ToFront(obj, schema, notes);
        }

        static string WriteDocument(BtlFrontDocument doc, FbsSchema schema, bool named)
        {
            if (doc?.Root == null) throw new ArgumentNullException(nameof(doc));
            if (schema == null) throw new ArgumentNullException(nameof(schema));
            var root = WriteTable(doc.Root, schema.RootType, schema, named);
            return root.ToJsonString(JsonOpts);
        }

        static JsonNode WriteNode(BtlNode node, string typeHint, FbsSchema schema, bool named)
        {
            if (node == null) return null;
            if (node is BtlScalar s) return ToJsonValue(s.V);
            if (node is BtlTable tbl) return WriteTable(tbl, typeHint, schema, named);
            if (node is BtlStruct st) return WriteStruct(st, typeHint, schema, named);
            if (node is BtlVector vec) return WriteVector(vec, typeHint, schema, named);
            return null;
        }

        static JsonObject WriteTable(BtlTable tbl, string typeName, FbsSchema schema, bool named)
        {
            var o = new JsonObject();
            foreach (var kv in tbl.F.OrderBy(x => x.Key))
            {
                if (kv.Value == null) continue;
                string key = named
                    ? schema.FieldNameOrId(typeName, kv.Key)
                    : kv.Key.ToString(CultureInfo.InvariantCulture);
                string childHint = null;
                if (schema.TryGetField(typeName, kv.Key, out var fi))
                    childHint = schema.ResolveChildType(fi.Type);
                o[key] = WriteNode(kv.Value, childHint, schema, named);
            }
            return o;
        }

        static JsonNode WriteStruct(BtlStruct st, string typeName, FbsSchema schema, bool named)
        {
            var members = schema.StructMembers(typeName);
            var o = new JsonObject();
            for (int i = 0; i < st.V.Count; i++)
            {
                string key;
                if (named && i < members.Count && !string.IsNullOrEmpty(members[i].Name))
                    key = members[i].Name;
                else if (i < members.Count)
                    key = members[i].Id.ToString(CultureInfo.InvariantCulture);
                else
                    key = i.ToString(CultureInfo.InvariantCulture);
                o[key] = WriteLoose(st.V[i], named, schema, i < members.Count ? schema.ResolveChildType(members[i].Type) : null);
            }
            return o;
        }

        static JsonArray WriteVector(BtlVector vec, string elemTypeHint, FbsSchema schema, bool named)
        {
            var arr = new JsonArray();
            foreach (var item in vec.V)
                arr.Add(WriteLoose(item, named, schema, elemTypeHint));
            return arr;
        }

        static JsonNode WriteLoose(object item, bool named, FbsSchema schema, string typeHint)
        {
            if (item is BtlNode bn) return WriteNode(bn, typeHint, schema, named);
            if (item is JsonNode jn) return jn.DeepClone();
            return ToJsonValue(item);
        }

        static JsonNode ToJsonValue(object v) => v switch
        {
            null => null,
            bool b => b,
            byte u8 => u8,
            sbyte i8 => i8,
            ushort u16 => u16,
            short i16 => i16,
            uint u32 => u32,
            int i32 => i32,
            ulong u64 => u64,
            long i64 => i64,
            float f32 => f32,
            double f64 => f64,
            string str => str,
            JsonNode jn => jn.DeepClone(),
            JsonElement je => JsonNode.Parse(je.GetRawText()),
            _ => v.ToString()
        };
    }
}
