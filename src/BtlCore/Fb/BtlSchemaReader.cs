/*
 * BtlSchemaReader.cs
 *
 * 临时「按 fbs 把文件里每个字段的值读出来」的读取器，给 Dump 工具和对照实验用。
 * 它不进入编辑器文档：输出是带字段名的 JSON，不是 BtlFront。
 *
 * 和 BtlToFront 的差别（刻意更严）：
 *   vtable 槽 0     → JSON null（缺省未写出）
 *   指针落在零填充   → { "_error":"zero_padding", "_ptr":… }，不编成空表
 *   未知多出来的槽   → 放在 _unknown 下
 *
 * packed_json 只给 Root 的 rle_metadata / 路径 /10 的 ubyte 向量附加一份，
 * 方便人看行为树；编回时 SchemaDumpToFront 仍优先用原始 bytes。
 */
using System.Globalization;
using System.Text;
using System.Text.Json.Nodes;

namespace BtlCore.Fb
{
    public sealed class BtlSchemaReadResult
    {
        /// <summary>format=btl-schema-dump 的 JSON：size、root_off、root、notes。</summary>
        public JsonObject Document { get; set; }
        public List<string> Notes { get; } = new List<string>();
        public bool Ok => Document != null;
    }

    /// <summary>按 fbs 把 BTL 读成带字段名的 JSON。坏指针不修复成空表。</summary>
    public static class BtlSchemaReader
    {
        const int MaxDepth = 64;
        const int MaxVectorElems = 2_000_000;

        public static BtlSchemaReadResult FromFile(string path, FbsSchema schema) =>
            FromBytes(File.ReadAllBytes(path), schema, Path.GetFileName(path));

        public static BtlSchemaReadResult FromBytes(byte[] data, FbsSchema schema, string fileName = null)
        {
            var result = new BtlSchemaReadResult();
            if (schema == null)
            {
                result.Notes.Add("缺少 battle.fbs schema");
                return result;
            }
            if (data == null || data.Length < 8)
            {
                result.Notes.Add("文件过短，无法作为 FlatBuffers BTL");
                return result;
            }

            var reader = new Reader(data, schema, result.Notes);
            int rootOff = reader.ReadI32(0);
            if (!reader.InRange(rootOff, 4))
            {
                result.Notes.Add($"Root 偏移无效: 0x{rootOff:X}");
                return result;
            }

            JsonNode rootNode = reader.ReadTable(rootOff, schema.RootType, "/", 0);
            result.Document = new JsonObject
            {
                ["format"] = "btl-schema-dump",
                ["file"] = fileName,
                ["size"] = data.Length,
                ["root_off"] = rootOff,
                ["fbs"] = Path.GetFileName(schema.SourcePath),
                ["root"] = rootNode
            };
            return result;
        }

        sealed class Reader
        {
            readonly byte[] _d;
            readonly FbsSchema _schema;
            readonly List<string> _notes;
            readonly HashSet<int> _visiting = new HashSet<int>();

            public Reader(byte[] data, FbsSchema schema, List<string> notes)
            {
                _d = data;
                _schema = schema;
                _notes = notes;
            }

            public bool InRange(int pos, int size) =>
                pos >= 0 && size >= 0 && (long)pos + size <= _d.Length;

            public int ReadI32(int pos) => BitConverter.ToInt32(_d, pos);

            void Note(string msg) => _notes.Add(msg);

            public JsonNode ReadTable(int pos, string typeName, string path, int depth)
            {
                if (depth > MaxDepth)
                {
                    Note($"{path}: 超过最大嵌套深度");
                    return Error("max_depth", pos);
                }
                if (!InRange(pos, 4))
                {
                    Note($"{path}: 表偏移越界 @0x{pos:X}");
                    return Error("oob", pos);
                }
                if (!_visiting.Add(pos))
                {
                    Note($"{path}: 环引用 @0x{pos:X}");
                    return new JsonObject { ["_ref"] = pos };
                }

                try
                {
                    int soff = ReadI32(pos);
                    int vt = pos - soff;
                    if (!InRange(vt, 4))
                    {
                        Note($"{path}: vtable 无效 @0x{pos:X}");
                        return Error("bad_vtable", pos);
                    }

                    ushort vtableSize = ReadU16(vt);
                    ushort objectSize = ReadU16(vt + 2);
                    int numSlots = vtableSize >= 4 ? (vtableSize - 4) / 2 : 0;
                    if (numSlots < 0) numSlots = 0;
                    if (numSlots > 512)
                    {
                        Note($"{path}: 字段数过多 ({numSlots})，截断到 512");
                        numSlots = 512;
                    }

                    var offsets = new ushort[numSlots];
                    for (int i = 0; i < numSlots; i++)
                    {
                        int op = vt + 4 + i * 2;
                        offsets[i] = InRange(op, 2) ? ReadU16(op) : (ushort)0;
                    }

                    var obj = new JsonObject();
                    if (!string.IsNullOrEmpty(typeName) && _schema.IsTable(typeName))
                        obj["_type"] = typeName;

                    var seen = new HashSet<int>();
                    foreach (var field in _schema.FieldsOf(typeName))
                    {
                        seen.Add(field.Id);
                        JsonNode value = null;
                        if (field.Id < numSlots && offsets[field.Id] != 0)
                        {
                            int fieldPos = pos + offsets[field.Id];
                            string fp = Join(path, field.Name);
                            value = ReadByType(fieldPos, field.Type, fp, depth + 1);
                        }
                        obj[field.Name] = value;
                    }

                    JsonObject extra = null;
                    for (int id = 0; id < numSlots; id++)
                    {
                        if (offsets[id] == 0 || seen.Contains(id)) continue;
                        extra ??= new JsonObject();
                        int fieldPos = pos + offsets[id];
                        int width = SlotWidth(offsets, id, objectSize);
                        extra[id.ToString(CultureInfo.InvariantCulture)] =
                            ReadUnknown(fieldPos, width, Join(path, "_" + id), depth + 1);
                    }
                    if (extra != null)
                        obj["_unknown"] = extra;

                    return obj;
                }
                finally
                {
                    _visiting.Remove(pos);
                }
            }

            JsonNode ReadByType(int fieldPos, string type, string path, int depth)
            {
                if (string.IsNullOrEmpty(type))
                    return ReadUnknown(fieldPos, 4, path, depth);

                if (FbsSchema.IsVectorType(type))
                    return ReadVectorField(fieldPos, FbsSchema.IsVectorType(type) ? type.Substring("vector_".Length) : type, path, depth);

                if (FbsSchema.IsStringType(type))
                    return ReadStringField(fieldPos, path);

                if (_schema.IsTable(type))
                    return ReadTablePointer(fieldPos, type, path, depth);

                if (_schema.IsStruct(type))
                    return ReadStruct(fieldPos, type, path);

                if (FbsSchema.IsScalarType(type))
                    return ReadScalar(fieldPos, type, path);

                return ReadUnknown(fieldPos, 4, path, depth);
            }

            JsonNode ReadTablePointer(int fieldPos, string childType, string path, int depth)
            {
                if (!InRange(fieldPos, 4))
                    return Error("oob", fieldPos);
                int rel = ReadI32(fieldPos);
                if (rel == 0) return null;
                int abs = fieldPos + rel;
                if (!InRange(abs, 4))
                {
                    Note($"{path}: 表指针越界 @0x{abs:X}");
                    return Error("oob", abs);
                }
                int soff = ReadI32(abs);
                if (soff == 0)
                {
                    Note($"{path}: 指针落在零填充 @0x{abs:X}，不当作空表");
                    return Error("zero_padding", abs);
                }
                if (!LooksLikeTable(abs))
                {
                    Note($"{path}: 不像合法表 @0x{abs:X}");
                    return Error("not_table", abs);
                }
                return ReadTable(abs, childType, path, depth);
            }

            JsonNode ReadVectorField(int fieldPos, string elemType, string path, int depth)
            {
                if (!InRange(fieldPos, 4))
                    return Error("oob", fieldPos);
                int rel = ReadI32(fieldPos);
                if (rel == 0) return null;
                int abs = fieldPos + rel;
                return ReadVectorAt(abs, elemType, path, depth);
            }

            JsonNode ReadVectorAt(int vecPos, string elemType, string path, int depth)
            {
                if (!InRange(vecPos, 4))
                {
                    Note($"{path}: 向量头越界");
                    return Error("oob", vecPos);
                }

                int count = ReadI32(vecPos);
                if (count < 0)
                {
                    Note($"{path}: 向量长度为负 ({count})");
                    count = 0;
                }
                if (count > MaxVectorElems)
                {
                    Note($"{path}: 向量过长 ({count})，截断");
                    count = MaxVectorElems;
                }

                int data = vecPos + 4;
                var arr = new JsonArray();

                if (_schema.IsTable(elemType))
                {
                    for (int i = 0; i < count; i++)
                    {
                        int ep = data + i * 4;
                        string ip = path + "[" + i + "]";
                        if (!InRange(ep, 4))
                        {
                            Note($"{ip}: 元素指针越界，截断");
                            break;
                        }
                        int er = ReadI32(ep);
                        if (er == 0)
                        {
                            arr.Add(null);
                            continue;
                        }
                        arr.Add(ReadTablePointer(ep, elemType, ip, depth));
                    }
                    return arr;
                }

                if (_schema.IsStruct(elemType))
                {
                    int stride = StructSize(elemType);
                    if (stride <= 0)
                    {
                        Note($"{path}: struct 向量 {elemType} 无法计算步长");
                        return Error("bad_struct_stride", vecPos);
                    }
                    for (int i = 0; i < count; i++)
                    {
                        int ep = data + i * stride;
                        if (!InRange(ep, stride))
                        {
                            Note($"{path}[{i}]: struct 越界，截断");
                            break;
                        }
                        arr.Add(ReadStruct(ep, elemType, path + "[" + i + "]"));
                    }
                    return arr;
                }

                if (FbsSchema.IsStringType(elemType))
                {
                    for (int i = 0; i < count; i++)
                    {
                        int ep = data + i * 4;
                        if (!InRange(ep, 4)) break;
                        arr.Add(ReadStringField(ep, path + "[" + i + "]"));
                    }
                    return arr;
                }

                int esz = FbsSchema.ScalarByteSize(elemType);
                if (esz <= 0) esz = 1;
                for (int i = 0; i < count; i++)
                {
                    int ep = data + i * esz;
                    if (!InRange(ep, esz))
                    {
                        Note($"{path}[{i}]: 标量越界，截断");
                        break;
                    }
                    arr.Add(ReadScalar(ep, elemType, path + "[" + i + "]"));
                }

                // 国家行为树在 Root.rle_metadata；其它 [ubyte] 保持原字节。
                if (count > 0
                    && string.Equals(elemType, "ubyte", StringComparison.OrdinalIgnoreCase)
                    && path == "/rle_metadata"
                    && TryPackedJson(arr, out JsonNode packed))
                {
                    return new JsonObject
                    {
                        ["_kind"] = "ubyte_vector",
                        ["bytes"] = arr,
                        ["packed_json"] = packed
                    };
                }

                return arr;
            }

            bool TryPackedJson(JsonArray bytes, out JsonNode packed)
            {
                packed = null;
                var raw = new byte[bytes.Count];
                for (int i = 0; i < bytes.Count; i++)
                {
                    if (bytes[i] is not JsonValue jv || !jv.TryGetValue(out int n) || n < 0 || n > 255)
                        return false;
                    raw[i] = (byte)n;
                }
                if (!PackedJsonStream.LooksLike(raw)) return false;
                if (!PackedJsonStream.TryDecode(raw, out var nodes)) return false;
                var arr = new JsonArray();
                foreach (var n in nodes)
                    arr.Add(n?.DeepClone());
                packed = arr;
                return true;
            }

            JsonObject ReadStruct(int pos, string typeName, string path)
            {
                var members = _schema.FieldsOf(typeName);
                var obj = new JsonObject();
                if (!string.IsNullOrEmpty(typeName))
                    obj["_type"] = typeName;
                int p = pos;
                foreach (var m in members)
                {
                    int sz = FbsSchema.ScalarByteSize(m.Type);
                    if (sz <= 0)
                    {
                        Note($"{path}.{m.Name}: struct 成员不是标量 ({m.Type})");
                        obj[m.Name] = null;
                        continue;
                    }
                    obj[m.Name] = ReadScalar(p, m.Type, path + "." + m.Name);
                    p += sz;
                }
                return obj;
            }

            int StructSize(string typeName)
            {
                int n = 0;
                foreach (var m in _schema.FieldsOf(typeName))
                {
                    int sz = FbsSchema.ScalarByteSize(m.Type);
                    if (sz <= 0) return 0;
                    n += sz;
                }
                return n;
            }

            JsonNode ReadStringField(int fieldPos, string path)
            {
                if (!InRange(fieldPos, 4)) return null;
                int rel = ReadI32(fieldPos);
                if (rel == 0) return null;
                int abs = fieldPos + rel;
                if (!InRange(abs, 4))
                {
                    Note($"{path}: 字符串越界");
                    return Error("oob", abs);
                }
                int len = ReadI32(abs);
                if (len < 0 || len > 1_000_000 || !InRange(abs + 4, len))
                {
                    Note($"{path}: 字符串长度异常 {len}");
                    return Error("bad_string", abs);
                }
                try
                {
                    return JsonValue.Create(Encoding.UTF8.GetString(_d, abs + 4, len));
                }
                catch (Exception ex)
                {
                    Note($"{path}: 字符串解码失败 {ex.Message}");
                    return Error("bad_string", abs);
                }
            }

            JsonNode ReadScalar(int pos, string type, string path)
            {
                int sz = FbsSchema.ScalarByteSize(type);
                if (sz <= 0 || !InRange(pos, sz))
                {
                    Note($"{path}: 标量越界 t={type}");
                    return null;
                }

                return type switch
                {
                    "bool" => JsonValue.Create(_d[pos] != 0),
                    "ubyte" or "uint8" => JsonValue.Create((int)_d[pos]),
                    "byte" or "int8" => JsonValue.Create((int)unchecked((sbyte)_d[pos])),
                    "ushort" or "uint16" => JsonValue.Create((int)ReadU16(pos)),
                    "short" or "int16" => JsonValue.Create((int)BitConverter.ToInt16(_d, pos)),
                    "uint" or "uint32" => JsonValue.Create(BitConverter.ToUInt32(_d, pos)),
                    "int" or "int32" => JsonValue.Create(ReadI32(pos)),
                    "float" => JsonValue.Create(BitConverter.ToSingle(_d, pos)),
                    "ulong" or "uint64" => JsonValue.Create(BitConverter.ToUInt64(_d, pos)),
                    "long" or "int64" => JsonValue.Create(BitConverter.ToInt64(_d, pos)),
                    "double" => JsonValue.Create(BitConverter.ToDouble(_d, pos)),
                    _ => JsonValue.Create(ReadI32(pos))
                };
            }

            JsonNode ReadUnknown(int fieldPos, int width, string path, int depth)
            {
                if (width >= 4 && InRange(fieldPos, 4))
                {
                    int rel = ReadI32(fieldPos);
                    if (rel != 0)
                    {
                        int abs = fieldPos + rel;
                        if (LooksLikeTable(abs))
                            return ReadTable(abs, "", path, depth);
                    }
                    return JsonValue.Create(rel);
                }
                if (width == 2 && InRange(fieldPos, 2))
                    return JsonValue.Create((int)ReadU16(fieldPos));
                if (width == 1 && InRange(fieldPos, 1))
                    return JsonValue.Create((int)_d[fieldPos]);
                return null;
            }

            static int SlotWidth(ushort[] offsets, int fieldId, ushort objectSize)
            {
                int off = offsets[fieldId];
                int next = objectSize > 0 ? objectSize : off + 4;
                for (int j = fieldId + 1; j < offsets.Length; j++)
                {
                    if (offsets[j] != 0 && offsets[j] > off)
                    {
                        next = offsets[j];
                        break;
                    }
                }
                int w = next - off;
                if (w <= 0) w = 4;
                if (w > 256) w = 4;
                return w;
            }

            bool LooksLikeTable(int pos)
            {
                if (!InRange(pos, 4)) return false;
                int soff = ReadI32(pos);
                if (soff == 0) return false;
                int vt = pos - soff;
                if (!InRange(vt, 4)) return false;
                ushort vs = ReadU16(vt);
                ushort os = ReadU16(vt + 2);
                if (vs < 4 || vs > 2048 || (vs & 1) != 0) return false;
                if (os < 4 || os > 65535) return false;
                return InRange(pos, Math.Min((int)os, 64));
            }

            ushort ReadU16(int pos) => BitConverter.ToUInt16(_d, pos);

            static string Join(string path, string name) =>
                path == "/" ? "/" + name : path + "/" + name;

            static JsonObject Error(string reason, int ptr) => new JsonObject
            {
                ["_error"] = reason,
                ["_ptr"] = ptr
            };
        }
    }
}
