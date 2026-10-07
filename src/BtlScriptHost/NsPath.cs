/*
 * NsPath.cs
 *
 * /号 是表字段，[下标] 是向量元素，.号 是 struct 成员。
 * 下一步的记号决定这一步要建表、向量还是 struct。get 不创建。
 */
using System.Globalization;
using System.Text;
using BtlCore.Front;

namespace BtlCore.Scripting
{
    static class NsPath
    {
        enum Kind { Field, Index, Member }

        readonly struct Step
        {
            public readonly Kind KindOf;
            public readonly int N;
            public Step(Kind kind, int n) { KindOf = kind; N = n; }
        }

        sealed class Leaf
        {
            public BtlTable Table;
            public int Field = -1;
            public BtlVector Vector;
            public int Index = -1;
            public BtlStruct Struct;
            public int Member = -1;
        }

        public static bool IsNs(string path) =>
            !string.IsNullOrEmpty(path) && path[0] == '/';

        public static object Get(BtlFrontDocument doc, string path)
        {
            var leaf = Walk(doc, path, create: false, scalarType: null, out _);
            if (leaf == null) return null;
            if (leaf.Struct != null)
            {
                if (leaf.Member < 0 || leaf.Member >= leaf.Struct.V.Count) return null;
                return leaf.Struct.V[leaf.Member];
            }
            if (leaf.Vector != null)
            {
                if (leaf.Index < 0 || leaf.Index >= leaf.Vector.V.Count) return null;
                return leaf.Vector.V[leaf.Index];
            }
            if (leaf.Table == null || !leaf.Table.F.TryGetValue(leaf.Field, out var node) || node is not BtlScalar sc)
                return null;
            return sc.V;
        }

        /// <summary>get 的显示用读法。缺槽返回 null。presence 为真时，表或结构存在返回 true。</summary>
        public static object Look(BtlFrontDocument doc, string path, bool presence)
        {
            Leaf leaf;
            try { leaf = Walk(doc, path, create: false, scalarType: null, out _); }
            catch (FrontEditException) { return null; }
            if (leaf == null) return null;
            if (leaf.Struct != null)
            {
                if (leaf.Member < 0) return presence ? (object)true : null;
                if (leaf.Member >= leaf.Struct.V.Count) return null;
                return leaf.Struct.V[leaf.Member];
            }
            if (leaf.Vector != null)
            {
                if (leaf.Index < 0) return presence ? (object)true : null;
                if (leaf.Index >= leaf.Vector.V.Count) return null;
                var item = leaf.Vector.V[leaf.Index];
                if (item is BtlNode) return presence ? (object)true : null;
                return item;
            }
            if (leaf.Table == null || leaf.Field < 0) return null;
            if (!leaf.Table.F.TryGetValue(leaf.Field, out var node) || node == null) return null;
            if (node is BtlScalar sc) return sc.V;
            return presence ? (object)true : null;
        }

        public static void Set(BtlFrontDocument doc, string path, string type, object value)
        {
            type = FrontEdit.ScalarType(type);
            var leaf = Walk(doc, path, create: true, scalarType: type, out string trace);
            if (leaf.Struct != null)
            {
                SetStructMember(leaf.Struct, leaf.Member, type, value);
                return;
            }
            if (leaf.Vector != null)
            {
                if (string.IsNullOrEmpty(leaf.Vector.Elem)) leaf.Vector.Elem = type;
                object written = value == null ? FrontEdit.CoerceOrZero(type) : FrontPath.Coerce(type, value);
                Grow(leaf.Vector, leaf.Index, type);
                FrontEdit.SetAt(leaf.Vector, leaf.Index, written);
                return;
            }
            if (value == null)
            {
                FrontEdit.SetScalar(leaf.Table, leaf.Field, type, null);
                return;
            }
            FrontEdit.SetScalar(leaf.Table, leaf.Field, type, FrontPath.Coerce(type, value));
            _ = trace;
        }

        public static void Ensure(BtlFrontDocument doc, string path, string elem, IList<string> layout)
        {
            if (layout != null && layout.Count > 0 && string.IsNullOrEmpty(elem))
            {
                var leaf = Walk(doc, path, create: true, scalarType: null, out _, forceStruct: true);
                if (leaf.Struct == null) throw new FrontEditException(leaf.Table == null ? "路径无法定位" : "路径无法定位");
                if (leaf.Field >= 0 && leaf.Table != null && leaf.Struct != null && leaf.Member < 0)
                {
                    if (leaf.Struct.Layout.Count == 0)
                        ReplaceStruct(leaf.Table, leaf.Field, layout, leaf.Struct);
                    return;
                }
                return;
            }
            if (elem == "struct")
            {
                var leaf = Walk(doc, path, create: true, scalarType: "struct", out _, asVector: true);
                if (leaf.Vector == null) throw new FrontEditException("路径无法定位");
                FrontEdit.SetVectorStructLayout(leaf.Vector, layout);
                return;
            }
            if (!string.IsNullOrEmpty(elem))
            {
                var leaf = Walk(doc, path, create: true, scalarType: elem == "table" || elem == "string" ? elem : FrontEdit.ScalarType(elem), out _, asVector: true);
                if (leaf.Vector == null) throw new FrontEditException("路径无法定位");
                return;
            }
            Walk(doc, path, create: true, scalarType: null, out _, asTables: true);
        }

        public static int Count(BtlFrontDocument doc, string path)
        {
            var leaf = Walk(doc, path, create: false, scalarType: null, out _, asVector: true);
            if (leaf?.Vector == null) return 0;
            return leaf.Vector.V.Count;
        }

        public static void Remove(BtlFrontDocument doc, string path, int? index)
        {
            if (index == null)
            {
                var leaf = Walk(doc, path, create: false, scalarType: null, out string trace);
                if (leaf?.Vector == null || leaf.Index < 0)
                    throw new FrontEditException(string.IsNullOrEmpty(trace) ? "路径无法定位" : trace);
                FrontEdit.RemoveAt(leaf.Vector, leaf.Index);
                return;
            }
            var vec = Walk(doc, path, create: false, scalarType: null, out _, asVector: true);
            if (vec?.Vector == null) throw new FrontEditException("路径无法定位");
            FrontEdit.RemoveAt(vec.Vector, index.Value);
        }

        public static int Append(BtlFrontDocument doc, string path, BtlNode node)
        {
            var leaf = Walk(doc, path, create: true, scalarType: node == null ? "table" : null, out _, asVector: true);
            if (leaf.Vector == null) throw new FrontEditException("路径无法定位");
            object item = node ?? Empty(leaf.Vector);
            int at = leaf.Vector.V.Count;
            FrontEdit.Insert(leaf.Vector, at, item);
            return at;
        }

        public static void Fill(BtlFrontDocument doc, string path, string type, IList<object> values)
        {
            type = FrontEdit.ScalarType(type);
            var leaf = Walk(doc, path, create: true, scalarType: type, out _, asVector: true);
            if (leaf.Vector == null) throw new FrontEditException("路径无法定位");
            var list = new List<object>();
            if (values != null)
            {
                foreach (var v in values)
                    list.Add(v == null ? FrontEdit.CoerceOrZero(type) : FrontPath.Coerce(type, v));
            }
            FrontEdit.Fill(leaf.Vector, list);
        }

        public static void Insert(BtlFrontDocument doc, string path, int index, BtlNode node)
        {
            var leaf = Walk(doc, path, create: true, scalarType: "table", out _, asVector: true);
            if (leaf.Vector == null || node == null) throw new FrontEditException("路径无法定位");
            FrontEdit.Insert(leaf.Vector, index, node);
        }

        public static void Move(BtlFrontDocument doc, string path, int from, int to)
        {
            var leaf = Walk(doc, path, create: false, scalarType: null, out _, asVector: true);
            if (leaf?.Vector == null) throw new FrontEditException("路径无法定位");
            var vec = leaf.Vector;
            if (from < 0 || from >= vec.V.Count) throw new FrontEditException("下标越界");
            var item = vec.V[from];
            FrontEdit.RemoveAt(vec, from);
            if (to < 0) to = 0;
            if (to > vec.V.Count) to = vec.V.Count;
            FrontEdit.Insert(vec, to, item);
        }

        public static BtlNode NodeAt(BtlFrontDocument doc, string path)
        {
            var leaf = Walk(doc, path, create: false, scalarType: null, out _);
            if (leaf == null) return null;
            if (leaf.Struct != null && leaf.Member < 0) return leaf.Struct;
            if (leaf.Table != null && leaf.Field >= 0 && leaf.Table.F.TryGetValue(leaf.Field, out var node))
                return node;
            if (leaf.Vector != null && leaf.Index >= 0 && leaf.Index < leaf.Vector.V.Count && leaf.Vector.V[leaf.Index] is BtlNode n)
                return n;
            return null;
        }

        static void SetStructMember(BtlStruct st, int index, string type, object value)
        {
            if (index < 0) throw new FrontEditException("下标越界");
            object written = value == null ? SoftDefault(type) : FrontPath.Coerce(type, value);
            if (index > st.Layout.Count || (st.Layout.Count == 0 && index > 0))
                throw new FrontEditException("前面的成员还没有类型");
            if (index == st.Layout.Count)
            {
                st.Layout.Add(type);
                st.V.Add(written);
                return;
            }
            st.Layout[index] = type;
            while (st.V.Count <= index) st.V.Add(0);
            st.V[index] = written;
        }

        static object SoftDefault(string type)
        {
            try { return BtlCore.Fb.SoftSchema.DefaultValue(type); }
            catch { return 0; }
        }

        static void ReplaceStruct(BtlTable parent, int field, IList<string> layout, BtlStruct existing)
        {
            var created = FrontEdit.NewStruct(layout);
            for (int i = 0; i < existing.V.Count && i < created.V.Count; i++)
                created.V[i] = existing.V[i];
            FrontEdit.SetField(parent, field, created);
        }

        static object Empty(BtlVector vec)
        {
            if (vec.Elem == "table") return BtlFrontJson.NewTable();
            if (vec.Elem == "struct") return FrontEdit.NewStruct(vec.StructLayout);
            return BtlCore.Fb.SoftSchema.DefaultValue(string.IsNullOrEmpty(vec.Elem) ? "u16" : vec.Elem);
        }

        static void Grow(BtlVector vec, int index, string scalarType)
        {
            if (index < 0) throw new FrontEditException("下标越界");
            while (vec.V.Count <= index)
                FrontEdit.Insert(vec, vec.V.Count, Empty(scalarType, vec));
        }

        static object Empty(string elem, BtlVector vec)
        {
            if (elem == "table" || vec.Elem == "table") return BtlFrontJson.NewTable();
            if (elem == "struct" || vec.Elem == "struct") return FrontEdit.NewStruct(vec.StructLayout);
            string t = string.IsNullOrEmpty(vec.Elem) ? elem : vec.Elem;
            return BtlCore.Fb.SoftSchema.DefaultValue(string.IsNullOrEmpty(t) ? "u16" : t);
        }

        static Leaf Walk(BtlFrontDocument doc, string path, bool create, string scalarType, out string trace,
            bool asVector = false, bool asTables = false, bool forceStruct = false)
        {
            trace = "";
            if (doc?.Root == null) throw new FrontEditException("路径无法定位");
            var steps = Parse(path);
            object cur = doc.Root;
            var names = create ? new List<string>() : null;
            for (int i = 0; i < steps.Count; i++)
            {
                var s = steps[i];
                bool last = i == steps.Count - 1;
                Step? next = last ? null : steps[i + 1];
                Step? after = i + 2 < steps.Count ? steps[i + 2] : null;
                if (s.KindOf == Kind.Field)
                {
                    if (cur is not BtlTable tbl) throw Fail(names, "这里不是表");
                    if (last && asTables)
                    {
                        names?.Add("表 " + s.N);
                        ChildTable(tbl, s.N, create);
                        trace = names == null ? "" : string.Join(" → ", names);
                        return new Leaf { Table = tbl, Field = s.N };
                    }
                    if (last && forceStruct)
                    {
                        names?.Add("结构 " + s.N);
                        var st = ChildStruct(tbl, s.N, create);
                        trace = names == null ? "" : string.Join(" → ", names);
                        return new Leaf { Table = tbl, Field = s.N, Struct = st };
                    }
                    if (last && asVector)
                    {
                        names?.Add("向量 " + s.N);
                        var vec = ChildVector(tbl, s.N, create, scalarType ?? "table");
                        trace = names == null ? "" : string.Join(" → ", names);
                        return new Leaf { Vector = vec };
                    }
                    if (last)
                    {
                        names?.Add("字段 " + s.N);
                        trace = names == null ? "" : string.Join(" → ", names);
                        return new Leaf { Table = tbl, Field = s.N };
                    }
                    if (next?.KindOf == Kind.Index)
                    {
                        names?.Add("向量 " + s.N);
                        string elem = ElemKind(after, scalarType);
                        cur = ChildVector(tbl, s.N, create, elem);
                    }
                    else if (next?.KindOf == Kind.Member)
                    {
                        names?.Add("结构 " + s.N);
                        cur = ChildStruct(tbl, s.N, create);
                    }
                    else
                    {
                        names?.Add("表 " + s.N);
                        cur = ChildTable(tbl, s.N, create);
                    }
                }
                else if (s.KindOf == Kind.Index)
                {
                    if (cur is not BtlVector vec) throw Fail(names, "这里不是向量");
                    names?.Add("元素 " + s.N);
                    if (s.N < 0) throw Fail(names, "下标越界");
                    if (!create && s.N >= vec.V.Count)
                    {
                        trace = names == null ? "" : string.Join(" → ", names);
                        return null;
                    }
                    if (last)
                    {
                        if (create) Grow(vec, s.N, scalarType ?? vec.Elem);
                        trace = names == null ? "" : string.Join(" → ", names);
                        return new Leaf { Vector = vec, Index = s.N };
                    }
                    if (create) Grow(vec, s.N, next?.KindOf == Kind.Member ? "struct" : "table");
                    if (s.N >= vec.V.Count) throw Fail(names, "下标越界");
                    if (next?.KindOf == Kind.Member)
                        cur = AsStruct(vec, s.N, create);
                    else if (next?.KindOf == Kind.Field)
                        cur = AsTable(vec, s.N, create);
                    else
                        throw Fail(names, "路径无法定位");
                }
                else
                {
                    if (!last) throw Fail(names, "路径无法定位");
                    if (cur is not BtlStruct st) throw Fail(names, "这里不是结构");
                    names?.Add("成员 " + s.N);
                    trace = names == null ? "" : string.Join(" → ", names);
                    return new Leaf { Struct = st, Member = s.N };
                }
                if (cur == null)
                {
                    trace = names == null ? "" : string.Join(" → ", names);
                    return null;
                }
            }
            throw new FrontEditException("路径无法定位");
        }

        static string ElemKind(Step? afterIndex, string scalarType)
        {
            if (afterIndex == null) return string.IsNullOrEmpty(scalarType) ? "u16" : scalarType;
            if (afterIndex.Value.KindOf == Kind.Field) return "table";
            if (afterIndex.Value.KindOf == Kind.Member) return "struct";
            throw new FrontEditException("路径无法定位");
        }

        static BtlTable ChildTable(BtlTable parent, int id, bool create)
        {
            if (parent.F.TryGetValue(id, out var node) && node is BtlTable t) return t;
            if (!create) return null;
            return FrontEdit.EnsureTable(parent, id);
        }

        static BtlVector ChildVector(BtlTable parent, int id, bool create, string elem)
        {
            if (parent.F.TryGetValue(id, out var node) && node is BtlVector v)
            {
                if (string.IsNullOrEmpty(v.Elem) && !string.IsNullOrEmpty(elem)) v.Elem = elem;
                return v;
            }
            if (!create) return null;
            if (elem == "struct") return FrontEdit.EnsureStructVector(parent, id, null);
            return FrontEdit.EnsureVec(parent, id, string.IsNullOrEmpty(elem) ? "table" : elem);
        }

        static BtlStruct ChildStruct(BtlTable parent, int id, bool create)
        {
            if (parent.F.TryGetValue(id, out var node) && node is BtlStruct st) return st;
            if (!create) return null;
            var created = BtlFrontJson.NewStruct();
            FrontEdit.SetField(parent, id, created);
            return created;
        }

        static BtlTable AsTable(BtlVector vec, int index, bool create)
        {
            if (vec.V[index] is BtlTable t) return t;
            if (!create) return null;
            var created = BtlFrontJson.NewTable();
            vec.V[index] = created;
            return created;
        }

        static BtlStruct AsStruct(BtlVector vec, int index, bool create)
        {
            if (vec.V[index] is BtlStruct st) return st;
            if (!create) return null;
            var created = FrontEdit.NewStruct(vec.StructLayout);
            vec.V[index] = created;
            return created;
        }

        static readonly System.Collections.Concurrent.ConcurrentDictionary<string, List<Step>> ParsedPaths = new(StringComparer.Ordinal);

        static List<Step> Parse(string path)
        {
            if (path != null && ParsedPaths.TryGetValue(path, out var cached)) return cached;
            var parsed = ParseUncached(path);
            // 常用相对字段路径数量很少；不缓存带行号的路径，避免大文件撑满缓存。
            if (path.Length <= 64 && !path.Contains('[') && ParsedPaths.Count < 1024) ParsedPaths.TryAdd(path, parsed);
            return parsed;
        }

        static List<Step> ParseUncached(string path)
        {
            if (!IsNs(path)) throw new FrontEditException("路径无法定位");
            var list = new List<Step>();
            int i = 0;
            while (i < path.Length)
            {
                char c = path[i];
                if (c == '/')
                {
                    i++;
                    list.Add(new Step(Kind.Field, ReadInt(path, ref i)));
                }
                else if (c == '[')
                {
                    i++;
                    int n = ReadInt(path, ref i);
                    if (i >= path.Length || path[i] != ']') throw new FrontEditException("路径无法定位");
                    i++;
                    list.Add(new Step(Kind.Index, n));
                }
                else if (c == '.')
                {
                    i++;
                    list.Add(new Step(Kind.Member, ReadInt(path, ref i)));
                }
                else throw new FrontEditException("路径无法定位");
            }
            if (list.Count == 0 || list[0].KindOf != Kind.Field)
                throw new FrontEditException("路径无法定位");
            return list;
        }

        static int ReadInt(string path, ref int i)
        {
            int start = i;
            while (i < path.Length && path[i] >= '0' && path[i] <= '9') i++;
            if (start == i) throw new FrontEditException("路径无法定位");
            return int.Parse(path.Substring(start, i - start), CultureInfo.InvariantCulture);
        }

        static FrontEditException Fail(List<string> names, string why)
        {
            if (names == null || names.Count == 0) return new FrontEditException(why);
            return new FrontEditException(string.Join(" → ", names) + " → " + why);
        }
    }
}
