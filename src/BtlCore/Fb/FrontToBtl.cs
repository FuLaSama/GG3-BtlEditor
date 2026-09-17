/*
 * FrontToBtl.cs
 *
 * 把内存里的 BtlFront 按 Google FlatBuffers 规则写成新的 .btl。
 * 不保留打开时的文件偏移、不对齐去「凑」原字节：能对上是因为算法和游戏一致。
 *
 * 写出顺序（影响文件里对象的前后位置，游戏验过能读）：
 *   Root 子对象创建：2,3,1,4,5,6,7,9,8,10（先创建的靠近文件尾）
 *   Root 槽 Add：0..7, 然后 9, 再 8, 再 10…
 *   其它表：创建按 id 升序，Add 按 id 降序（和 flatc 生成的 CreateXxx 一样）
 *   向量元素倒序 Push
 *
 * 国家行为树：向量 Enc=country_ai_bt 时走 PackedJsonStream，得到的是 u8 向量。
 * struct 布局来自 battle.fbs；没有布局时只好按 u8 堆，并记 Info。
 * 真正读到的空表 {}（vtable 合法、没有字段）仍要写出去；只有读入时跳过的零填充槽才不会出现在 Front 里。
 */
using System.Globalization;
using BtlCore.Front;

namespace BtlCore.Fb
{
    public sealed class FrontToBtlResult
    {
        public byte[] Bytes { get; set; }
        public List<ConvertIssue> Issues { get; } = new List<ConvertIssue>();
        public bool Ok => Bytes != null && Bytes.Length >= 8
                          && !Issues.Any(i => i.Severity == IssueSeverity.Error);
    }

    public static class FrontToBtl
    {
        /// <summary>编一份全新缓冲区。失败时 Bytes 可能为空数组，Issues 里有 Error。</summary>
        public static FrontToBtlResult Build(BtlFrontDocument doc)
        {
            var result = new FrontToBtlResult();
            if (doc?.Root == null)
            {
                result.Issues.Add(new ConvertIssue
                {
                    Severity = IssueSeverity.Error,
                    Path = "/",
                    Message = "缺少 Root 表"
                });
                result.Bytes = Array.Empty<byte>();
                return result;
            }

            try
            {
                var w = new Writer(result.Issues);
                result.Bytes = w.Build(doc.Root);
            }
            catch (Exception ex)
            {
                result.Issues.Add(new ConvertIssue
                {
                    Severity = IssueSeverity.Error,
                    Path = "/",
                    Message = "编回失败: " + ex.Message
                });
                result.Bytes = Array.Empty<byte>();
            }

            return result;
        }

        /// <summary>成功才落盘；有 Error 抛 InvalidDataException，避免写出半截文件。</summary>
        public static void ToFile(BtlFrontDocument doc, string path)
        {
            var r = Build(doc);
            if (!r.Ok)
            {
                string preview = string.Join("; ", r.Issues.Where(i => i.Severity == IssueSeverity.Error).Take(5));
                throw new InvalidDataException("Front→BTL 失败: " + preview);
            }
            File.WriteAllBytes(path, r.Bytes);
        }

        sealed class Writer
        {
            readonly FbBuilder _b = new FbBuilder(4096);
            readonly List<ConvertIssue> _issues;

            public Writer(List<ConvertIssue> issues) => _issues = issues;

            public byte[] Build(BtlTable root)
            {
                int off = WriteTable(root, SoftSchema.RootType, "/");
                _b.Finish(off);
                return _b.SizedByteArray();
            }

            void Warn(string path, string msg) =>
                _issues.Add(new ConvertIssue { Severity = IssueSeverity.Warning, Path = path, Message = msg });

            void Info(string path, string msg) =>
                _issues.Add(new ConvertIssue { Severity = IssueSeverity.Info, Path = path, Message = msg });

            static int SizeOf(string t) => t switch
            {
                "bool" or "u8" or "i8" => 1,
                "u16" or "i16" => 2,
                "u32" or "i32" or "f32" => 4,
                "u64" or "i64" or "f64" => 8,
                _ => 4
            };

            /// <summary>
            /// 先按 createOrder 写出子表/字符串/向量，再 StartTable，再按 addOrder Slot。
            /// context 是当前表在 fbs 里的类型名，用来查 hint 和 Root 的特殊顺序。
            /// </summary>
            int WriteTable(BtlTable table, string context, string path)
            {
                table ??= BtlFrontJson.NewTable();

                int maxId = -1;
                foreach (var id in table.F.Keys)
                    if (id > maxId) maxId = id;

                int numSlots = maxId + 1;
                if (numSlots < 0) numSlots = 0;
                if (numSlots > 512)
                {
                    Warn(path, $"字段过多 ({numSlots})，截断到 512");
                    numSlots = 512;
                }

                var fields = new Dictionary<int, BtlNode>(table.F);
                var childOff = new Dictionary<int, int>();
                int[] createOrder = ChildCreateOrder(context, numSlots);
                int[] addOrder = FieldAddOrder(context, numSlots);

                foreach (int fid in createOrder)
                {
                    if (!fields.TryGetValue(fid, out var node) || node == null)
                        continue;

                    SoftSchema.TryGet(context, fid, out FieldHint hint);
                    string fp = path == "/" ? "/" + fid : path + "/" + fid;
                    node = CoerceNode(node, hint, fp);
                    if (node == null)
                    {
                        fields.Remove(fid);
                        continue;
                    }
                    fields[fid] = node;

                    if (node is BtlTable childTbl)
                    {
                        string childCtx = hint?.ChildTable ?? "";
                        childOff[fid] = WriteTable(childTbl, childCtx, fp);
                    }
                    else if (node is BtlScalar sc && string.Equals(sc.T, "string", StringComparison.OrdinalIgnoreCase))
                    {
                        childOff[fid] = _b.CreateString(sc.V?.ToString() ?? "");
                    }
                    else if (node is BtlVector vec)
                    {
                        childOff[fid] = WriteVector(vec, hint, context, fid, fp);
                    }
                }

                _b.StartTable(numSlots);

                foreach (int fid in addOrder)
                {
                    if (!fields.TryGetValue(fid, out var node) || node == null)
                        continue;

                    SoftSchema.TryGet(context, fid, out FieldHint hint);
                    string fp = path == "/" ? "/" + fid : path + "/" + fid;

                    if (childOff.TryGetValue(fid, out int abs))
                    {
                        _b.AddOffset(abs);
                        _b.Slot(fid);
                        continue;
                    }

                    if (node is BtlScalar sc)
                    {
                        string t = sc.T ?? hint?.ScalarT ?? "u16";
                        PutScalar(t, sc.V, fp);
                        _b.Slot(fid);
                        continue;
                    }

                    if (node is BtlStruct st)
                    {
                        PutStruct(st, hint?.StructMembers, fp);
                        _b.Slot(fid);
                        continue;
                    }

                    Warn(fp, $"无法编回的节点类型 {node.T}，跳过");
                }

                return _b.EndTable();
            }

            /// <summary>
            /// 子对象创建顺序（先创建的靠近文件尾）。战役 Root 实测为
            /// metadata → battle → map → faction → …，且 9 先于 8。
            /// 其它表按字段 id 升序（C++ CreateX 实参从左到右）。
            /// </summary>
            static int[] ChildCreateOrder(string context, int numSlots)
            {
                if (string.Equals(context, "Root", StringComparison.Ordinal))
                    return RootChildCreateOrder(numSlots);
                var ids = new int[numSlots];
                for (int i = 0; i < numSlots; i++) ids[i] = i;
                return ids;
            }

            /// <summary>
            /// StartTable 之后的 Add 顺序：子表高 id 先 Add（flatc Create）；
            /// Root 实测为 id 升序，但 9 先于 8。
            /// </summary>
            static int[] FieldAddOrder(string context, int numSlots)
            {
                if (string.Equals(context, "Root", StringComparison.Ordinal))
                    return RootFieldAddOrder(numSlots);
                var ids = new int[numSlots];
                for (int i = 0; i < numSlots; i++) ids[i] = numSlots - 1 - i;
                return ids;
            }

            /// <summary>战役原版 Root 子对象地址从高到低：2,3,1,4,5,6,7,9,8,10。</summary>
            static int[] RootChildCreateOrder(int numSlots)
            {
                int[] preferred = { 2, 3, 1, 4, 5, 6, 7, 9, 8, 10 };
                return MergeIdOrder(preferred, numSlots);
            }

            /// <summary>Root 对象槽位写入顺序：0..7,9,8,10…（vtable 偏移与原版一致）。</summary>
            static int[] RootFieldAddOrder(int numSlots)
            {
                var ids = new List<int>(numSlots);
                for (int i = 0; i < numSlots; i++)
                {
                    if (i == 8) continue;
                    ids.Add(i);
                    if (i == 9 && 8 < numSlots) ids.Add(8);
                }
                return ids.ToArray();
            }

            static int[] MergeIdOrder(int[] preferred, int numSlots)
            {
                var ids = new List<int>(numSlots);
                var seen = new bool[Math.Max(numSlots, 1)];
                foreach (int id in preferred)
                {
                    if (id >= 0 && id < numSlots)
                    {
                        ids.Add(id);
                        seen[id] = true;
                    }
                }
                for (int i = 0; i < numSlots; i++)
                    if (!seen[i]) ids.Add(i);
                return ids.ToArray();
            }

            /// <summary>
            /// 向量：有 enc 先打包成 u8；table/string 先写元素再倒序填指针；
            /// struct 按 fbs 步长内联；标量倒序 Push。
            /// </summary>
            int WriteVector(BtlVector vec, FieldHint hint, string parentCtx, int fieldId, string path)
            {
                string elem = vec.Elem ?? hint?.VectorElem ?? "u8";

                if (PackedJsonStream.ShouldEncode(vec, out byte[] packed))
                {
                    Info(path, $"enc={vec.Enc ?? PackedJsonStream.EncName} 打包为 {packed.Length} 字节 u8 向量");
                    return _b.CreateByteVector(packed);
                }

                if (!string.IsNullOrEmpty(vec.Enc) &&
                    vec.V.Count > 0 &&
                    !IsNumericLike(vec.V[0]))
                {
                    Warn(path, $"enc={vec.Enc} 无法打包，写出空行为树头");
                    return _b.CreateByteVector(PackedJsonStream.EmptyHeader);
                }

                if (elem == "table")
                {
                    string childCtx = hint?.VectorElemTable ?? "";
                    var offs = new int[vec.V.Count];
                    for (int i = 0; i < vec.V.Count; i++)
                    {
                        var item = vec.V[i];
                        BtlTable tbl = item as BtlTable;
                        if (tbl == null)
                        {
                            Warn(path + "/i" + i, "期望 table 元素，已插入空表");
                            tbl = BtlFrontJson.NewTable();
                        }
                        offs[i] = WriteTable(tbl, childCtx, path + "/i" + i);
                    }
                    _b.StartVector(4, offs.Length, 4);
                    for (int i = offs.Length - 1; i >= 0; i--)
                        _b.AddOffset(offs[i]);
                    return _b.EndVector();
                }

                if (elem == "string")
                {
                    var offs = new int[vec.V.Count];
                    for (int i = 0; i < vec.V.Count; i++)
                        offs[i] = _b.CreateString(vec.V[i]?.ToString() ?? "");
                    _b.StartVector(4, offs.Length, 4);
                    for (int i = offs.Length - 1; i >= 0; i--)
                        _b.AddOffset(offs[i]);
                    return _b.EndVector();
                }

                if (elem == "struct")
                {
                    var members = hint?.VectorStructMembers;
                    int stride = members?.Sum(m => m.Size) ?? 0;
                    int align = members != null && members.Length > 0
                        ? members.Max(m => m.Size)
                        : 1;
                    if (stride <= 0)
                    {
                        Warn(path, "struct 向量缺少布局，按 u8 写出");
                        elem = "u8";
                    }
                    else
                    {
                        _b.StartVector(stride, vec.V.Count, Math.Max(1, align));
                        for (int i = vec.V.Count - 1; i >= 0; i--)
                        {
                            BtlStruct st = vec.V[i] as BtlStruct ?? new BtlStruct { T = "struct" };
                            PutStructMembers(st, members, path + "/i" + i);
                        }
                        return _b.EndVector();
                    }
                }

                int esz = SizeOf(elem);
                _b.StartVector(esz, vec.V.Count, esz);
                for (int i = vec.V.Count - 1; i >= 0; i--)
                    PutScalar(elem, vec.V[i], path + "/i" + i);
                return _b.EndVector();
            }

            void PutStruct(BtlStruct st, StructMember[] members, string path)
            {
                if (members != null && members.Length > 0)
                {
                    int size = members.Sum(m => m.Size);
                    int align = members.Max(m => m.Size);
                    _b.Prep(Math.Max(1, align), size);
                    PutStructMembers(st, members, path);
                    return;
                }

                Info(path, $"struct 无 fbs 布局，按 u8×{st.V.Count} 写出");
                _b.Prep(1, st.V.Count);
                for (int i = st.V.Count - 1; i >= 0; i--)
                    PutScalar("u8", st.V[i], path);
            }

            void PutStructMembers(BtlStruct st, StructMember[] members, string path)
            {
                for (int i = members.Length - 1; i >= 0; i--)
                {
                    object v = i < st.V.Count ? st.V[i] : 0;
                    PutScalarRaw(members[i].T, v, path);
                }
            }

            void PutScalar(string t, object v, string path)
            {
                int sz = SizeOf(t);
                _b.Prep(sz, 0);
                PutScalarRaw(t, v, path);
            }

            void PutScalarRaw(string t, object v, string path)
            {
                try
                {
                    switch (t)
                    {
                        case "bool":
                            _b.PutByte(ToBool(v) ? (byte)1 : (byte)0);
                            break;
                        case "u8":
                            _b.PutByte(unchecked((byte)ToULong(v)));
                            break;
                        case "i8":
                            _b.PutByte(unchecked((byte)ToLong(v)));
                            break;
                        case "u16":
                            _b.PutUshort(unchecked((ushort)ToULong(v)));
                            break;
                        case "i16":
                            _b.PutShort(unchecked((short)ToLong(v)));
                            break;
                        case "u32":
                            _b.PutUint(unchecked((uint)ToULong(v)));
                            break;
                        case "i32":
                            _b.PutInt(unchecked((int)ToLong(v)));
                            break;
                        case "f32":
                            _b.PutFloat(ToFloat(v));
                            break;
                        case "u64":
                            _b.PutLong(unchecked((long)ToULong(v)));
                            break;
                        case "i64":
                            _b.PutLong(ToLong(v));
                            break;
                        case "f64":
                            _b.PutDouble(ToDouble(v));
                            break;
                        default:
                            Warn(path, $"未知标量类型 {t}，按 u32");
                            _b.PutUint(unchecked((uint)ToULong(v)));
                            break;
                    }
                }
                catch (Exception ex)
                {
                    Warn(path, $"标量写入失败 t={t}: {ex.Message}，写 0");
                    for (int i = 0; i < SizeOf(t); i++) _b.PutByte(0);
                }
            }

            static bool ToBool(object v) => v switch
            {
                null => false,
                bool b => b,
                string s => s == "1" || s.Equals("true", StringComparison.OrdinalIgnoreCase),
                IConvertible c => Convert.ToInt64(c, CultureInfo.InvariantCulture) != 0,
                _ => false
            };

            static long ToLong(object v) => v switch
            {
                null => 0,
                long l => l,
                ulong ul => unchecked((long)ul),
                int i => i,
                uint ui => ui,
                short s => s,
                ushort us => us,
                byte b => b,
                sbyte sb => sb,
                bool bl => bl ? 1 : 0,
                float f => (long)f,
                double d => (long)d,
                string s when long.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out var p) => p,
                IConvertible c => Convert.ToInt64(c, CultureInfo.InvariantCulture),
                _ => 0
            };

            static ulong ToULong(object v) => v switch
            {
                null => 0,
                ulong ul => ul,
                long l => unchecked((ulong)l),
                int i => unchecked((ulong)i),
                uint ui => ui,
                short s => unchecked((ulong)s),
                ushort us => us,
                byte b => b,
                sbyte sb => unchecked((ulong)sb),
                bool bl => bl ? 1u : 0u,
                float f => (ulong)f,
                double d => (ulong)d,
                string s when ulong.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out var p) => p,
                IConvertible c => Convert.ToUInt64(c, CultureInfo.InvariantCulture),
                _ => 0
            };

            static float ToFloat(object v) => v switch
            {
                null => 0f,
                float f => f,
                double d => (float)d,
                IConvertible c => Convert.ToSingle(c, CultureInfo.InvariantCulture),
                _ => 0f
            };

            static double ToDouble(object v) => v switch
            {
                null => 0d,
                double d => d,
                float f => f,
                IConvertible c => Convert.ToDouble(c, CultureInfo.InvariantCulture),
                _ => 0d
            };

            static bool IsNumericLike(object v) =>
                v is byte or sbyte or short or ushort or int or uint or long or ulong
                    or float or double or bool or decimal
                || (v is string s && double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out _));

            BtlNode CoerceNode(BtlNode node, FieldHint hint, string path)
            {
                if (node == null || hint == null) return node;

                if (hint.Kind == FieldKind.Table)
                {
                    if (node is BtlTable) return node;
                    if (node is BtlScalar sc && IsZeroScalar(sc))
                    {
                        Info(path, "提示为 table，Front 为 0 标量 → 按缺省跳过");
                        return null;
                    }
                    Warn(path, $"提示为 table，Front 为 {node.T}，改为空表");
                    return BtlFrontJson.NewTable();
                }

                if (hint.Kind == FieldKind.Vector)
                {
                    if (node is BtlVector) return node;
                    if (node is BtlScalar sc && IsZeroScalar(sc))
                    {
                        Info(path, "提示为 vector，Front 为 0 标量 → 按缺省跳过");
                        return null;
                    }
                    Warn(path, $"提示为 vector，Front 为 {node.T}，改为空向量");
                    return new BtlVector
                    {
                        T = "vector",
                        Elem = hint.VectorElem ?? "u8"
                    };
                }

                if (hint.Kind == FieldKind.String)
                {
                    if (node is BtlScalar s && string.Equals(s.T, "string", StringComparison.OrdinalIgnoreCase))
                        return node;
                    if (node is BtlScalar z && IsZeroScalar(z))
                        return null;
                }

                if (hint.Kind is FieldKind.Scalar or FieldKind.Bool)
                {
                    if (node is BtlScalar) return node;
                    if (node is BtlTable or BtlVector)
                    {
                        Warn(path, $"提示为标量，Front 为 {node.T}，仍按 Front 写出");
                        return node;
                    }
                }

                if (hint.Kind == FieldKind.Struct && node is BtlScalar sc2 && IsZeroScalar(sc2))
                {
                    Info(path, "提示为 struct，Front 为 0 标量 → 按缺省跳过");
                    return null;
                }

                return node;
            }

            static bool IsZeroScalar(BtlScalar sc)
            {
                if (sc?.V == null) return true;
                if (sc.V is bool b) return !b;
                if (sc.V is string s)
                    return string.IsNullOrEmpty(s) || s == "0";
                try { return ToLong(sc.V) == 0 && ToDouble(sc.V) == 0; }
                catch { return false; }
            }
        }
    }
}
