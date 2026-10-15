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

    /// <summary>旧调用的默认上下文入口。已创建文档持有自己的 Context。</summary>
    public static class SoftSchema
    {
        static readonly object Gate = new();
        static SchemaContext _default;
        public static SchemaContext DefaultContext
        {
            get { lock (Gate) return _default ??= new SchemaContext(TryLoad()); }
        }
        public static FbsSchema Schema => DefaultContext.Schema;
        public static string RootType => DefaultContext.RootType;
        public static void SetSchema(FbsSchema schema)
        {
            lock (Gate) _default = new SchemaContext(schema);
        }
        public static bool TryGet(string tableContext, int fieldId, out FieldHint hint) =>
            DefaultContext.TryGet(tableContext, fieldId, out hint);
        public static bool TryStructMembers(string structName, out StructMember[] members) =>
            DefaultContext.TryStructMembers(structName, out members);

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
