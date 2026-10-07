/*
 * BtlFrontModel.cs
 *
 * BtlFront 是编辑器和编解码共用的「内存关卡树」，不是游戏里的 C++ 类，也不是 StageModel。
 * 每个节点只记录 FlatBuffers 自己的形状：
 *
 *   table  → 字段号 id → 子节点（缺省未写出的字段根本不出现在 F 里）
 *   struct → 按声明顺序的成员列表（内联，没有 vtable）
 *   vector → elem 元素类型 + V 元素列表；国家行为树另用 enc=country_ai_bt
 *   scalar → t 为 u8/i16/string/bool…，V 是 CLR 值
 *
 * JSON 形态（撤销栈、调试导出）是同一棵树：{ "t":"table", "f":{ "0":… } }。
 * 有 enc 的向量元素保持 JsonNode，不再 Recurse 成 BtlNode，以免把行为树对象拆碎。
 *
 * 本文件不读 battle.fbs：类型名字只是字符串标签。字段语义由 SoftSchema / 编辑器控件绑定。
 */
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace BtlCore.Front
{
    /// <summary>Front 树节点基类。T 为 "table" / "struct" / "vector" / 标量类型名。</summary>
    public abstract class BtlNode
    {
        /// <summary>节点种类。标量时为 u8、u16、i16、f32、bool、string 等。</summary>
        public string T { get; set; }
    }

    /// <summary>标量字段：一个数、布尔或字符串。V 为拆箱后的 CLR 值。</summary>
    public sealed class BtlScalar : BtlNode
    {
        public object V { get; set; }
    }

    /// <summary>
    /// FlatBuffers table。键是 schema 里的 field id（不是名字）。
    /// 槽不存在 = 文件里没写（缺省 0 / null），不要用 0 去填没出现过的槽。
    /// </summary>
    public sealed class BtlTable : BtlNode
    {
        public Dictionary<int, BtlNode> F { get; } = new Dictionary<int, BtlNode>();
    }

    /// <summary>
    /// 内联 struct。V[i] 是第 i 个成员的值。
    /// Layout[i] 是该成员的标量类型。读文件时从 fbs 抄来，之后可以改，写出时以它为准。
    /// </summary>
    public sealed class BtlStruct : BtlNode
    {
        public List<object> V { get; } = new List<object>();
        public List<string> Layout { get; } = new List<string>();
    }

    /// <summary>
    /// 向量。Elem 告诉写出时每个元素怎么编码（u16 / table / struct…）。
    /// Enc 非空表示内容不是普通 FB 元素：目前只有 country_ai_bt，V 里是 JsonNode。
    /// </summary>
    public sealed class BtlVector : BtlNode
    {
        /// <summary>元素类型：u8/u16/table/struct/string 等。</summary>
        public string Elem { get; set; }
        /// <summary>特殊编码名。country_ai_bt = 国家行为树打包字节流已解成 JSON。</summary>
        public string Enc { get; set; }
        /// <summary>元素是 struct 时，整条向量共用的成员类型。空则写出时再看元素自己的 Layout 或 fbs。</summary>
        public List<string> StructLayout { get; } = new List<string>();
        public List<object> V { get; } = new List<object>();
    }

    /// <summary>一份关卡：Root 即 battle.fbs 的 Root 表。FormatVersion 是 Front JSON 自己的版本，不是 BTL 的 version 字段。</summary>
    public sealed class BtlFrontDocument
    {
        public int FormatVersion { get; set; } = 1;
        public BtlTable Root { get; set; }
    }

    /// <summary>BtlFront ↔ JSON 文本，以及不经 JSON 的深拷贝和文档比较。</summary>
    public static class BtlFrontJson
    {
        public static readonly JsonSerializerOptions JsonOptions = new JsonSerializerOptions
        {
            WriteIndented = true,
            MaxDepth = 1024,
            PropertyNameCaseInsensitive = true
        };

        /// <summary>解析 format=btlfront 的 JSON。root 必须是 table。</summary>
        public static BtlFrontDocument Parse(string json)
        {
            var node = JsonNode.Parse(json, documentOptions: new JsonDocumentOptions { MaxDepth = 1024 });
            if (node is not JsonObject obj)
                throw new InvalidDataException("BtlFront 根必须是 JSON 对象");

            string format = obj["format"]?.GetValue<string>();
            if (!string.Equals(format, "btlfront", StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("不是 BtlFront 文档（缺少 format=btlfront）");

            var doc = new BtlFrontDocument
            {
                FormatVersion = obj["format_version"]?.GetValue<int>() ?? 1,
                Root = ReadNode(obj["root"]) as BtlTable
            };
            if (doc.Root == null)
                throw new InvalidDataException("BtlFront 缺少 root 表");
            return doc;
        }

        /// <summary>写成缩进 JSON。字段号按升序输出，便于 diff。</summary>
        public static string Serialize(BtlFrontDocument doc)
        {
            if (doc?.Root == null) throw new ArgumentNullException(nameof(doc));
            var obj = new JsonObject
            {
                ["format"] = "btlfront",
                ["format_version"] = doc.FormatVersion,
                ["root"] = WriteNode(doc.Root)
            };
            return obj.ToJsonString(JsonOptions);
        }

        public static BtlFrontDocument LoadFile(string path) => Parse(File.ReadAllText(path));

        public static void SaveFile(BtlFrontDocument doc, string path) =>
            File.WriteAllText(path, Serialize(doc), System.Text.Encoding.UTF8);

        public static BtlTable NewTable() => new BtlTable { T = "table" };

        public static BtlStruct NewStruct(params object[] members)
        {
            var s = new BtlStruct { T = "struct" };
            if (members != null) s.V.AddRange(members);
            return s;
        }

        public static BtlVector NewVector(string elem)
        {
            return new BtlVector { T = "vector", Elem = elem };
        }

        public static BtlScalar Scalar(string t, object v) => new BtlScalar { T = t, V = v };

        /// <summary>直接深拷贝树，保留数值类型、结构布局及打包 JSON，避免中间 JSON 分配。</summary>
        public static BtlNode Clone(BtlNode node)
        {
            if (node == null) return null;
            if (node is BtlScalar scalar) return new BtlScalar { T = scalar.T, V = CloneValue(scalar.V) };
            if (node is BtlTable table)
            {
                var copy = new BtlTable { T = table.T };
                foreach (var field in table.F) copy.F.Add(field.Key, Clone(field.Value));
                return copy;
            }
            if (node is BtlStruct structure)
            {
                var copy = new BtlStruct { T = structure.T };
                copy.Layout.AddRange(structure.Layout);
                copy.V.Capacity = structure.V.Count;
                foreach (var value in structure.V) copy.V.Add(CloneValue(value));
                return copy;
            }
            if (node is BtlVector vector)
            {
                var copy = new BtlVector { T = vector.T, Elem = vector.Elem, Enc = vector.Enc };
                copy.StructLayout.AddRange(vector.StructLayout);
                copy.V.Capacity = vector.V.Count;
                foreach (var value in vector.V) copy.V.Add(CloneValue(value));
                return copy;
            }
            throw new InvalidDataException("未知 Front 节点类型");
        }

        static object CloneValue(object value) => value switch
        {
            BtlNode node => Clone(node),
            JsonNode json => json.DeepClone(),
            JsonElement element => element.Clone(),
            _ => value // Front 标量是不可变的 CLR 数值、布尔和字符串。
        };

        public static bool ContentEquals(BtlFrontDocument left, BtlFrontDocument right) =>
            ReferenceEquals(left, right) || left != null && right != null
            && left.FormatVersion == right.FormatVersion && ValueEquals(left.Root, right.Root);

        static bool ValueEquals(object left, object right)
        {
            if (ReferenceEquals(left, right)) return true;
            if (left == null || right == null) return false;
            if (left is BtlNode a && right is BtlNode b)
            {
                if (a.T != b.T) return false;
                if (a is BtlScalar sa && b is BtlScalar sb) return ValueEquals(sa.V, sb.V);
                if (a is BtlTable ta && b is BtlTable tb)
                {
                    if (ta.F.Count != tb.F.Count) return false;
                    foreach (var field in ta.F)
                        if (!tb.F.TryGetValue(field.Key, out var value) || !ValueEquals(field.Value, value)) return false;
                    return true;
                }
                if (a is BtlStruct sta && b is BtlStruct stb)
                    return sta.Layout.SequenceEqual(stb.Layout) && ValuesEqual(sta.V, stb.V);
                if (a is BtlVector va && b is BtlVector vb)
                    return va.Elem == vb.Elem && va.Enc == vb.Enc
                        && va.StructLayout.SequenceEqual(vb.StructLayout) && ValuesEqual(va.V, vb.V);
                return false;
            }
            if (left is JsonNode ja && right is JsonNode jb) return JsonNode.DeepEquals(ja, jb);
            if (left.Equals(right)) return true;
            // JSON 导入的 long 与写接口的 u16 等数值类型可以不同，但值相同不应增加撤销帧。
            if (IsNumber(left) && IsNumber(right))
            {
                if (left is float or double || right is float or double)
                    return JsonSerializer.Serialize(left) == JsonSerializer.Serialize(right);
                return Convert.ToDecimal(left, CultureInfo.InvariantCulture) == Convert.ToDecimal(right, CultureInfo.InvariantCulture);
            }
            if (left is JsonElement ea && right is JsonElement eb) return ea.GetRawText() == eb.GetRawText();
            return false;
        }

        static bool IsNumber(object value) => value is byte or sbyte or ushort or short or uint or int or ulong or long or float or double or decimal;
        static bool ValuesEqual(List<object> left, List<object> right)
        {
            if (left.Count != right.Count) return false;
            for (int i = 0; i < left.Count; i++) if (!ValueEquals(left[i], right[i])) return false;
            return true;
        }

        public static BtlFrontDocument CloneDocument(BtlFrontDocument doc)
        {
            if (doc?.Root == null) return new BtlFrontDocument { Root = NewTable() };
            return new BtlFrontDocument
            {
                FormatVersion = doc.FormatVersion,
                Root = Clone(doc.Root) as BtlTable ?? NewTable()
            };
        }

        /// <summary>
        /// JSON → 节点。裸数字当成标量；带 t/f 的对象是 table。
        /// enc 向量的元素原样 DeepClone，不再当 Front 节点解析。
        /// </summary>
        public static BtlNode ReadNode(JsonNode node)
        {
            if (node == null || node is JsonValue && node.GetValueKind() == JsonValueKind.Null)
                return null;

            if (node is JsonValue val)
                return ScalarFromJsonValue(val);

            if (node is JsonArray arr)
            {
                var vec = new BtlVector { T = "vector", Elem = "unknown" };
                foreach (var item in arr) vec.V.Add(Unpack(ReadNode(item)));
                return vec;
            }

            if (node is not JsonObject obj)
                return null;

            string t = obj["t"]?.GetValue<string>() ?? obj["T"]?.GetValue<string>();
            if (string.IsNullOrEmpty(t) && obj["f"] != null) t = "table";
            if (string.IsNullOrEmpty(t)) t = "table";

            switch (t)
            {
                case "table":
                    {
                        var tbl = new BtlTable { T = "table" };
                        if (obj["f"] is JsonObject fields)
                        {
                            foreach (var kv in fields)
                            {
                                if (int.TryParse(kv.Key, NumberStyles.Integer, CultureInfo.InvariantCulture, out int id))
                                    tbl.F[id] = ReadNode(kv.Value);
                            }
                        }
                        return tbl;
                    }
                case "struct":
                    {
                        var st = new BtlStruct { T = "struct" };
                        ReadLayout(obj["layout"], st.Layout);
                        if (obj["v"] is JsonArray members)
                        {
                            foreach (var m in members)
                                st.V.Add(Unpack(ReadNode(m)));
                        }
                        return st;
                    }
                case "vector":
                    {
                        var vec = new BtlVector
                        {
                            T = "vector",
                            Elem = obj["elem"]?.GetValue<string>(),
                            Enc = obj["enc"]?.GetValue<string>()
                        };
                        ReadLayout(obj["layout"], vec.StructLayout);
                        if (obj["v"] is JsonArray items)
                        {
                            bool rawEnc = !string.IsNullOrEmpty(vec.Enc);
                            foreach (var item in items)
                            {
                                if (rawEnc)
                                {
                                    vec.V.Add(item?.DeepClone());
                                    continue;
                                }
                                var n = ReadNode(item);
                                vec.V.Add(vec.Elem == "table" || vec.Elem == "struct" ? n : Unpack(n));
                            }
                        }
                        return vec;
                    }
                default:
                    return new BtlScalar { T = t, V = Unpack(ScalarFromJsonValue(obj["v"] as JsonValue) ?? ReadNode(obj["v"])) };
            }
        }

        /// <summary>节点 → JSON。table 的 f 用字符串形式的字段号作键。</summary>
        static JsonNode WriteNode(BtlNode node)
        {
            if (node == null) return null;
            if (node is BtlScalar s)
            {
                var o = new JsonObject { ["t"] = s.T ?? "u16" };
                o["v"] = ToJsonValue(s.V);
                return o;
            }
            if (node is BtlTable tbl)
            {
                var f = new JsonObject();
                foreach (var kv in tbl.F.OrderBy(x => x.Key))
                    f[kv.Key.ToString(CultureInfo.InvariantCulture)] = WriteNode(kv.Value);
                return new JsonObject { ["t"] = "table", ["f"] = f };
            }
            if (node is BtlStruct st)
            {
                var arr = new JsonArray();
                foreach (var m in st.V) arr.Add(WriteLoose(m));
                var o = new JsonObject { ["t"] = "struct", ["v"] = arr };
                WriteLayout(o, st.Layout);
                return o;
            }
            if (node is BtlVector vec)
            {
                var arr = new JsonArray();
                foreach (var item in vec.V)
                {
                    if (item is BtlNode bn) arr.Add(WriteNode(bn));
                    else arr.Add(WriteLoose(item));
                }
                var o = new JsonObject { ["t"] = "vector", ["elem"] = vec.Elem ?? "unknown", ["v"] = arr };
                if (!string.IsNullOrEmpty(vec.Enc)) o["enc"] = vec.Enc;
                WriteLayout(o, vec.StructLayout);
                return o;
            }
            return null;
        }

        static void ReadLayout(JsonNode node, List<string> dest)
        {
            if (node is not JsonArray arr) return;
            foreach (var item in arr)
            {
                string t = item?.GetValue<string>();
                if (!string.IsNullOrEmpty(t)) dest.Add(t);
            }
        }

        static void WriteLayout(JsonObject obj, List<string> layout)
        {
            if (layout == null || layout.Count == 0) return;
            var arr = new JsonArray();
            foreach (var t in layout) arr.Add((JsonNode)t);
            obj["layout"] = arr;
        }

        static JsonNode WriteLoose(object v)
        {
            if (v is BtlNode n) return WriteNode(n);
            if (v is JsonNode jn) return jn.DeepClone();
            return ToJsonValue(v);
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

        static BtlScalar ScalarFromJsonValue(JsonValue val)
        {
            if (val == null) return null;
            return new BtlScalar { T = GuessScalarType(val), V = UnpackValue(val) };
        }

        // JSON 里没有 u16/i32 标签时的猜测：小的非负整数当 u16（战役里最常见），其余当 i32。
        static string GuessScalarType(JsonValue val) => val.GetValueKind() switch
        {
            JsonValueKind.True or JsonValueKind.False => "bool",
            JsonValueKind.String => "string",
            JsonValueKind.Number when val.TryGetValue(out long l) && l >= 0 && l <= ushort.MaxValue => "u16",
            JsonValueKind.Number => "i32",
            _ => "u16"
        };

        static object Unpack(BtlNode node) => node is BtlScalar s ? s.V : node;

        static object UnpackValue(JsonValue val)
        {
            if (val.TryGetValue(out bool b) && val.GetValueKind() is JsonValueKind.True or JsonValueKind.False)
                return b;
            if (val.GetValueKind() == JsonValueKind.String)
                return val.GetValue<string>();
            if (val.GetValueKind() == JsonValueKind.Number)
            {
                if (val.TryGetValue(out long l)) return l;
                if (val.TryGetValue(out ulong u)) return u;
                if (val.TryGetValue(out double d)) return d;
                // JsonValue 里实际装着 byte/sbyte 时，TryGetValue<long> 会失败，ToString 又只是 "5"。
                string raw = val.ToString();
                if (long.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out long n)) return n;
                if (ulong.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out ulong un)) return un;
                if (double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out double x)) return x;
            }
            return val.ToString();
        }
    }
}
