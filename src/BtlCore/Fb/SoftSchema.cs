/*
 * SoftSchema.cs
 *
 * 读 BTL / 写 BTL 时需要知道「这个 field id 是表、向量还是内联 struct」。
 * 这些知识来自 battle.fbs，本文件只做翻译：FbsField → FieldHint。
 *
 * 不要在这里再抄一份表结构（以前那种 AgentInfoMembers、Root[0]=u16 已删除）。
 * 找不到 fbs 时 TryGet 返回 false，BtlToFront 会改用启发式猜指针。
 *
 * 加载顺序：工作区 schema/battle.fbs 或 exe 旁 battle.fbs →
 * 再退回嵌入 BtlCore.dll 的同名资源。可用 SetSchema 在测试里强制指定。
 *
 * Front 标量名用 u8/u16，fbs 里是 ubyte/ushort，ToFrontScalar 做对照。
 */
using System.Globalization;

namespace BtlCore.Fb
{
    /// <summary>读/写时对一个字段的处理方式。由 battle.fbs 推导，不是关卡数据。</summary>
    public enum FieldKind
    {
        Scalar,
        Bool,
        String,
        Table,
        Struct,
        Vector
    }

    /// <summary>struct 里一个成员的 Front 类型名（u8/i8/u16…）以及它占的字节数。</summary>
    public sealed class StructMember
    {
        /// <summary>Front 标量类型名：u8 / i8 / u16 / i16 / u32 / i32 / f32 / bool 等。</summary>
        public string T { get; init; } // u8 / i8 / u16 / ...
        public int Size => T switch
        {
            "u8" or "i8" or "bool" => 1,
            "u16" or "i16" => 2,
            "u32" or "i32" or "f32" => 4,
            "u64" or "i64" or "f64" => 8,
            _ => 1
        };
    }

    /// <summary>
    /// 某个 table 上一个 field id 该怎么读/写。
    /// 向量元素若是 struct，VectorStructMembers 给出逐步长；否则启发式会把 struct 向量误读成字节。
    /// </summary>
    public sealed class FieldHint
    {
        public FieldKind Kind { get; init; }
        /// <summary>Kind=Scalar/Bool 时的 Front 类型名。</summary>
        public string ScalarT { get; init; }
        /// <summary>Kind=Table 时子表在 fbs 里的类型名，继续查字段用。</summary>
        public string ChildTable { get; init; }
        /// <summary>Kind=Struct 或 struct 向量时的结构体名（Size / AgentInfo / TileAttr…）。</summary>
        public string StructName { get; init; }
        public StructMember[] StructMembers { get; init; }
        /// <summary>向量元素：u8 / table / struct / string。</summary>
        public string VectorElem { get; init; }
        /// <summary>table/struct 向量的元素类型名。</summary>
        public string VectorElemTable { get; init; }
        public StructMember[] VectorStructMembers { get; init; }

        public int StructSize => StructMembers?.Sum(m => m.Size) ?? 0;
        public int VectorStructSize => VectorStructMembers?.Sum(m => m.Size) ?? 0;
    }

    /// <summary>进程内一份 fbs → hint 缓存。BtlToFront / FrontToBtl / 编辑器共用。</summary>
    public static class SoftSchema
    {
        static readonly object Gate = new object();
        static FbsSchema _schema;
        static bool _tried;
        static readonly Dictionary<string, FieldHint> HintCache =
            new Dictionary<string, FieldHint>(StringComparer.Ordinal);

        /// <summary>已解析的 battle.fbs。第一次访问时加载，失败则为 null。</summary>
        public static FbsSchema Schema
        {
            get
            {
                lock (Gate)
                {
                    if (!_tried)
                    {
                        _tried = true;
                        _schema = TryLoad();
                    }
                    return _schema;
                }
            }
        }

        public static string RootType => Schema?.RootType ?? "Root";

        /// <summary>测试或 Dump 工具可注入自己的 schema，并清空 hint 缓存。</summary>
        public static void SetSchema(FbsSchema schema)
        {
            lock (Gate)
            {
                _schema = schema;
                _tried = true;
                HintCache.Clear();
            }
        }

        /// <summary>查 tableContext 上 fieldId 的读写提示。没有 fbs 或没有这个字段则 false。</summary>
        public static bool TryGet(string tableContext, int fieldId, out FieldHint hint)
        {
            hint = null;
            var schema = Schema;
            if (schema == null || string.IsNullOrEmpty(tableContext)) return false;

            string key = tableContext + "#" + fieldId.ToString(CultureInfo.InvariantCulture);
            lock (Gate)
            {
                if (HintCache.TryGetValue(key, out hint))
                    return hint != null;
            }

            if (!schema.TryGetField(tableContext, fieldId, out var field) || field == null)
            {
                lock (Gate) HintCache[key] = null;
                return false;
            }

            hint = FromFbs(schema, field);
            lock (Gate) HintCache[key] = hint;
            return hint != null;
        }

        /// <summary>按结构体名取出成员类型列表，给编辑器新建 AgentInfo / Size 等用。</summary>
        public static bool TryStructMembers(string structName, out StructMember[] members)
        {
            members = null;
            var schema = Schema;
            if (schema == null || string.IsNullOrEmpty(structName) || !schema.IsStruct(structName))
                return false;
            members = ToMembers(schema, structName);
            return members != null && members.Length > 0;
        }

        /// <summary>该 Front 标量类型的「零」：整数 0、float 0、bool false。新建 struct 填坑用。</summary>
        public static object DefaultValue(string t) => t switch
        {
            "bool" => false,
            "u8" => (byte)0,
            "i8" => (sbyte)0,
            "u16" => (ushort)0,
            "i16" => (short)0,
            "u32" => 0u,
            "i32" => 0,
            "u64" => 0ul,
            "i64" => 0L,
            "f32" => 0f,
            "f64" => 0d,
            _ => 0
        };

        /// <summary>先找磁盘上的 battle.fbs，再找嵌入资源。都没有就返回 null。</summary>
        static FbsSchema TryLoad()
        {
            string path = FbsSchema.FindNearby();
            if (!string.IsNullOrEmpty(path) && File.Exists(path))
                return FbsSchema.LoadFile(path);

            var asm = typeof(SoftSchema).Assembly;
            using var stream = asm.GetManifestResourceStream("battle.fbs")
                               ?? asm.GetManifestResourceStream("BtlCore.battle.fbs");
            if (stream == null)
            {
                foreach (string name in asm.GetManifestResourceNames())
                {
                    if (name.EndsWith("battle.fbs", StringComparison.OrdinalIgnoreCase))
                    {
                        using var s2 = asm.GetManifestResourceStream(name);
                        if (s2 == null) break;
                        using var r2 = new StreamReader(s2);
                        return FbsSchema.LoadFromText(r2.ReadToEnd(), "embedded:" + name);
                    }
                }
                return null;
            }

            using var reader = new StreamReader(stream);
            return FbsSchema.LoadFromText(reader.ReadToEnd(), "embedded:battle.fbs");
        }

        /// <summary>fbs 类型串 → FieldHint。bool 向量按 u8 存（FB 里 bool 向量就是字节）。</summary>
        static FieldHint FromFbs(FbsSchema schema, FbsField field)
        {
            string t = field.Type;
            if (string.IsNullOrEmpty(t)) return null;

            if (FbsSchema.IsVectorType(t))
            {
                string elem = schema.ResolveChildType(t);
                if (FbsSchema.IsStringType(elem))
                    return new FieldHint { Kind = FieldKind.Vector, VectorElem = "string" };
                if (FbsSchema.IsScalarType(elem))
                {
                    string st = ToFrontScalar(elem);
                    return new FieldHint { Kind = FieldKind.Vector, VectorElem = st == "bool" ? "u8" : st };
                }
                if (schema.IsStruct(elem))
                {
                    return new FieldHint
                    {
                        Kind = FieldKind.Vector,
                        VectorElem = "struct",
                        VectorElemTable = elem,
                        StructName = elem,
                        VectorStructMembers = ToMembers(schema, elem)
                    };
                }
                return new FieldHint
                {
                    Kind = FieldKind.Vector,
                    VectorElem = "table",
                    VectorElemTable = elem ?? ""
                };
            }

            if (FbsSchema.IsStringType(t))
                return new FieldHint { Kind = FieldKind.String };

            if (FbsSchema.IsScalarType(t))
            {
                string st = ToFrontScalar(t);
                if (st == "bool")
                    return new FieldHint { Kind = FieldKind.Bool, ScalarT = "bool" };
                return new FieldHint { Kind = FieldKind.Scalar, ScalarT = st };
            }

            if (schema.IsStruct(t))
            {
                return new FieldHint
                {
                    Kind = FieldKind.Struct,
                    StructName = t,
                    StructMembers = ToMembers(schema, t)
                };
            }

            return new FieldHint { Kind = FieldKind.Table, ChildTable = t };
        }

        static StructMember[] ToMembers(FbsSchema schema, string structName)
        {
            var fields = schema.StructMembers(structName);
            var list = new List<StructMember>(fields.Count);
            foreach (var f in fields)
                list.Add(new StructMember { T = ToFrontScalar(f.Type) });
            return list.ToArray();
        }

        /// <summary>battle.fbs 的 ushort/ubyte 等 → Front 的 u16/u8。</summary>
        public static string ToFrontScalar(string fbs) => fbs switch
        {
            "bool" => "bool",
            "ubyte" or "uint8" => "u8",
            "byte" or "int8" => "i8",
            "ushort" or "uint16" => "u16",
            "short" or "int16" => "i16",
            "uint" or "uint32" => "u32",
            "int" or "int32" => "i32",
            "ulong" or "uint64" => "u64",
            "long" or "int64" => "i64",
            "float" => "f32",
            "double" => "f64",
            "string" => "string",
            _ => fbs
        };
    }
}
