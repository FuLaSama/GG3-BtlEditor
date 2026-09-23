/*
 * FrontEdit.cs
 *
 * BtlFront 的唯一写入。注册表和以后的 Lua 都调用这里，不再各自改 F / V。
 *
 *   SetScalar(null)  删掉表上的槽（vtable 里没有这个字段）
 *   SetScalar(0)     留下槽，值是 0
 *   SetMember(null)  struct 成员写成 0，不能删掉中间成员
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

        /// <summary>只删除最后一个成员。删中间成员会把后面的字段错位。</summary>
        public static void RemoveStructTail(BtlStruct st, int index)
        {
            if (st?.V == null) throw new FrontEditException("struct 不存在");
            if (st.V.Count == 0 || index != st.V.Count - 1)
                throw new FrontEditException("只能删除 struct 尾部成员");
            st.V.RemoveAt(index);
        }

        public static BtlStruct StructFromFbs(string structName, params object[] values)
        {
            if (SoftSchema.TryStructMembers(structName, out var members))
            {
                var st = BtlFrontJson.NewStruct();
                for (int i = 0; i < members.Length; i++)
                {
                    object v = i < values.Length && values[i] != null
                        ? values[i]
                        : SoftSchema.DefaultValue(members[i].T);
                    st.V.Add(v);
                }
                return st;
            }
            return BtlFrontJson.NewStruct(values);
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
