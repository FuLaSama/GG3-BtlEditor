/*
 * BtlToFront.cs
 *
 * 把一份 .btl（FlatBuffers 二进制）读成内存里的 BtlFront 树，供编辑器改。
 *
 * 流程：
 *   文件头 4 字节 = Root 表的绝对偏移
 *   对每张表：soffset 找到 vtable → 槽偏移为 0 表示没写（跳过，不要造 0）
 *   每个非空槽按 SoftSchema（battle.fbs）决定读标量 / 表指针 / 向量 / 内联 struct
 *   没有 hint 时才启发式：像表指针就当表，像向量就当向量
 *
 * 容错：单个坏字段记 Issue 后跳过，尽量交出能打开的树，而不是整文件失败。
 * 原版里未用的空嵌套表指针有时落在 00 00 00 00 填充上（不是合法表）。
 * 这种槽按缺省跳过，不要发明 {}，否则写回会变成真的空表，游戏不认。
 *
 * Root/10 的 u8 向量若是国家行为树流，调用 PackedJsonStream 解成 JSON，仍挂在该向量上。
 */
using System.Text;
using BtlCore.Front;

namespace BtlCore.Fb
{
    public enum IssueSeverity
    {
        Info,
        Warning,
        Error
    }

    /// <summary>转换过程中的一条说明。Error 才应阻止保存；Warning 仍可编辑。</summary>
    public sealed class ConvertIssue
    {
        public IssueSeverity Severity { get; init; }
        public string Path { get; init; }
        public string Message { get; init; }

        public override string ToString() => $"[{Severity}] {Path}: {Message}";
    }

    public sealed class ConvertResult
    {
        public BtlFrontDocument Document { get; set; }
        public List<ConvertIssue> Issues { get; } = new List<ConvertIssue>();
        /// <summary>只要 Root 表读出来就算成功，Issues 里仍可能有 Warning。</summary>
        public bool Ok => Document?.Root != null;
    }

    public static class BtlToFront
    {
        const int MaxDepth = 64;
        const int MaxVectorElems = 2_000_000;

        /// <summary>读文件。失败也返回空 Root，方便编辑器弹 Issue 而不是直接崩。</summary>
        public static ConvertResult FromFile(string path) => FromBytes(File.ReadAllBytes(path));

        public static ConvertResult FromBytes(byte[] data)
        {
            var result = new ConvertResult();
            var ctx = new Reader(data, result.Issues);

            if (data == null || data.Length < 8)
            {
                result.Issues.Add(new ConvertIssue
                {
                    Severity = IssueSeverity.Error,
                    Path = "/",
                    Message = "文件过短，无法作为 FlatBuffers BTL"
                });
                result.Document = new BtlFrontDocument { Root = BtlFrontJson.NewTable() };
                return result;
            }

            int rootOff = ctx.ReadI32(0);
            if (!ctx.InRange(rootOff, 4))
            {
                result.Issues.Add(new ConvertIssue
                {
                    Severity = IssueSeverity.Error,
                    Path = "/",
                    Message = $"Root 偏移无效: 0x{rootOff:X}"
                });
                result.Document = new BtlFrontDocument { Root = BtlFrontJson.NewTable() };
                return result;
            }

            var root = ctx.ReadTable(rootOff, SoftSchema.RootType, "/", 0);
            if (root == null)
            {
                result.Issues.Add(new ConvertIssue
                {
                    Severity = IssueSeverity.Error,
                    Path = "/",
                    Message = "Root 表解析失败，已返回空表"
                });
                root = BtlFrontJson.NewTable();
            }

            result.Document = new BtlFrontDocument { FormatVersion = 1, Root = root };
            return result;
        }

        sealed class Reader
        {
            readonly byte[] _d;
            readonly List<ConvertIssue> _issues;
            readonly HashSet<int> _visiting = new HashSet<int>();
            readonly Dictionary<int, BtlTable> _done = new Dictionary<int, BtlTable>();

            public Reader(byte[] data, List<ConvertIssue> issues)
            {
                _d = data;
                _issues = issues;
            }

            public bool InRange(int pos, int size) =>
                pos >= 0 && size >= 0 && (long)pos + size <= _d.Length;

            public int ReadI32(int pos) => BitConverter.ToInt32(_d, pos);
            public ushort ReadU16(int pos) => BitConverter.ToUInt16(_d, pos);
            public short ReadI16(int pos) => BitConverter.ToInt16(_d, pos);
            public byte ReadU8(int pos) => _d[pos];
            public sbyte ReadI8(int pos) => unchecked((sbyte)_d[pos]);
            public uint ReadU32(int pos) => BitConverter.ToUInt32(_d, pos);
            public int ReadI32At(int pos) => BitConverter.ToInt32(_d, pos);
            public float ReadF32(int pos) => BitConverter.ToSingle(_d, pos);
            public double ReadF64(int pos) => BitConverter.ToDouble(_d, pos);
            public ulong ReadU64(int pos) => BitConverter.ToUInt64(_d, pos);
            public long ReadI64(int pos) => BitConverter.ToInt64(_d, pos);

            void Warn(string path, string msg) =>
                _issues.Add(new ConvertIssue { Severity = IssueSeverity.Warning, Path = path, Message = msg });

            void Info(string path, string msg) =>
                _issues.Add(new ConvertIssue { Severity = IssueSeverity.Info, Path = path, Message = msg });

            public BtlTable ReadTable(int pos, string context, string path, int depth)
            {
                if (depth > MaxDepth)
                {
                    Warn(path, "超过最大嵌套深度，跳过");
                    return null;
                }

                if (!InRange(pos, 4))
                {
                    Warn(path, $"表偏移越界 0x{pos:X}");
                    return null;
                }

                // 同一物理地址只建一棵节点，避免共享表被复制成两份。
                if (_done.TryGetValue(pos, out var cached))
                    return cached;

                if (!_visiting.Add(pos))
                {
                    Warn(path, $"检测到环引用 @0x{pos:X}，跳过");
                    return BtlFrontJson.NewTable();
                }

                try
                {
                    int vtableSoff = ReadI32(pos);
                    int vtablePos = pos - vtableSoff;
                    if (!InRange(vtablePos, 4))
                    {
                        Warn(path, $"vtable 无效 @0x{pos:X}");
                        return BtlFrontJson.NewTable();
                    }

                    ushort vtableSize = ReadU16(vtablePos);
                    ushort objectSize = ReadU16(vtablePos + 2);
                    if (vtableSize < 4 || (vtableSize & 1) != 0)
                    {
                        Warn(path, $"vtable 尺寸异常 size={vtableSize}，尝试继续");
                        if (vtableSize < 4) return BtlFrontJson.NewTable();
                    }

                    int numSlots = (vtableSize - 4) / 2;
                    if (numSlots < 0) numSlots = 0;
                    if (numSlots > 512)
                    {
                        Warn(path, $"字段数过多 ({numSlots})，截断到 512");
                        numSlots = 512;
                    }

                    // 收集每个 slot 的相对偏移
                    var offsets = new ushort[numSlots];
                    for (int i = 0; i < numSlots; i++)
                    {
                        int op = vtablePos + 4 + i * 2;
                        offsets[i] = InRange(op, 2) ? ReadU16(op) : (ushort)0;
                    }

                    var table = BtlFrontJson.NewTable();
                    _done[pos] = table;

                    for (int fieldId = 0; fieldId < numSlots; fieldId++)
                    {
                        ushort off = offsets[fieldId];
                        if (off == 0) continue;

                        int fieldPos = pos + off;
                        if (!InRange(fieldPos, 1))
                        {
                            Warn($"{path}/{fieldId}", $"字段偏移越界 off={off}");
                            continue;
                        }

                        int slotWidth = EstimateSlotWidth(offsets, fieldId, objectSize, off);
                        string fieldPath = path == "/" ? "/" + fieldId : path + "/" + fieldId;

                        SoftSchema.TryGet(context, fieldId, out FieldHint hint);
                        BtlNode node = null;
                        try
                        {
                            node = ReadField(fieldPos, slotWidth, hint, fieldPath, depth + 1);
                        }
                        catch (Exception ex)
                        {
                            Warn(fieldPath, "字段异常已跳过: " + ex.Message);
                            node = null;
                        }

                        if (node != null)
                            table.F[fieldId] = node;
                    }

                    return table;
                }
                finally
                {
                    _visiting.Remove(pos);
                }
            }

            /// <summary>
            /// 用后续非空槽或 objectSize 估算本槽宽度，用来区分 1/2/4 字节标量和 4 字节指针。
            /// 宽得离谱时按指针宽 4 猜，避免把整张对象尾当成一个 struct。
            /// </summary>
            static int EstimateSlotWidth(ushort[] offsets, int fieldId, ushort objectSize, ushort off)
            {
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
                if (w > 256) w = 4; // 异常宽：按指针宽猜
                return w;
            }

            /// <summary>有 hint 按 schema 读；没有则试表指针 / 向量 / 字符串，再退回按槽宽当标量。</summary>
            BtlNode ReadField(int fieldPos, int slotWidth, FieldHint hint, string path, int depth)
            {
                if (hint != null)
                {
                    switch (hint.Kind)
                    {
                        case FieldKind.Bool:
                            return ReadScalarAt(fieldPos, "bool", 1, path);
                        case FieldKind.Scalar:
                            return ReadScalarAt(fieldPos, hint.ScalarT ?? WidthToScalarT(slotWidth),
                                SizeOf(hint.ScalarT ?? WidthToScalarT(slotWidth)), path);
                        case FieldKind.String:
                            return ReadStringField(fieldPos, path) ?? FallbackScalar(fieldPos, slotWidth, path);
                        case FieldKind.Table:
                            // null = 槽位相对指针为 0（缺省），或指针落在零填充 / 不像表（不要发明 {}）
                            return ReadTableField(fieldPos, hint.ChildTable, path, depth);
                        case FieldKind.Struct:
                            return ReadInlineStruct(fieldPos, hint.StructMembers, path)
                                   ?? FallbackOpaqueStruct(fieldPos, slotWidth, path);
                        case FieldKind.Vector:
                            return ReadVectorField(fieldPos, hint, path, depth)
                                   ?? FallbackScalar(fieldPos, slotWidth, path);
                    }
                }

                // 无提示：启发式
                if (slotWidth == 4 || slotWidth >= 4)
                {
                    var asTable = TryAsTablePointer(fieldPos, path, depth);
                    if (asTable != null) return asTable;

                    var asVec = TryAsVectorPointer(fieldPos, path, depth);
                    if (asVec != null) return asVec;

                    var asStr = TryAsStringPointer(fieldPos, path);
                    if (asStr != null) return asStr;
                }

                if (slotWidth > 8)
                    return FallbackOpaqueStruct(fieldPos, slotWidth, path);

                return FallbackScalar(fieldPos, slotWidth, path);
            }

            BtlNode ReadTableField(int fieldPos, string childCtx, string path, int depth)
            {
                if (!InRange(fieldPos, 4)) return null;
                int rel = ReadI32(fieldPos);
                if (rel == 0) return null; // FlatBuffers 缺省：字段未写出
                int abs = fieldPos + rel;
                if (!LooksLikeTable(abs))
                {
                    // 原版常见：TriggerEventSub3 等未用槽的相对指针落在相邻对象之间的零填充上。
                    // soffset==0 不是坏文件，也不是空表。跳过该槽 = FlatBuffers 缺省（游戏当 nullptr）。
                    // 若收成 {} 再写回，会变成合法空对象，游戏和原版行为不一致。
                    if (InRange(abs, 4) && ReadI32(abs) == 0)
                    {
                        Info(path, $"表指针落在零填充 @0x{abs:X}，按缺省跳过");
                        return null;
                    }
                    Warn(path, $"表指针不像合法表 @0x{abs:X}，跳过");
                    return null;
                }
                return ReadTable(abs, childCtx ?? "", path, depth);
            }

            BtlNode TryAsTablePointer(int fieldPos, string path, int depth)
            {
                if (!InRange(fieldPos, 4)) return null;
                int rel = ReadI32(fieldPos);
                if (rel == 0) return null;
                int abs = fieldPos + rel;
                if (!LooksLikeTable(abs)) return null;
                Info(path, "启发式识别为 table");
                return ReadTable(abs, "", path, depth);
            }

            BtlNode ReadStringField(int fieldPos, string path)
            {
                if (!InRange(fieldPos, 4)) return null;
                int rel = ReadI32(fieldPos);
                if (rel == 0) return null;
                int abs = fieldPos + rel;
                if (!TryReadString(abs, out string s))
                {
                    Warn(path, "字符串无效，跳过");
                    return null;
                }
                return BtlFrontJson.Scalar("string", s);
            }

            BtlNode TryAsStringPointer(int fieldPos, string path)
            {
                if (!InRange(fieldPos, 4)) return null;
                int rel = ReadI32(fieldPos);
                if (rel == 0) return null;
                int abs = fieldPos + rel;
                if (!TryReadString(abs, out string s)) return null;
                if (s.Length > 4096) return null;
                // 拒绝大量不可打印字符
                int bad = s.Count(c => c < 32 && c != '\t' && c != '\n' && c != '\r');
                if (bad > s.Length / 4 && s.Length > 0) return null;
                Info(path, "启发式识别为 string");
                return BtlFrontJson.Scalar("string", s);
            }

            BtlNode ReadVectorField(int fieldPos, FieldHint hint, string path, int depth)
            {
                if (!InRange(fieldPos, 4)) return null;
                int rel = ReadI32(fieldPos);
                if (rel == 0) return null;
                int abs = fieldPos + rel;
                return ReadVectorAt(abs, hint, path, depth);
            }

            BtlNode TryAsVectorPointer(int fieldPos, string path, int depth)
            {
                if (!InRange(fieldPos, 4)) return null;
                int rel = ReadI32(fieldPos);
                if (rel == 0) return null;
                int abs = fieldPos + rel;
                if (!InRange(abs, 4)) return null;
                int count = ReadI32(abs);
                if (count < 0 || count > MaxVectorElems) return null;
                // 粗检：至少能放下 count 个字节或指针
                if (!InRange(abs + 4, Math.Min(count, 1))) return null;
                if (count > 0 && !InRange(abs + 4, 1)) return null;

                // 优先猜 table 向量
                if (count > 0 && count <= 100000 && InRange(abs + 4, 4))
                {
                    int sample = Math.Min(count, 5);
                    int ok = 0;
                    for (int i = 0; i < sample; i++)
                    {
                        int ep = abs + 4 + i * 4;
                        if (!InRange(ep, 4)) break;
                        int er = ReadI32(ep);
                        if (er != 0 && LooksLikeTable(ep + er)) ok++;
                    }
                    if (ok == sample && sample > 0)
                    {
                        Info(path, "启发式识别为 vector<table>");
                        var hint = new FieldHint { Kind = FieldKind.Vector, VectorElem = "table", VectorElemTable = "" };
                        return ReadVectorAt(abs, hint, path, depth);
                    }
                }

                // 标量 u8 向量（含空）
                if (InRange(abs + 4, count))
                {
                    Info(path, "启发式识别为 vector<u8>");
                    var hint = new FieldHint { Kind = FieldKind.Vector, VectorElem = "u8" };
                    return ReadVectorAt(abs, hint, path, depth);
                }

                return null;
            }

            BtlVector ReadVectorAt(int vecPos, FieldHint hint, string path, int depth)
            {
                if (!InRange(vecPos, 4))
                {
                    Warn(path, "向量头越界");
                    return null;
                }

                int count = ReadI32(vecPos);
                if (count < 0)
                {
                    Warn(path, $"向量长度为负 ({count})，置空");
                    count = 0;
                }
                if (count > MaxVectorElems)
                {
                    Warn(path, $"向量过长 ({count})，截断到 {MaxVectorElems}");
                    count = MaxVectorElems;
                }

                string elem = hint?.VectorElem ?? "u8";
                var vec = new BtlVector { T = "vector", Elem = elem };
                int data = vecPos + 4;

                if (elem == "table")
                {
                    for (int i = 0; i < count; i++)
                    {
                        int ep = data + i * 4;
                        string ip = path + "/i" + i;
                        if (!InRange(ep, 4))
                        {
                            Warn(ip, "元素指针越界，截断向量");
                            break;
                        }
                        int er = ReadI32(ep);
                        if (er == 0)
                        {
                            vec.V.Add(BtlFrontJson.NewTable());
                            continue;
                        }
                        int abs = ep + er;
                        if (!LooksLikeTable(abs))
                        {
                            Warn(ip, $"坏表元素 @0x{abs:X}，插入空表");
                            vec.V.Add(BtlFrontJson.NewTable());
                            continue;
                        }
                        var child = ReadTable(abs, hint?.VectorElemTable ?? "", ip, depth);
                        vec.V.Add(child ?? BtlFrontJson.NewTable());
                    }
                    return vec;
                }

                if (elem == "string")
                {
                    for (int i = 0; i < count; i++)
                    {
                        int ep = data + i * 4;
                        string ip = path + "/i" + i;
                        if (!InRange(ep, 4)) { Warn(ip, "截断"); break; }
                        int er = ReadI32(ep);
                        if (er == 0) { vec.V.Add(""); continue; }
                        if (!TryReadString(ep + er, out string s))
                        {
                            Warn(ip, "坏字符串，用空串");
                            vec.V.Add("");
                            continue;
                        }
                        vec.V.Add(s);
                    }
                    return vec;
                }

                if (elem == "struct")
                {
                    var members = hint?.VectorStructMembers;
                    int stride = members?.Sum(m => m.Size) ?? 0;
                    if (stride <= 0)
                    {
                        Warn(path, "struct 向量缺少布局，降级为 u8");
                        elem = "u8";
                        vec.Elem = "u8";
                    }
                    else
                    {
                        long need = (long)count * stride;
                        if (!InRange(data, (int)Math.Min(need, int.MaxValue)))
                        {
                            int fit = Math.Max(0, (_d.Length - data) / stride);
                            Warn(path, $"struct 向量越界，截断 {count}→{fit}");
                            count = fit;
                        }
                        for (int i = 0; i < count; i++)
                        {
                            var st = ReadInlineStruct(data + i * stride, members, path + "/i" + i);
                            if (st != null) vec.V.Add(st);
                            else
                            {
                                Warn(path + "/i" + i, "坏 struct 元素，跳过");
                            }
                        }
                        return vec;
                    }
                }

                // 标量向量
                int esz = SizeOf(elem);
                long bytesNeed = (long)count * esz;
                if (!InRange(data, (int)Math.Min(bytesNeed, int.MaxValue)))
                {
                    int fit = esz > 0 ? Math.Max(0, (_d.Length - data) / esz) : 0;
                    Warn(path, $"标量向量越界，截断 {count}→{fit}");
                    count = fit;
                }
                for (int i = 0; i < count; i++)
                {
                    var sc = ReadScalarAt(data + i * esz, elem, esz, path + "/i" + i);
                    vec.V.Add(sc?.V ?? 0);
                }

                // 只解 Root 字段 10。RegionInfo 里也有空的 00 00 28 01 字节向量，不能当行为树解。
                if (elem == "u8" && path == "/10" && PackedJsonStream.TryUnpackVector(vec))
                    Info(path, $"国家行为树 JSON-like 打包已解出 {vec.V.Count} 个对象（enc={PackedJsonStream.EncName}）");

                return vec;
            }

            BtlStruct ReadInlineStruct(int pos, StructMember[] members, string path)
            {
                if (members == null || members.Length == 0) return null;
                int size = members.Sum(m => m.Size);
                if (!InRange(pos, size))
                {
                    Warn(path, $"struct 越界 need={size} @0x{pos:X}");
                    return null;
                }

                var st = new BtlStruct { T = "struct" };
                int p = pos;
                foreach (var m in members)
                {
                    var sc = ReadScalarAt(p, m.T, m.Size, path);
                    st.V.Add(sc?.V ?? 0);
                    p += m.Size;
                }
                return st;
            }

            BtlStruct FallbackOpaqueStruct(int pos, int width, string path)
            {
                width = Math.Max(1, Math.Min(width, 256));
                if (!InRange(pos, width))
                {
                    width = Math.Max(0, _d.Length - pos);
                    if (width <= 0) return null;
                    Warn(path, "不透明 struct 截断到文件尾");
                }
                Info(path, $"未知内联块按 struct<u8×{width}>");
                var st = new BtlStruct { T = "struct" };
                for (int i = 0; i < width; i++)
                    st.V.Add(_d[pos + i]);
                return st;
            }

            BtlScalar ReadScalarAt(int pos, string t, int size, string path)
            {
                if (!InRange(pos, size))
                {
                    Warn(path, $"标量越界 t={t}");
                    return BtlFrontJson.Scalar(t, 0);
                }

                object v = t switch
                {
                    "bool" => _d[pos] != 0,
                    "u8" => _d[pos],
                    "i8" => ReadI8(pos),
                    "u16" => ReadU16(pos),
                    "i16" => ReadI16(pos),
                    "u32" => ReadU32(pos),
                    "i32" => ReadI32At(pos),
                    "f32" => ReadF32(pos),
                    "u64" => ReadU64(pos),
                    "i64" => ReadI64(pos),
                    "f64" => ReadF64(pos),
                    _ => size switch
                    {
                        1 => _d[pos],
                        2 => ReadU16(pos),
                        8 => ReadU64(pos),
                        _ => ReadU32(pos)
                    }
                };
                return BtlFrontJson.Scalar(t, v);
            }

            BtlScalar FallbackScalar(int fieldPos, int slotWidth, string path)
            {
                string t = WidthToScalarT(slotWidth);
                return ReadScalarAt(fieldPos, t, SizeOf(t), path);
            }

            /// <summary>
            /// 对象头 4 字节像不像指向前方 vtable 的 soffset：
            /// vtable 尺寸偶、合理范围，objectSize 能盖住对象头。
            /// </summary>
            bool LooksLikeTable(int pos)
            {
                if (!InRange(pos, 4)) return false;
                int soff = ReadI32(pos);
                if (soff == 0) return false;
                int vt = pos - soff;
                if (!InRange(vt, 4)) return false;
                ushort vs = ReadU16(vt);
                ushort os = ReadU16(vt + 2);
                if (vs < 4 || vs > 2048) return false;
                if ((vs & 1) != 0) return false; // 通常 2 对齐
                if (os < 4 || os > 65535) return false;
                // object 应覆盖到 pos+4
                if (!InRange(pos, Math.Min((int)os, 64))) return false;
                return true;
            }

            bool TryReadString(int pos, out string s)
            {
                s = null;
                if (!InRange(pos, 4)) return false;
                int len = ReadI32(pos);
                if (len < 0 || len > 1_000_000) return false;
                if (!InRange(pos + 4, len + 1)) return false; // +NUL
                // 允许无严格 NUL
                try
                {
                    s = Encoding.UTF8.GetString(_d, pos + 4, len);
                    return true;
                }
                catch
                {
                    return false;
                }
            }

            static int SizeOf(string t) => t switch
            {
                "bool" or "u8" or "i8" => 1,
                "u16" or "i16" => 2,
                "u32" or "i32" or "f32" => 4,
                "u64" or "i64" or "f64" => 8,
                _ => 4
            };

            static string WidthToScalarT(int w) => w switch
            {
                1 => "u8",
                2 => "u16",
                8 => "u64",
                _ => "u32"
            };
        }
    }
}
