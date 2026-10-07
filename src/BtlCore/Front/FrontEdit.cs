/*
 * FrontEdit.cs
 *
 * BtlFront 的唯一写入。注册表和以后的 Lua 都调用这里，不再各自改 F / V。
 *
 *   SetScalar(null)  删掉表上的槽（vtable 里没有这个字段）
 *   SetScalar(0)     留下槽，值是 0
 *   SetMember(null)  struct 成员写成 0。成员能不能缺，由节点上的 Layout 决定，不由 fbs 封死
 *   Enc=country_ai_bt 的向量拒绝 SetAt / Insert / RemoveAt / Fill，调用前不改树
 */
using BtlCore.Fb;

namespace BtlCore.Front
{
    public sealed class FrontEditException : InvalidOperationException
    {
        public FrontEditException(string message) : base(message) { }
    }

    public static class FrontEdit
    {
        /// <summary>写表上的标量。value 为 null 时删除槽。已有 BtlScalar 时就地改值，避免持有该节点的界面指向旧对象。</summary>
        public static void SetScalar(BtlTable tbl, int id, string type, object value)
        {
            if (tbl == null) return;
            if (value == null)
            {
                tbl.F.Remove(id);
                return;
            }
            if (tbl.F.TryGetValue(id, out var existing) && existing is BtlScalar sc)
            {
                if (!string.IsNullOrEmpty(type)) sc.T = type;
                sc.V = value;
                return;
            }
            tbl.F[id] = BtlFrontJson.Scalar(string.IsNullOrEmpty(type) ? "i64" : type, value);
        }

        /// <summary>挂上或替换表字段。node 为 null 时删除槽，表、向量、struct 都适用。</summary>
        public static void SetField(BtlTable tbl, int id, BtlNode node)
        {
            if (tbl == null) return;
            if (node == null) tbl.F.Remove(id);
            else tbl.F[id] = node;
        }

        /// <summary>没有这张子表就新建空表并挂上。</summary>
        public static BtlTable EnsureTable(BtlTable parent, int id)
        {
            if (parent == null) return null;
            if (parent.F.TryGetValue(id, out var n) && n is BtlTable t) return t;
            var created = BtlFrontJson.NewTable();
            SetField(parent, id, created);
            return created;
        }

        /// <summary>没有这个向量就新建。已有向量但没标元素类型时补上 elem。</summary>
        public static BtlVector EnsureVec(BtlTable parent, int id, string elem)
        {
            if (parent == null) return null;
            if (parent.F.TryGetValue(id, out var n) && n is BtlVector v)
            {
                if (string.IsNullOrEmpty(v.Elem)) v.Elem = elem;
                return v;
            }
            var created = BtlFrontJson.NewVector(elem);
            SetField(parent, id, created);
            return created;
        }

        /// <summary>写 struct 成员。null 写成 0。下标超过现有长度时用 0 补齐。</summary>
        public static void SetMember(BtlStruct st, int index, object value)
        {
            if (st == null || index < 0) return;
            while (st.V.Count <= index) st.V.Add(0);
            st.V[index] = value ?? 0;
        }

        /// <summary>已挂在树上的标量节点，只改值，不换节点。</summary>
        public static void SetHeldScalar(BtlScalar scalar, object value)
        {
            if (scalar == null || value == null) return;
            scalar.V = value;
        }

        public static void SetAt(BtlVector vec, int index, object value)
        {
            if (vec?.V == null) throw new FrontEditException("向量不存在");
            RefusePacked(vec);
            if (index < 0 || index >= vec.V.Count)
                throw new FrontEditException("下标越界");
            vec.V[index] = value;
        }

        public static void Insert(BtlVector vec, int index, object item)
        {
            if (vec?.V == null) throw new FrontEditException("向量不存在");
            if (item == null) throw new FrontEditException("不能插入空元素");
            RefusePacked(vec);
            if (index < 0 || index > vec.V.Count)
                throw new FrontEditException("下标越界");
            vec.V.Insert(index, item);
        }

        public static void RemoveAt(BtlVector vec, int index)
        {
            if (vec?.V == null) throw new FrontEditException("向量不存在");
            RefusePacked(vec);
            if (index < 0 || index >= vec.V.Count)
                throw new FrontEditException("下标越界");
            vec.V.RemoveAt(index);
        }

        /// <summary>用新序列换掉标量向量的全部元素。items 为 null 时清空。</summary>
        public static void Fill(BtlVector vec, IEnumerable<object> items)
        {
            if (vec?.V == null) throw new FrontEditException("向量不存在");
            RefusePacked(vec);
            vec.V.Clear();
            if (items == null) return;
            foreach (var x in items) vec.V.Add(x);
        }

        /// <summary>删掉任意下标的成员，后面的成员前移。有 Layout 时一并改掉。</summary>
        public static void RemoveStructTail(BtlStruct st, int index) => RemoveMember(st, index);

        public static BtlStruct StructFromFbs(string structName, params object[] values)
        {
            if (SoftSchema.TryStructMembers(structName, out var members))
            {
                var types = new string[members.Length];
                for (int i = 0; i < members.Length; i++) types[i] = members[i].T;
                var st = NewStruct(types);
                for (int i = 0; i < values.Length && i < st.V.Count; i++)
                {
                    if (values[i] != null) st.V[i] = values[i];
                }
                return st;
            }
            return BtlFrontJson.NewStruct(values);
        }

        /// <summary>按类型列表新建 struct，每个成员先填该类型的 0。</summary>
        public static BtlStruct NewStruct(IList<string> layout)
        {
            var st = BtlFrontJson.NewStruct();
            ApplyLayout(st, layout);
            return st;
        }

        /// <summary>没有这份 struct 就按 layout 建上。已有 struct 但还没有 Layout 时，用 layout 补上，不改已有的值。</summary>
        public static BtlStruct EnsureStruct(BtlTable parent, int id, IList<string> layout)
        {
            if (parent == null) return null;
            if (parent.F.TryGetValue(id, out var n) && n is BtlStruct st)
            {
                if (st.Layout.Count == 0) ApplyLayout(st, layout);
                return st;
            }
            var created = NewStruct(layout);
            SetField(parent, id, created);
            return created;
        }

        /// <summary>没有这条 struct 向量就建上，并记下整条向量共用的成员类型。</summary>
        public static BtlVector EnsureStructVector(BtlTable parent, int id, IList<string> layout)
        {
            var vec = EnsureVec(parent, id, "struct");
            if (vec == null) return null;
            if (vec.StructLayout.Count == 0 && layout != null)
            {
                foreach (var t in layout) vec.StructLayout.Add(ScalarType(t));
                foreach (var item in vec.V)
                {
                    if (item is BtlStruct st && st.Layout.Count == 0)
                        ApplyLayout(st, vec.StructLayout);
                }
            }
            return vec;
        }

        /// <summary>在下标处插入成员。shared 是包着它的 struct 向量时，整条向量一起插入，步长保持一致。</summary>
        public static void InsertMember(BtlStruct st, int index, string type, object value, BtlVector shared = null)
        {
            if (st == null) throw new FrontEditException("struct 不存在");
            if (shared != null && (shared.Elem == "struct" || shared.StructLayout.Count > 0))
            {
                AdoptVectorLayout(shared, st);
                if (index < 0 || index > shared.StructLayout.Count)
                    throw new FrontEditException("下标越界");
                string t = ScalarType(type);
                shared.StructLayout.Insert(index, t);
                foreach (var item in shared.V)
                {
                    if (item is not BtlStruct el) continue;
                    object v = ReferenceEquals(el, st) ? value : SoftSchema.DefaultValue(t);
                    InsertOne(el, index, t, v);
                }
                if (!shared.V.Contains(st)) InsertOne(st, index, t, value);
                return;
            }
            InsertOne(st, index, ScalarType(type), value);
        }

        /// <summary>删掉下标处的成员，后面的前移。shared 不为空时整条向量一起删。</summary>
        public static void RemoveMember(BtlStruct st, int index, BtlVector shared = null)
        {
            if (st?.V == null) throw new FrontEditException("struct 不存在");
            if (shared != null && (shared.Elem == "struct" || shared.StructLayout.Count > 0))
            {
                AdoptVectorLayout(shared, st);
                if (index < 0 || index >= shared.StructLayout.Count)
                    throw new FrontEditException("下标越界");
                shared.StructLayout.RemoveAt(index);
                foreach (var item in shared.V)
                {
                    if (item is BtlStruct el) RemoveOne(el, index);
                }
                if (!shared.V.Contains(st)) RemoveOne(st, index);
                return;
            }
            if (index < 0 || index >= st.V.Count)
                throw new FrontEditException("下标越界");
            RemoveOne(st, index);
        }

        /// <summary>只改下标处的类型，值不动。shared 不为空时整条向量的这一位一起改。</summary>
        public static void SetMemberType(BtlStruct st, int index, string type, BtlVector shared = null)
        {
            if (st == null) throw new FrontEditException("struct 不存在");
            string t = ScalarType(type);
            if (shared != null && (shared.Elem == "struct" || shared.StructLayout.Count > 0))
            {
                AdoptVectorLayout(shared, st);
                if (index < 0 || index >= shared.StructLayout.Count)
                    throw new FrontEditException("下标越界");
                shared.StructLayout[index] = t;
                foreach (var item in shared.V)
                {
                    if (item is BtlStruct el && index < el.Layout.Count)
                        el.Layout[index] = t;
                }
                if (index < st.Layout.Count) st.Layout[index] = t;
                return;
            }
            if (st.Layout.Count == 0 || index < 0 || index >= st.Layout.Count)
                throw new FrontEditException("下标越界");
            st.Layout[index] = t;
        }

        public static void SetVectorStructLayout(BtlVector vec, IList<string> layout)
        {
            if (vec == null) return;
            vec.Elem = "struct";
            if (vec.StructLayout.Count > 0 || layout == null) return;
            foreach (var t in layout) vec.StructLayout.Add(ScalarType(t));
        }

        public static object CoerceOrZero(string type) => BtlCore.Fb.SoftSchema.DefaultValue(type);

        public static string ScalarType(string type)
        {
            switch (type)
            {
                case "bool":
                case "u8":
                case "i8":
                case "u16":
                case "i16":
                case "u32":
                case "i32":
                case "u64":
                case "i64":
                case "f32":
                case "f64":
                    return type;
                default:
                    throw new FrontEditException("路径无法定位");
            }
        }

        static void ApplyLayout(BtlStruct st, IList<string> layout)
        {
            st.Layout.Clear();
            if (layout == null) return;
            foreach (var t in layout)
            {
                string name = ScalarType(t);
                st.Layout.Add(name);
                if (st.V.Count < st.Layout.Count)
                    st.V.Add(SoftSchema.DefaultValue(name));
            }
        }

        static void AdoptVectorLayout(BtlVector vec, BtlStruct sample)
        {
            if (vec.StructLayout.Count > 0 || sample?.Layout == null) return;
            vec.StructLayout.AddRange(sample.Layout);
        }

        static void InsertOne(BtlStruct st, int index, string type, object value)
        {
            if (st.Layout.Count == 0 && st.V.Count > 0)
                throw new FrontEditException("路径无法定位");
            if (index < 0 || index > st.Layout.Count)
                throw new FrontEditException("下标越界");
            st.Layout.Insert(index, type);
            st.V.Insert(index, value ?? SoftSchema.DefaultValue(type));
        }

        static void RemoveOne(BtlStruct st, int index)
        {
            if (index < st.Layout.Count) st.Layout.RemoveAt(index);
            if (index < st.V.Count) st.V.RemoveAt(index);
        }

        public static BtlStruct CloneStruct(BtlStruct st) =>
            st == null ? null : BtlFrontJson.Clone(st) as BtlStruct;

        public static BtlTable CloneTable(BtlTable tbl) =>
            tbl == null ? null : BtlFrontJson.Clone(tbl) as BtlTable;

        public static ushort? RemapCellIndex(int oldIdx, int oldW, int expandLeft, int expandUp, int newW, int newH)
        {
            if (oldIdx < 0 || oldW <= 0) return null;
            int x = oldIdx % oldW + expandLeft;
            int y = oldIdx / oldW + expandUp;
            if (x < 0 || y < 0 || x >= newW || y >= newH) return null;
            return (ushort)(y * newW + x);
        }

        static void RefusePacked(BtlVector vec)
        {
            if (PackedJsonStream.EncIsPackedJson(vec.Enc))
                throw new FrontEditException("这是国家行为树的打包数据，不能当作普通数组修改。");
        }
    }
}
