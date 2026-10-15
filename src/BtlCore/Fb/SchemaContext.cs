using System.Globalization;

namespace BtlCore.Fb
{
    /// <summary>固定的一份格式解释；提示缓存属于本实例，不受默认 schema 切换影响。</summary>
    public sealed class SchemaContext
    {
        readonly object _gate = new();
        readonly Dictionary<string, FieldHint> _hints = new(StringComparer.Ordinal);
        public FbsSchema Schema { get; }
        public string RootType => Schema?.RootType ?? "Root";
        public string Fingerprint => Schema?.ContentHash;
        /// <summary>脚本覆盖前的解释；重新加载扩展时从该基础构造，避免重复叠加。</summary>
        public SchemaContext BaseContext { get; private init; }

        public SchemaContext(FbsSchema schema) => Schema = schema;

        public SchemaContext WithOverlays(IEnumerable<string> definitions)
        {
            var basis = BaseContext ?? this;
            var patches = definitions.ToArray();
            return patches.Length == 0 ? basis : new SchemaContext(
                (basis.Schema ?? FbsSchema.LoadFromText("table Root {} root_type Root;")).WithOverlays(patches))
                { BaseContext = basis };
        }

        public bool TryGet(string tableContext, int fieldId, out FieldHint hint)
        {
            hint = null;
            if (Schema == null || string.IsNullOrEmpty(tableContext)) return false;
            string key = tableContext + "#" + fieldId.ToString(CultureInfo.InvariantCulture);
            lock (_gate)
            {
                if (!_hints.TryGetValue(key, out hint))
                {
                    hint = Schema.TryGetField(tableContext, fieldId, out var field) ? FromFbs(Schema, field) : null;
                    _hints[key] = hint;
                }
                return hint != null;
            }
        }

        public bool TryStructMembers(string structName, out StructMember[] members)
        {
            members = null;
            if (Schema == null || string.IsNullOrEmpty(structName) || !Schema.IsStruct(structName)) return false;
            members = ToMembers(Schema, structName);
            return members.Length > 0;
        }

        /// <summary>fbs 类型串 → FieldHint。bool 向量按 u8 存（FB 里 bool 向量就是字节）。</summary>
        static FieldHint FromFbs(FbsSchema schema, FbsField field)
        {
            string t = field.Type;
            if (string.IsNullOrEmpty(t)) return null;

            if (FbsSchema.IsVectorType(t))
            {
                string elem = schema.ResolveScalarType(schema.ResolveChildType(t));
                if (FbsSchema.IsStringType(elem))
                    return new FieldHint { Kind = FieldKind.Vector, VectorElem = "string" };
                if (FbsSchema.IsScalarType(elem))
                {
                    string st = SoftSchema.ToFrontScalar(elem);
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

            t = schema.ResolveScalarType(t);

            if (FbsSchema.IsScalarType(t))
            {
                string st = SoftSchema.ToFrontScalar(t);
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
                list.Add(new StructMember { T = SoftSchema.ToFrontScalar(schema.ResolveScalarType(f.Type)) });
            return list.ToArray();
        }

    }
}
