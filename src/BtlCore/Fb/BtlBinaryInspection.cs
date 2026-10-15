using System.Buffers.Binary;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;

namespace BtlCore.Fb;

public enum BinaryValueState { Present, Absent, Unknown, Invalid, NullPointer, ZeroPadding, Decoded }

/// <summary>查看节点。地址和长度指向原字节；未知槽的 Length 只是物理间隔，不声称是类型宽度。</summary>
public sealed class BinaryInspectionNode
{
    public string Path { get; internal set; }
    public string Name { get; internal set; }
    public string Type { get; internal set; }
    public string Kind { get; internal set; }
    public BinaryValueState State { get; internal set; } = BinaryValueState.Present;
    public int? Offset { get; internal set; }
    public int? PointerOffset { get; internal set; }
    public int Length { get; internal set; }
    public bool ExactLength { get; internal set; } = true;
    public object Value { get; internal set; }
    public string Diagnostic { get; internal set; }
    public int? VtableOffset { get; internal set; }
    public int VtableSize { get; internal set; }
    public int ObjectSize { get; internal set; }
    public int PhysicalSlotCount { get; internal set; }
    public int ChildCount { get; internal set; }
    internal Func<int, BinaryInspectionNode> ChildFactory;
    Dictionary<int, BinaryInspectionNode> _fieldCache;
    public BinaryInspectionNode GetChild(int index)
    {
        if ((uint)index >= (uint)ChildCount) throw new ArgumentOutOfRangeException(nameof(index));
        if (Kind is "table" or "struct")
        {
            _fieldCache ??= new();
            if (!_fieldCache.TryGetValue(index, out var field)) _fieldCache[index] = field = ChildFactory(index);
            return field;
        }
        return ChildFactory(index);
    }
}

/// <summary>
/// 独立、只读的二进制解释。复制源字节，schema 为本实例局部参数，不使用 SoftSchema 的全局提示缓存。
/// 子节点按需生成；解释失败保留字段节点和原字节，不生成默认 0/空表，也不猜未知字段的类型。
/// </summary>
public sealed class BtlBinaryInspection
{
    const int MaxDepth = 64;
    const int MaxVectorCount = 2_000_000;
    const int MaxPackedBytes = 2_000_000;
    static readonly UTF8Encoding StrictUtf8 = new(false, true);
    readonly byte[] _bytes;
    readonly Dictionary<string, StructLayout> _layouts = new(StringComparer.Ordinal);
    public FbsSchema Schema { get; }
    public BinaryInspectionNode Root { get; }
    public int ByteLength => _bytes.Length;
    public string Sha256 { get; }
    public byte[] CopyBytes() => (byte[])_bytes.Clone();

    public BtlBinaryInspection(byte[] bytes, FbsSchema schema = null)
    {
        _bytes = (byte[])(bytes ?? Array.Empty<byte>()).Clone();
        Schema = schema;
        Sha256 = Convert.ToHexString(SHA256.HashData(_bytes));
        if (schema != null)
        {
            if (!schema.IsTable(schema.RootType)) throw new InvalidDataException("schema 的 root_type 必须是已声明的 table");
            ValidateType(schema.RootType, new HashSet<string>(), new HashSet<string>());
        }
        if (!Range(0, 4)) Root = Invalid("/", "Root", 0, _bytes.Length, "文件过短，缺少 Root 偏移");
        else
        {
            uint offset = U32(0);
            Root = offset < 4 || offset > int.MaxValue
                ? Invalid("/", "Root", 0, 4, "Root 偏移无效")
                : Table((int)offset, schema?.RootType, "/", "Root", Array.Empty<int>(), 0);
        }
    }

    public BtlBinaryInspection Reinterpret(FbsSchema schema) => new(_bytes, schema);

    public string Hex(int offset, int length, int maxBytes = 256)
    {
        if (offset < 0 || length < 0 || offset >= _bytes.Length) return "";
        int count = Math.Min(Math.Min(length, _bytes.Length - offset), Math.Max(0, maxBytes));
        var text = new StringBuilder();
        for (int i = 0; i < count; i += 16)
        {
            text.Append($"{offset + i:X8}  ");
            text.AppendLine(string.Join(" ", _bytes.AsSpan(offset + i, Math.Min(16, count - i)).ToArray().Select(b => b.ToString("X2"))));
        }
        if (count < length) text.AppendLine($"… 共 {length} 字节（此处预览 {count} 字节）");
        return text.ToString();
    }

    public BinaryInspectionNode Find(string path)
    {
        var parts = (path ?? "").Trim('/').Split('/', StringSplitOptions.RemoveEmptyEntries);
        var current = Root;
        foreach (string part in parts)
        {
            BinaryInspectionNode next = null;
            if (current.Kind == "vector")
            {
                if (part.StartsWith('i') && int.TryParse(part[1..], out int i) && i >= 0 && i < current.ChildCount)
                {
                    var candidate = current.GetChild(i);
                    if (candidate.Path.EndsWith("/" + part, StringComparison.Ordinal)) next = candidate;
                }
                else if (part == "decoded" && current.ChildCount > 0)
                {
                    var candidate = current.GetChild(current.ChildCount - 1);
                    if (candidate.Path.EndsWith("/decoded", StringComparison.Ordinal)) next = candidate;
                }
            }
            else
                for (int j = 0; j < current.ChildCount; j++)
                {
                    var child = current.GetChild(j);
                    if (child.Path[(child.Path.LastIndexOf('/') + 1)..] == part) { next = child; break; }
                }
            if (next == null) return null;
            current = next;
        }
        return current;
    }

    BinaryInspectionNode Table(int pos, string type, string path, string name, int[] ancestors, int depth)
    {
        if (depth > MaxDepth) return Invalid(path, name, pos, 4, "超过最大查看深度");
        if (ancestors.Contains(pos)) return Invalid(path, name, pos, 4, "检测到环引用");
        if (!Range(pos, 4)) return Invalid(path, name, pos, 4, "表头越界");
        long vtLong = (long)pos - I32(pos);
        if (I32(pos) == 0 || !Range(vtLong, 4)) return Invalid(path, name, pos, 4, "vtable 偏移无效");
        int vt = (int)vtLong;
        int vs = U16(vt), os = U16(vt + 2);
        if (vs < 4 || (vs & 1) != 0 || !Range(vt, vs) || os < 4 || !Range(pos, os))
            return Invalid(path, name, pos, Math.Max(4, os), "vtable 或对象范围无效／截断");
        int count = (vs - 4) / 2;
        var offsets = new ushort[count];
        for (int i = 0; i < count; i++) offsets[i] = U16(vt + 4 + i * 2);
        var ids = new SortedSet<int>(Enumerable.Range(0, count));
        if (type != null && Schema != null) foreach (var f in Schema.FieldsOf(type)) ids.Add(f.Id);
        var visible = ids.ToArray();
        var chain = ancestors.Append(pos).ToArray();
        var node = Node(path, name, "table", type == null ? "table（未套用 schema）" : "table (" + type + ")", pos, os);
        node.VtableOffset = vt; node.VtableSize = vs; node.ObjectSize = os; node.PhysicalSlotCount = count;
        node.Value = $"物理槽={count}，对象={os} 字节，vtable=0x{vt:X}";
        node.ChildCount = visible.Length;
        node.ChildFactory = index =>
        {
            int id = visible[index];
            string fp = Join(path, id.ToString(CultureInfo.InvariantCulture));
            FbsField field = null;
            Schema?.TryGetField(type, id, out field);
            string label = field?.Name ?? id.ToString(CultureInfo.InvariantCulture);
            int off = id < count ? offsets[id] : 0;
            if (off == 0)
            {
                var absent = Node(fp, label, "scalar", DisplayType(field?.Type), null, 0);
                absent.State = BinaryValueState.Absent;
                absent.Value = DefaultValue(field);
                absent.Diagnostic = id < count ? "vtable 槽为 0：文件未写出此字段" : "schema 中的字段；文件没有这个 vtable 槽";
                if (field?.Required == true)
                {
                    absent.State = BinaryValueState.Invalid;
                    absent.Value = "schema 的 required 字段未写出";
                    absent.Diagnostic += "；不满足 required 声明";
                }
                return absent;
            }
            if (off < 4 || off >= os)
            {
                int preview = Math.Max(0, Math.Min(4, _bytes.Length - (pos + off)));
                var failed = Invalid(fp, label, pos + off, preview, "字段偏移超出所属对象；仅预览该地址处的字节，不当作字段值");
                if (field != null) failed.Type = DisplayType(field.Type);
                return failed;
            }
            int span = os - off;
            foreach (int next in offsets) if (next > off) span = Math.Min(span, next - off);
            if (field == null)
            {
                var raw = Node(fp, label, "raw", "未确定类型", pos + off, span);
                raw.State = BinaryValueState.Unknown; raw.ExactLength = false;
                raw.Value = Convert.ToHexString(_bytes.AsSpan(pos + off, Math.Min(span, 16)));
                raw.Diagnostic = "无类型定义；展示到下一物理槽／对象尾的间隔，可能包含填充，不推断语义宽度";
                return raw;
            }
            var interpreted = Typed(pos + off, span, field.Type, fp, label, chain, depth + 1);
            if (interpreted.State == BinaryValueState.Invalid) interpreted.Type = DisplayType(field.Type);
            return interpreted;
        };
        return node;
    }

    BinaryInspectionNode Typed(int pos, int available, string type, string path, string name, int[] ancestors, int depth)
    {
        if (depth > MaxDepth) return Invalid(path, name, pos, available, "超过最大查看深度");
        string scalar = Schema?.ResolveScalarType(type) ?? type;
        int size = FbsSchema.ScalarByteSize(scalar);
        if (size > 0)
        {
            if (size > available || !Range(pos, size)) return Invalid(path, name, pos, available, "标量超出所属范围，未生成替代值");
            var value = Node(path, name, "scalar", type, pos, size);
            value.Value = ReadScalar(pos, scalar);
            return value;
        }
        if (Schema?.IsStruct(type) == true) return Struct(pos, available, type, path, name, ancestors, depth);
        if (available < 4 || !Range(pos, 4)) return Invalid(path, name, pos, available, "相对指针槽不足 4 字节");
        uint rel = U32(pos);
        if (rel == 0)
        {
            var nil = Node(path, name, "scalar", type, pos, 4);
            nil.State = BinaryValueState.NullPointer; nil.Value = "相对偏移为 0";
            nil.Diagnostic = "槽已写出，相对偏移为 0；不是未写出的字段，不据此保证游戏会把它当作缺省值";
            return nil;
        }
        long target = (long)pos + rel;
        if (!Range(target, 4)) return Invalid(path, name, pos, 4, $"相对指针越界，目标 0x{target:X}");
        int at = (int)target;
        BinaryInspectionNode node;
        if (Schema?.IsTable(type) == true)
        {
            if (I32(at) == 0)
            {
                node = Node(path, name, "scalar", type, pos, 4);
                node.State = BinaryValueState.ZeroPadding; node.Value = $"指向零填充 @0x{at:X}";
                node.Diagnostic = "不是合法表，也不是未写出的槽；编辑器读取链路按游戏兼容约定跳过，查看保留原指针";
                return node;
            }
            node = Table(at, type, path, name, ancestors, depth);
        }
        else if (type == "string") node = String(at, path, name);
        else if (FbsSchema.IsVectorType(type)) node = Vector(at, Schema.ResolveChildType(type), path, name, ancestors, depth);
        else return Invalid(path, name, pos, 4, "不支持的 schema 类型: " + type);
        if (node.State == BinaryValueState.Invalid)
        {
            node.Diagnostic = $"槽 @0x{pos:X} → 目标 @0x{at:X}：" + node.Diagnostic;
            node.Offset = pos; node.Length = 4;
            node.ExactLength = true;
        }
        else node.Diagnostic = $"相对指针槽 @0x{pos:X}：{rel}；目标 @0x{at:X}\r\n" + node.Diagnostic;
        node.PointerOffset = pos;
        return node;
    }

    BinaryInspectionNode String(int pos, string path, string name)
    {
        uint length = U32(pos);
        if (length > int.MaxValue || !Range((long)pos + 4, (long)length + 1))
            return Invalid(path, name, pos, 4, "字符串长度越界／截断");
        int len = (int)length;
        if (_bytes[pos + 4 + len] != 0) return Invalid(path, name, pos, len + 5, "字符串缺少 NUL 终止符");
        try
        {
            var node = Node(path, name, "scalar", "string", pos, len + 5);
            node.Value = StrictUtf8.GetString(_bytes, pos + 4, len);
            return node;
        }
        catch (DecoderFallbackException) { return Invalid(path, name, pos, len + 5, "字符串不是有效 UTF-8"); }
    }

    BinaryInspectionNode Vector(int pos, string elem, string path, string name, int[] ancestors, int depth)
    {
        uint count = U32(pos);
        string scalar = Schema.ResolveScalarType(elem);
        int stride = FbsSchema.ScalarByteSize(scalar);
        if (Schema.IsStruct(elem)) stride = Layout(elem, new HashSet<string>()).Size;
        if (stride == 0) stride = 4;
        if (count > MaxVectorCount) return Invalid(path, name, pos, 4, $"向量元素数 {count} 超过查看预算 {MaxVectorCount}");
        long length = 4 + (long)count * stride;
        if (!Range(pos, length)) return Invalid(path, name, pos, 4, $"向量声明 {count} 个元素，但内容越界／截断");
        var node = Node(path, name, "vector", "vector<" + elem + ">", pos, (int)length);
        node.Value = $"len={count}";
        node.ChildCount = (int)count;
        node.ChildFactory = i => Typed(pos + 4 + i * stride, stride, elem, Join(path, "i" + i), "[" + i + "]", ancestors, depth + 1);
        // 原字节向量始终保留；解码是一个额外的子视图。
        if (path == "/10" && (scalar is "ubyte" or "uint8") && count <= MaxPackedBytes)
        {
            var packed = new ArraySegment<byte>(_bytes, pos + 4, (int)count);
            if (PackedJsonStream.LooksLike(packed))
            {
                int rawCount = node.ChildCount;
                var rawFactory = node.ChildFactory;
                BinaryInspectionNode decoded = null;
                node.ChildCount++;
                node.ChildFactory = i => i < rawCount ? rawFactory(i) : decoded ??= Decode();
                node.Diagnostic = "原字节保留；decoded 子节点按游戏 Root/10 约定按需解码，无独立物理字节范围";
                BinaryInspectionNode Decode()
                {
                    return PackedJsonStream.TryDecode(packed, out var trees)
                        ? Json(new JsonArray(trees.Select(t => t?.DeepClone()).ToArray()), Join(path, "decoded"), "解码内容", 0)
                        : Invalid(Join(path, "decoded"), "解码失败", pos + 4, (int)count,
                            "行为树解码失败／暂不支持／超过预算，原始字节完整保留");
                }
            }
        }
        return node;
    }

    BinaryInspectionNode Struct(int pos, int available, string type, string path, string name, int[] ancestors, int depth)
    {
        var layout = Layout(type, new HashSet<string>());
        if (layout.Size > available || !Range(pos, layout.Size)) return Invalid(path, name, pos, available, "struct 超出所属范围");
        var fields = Schema.StructMembers(type);
        var node = Node(path, name, "struct", "struct (" + type + ")", pos, layout.Size);
        node.ChildCount = fields.Count; node.Value = $"members={fields.Count}，size={layout.Size}";
        node.ChildFactory = i => Typed(pos + layout.Offsets[i], layout.Sizes[i], fields[i].Type,
            Join(path, i.ToString(CultureInfo.InvariantCulture)), fields[i].Name, ancestors, depth + 1);
        return node;
    }

    static BinaryInspectionNode Json(JsonNode json, string path, string name, int depth)
    {
        var node = Node(path, name, "json", "JSON（解码解释）", null, 0);
        node.State = BinaryValueState.Decoded;
        if (depth > MaxDepth) { node.Diagnostic = "解码内容超过显示深度"; return node; }
        if (json is JsonObject obj)
        {
            var entries = obj.ToArray(); node.ChildCount = entries.Length; node.Value = $"属性={entries.Length}";
            node.ChildFactory = i => Json(entries[i].Value, Join(path, i.ToString()), entries[i].Key, depth + 1);
        }
        else if (json is JsonArray arr)
        {
            node.ChildCount = arr.Count; node.Value = $"元素={arr.Count}";
            node.ChildFactory = i => Json(arr[i], Join(path, "i" + i), "[" + i + "]", depth + 1);
        }
        else { node.Kind = "scalar"; node.Value = json?.ToJsonString() ?? "null"; }
        return node;
    }

    void ValidateType(string type, HashSet<string> visited, HashSet<string> structs, int depth = 0)
    {
        if (depth > MaxDepth) throw new InvalidDataException("schema 类型嵌套超过支持的深度");
        if (FbsSchema.IsScalarType(Schema.ResolveScalarType(type)) || type == "string") return;
        if (FbsSchema.IsVectorType(type)) { ValidateType(Schema.ResolveChildType(type), visited, structs, depth + 1); return; }
        if (Schema.IsStruct(type)) { Layout(type, structs); return; }
        if (!Schema.IsTable(type)) throw new InvalidDataException("schema 类型未定义或暂不支持: " + type);
        if (!visited.Add(type)) return;
        foreach (var f in Schema.FieldsOf(type))
        {
            if (f.Required && FbsSchema.IsScalarType(Schema.ResolveScalarType(f.Type)))
                throw new InvalidDataException("required 不能用于标量字段: " + f.Name);
            ValidateType(f.Type, visited, structs, depth + 1);
            _ = DefaultValue(f);
        }
    }

    sealed record StructLayout(int Size, int Alignment, int[] Offsets, int[] Sizes);
    StructLayout Layout(string type, HashSet<string> visiting)
    {
        if (_layouts.TryGetValue(type, out var cached)) return cached;
        if (visiting.Count > MaxDepth) throw new InvalidDataException("schema struct 嵌套超过支持的深度");
        if (!visiting.Add(type)) throw new InvalidDataException("schema 存在循环 struct: " + type);
        var fields = Schema.StructMembers(type);
        int p = 0, align = 1;
        var offsets = new int[fields.Count]; var sizes = new int[fields.Count];
        for (int i = 0; i < fields.Count; i++)
        {
            string ft = fields[i].Type;
            int size = FbsSchema.ScalarByteSize(Schema.ResolveScalarType(ft)), a = size;
            if (Schema.IsStruct(ft)) { var nested = Layout(ft, visiting); size = nested.Size; a = nested.Alignment; }
            if (size == 0) throw new InvalidDataException("struct 成员必须是标量或 struct，暂不支持: " + ft);
            align = Math.Max(align, a); p = checked((p + a - 1) / a * a);
            offsets[i] = p; sizes[i] = size; p = checked(p + size);
        }
        visiting.Remove(type);
        if (p == 0) throw new InvalidDataException("不支持空 struct: " + type);
        var layout = new StructLayout(checked((p + align - 1) / align * align), align, offsets, sizes);
        _layouts[type] = layout;
        return layout;
    }

    object DefaultValue(FbsField field)
    {
        if (field == null) return "未写出（类型未知）";
        string scalar = Schema.ResolveScalarType(field.Type);
        if (!FbsSchema.IsScalarType(scalar)) return "NULL（未写出）";
        string literal = field.DefaultLiteral;
        if (literal == "null") return "null（schema 可选标量）";
        if (literal == null) return scalar == "bool" ? "false（schema 缺省）" : "0（schema 缺省）";
        if (Schema.TryEnumValue(field.Type, literal, out long enumeration)) literal = enumeration.ToString(CultureInfo.InvariantCulture);
        try
        {
            object value = scalar switch
            {
                "bool" => bool.Parse(literal),
                "float" => float.Parse(literal, CultureInfo.InvariantCulture),
                "double" => double.Parse(literal, CultureInfo.InvariantCulture),
                "ubyte" or "uint8" => byte.Parse(literal, CultureInfo.InvariantCulture),
                "byte" or "int8" => sbyte.Parse(literal, CultureInfo.InvariantCulture),
                "ushort" or "uint16" => ushort.Parse(literal, CultureInfo.InvariantCulture),
                "short" or "int16" => short.Parse(literal, CultureInfo.InvariantCulture),
                "uint" or "uint32" => uint.Parse(literal, CultureInfo.InvariantCulture),
                "int" or "int32" => int.Parse(literal, CultureInfo.InvariantCulture),
                "ulong" or "uint64" => ulong.Parse(literal, CultureInfo.InvariantCulture),
                _ => long.Parse(literal, CultureInfo.InvariantCulture)
            };
            return Convert.ToString(value, CultureInfo.InvariantCulture) + "（schema 缺省）";
        }
        catch (Exception ex) when (ex is FormatException or OverflowException)
        { throw new InvalidDataException($"不支持或超出范围的缺省值 {field.Name}={literal}", ex); }
    }

    object ReadScalar(int p, string type) => type switch
    {
        "bool" => _bytes[p] != 0,
        "ubyte" or "uint8" => _bytes[p], "byte" or "int8" => unchecked((sbyte)_bytes[p]),
        "ushort" or "uint16" => U16(p), "short" or "int16" => BinaryPrimitives.ReadInt16LittleEndian(_bytes.AsSpan(p)),
        "uint" or "uint32" => U32(p), "int" or "int32" => I32(p),
        "ulong" or "uint64" => BinaryPrimitives.ReadUInt64LittleEndian(_bytes.AsSpan(p)),
        "long" or "int64" => BinaryPrimitives.ReadInt64LittleEndian(_bytes.AsSpan(p)),
        "float" => BitConverter.Int32BitsToSingle(I32(p)),
        "double" => BitConverter.Int64BitsToDouble(BinaryPrimitives.ReadInt64LittleEndian(_bytes.AsSpan(p))),
        _ => throw new InvalidDataException("未知标量: " + type)
    };
    bool Range(long pos, long length) => pos >= 0 && length >= 0 && pos <= _bytes.Length && length <= _bytes.Length - pos;
    static string DisplayType(string type) => type == null ? "未确定类型" : FbsSchema.IsVectorType(type) ? "vector<" + type["vector_".Length..] + ">" : type;
    ushort U16(int p) => BinaryPrimitives.ReadUInt16LittleEndian(_bytes.AsSpan(p));
    uint U32(int p) => BinaryPrimitives.ReadUInt32LittleEndian(_bytes.AsSpan(p));
    int I32(int p) => BinaryPrimitives.ReadInt32LittleEndian(_bytes.AsSpan(p));
    static string Join(string parent, string part) => parent == "/" ? "/" + part : parent + "/" + part;
    static BinaryInspectionNode Node(string path, string name, string kind, string type, int? pos, int length) =>
        new() { Path = path, Name = name, Kind = kind, Type = type, Offset = pos, Length = length };
    static BinaryInspectionNode Invalid(string path, string name, int pos, int length, string diagnostic) =>
        new() { Path = path, Name = name, Kind = "raw", Type = "无法解释", Offset = pos, Length = length,
            ExactLength = false, State = BinaryValueState.Invalid, Diagnostic = diagnostic, Value = diagnostic };
}
