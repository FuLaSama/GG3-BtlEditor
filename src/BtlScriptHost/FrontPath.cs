/*
 * FrontPath.cs
 *
 * 把 Root.stage_metadata.round_limit 或 # 字段号解析成表和 field id，再调用 FrontEdit。
 * 读不创建节点。写和 ensure 会补齐路径上缺的表、向量，与按钮里的 EnsureTable / EnsureVec 相同。
 */
using System.Globalization;
using BtlCore.Fb;
using BtlCore.Front;

namespace BtlCore.Scripting
{
    static class FrontPath
    {
        public static object GetScalar(BtlFrontDocument doc, string path)
        {
            var steps = Segments(path);
            var parent = Walk(doc.Root, SoftSchema.RootType, steps, steps.Length - 1, create: false);
            if (parent == null) return null;
            return ReadScalar(parent, steps[^1]);
        }

        public static object GetScalar(BtlTable table, string typeName, string path)
        {
            var steps = Segments(path, keepRoot: true);
            var parent = Walk(table, typeName, steps, steps.Length - 1, create: false);
            if (parent == null) return null;
            return ReadScalar(parent, steps[^1]);
        }

        public static void SetScalar(BtlFrontDocument doc, string path, object value)
        {
            var steps = Segments(path);
            var parent = Walk(doc.Root, SoftSchema.RootType, steps, steps.Length - 1, create: true);
            WriteScalar(parent, steps[^1], value);
        }

        public static void SetScalar(BtlTable table, string typeName, string path, object value)
        {
            var steps = Segments(path, keepRoot: true);
            var parent = Walk(table, typeName, steps, steps.Length - 1, create: true);
            WriteScalar(parent, steps[^1], value);
        }

        public static void Ensure(BtlFrontDocument doc, string path)
        {
            var steps = Segments(path);
            Walk(doc.Root, SoftSchema.RootType, steps, steps.Length, create: true);
        }

        public static void Ensure(BtlTable table, string typeName, string path)
        {
            if (table == null) throw new FrontEditException("路径无法定位");
            var steps = Segments(path, keepRoot: true);
            Walk(table, typeName, steps, steps.Length, create: true);
        }

        public static object GetMemberOnTable(BtlTable table, string typeName, string path)
        {
            if (table == null) return null;
            var steps = Segments(path, keepRoot: true);
            if (steps.Length < 2) return null;
            var parent = new string[steps.Length - 1];
            Array.Copy(steps, parent, parent.Length);
            var loc = Walk(table, typeName, parent, parent.Length, create: false);
            if (loc?.Node is not BtlStruct st) return null;
            return ReadStructMember(st, loc.TypeName, steps[^1]);
        }

        public static void SetMemberOnTable(BtlTable table, string typeName, string path, object value)
        {
            if (table == null) throw new FrontEditException("路径无法定位");
            var steps = Segments(path, keepRoot: true);
            if (steps.Length < 2) throw new FrontEditException("路径无法定位");
            var parent = new string[steps.Length - 1];
            Array.Copy(steps, parent, parent.Length);
            var loc = Walk(table, typeName, parent, parent.Length, create: true);
            if (loc?.Node is not BtlStruct st) throw new FrontEditException("路径无法定位");
            SetStructMember(st, loc.TypeName, steps[^1], value);
        }

        public static int Count(BtlFrontDocument doc, string path)
        {
            var vec = FindVector(doc, path, create: false);
            return vec?.Vector.V.Count ?? 0;
        }

        public static object At(BtlFrontDocument doc, string path, int index, out string elemType)
        {
            return At(doc, path, index, out elemType, out _);
        }

        public static object At(BtlFrontDocument doc, string path, int index, out string elemType, out BtlVector vector)
        {
            var vec = FindVector(doc, path, create: false) ?? throw Missing();
            elemType = vec.ElemType;
            vector = vec.Vector;
            if (index < 0 || index >= vec.Vector.V.Count)
                throw new FrontEditException("下标越界");
            return vec.Vector.V[index];
        }

        public static void SetAt(BtlFrontDocument doc, string path, int index, object value)
        {
            var found = FindVector(doc, path, create: false) ?? throw Missing();
            if (found.Hint.VectorElem == "table" || found.Hint.VectorElem == "struct")
                throw new FrontEditException("路径无法定位");
            FrontEdit.SetAt(found.Vector, index, Coerce(found.Hint.VectorElem, value));
        }

        public static void SetAtNode(BtlFrontDocument doc, string path, int index, BtlNode node)
        {
            if (node == null) throw new FrontEditException("不能插入空元素");
            var found = FindVector(doc, path, create: false) ?? throw Missing();
            FrontEdit.SetAt(found.Vector, index, node);
        }

        public static object AppendEmpty(BtlFrontDocument doc, string path)
        {
            var found = FindVector(doc, path, create: true) ?? throw Missing();
            object item = EmptyElement(found);
            FrontEdit.Insert(found.Vector, found.Vector.V.Count, item);
            return item;
        }

        public static bool TryGetVector(BtlFrontDocument doc, string path, bool create, out BtlVector vec)
        {
            var found = FindVector(doc, path, create);
            vec = found?.Vector;
            return vec != null;
        }

        public static BtlVector GetVector(BtlFrontDocument doc, string path, bool create)
        {
            return (FindVector(doc, path, create) ?? throw Missing()).Vector;
        }

        public static BtlVector InsertNode(BtlFrontDocument doc, string path, int index, BtlNode node)
        {
            if (node == null) throw new FrontEditException("不能插入空元素");
            var found = FindVector(doc, path, create: true) ?? throw Missing();
            FrontEdit.Insert(found.Vector, index, node);
            return found.Vector;
        }

        public static void SetMember(BtlFrontDocument doc, string path, object value)
        {
            var steps = Segments(path);
            if (steps.Length < 2) throw new FrontEditException("路径无法定位");
            var parent = new string[steps.Length - 1];
            Array.Copy(steps, parent, parent.Length);
            var loc = Walk(doc.Root, SoftSchema.RootType, parent, parent.Length, create: true);
            if (loc?.Node is not BtlStruct st) throw new FrontEditException("路径无法定位");
            SetStructMember(st, loc.TypeName, steps[^1], value);
        }

        public static object GetMember(BtlFrontDocument doc, string path)
        {
            var steps = Segments(path);
            if (steps.Length < 2) return null;
            var parent = new string[steps.Length - 1];
            Array.Copy(steps, parent, parent.Length);
            var loc = Walk(doc.Root, SoftSchema.RootType, parent, parent.Length, create: false);
            if (loc?.Node is not BtlStruct st) return null;
            return ReadStructMember(st, loc.TypeName, steps[^1]);
        }

        public static object ReadStructMember(BtlStruct st, string structName, string segment)
        {
            int index = StructMemberIndex(structName, segment);
            if (st == null || index >= st.V.Count) return 0;
            return st.V[index];
        }

        public static void SetStructMember(BtlStruct st, string structName, string segment, object value)
        {
            if (st == null) throw new FrontEditException("路径无法定位");
            int index = StructMemberIndex(structName, segment);
            if (!SoftSchema.TryStructMembers(structName, out var members) || index >= members.Length)
                throw new FrontEditException("路径无法定位");
            FrontEdit.SetMember(st, index, Coerce(members[index].T, value));
        }

        public static BtlTable AppendTable(BtlFrontDocument doc, string path)
        {
            var found = FindVector(doc, path, create: true) ?? throw Missing();
            if (found.Hint.VectorElem != "table")
                throw new FrontEditException("路径无法定位");
            var row = BtlFrontJson.NewTable();
            FrontEdit.Insert(found.Vector, found.Vector.V.Count, row);
            return row;
        }

        public static string ElementType(BtlFrontDocument doc, string path)
        {
            var found = FindVector(doc, path, create: false) ?? throw Missing();
            return found.ElemType;
        }

        public static void Fill(BtlFrontDocument doc, string path, IList<object> values)
        {
            var found = FindVector(doc, path, create: true) ?? throw Missing();
            Fill(found, values);
        }

        public static void Fill(BtlTable table, string typeName, string path, IList<object> values)
        {
            if (table == null) throw new FrontEditException("路径无法定位");
            var steps = Segments(path, keepRoot: true);
            var loc = Walk(table, typeName, steps, steps.Length, create: true);
            if (loc?.Node is not BtlVector vec || loc.Hint == null || loc.Hint.Kind != FieldKind.Vector)
                throw new FrontEditException("路径无法定位");
            Fill(new FoundVec { Vector = vec, Hint = loc.Hint, ElemType = loc.Hint.VectorElemTable ?? "" }, values);
        }

        static void Fill(FoundVec found, IList<object> values)
        {
            if (found.Hint.VectorElem == "table" || found.Hint.VectorElem == "struct")
                throw new FrontEditException("路径无法定位");
            var coerced = new List<object>();
            if (values != null)
            {
                foreach (var value in values)
                    coerced.Add(Coerce(found.Hint.VectorElem, value));
            }
            FrontEdit.Fill(found.Vector, coerced);
        }

        public static void Remove(BtlFrontDocument doc, string path, int index)
        {
            var found = FindVector(doc, path, create: false) ?? throw Missing();
            FrontEdit.RemoveAt(found.Vector, index);
        }

        sealed class FoundVec
        {
            public BtlVector Vector;
            public FieldHint Hint;
            public string ElemType;
        }

        sealed class Loc
        {
            public BtlNode Node;
            public string TypeName;
            public FieldHint Hint;
        }

        static FoundVec FindVector(BtlFrontDocument doc, string path, bool create)
        {
            var steps = Segments(path);
            var loc = Walk(doc.Root, SoftSchema.RootType, steps, steps.Length, create);
            if (loc?.Node is not BtlVector vec) return null;
            if (loc.Hint == null || loc.Hint.Kind != FieldKind.Vector)
                throw new FrontEditException("路径无法定位");
            return new FoundVec { Vector = vec, Hint = loc.Hint, ElemType = loc.Hint.VectorElemTable ?? "" };
        }

        static Loc Walk(BtlNode node, string typeName, string[] steps, int count, bool create)
        {
            FieldHint hint = null;
            for (int i = 0; i < count; i++)
            {
                if (node is not BtlTable tbl)
                    throw new FrontEditException("路径无法定位");
                int id = FieldId(typeName, steps[i]);
                if (!SoftSchema.TryGet(typeName, id, out hint))
                    throw new FrontEditException("路径无法定位");
                tbl.F.TryGetValue(id, out var child);
                if (child == null)
                {
                    if (!create) return null;
                    child = Create(hint) ?? throw new FrontEditException("路径无法定位");
                    FrontEdit.SetField(tbl, id, child);
                }
                node = (BtlNode)child;
                typeName = NextType(hint);
            }
            return new Loc { Node = node, TypeName = typeName, Hint = hint };
        }

        static BtlNode Create(FieldHint hint)
        {
            if (hint.Kind == FieldKind.Table)
                return BtlFrontJson.NewTable();
            if (hint.Kind == FieldKind.Vector)
                return BtlFrontJson.NewVector(hint.VectorElem ?? "u8");
            if (hint.Kind == FieldKind.Struct)
                return FrontEdit.StructFromFbs(hint.StructName);
            return null;
        }

        static string NextType(FieldHint hint)
        {
            if (hint.Kind == FieldKind.Table) return hint.ChildTable ?? "";
            if (hint.Kind == FieldKind.Struct) return hint.StructName ?? "";
            if (hint.Kind == FieldKind.Vector) return hint.VectorElemTable ?? "";
            return "";
        }

        static object ReadScalar(Loc parent, string segment)
        {
            if (parent?.Node is not BtlTable tbl) return null;
            int id = FieldId(parent.TypeName, segment);
            if (!tbl.F.TryGetValue(id, out var node) || node is not BtlScalar sc) return null;
            return sc.V;
        }

        static void WriteScalar(Loc parent, string segment, object value)
        {
            if (parent?.Node is not BtlTable tbl)
                throw new FrontEditException("路径无法定位");
            int id = FieldId(parent.TypeName, segment);
            if (!SoftSchema.TryGet(parent.TypeName, id, out var hint))
                throw new FrontEditException("路径无法定位");
            if (value == null)
            {
                FrontEdit.SetField(tbl, id, null);
                return;
            }
            string t = hint.Kind switch
            {
                FieldKind.Bool => "bool",
                FieldKind.String => "string",
                FieldKind.Scalar => hint.ScalarT,
                _ => throw new FrontEditException("路径无法定位")
            };
            FrontEdit.SetScalar(tbl, id, t, Coerce(t, value));
        }

        static int FieldId(string typeName, string segment)
        {
            if (segment.StartsWith('#'))
            {
                if (!int.TryParse(segment.AsSpan(1), NumberStyles.Integer, CultureInfo.InvariantCulture, out int id))
                    throw new FrontEditException("路径无法定位");
                return id;
            }
            var schema = SoftSchema.Schema ?? throw new FrontEditException("没有 battle.fbs");
            if (!schema.TryGetFieldByName(typeName, segment, out var field))
                throw new FrontEditException("路径无法定位");
            return field.Id;
        }

        public static object Coerce(string t, object value)
        {
            if (value == null) return null;
            if (t == "bool")
            {
                if (value is bool b) return b;
                if (value is double d) return d != 0;
                throw new FrontEditException("整数超出声明类型的范围");
            }
            if (t == "string")
            {
                if (value is string s) return s;
                throw new FrontEditException("路径无法定位");
            }
            double n = value switch
            {
                double d => d,
                float f => f,
                byte or sbyte or short or ushort or int or uint or long or ulong => Convert.ToDouble(value, CultureInfo.InvariantCulture),
                _ => throw new FrontEditException("整数超出声明类型的范围")
            };
            if (t == "f32") return (float)n;
            if (t == "f64") return n;
            if (Math.Abs(n - Math.Truncate(n)) > 1e-9)
                throw new FrontEditException("整数超出声明类型的范围");
            try
            {
                return t switch
                {
                    "u8" => checked((byte)n),
                    "i8" => checked((sbyte)n),
                    "u16" => checked((ushort)n),
                    "i16" => checked((short)n),
                    "u32" => checked((uint)n),
                    "i32" => checked((int)n),
                    "u64" => checked((ulong)n),
                    "i64" => checked((long)n),
                    _ => throw new FrontEditException("路径无法定位")
                };
            }
            catch (OverflowException)
            {
                throw new FrontEditException("整数超出声明类型的范围");
            }
        }

        static string[] Segments(string path, bool keepRoot = false)
        {
            if (string.IsNullOrWhiteSpace(path))
                throw new FrontEditException("路径无法定位");
            var parts = path.Split('.');
            int start = 0;
            if (!keepRoot && parts[0].Equals("Root", StringComparison.OrdinalIgnoreCase))
                start = 1;
            if (start >= parts.Length)
                throw new FrontEditException("路径无法定位");
            var list = new string[parts.Length - start];
            for (int i = start; i < parts.Length; i++)
            {
                if (string.IsNullOrWhiteSpace(parts[i]))
                    throw new FrontEditException("路径无法定位");
                list[i - start] = parts[i].Trim();
            }
            return list;
        }

        static object EmptyElement(FoundVec found)
        {
            string elem = found.Hint.VectorElem;
            if (elem == "table") return BtlFrontJson.NewTable();
            if (elem == "struct")
            {
                string name = found.Hint.StructName ?? found.Hint.VectorElemTable;
                if (string.IsNullOrEmpty(name) || !SoftSchema.TryStructMembers(name, out _))
                    throw new FrontEditException("路径无法定位");
                return FrontEdit.StructFromFbs(name);
            }
            if (string.IsNullOrEmpty(elem))
                throw new FrontEditException("路径无法定位");
            return SoftSchema.DefaultValue(elem);
        }

        static int StructMemberIndex(string structName, string segment)
        {
            var schema = SoftSchema.Schema ?? throw new FrontEditException("路径无法定位");
            if (segment.StartsWith('#'))
            {
                if (!int.TryParse(segment.AsSpan(1), NumberStyles.Integer, CultureInfo.InvariantCulture, out int id))
                    throw new FrontEditException("路径无法定位");
                var fields = schema.StructMembers(structName);
                if (id < 0 || id >= fields.Count) throw new FrontEditException("路径无法定位");
                return id;
            }
            if (!schema.TryGetFieldByName(structName, segment, out var field))
                throw new FrontEditException("路径无法定位");
            return field.Id;
        }

        static FrontEditException Missing() => new FrontEditException("路径无法定位");
    }
}
