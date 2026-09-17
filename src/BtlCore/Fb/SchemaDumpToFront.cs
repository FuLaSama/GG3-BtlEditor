/*
 * SchemaDumpToFront.cs
 *
 * 把 BtlSchemaReader 的 dump JSON 转成 BtlFront，再交给 FrontToBtl 编回 .btl。
 * 用途：验证「读出所有字段值 → 用我们的算法写回去」能不能被游戏吃，而不是逐字节对齐原文件。
 *
 * 规则：
 *   JSON null / { _error }  → 跳过（对应没写或坏指针，不要发明空表）
 *   空数组 []               → 写出空向量（文件里确实写了长度为 0 的 vector）
 *   ubyte 向量              → 优先用 dump 里的原始 bytes；有 packed_json 也不重新打包，
 *                             以免行为树编码器改字节
 *
 * 导入关卡 JSON 时：字段名按 fbs；对不上再试 @alias 和旧名（playable_flag 等）；
 * 键是数字则按 field id 取类型。ubyte 向量若元素是对象，当成国家行为树 JSON。
 *
 * UnpackScalar 必须先试 int 再试 long：JsonValue.Create((int)n) 之后 TryGetValue(out long) 会失败。
 */
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using BtlCore.Front;

namespace BtlCore.Fb
{
    public static class SchemaDumpToFront
    {
        /// <summary>dump 可以是整个文档对象（含 root 键）或已经是 root 表 JSON。</summary>
        public static BtlFrontDocument ToFront(JsonNode dump, FbsSchema schema, List<string> notes = null)
        {
            if (schema == null) throw new ArgumentNullException(nameof(schema));
            notes ??= new List<string>();

            JsonObject rootJson = dump as JsonObject;
            if (rootJson != null && rootJson["root"] is JsonObject nested)
                rootJson = nested;
            if (rootJson == null)
                throw new InvalidDataException("关卡 JSON 缺少根对象");

            var conv = new Conv(schema, notes);
            var root = conv.Table(rootJson, schema.RootType, "/");
            return new BtlFrontDocument { FormatVersion = 1, Root = root ?? BtlFrontJson.NewTable() };
        }

        sealed class Conv
        {
            readonly FbsSchema _schema;
            readonly List<string> _notes;

            public Conv(FbsSchema schema, List<string> notes)
            {
                _schema = schema;
                _notes = notes;
            }

            void Note(string msg) => _notes.Add(msg);

            static readonly Dictionary<string, Dictionary<string, string>> ImportAliases =
                new Dictionary<string, Dictionary<string, string>>(StringComparer.OrdinalIgnoreCase)
                {
                    ["Root"] = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                    {
                        ["country_ai_bt"] = "rle_metadata",
                        ["weather_info"] = "decal_info"
                    },
                    ["MapTerrain"] = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                    {
                        ["playable_flag"] = "val1",
                        ["fog_or_visibility"] = "val2"
                    },
                    ["StageMetadata"] = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                    {
                        ["stage_num"] = "round_limit"
                    },
                    ["StageTarget"] = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                    {
                        ["flag"] = "unk"
                    },
                    ["ReinforcePoint"] = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                    {
                        ["is_key_unit"] = "val1",
                        ["flag"] = "val2"
                    },
                    ["AIInfo"] = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                    {
                        ["ai_behaviors"] = "behaviors"
                    },
                    ["AIBehavior"] = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                    {
                        ["action_flag"] = "flag",
                        ["target_cells"] = "params"
                    },
                    ["Faction"] = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                    {
                        ["status"] = "val1",
                        ["config_ref"] = "val2"
                    },
                    ["DecalInfo"] = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                    {
                        ["weathers"] = "decals"
                    },
                    ["AIAgent"] = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                    {
                        ["behavior"] = "field_3",
                        ["ai_target"] = "field_5"
                    },
                    ["AIAgentTable11"] = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                    {
                        ["is_active"] = "param1",
                        ["param_2"] = "param2"
                    },
                    ["BuildingData"] = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                    {
                        ["flag"] = "val1",
                        ["extra_flag"] = "val2"
                    }
                };

            bool TryResolveField(string typeName, string key, JsonNode value, out int id, out string fieldType)
            {
                id = -1;
                fieldType = null;
                if (_schema.TryGetFieldByName(typeName, key, out var fi) && fi != null)
                {
                    id = fi.Id;
                    fieldType = fi.Type;
                    return true;
                }
                if (ImportAliases.TryGetValue(typeName ?? "", out var map)
                    && map.TryGetValue(key, out string canon)
                    && _schema.TryGetFieldByName(typeName, canon, out fi) && fi != null)
                {
                    id = fi.Id;
                    fieldType = fi.Type;
                    return true;
                }
                if (int.TryParse(key, NumberStyles.Integer, CultureInfo.InvariantCulture, out id))
                {
                    if (_schema.TryGetField(typeName, id, out fi) && fi != null)
                        fieldType = fi.Type;
                    else
                        fieldType = GuessType(value);
                    return true;
                }
                return false;
            }

            /// <summary>按 fbs 字段名填 table.F[id]。null 和 _error 不建槽；_unknown 里数字键作为额外 field id。</summary>
            public BtlTable Table(JsonObject obj, string typeName, string path)
            {
                if (obj == null) return null;
                if (obj["_error"] != null)
                {
                    Note($"{path}: 跳过错误节点 {obj["_error"]}");
                    return null;
                }

                var tbl = BtlFrontJson.NewTable();
                foreach (var kv in obj)
                {
                    if (IsMeta(kv.Key)) continue;
                    if (IsNull(kv.Value)) continue;

                    if (!TryResolveField(typeName, kv.Key, kv.Value, out int id, out string fieldType))
                    {
                        Note($"{path}/{kv.Key}: 不在 {typeName} schema 中，跳过");
                        continue;
                    }

                    var node = Value(kv.Value, fieldType, path + "/" + kv.Key);
                    if (node != null)
                        tbl.F[id] = node;
                }

                if (obj["_unknown"] is JsonObject extra)
                {
                    foreach (var kv in extra)
                    {
                        if (!int.TryParse(kv.Key, NumberStyles.Integer, CultureInfo.InvariantCulture, out int id))
                            continue;
                        if (tbl.F.ContainsKey(id) || IsNull(kv.Value) || !IsPlainValue(kv.Value))
                            continue;
                        var node = Value(kv.Value, GuessType(kv.Value), path + "/_" + id);
                        if (node != null) tbl.F[id] = node;
                    }
                }

                return tbl;
            }

            BtlNode Value(JsonNode node, string fbsType, string path)
            {
                if (IsNull(node)) return null;
                if (node is JsonObject err && err["_error"] != null)
                {
                    Note($"{path}: 跳过 {_Display(err)}");
                    return null;
                }

                if (FbsSchema.IsVectorType(fbsType))
                    return Vector(node, _schema.ResolveChildType(fbsType), path);

                if (FbsSchema.IsStringType(fbsType))
                    return BtlFrontJson.Scalar("string", node.GetValue<string>() ?? "");

                if (_schema.IsTable(fbsType))
                {
                    if (node is not JsonObject to) { Note($"{path}: 期望 table"); return null; }
                    return Table(to, fbsType, path);
                }

                if (_schema.IsStruct(fbsType))
                    return Struct(node, fbsType, path);

                if (FbsSchema.IsScalarType(fbsType))
                    return BtlFrontJson.Scalar(ToFrontScalar(fbsType), UnpackScalar(node, fbsType));

                return Value(node, GuessType(node), path);
            }

            BtlVector Vector(JsonNode node, string elemType, string path)
            {
                JsonArray arr = null;
                JsonArray packedJson = null;
                if (node is JsonObject wrap)
                {
                    if (wrap["bytes"] is JsonArray bytes) arr = bytes;
                    if (wrap["packed_json"] is JsonArray pj) packedJson = pj;
                }
                else if (node is JsonArray a)
                    arr = a;

                if (arr == null && packedJson != null)
                    return PackedJsonVector(packedJson);

                if (arr == null)
                {
                    Note($"{path}: 期望向量");
                    return null;
                }

                if (IsByteElem(elemType) && LooksLikePackedItems(arr))
                    return PackedJsonVector(arr);

                var vec = BtlFrontJson.NewVector(FrontElem(elemType));
                if (_schema.IsTable(elemType))
                {
                    vec.Elem = "table";
                    foreach (var item in arr)
                    {
                        if (IsNull(item)) { vec.V.Add(BtlFrontJson.NewTable()); continue; }
                        if (item is JsonObject to)
                            vec.V.Add(Table(to, elemType, path) ?? BtlFrontJson.NewTable());
                        else
                            vec.V.Add(BtlFrontJson.NewTable());
                    }
                    return vec;
                }

                if (_schema.IsStruct(elemType))
                {
                    vec.Elem = "struct";
                    foreach (var item in arr)
                    {
                        var st = Struct(item, elemType, path);
                        if (st != null) vec.V.Add(st);
                    }
                    return vec;
                }

                if (FbsSchema.IsStringType(elemType))
                {
                    vec.Elem = "string";
                    foreach (var item in arr)
                        vec.V.Add(IsNull(item) ? "" : item.GetValue<string>() ?? item.ToString());
                    return vec;
                }

                vec.Elem = ToFrontScalar(elemType);
                foreach (var item in arr)
                    vec.V.Add(UnpackScalar(item, elemType));
                return vec;
            }

            static BtlVector PackedJsonVector(JsonArray arr)
            {
                var vec = BtlFrontJson.NewVector("u8");
                vec.Enc = PackedJsonStream.EncName;
                foreach (var item in arr)
                    vec.V.Add(item?.DeepClone());
                return vec;
            }

            static bool IsByteElem(string t) =>
                t is "ubyte" or "uint8" or "u8" or "byte" or "int8";

            static bool LooksLikePackedItems(JsonArray arr) =>
                arr != null && arr.Count > 0 && arr[0] is JsonObject or JsonArray;

            BtlStruct Struct(JsonNode node, string typeName, string path)
            {
                var members = _schema.FieldsOf(typeName);
                var st = new BtlStruct { T = "struct" };
                if (node is JsonArray arr)
                {
                    for (int i = 0; i < members.Count; i++)
                    {
                        st.V.Add(i < arr.Count && !IsNull(arr[i])
                            ? UnpackScalar(arr[i], members[i].Type)
                            : DefaultScalar(members[i].Type));
                    }
                    return st;
                }
                if (node is not JsonObject obj)
                {
                    Note($"{path}: 期望 struct");
                    return null;
                }
                foreach (var m in members)
                {
                    JsonNode n = obj[m.Name];
                    if (n == null)
                        n = obj[m.Id.ToString(CultureInfo.InvariantCulture)];
                    st.V.Add(n != null && !IsNull(n)
                        ? UnpackScalar(n, m.Type)
                        : DefaultScalar(m.Type));
                }
                return st;
            }

            string FrontElem(string elemType)
            {
                if (_schema.IsTable(elemType)) return "table";
                if (_schema.IsStruct(elemType)) return "struct";
                if (FbsSchema.IsStringType(elemType)) return "string";
                return ToFrontScalar(elemType);
            }

            static string ToFrontScalar(string fbs) => fbs switch
            {
                "bool" => "bool",
                "ubyte" or "uint8" => "u8",
                "byte" or "int8" => "i8",
                "ushort" or "uint16" => "u16",
                "short" or "int16" => "i16",
                "uint" or "uint32" => "u32",
                "int" or "int32" => "i32",
                "float" => "f32",
                "ulong" or "uint64" => "u64",
                "long" or "int64" => "i64",
                "double" => "f64",
                _ => "u16"
            };

            static object DefaultScalar(string fbs) => fbs == "bool" ? false : (object)0;

            /// <summary>
            /// JSON 数字拆成 CLR 值。必须先 TryGetValue(int) 再 long：
            /// dump 里小整数经常是 JsonValue(int)，直接取 long 会失败并变成 0。
            /// </summary>
            static object UnpackScalar(JsonNode node, string fbsType)
            {
                if (IsNull(node)) return DefaultScalar(fbsType);
                if (node is not JsonValue v)
                    return node.ToString();

                var kind = v.GetValueKind();
                if (fbsType == "bool" || kind is JsonValueKind.True or JsonValueKind.False)
                {
                    if (kind is JsonValueKind.True or JsonValueKind.False)
                        return v.GetValue<bool>();
                    if (v.TryGetValue(out int ib)) return ib != 0;
                    return false;
                }
                if (kind == JsonValueKind.String)
                    return v.GetValue<string>() ?? "";

                if (kind != JsonValueKind.Number)
                    return DefaultScalar(fbsType);

                if (fbsType is "float")
                {
                    if (v.TryGetValue(out float fv)) return fv;
                    if (v.TryGetValue(out double dv)) return (float)dv;
                    if (TryInt(v, out long li)) return (float)li;
                    return 0f;
                }
                if (fbsType is "double")
                {
                    if (v.TryGetValue(out double dv)) return dv;
                    if (v.TryGetValue(out float fv)) return (double)fv;
                    if (TryInt(v, out long li)) return (double)li;
                    return 0d;
                }

                if (TryInt(v, out long n)) return n;
                if (v.TryGetValue(out double d)) return (long)d;
                return 0;
            }

            static bool TryInt(JsonValue v, out long n)
            {
                n = 0;
                if (v.TryGetValue(out int i32)) { n = i32; return true; }
                if (v.TryGetValue(out long i64)) { n = i64; return true; }
                if (v.TryGetValue(out uint u32)) { n = u32; return true; }
                if (v.TryGetValue(out ulong u64) && u64 <= long.MaxValue) { n = (long)u64; return true; }
                if (v.TryGetValue(out short i16)) { n = i16; return true; }
                if (v.TryGetValue(out ushort u16)) { n = u16; return true; }
                if (v.TryGetValue(out byte u8)) { n = u8; return true; }
                if (v.TryGetValue(out sbyte i8)) { n = i8; return true; }
                return false;
            }

            static bool IsNull(JsonNode n) =>
                n == null || n.GetValueKind() == JsonValueKind.Null;

            static bool IsMeta(string key) =>
                key is "_type" or "_error" or "_ptr" or "_ref" or "_kind" or "_unknown" or "_comment"
                    or "format" or "format_version" or "file" or "root_off" or "fbs" or "notes" or "_pos";

            static bool IsPlainValue(JsonNode n) =>
                n is JsonValue || (n is JsonObject o && o["_error"] == null && o["_type"] != null);

            static string GuessType(JsonNode n) => n switch
            {
                JsonArray => "vector_ubyte",
                JsonObject o when o["_type"] != null => o["_type"]!.GetValue<string>(),
                JsonValue v when v.GetValueKind() is JsonValueKind.True or JsonValueKind.False => "bool",
                JsonValue v when v.GetValueKind() == JsonValueKind.String => "string",
                _ => "int"
            };

            static string _Display(JsonObject o) =>
                o["_error"]?.ToString() ?? "error";
        }
    }
}
