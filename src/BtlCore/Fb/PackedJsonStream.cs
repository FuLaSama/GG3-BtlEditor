/*
 * PackedJsonStream.cs
 *
 * 国家行为树存在 Root 字段 10（fbs 名叫 rle_metadata，实际是 [ubyte]）。
 * 这不是 FlatBuffers 表，而是游戏自己的「类 JSON」字节流：
 *
 *   空内容：固定 4 字节头 00 00 28 01
 *   有内容：文件里能扫到 ASCII "btid\0"；字符串值是 GBK；键是 ASCII + NUL
 *   类型码存在描述字节高位：1/2 整数、5 字符串、9 对象、10 数组、26 布尔
 *   描述字节低 2 位是宽度档：1<<(typeByte&3) 得到指针宽度
 *
 * 解码结果仍挂在原来的 u8 向量上：Elem 保持 u8，Enc=country_ai_bt，V 换成 JsonNode。
 * 写出时 ShouldEncode 只认 Enc，避免把地形属性等普通字节向量误当成行为树。
 * 对象键顺序与解码时一致，不排序（排序会改变字节）。
 */
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using BtlCore.Front;

namespace BtlCore.Fb
{
    public static class PackedJsonStream
    {
        /// <summary>写在 BtlVector.Enc 上的标记，FrontToBtl 见到就走本编码器。</summary>
        public const string EncName = "country_ai_bt";

        /// <summary>游戏里「没有行为树」时的 4 字节占位。</summary>
        public static readonly byte[] EmptyHeader = { 0, 0, 0x28, 1 };

        const int MaxItems = 200_000;
        const int MaxDepth = 64;

        static Encoding _gbk;

        static Encoding Gbk
        {
            get
            {
                if (_gbk != null) return _gbk;
                try
                {
                    Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
                    _gbk = Encoding.GetEncoding("GBK");
                }
                catch
                {
                    _gbk = Encoding.GetEncoding(936);
                }
                return _gbk;
            }
        }

        public static bool EncIsPackedJson(string enc) =>
            string.Equals(enc, EncName, StringComparison.OrdinalIgnoreCase);

        /// <summary>扫描是否像行为树流：空头，或出现 "btid\\0"。</summary>
        public static bool LooksLike(IList<byte> bytes)
        {
            if (bytes == null || bytes.Count == 0) return false;
            if (IsEmptyHeader(bytes)) return true;
            int n = bytes.Count;
            for (int i = 0; i <= n - 5; i++)
            {
                if (bytes[i] == (byte)'b' && bytes[i + 1] == (byte)'t' &&
                    bytes[i + 2] == (byte)'i' && bytes[i + 3] == (byte)'d' &&
                    bytes[i + 4] == 0)
                    return true;
            }
            return false;
        }

        public static bool IsEmptyHeader(IList<byte> bytes) =>
            bytes != null && bytes.Count == 4 &&
            bytes[0] == 0 && bytes[1] == 0 && bytes[2] == 0x28 && bytes[3] == 1;

        /// <summary>
        /// 若向量还是原始 u8 且 LooksLike，则换成 JsonNode 列表并打 Enc。
        /// 已经有 Enc 则只检查名字对不对。失败时向量原样留下。
        /// </summary>
        public static bool TryUnpackVector(BtlVector vec)
        {
            if (vec == null || vec.V == null || vec.V.Count == 0) return false;
            if (!string.IsNullOrEmpty(vec.Enc)) return EncIsPackedJson(vec.Enc);
            if (!TryToBytes(vec.V, out byte[] raw)) return false;
            if (!TryDecode(raw, out var nodes)) return false;
            vec.Enc = EncName;
            vec.Elem = string.IsNullOrEmpty(vec.Elem) ? "u8" : vec.Elem;
            vec.V.Clear();
            foreach (var n in nodes)
                vec.V.Add(n);
            return true;
        }

        /// <summary>
        /// 从尾部读类型/宽度，再递归解析。成功则 nodes 为一棵或多棵行为树对象。
        /// 空头返回空列表（不是失败）。
        /// </summary>
        public static bool TryDecode(IList<byte> bytes, out List<JsonNode> nodes)
        {
            nodes = null;
            if (bytes == null || bytes.Count == 0) return false;
            if (IsEmptyHeader(bytes))
            {
                nodes = new List<JsonNode>();
                return true;
            }
            if (!LooksLike(bytes)) return false;

            try
            {
                byte[] b = new byte[bytes.Count];
                for (int i = 0; i < bytes.Count; i++) b[i] = bytes[i];
                if (b.Length < 2) return false;

                int n = b.Length;
                int typeByte = b[n - 2];
                int lenByte = b[n - 1];
                if (lenByte < 1 || lenByte > 8) return false;
                int ptr = n - 2 - lenByte;
                if (ptr < 0 || ptr >= n) return false;

                var top = new BtValue
                {
                    Raw = b,
                    Ptr = ptr,
                    Len = lenByte,
                    Typ = typeByte >> 2,
                    Shift = 1 << (typeByte & 3)
                };

                object parsed = BtParse(top, 0);
                nodes = new List<JsonNode>();
                if (parsed is List<object> list)
                {
                    foreach (var item in list)
                    {
                        var jn = ToJsonNode(item);
                        if (jn != null) nodes.Add(jn);
                    }
                    return true;
                }
                if (parsed is Dictionary<string, object> dict)
                {
                    var jn = ToJsonNode(dict);
                    if (jn != null) nodes.Add(jn);
                    return true;
                }
                return false;
            }
            catch
            {
                nodes = null;
                return false;
            }
        }

        /// <summary>仅当 Enc 是 country_ai_bt 才打包。其它向量即使元素是对象也不动。</summary>
        public static bool ShouldEncode(BtlVector vec, out byte[] packed)
        {
            packed = null;
            if (vec == null || !EncIsPackedJson(vec.Enc)) return false;

            try
            {
                packed = Encode(vec.V);
                return packed != null && packed.Length > 0;
            }
            catch
            {
                packed = null;
                return false;
            }
        }

        /// <summary>把若干 JSON 对象编成游戏那种字节流。空列表写出 EmptyHeader。</summary>
        public static byte[] Encode(IList<object> items)
        {
            if (items == null || items.Count == 0)
                return (byte[])EmptyHeader.Clone();

            var buf = new List<byte>();
            var refs = new List<BtEmitRef>(items.Count);
            foreach (var item in items)
                refs.Add(EmitValue(buf, ToEmit(item)));

            int count = refs.Count;
            int countPos = buf.Count;
            buf.Add(0); buf.Add(0);
            int descStart = buf.Count;
            for (int i = 0; i < count; i++) { buf.Add(0); buf.Add(0); }
            int typeStart = buf.Count;
            for (int i = 0; i < count; i++) buf.Add(0);

            WriteLe16(buf, countPos, count);
            for (int i = 0; i < count; i++)
            {
                WriteSlot(buf, descStart + i * 2, refs[i]);
                buf[typeStart + i] = (byte)((refs[i].TypeCode << 2) | 1);
            }

            int topDescPos = buf.Count;
            buf.Add(0); buf.Add(0);
            WriteLe16(buf, topDescPos, topDescPos - descStart);
            buf.Add((byte)((10 << 2) | 1));
            buf.Add(2);
            return buf.ToArray();
        }

        static bool TryToBytes(IList<object> v, out byte[] raw)
        {
            raw = null;
            if (v == null) return false;
            raw = new byte[v.Count];
            for (int i = 0; i < v.Count; i++)
            {
                if (!TryToByte(v[i], out raw[i]))
                {
                    raw = null;
                    return false;
                }
            }
            return true;
        }

        static bool TryToByte(object v, out byte b)
        {
            b = 0;
            switch (v)
            {
                case byte u8: b = u8; return true;
                case sbyte i8: b = unchecked((byte)i8); return true;
                case int i when i >= 0 && i <= 255: b = (byte)i; return true;
                case uint ui when ui <= 255: b = (byte)ui; return true;
                case long l when l >= 0 && l <= 255: b = (byte)l; return true;
                case ulong ul when ul <= 255: b = (byte)ul; return true;
                case short s when s >= 0 && s <= 255: b = (byte)s; return true;
                case ushort us when us <= 255: b = (byte)us; return true;
                case JsonValue jv when jv.TryGetValue(out int n) && n >= 0 && n <= 255:
                    b = (byte)n; return true;
                default: return false;
            }
        }

        static bool IsByteLike(object v) =>
            v is byte or sbyte or short or ushort or int or uint or long or ulong
            || (v is JsonValue jv && jv.TryGetValue(out int n) && n >= 0 && n <= 255
                && jv.GetValueKind() == JsonValueKind.Number);

        /// <summary>解码游标：Ptr 指向描述，Len 是指针宽度，Typ 是类型码，Shift 是内层宽度。</summary>
        sealed class BtValue
        {
            public byte[] Raw;
            public int Ptr;
            public int Len;
            public int Typ;
            public int Shift;
        }

        static bool InRange(byte[] raw, int pos, int size) =>
            raw != null && pos >= 0 && size > 0 && (long)pos + size <= raw.Length;

        static int ReadLe(byte[] raw, int pos, int size)
        {
            if (!InRange(raw, pos, size)) throw new InvalidDataException("PackedJson 越界读");
            if (size == 1) return raw[pos];
            if (size == 2) return BitConverter.ToUInt16(raw, pos);
            if (size == 4) return (int)BitConverter.ToUInt32(raw, pos);
            throw new InvalidDataException("PackedJson 非法宽度 " + size);
        }

        static int BtCount(BtValue v)
        {
            int c = ReadLe(v.Raw, v.Ptr - v.Len, v.Len);
            if (c < 0 || c > MaxItems) throw new InvalidDataException("PackedJson 容器过大");
            return c;
        }

        static BtValue BtElement(BtValue v, int i)
        {
            int c = BtCount(v);
            int typePos = v.Ptr + c * v.Len + i;
            if (!InRange(v.Raw, typePos, 1)) throw new InvalidDataException("PackedJson 元素类型越界");
            int typeByte = v.Raw[typePos];
            return new BtValue
            {
                Raw = v.Raw,
                Ptr = v.Ptr + i * v.Len,
                Len = v.Len,
                Typ = typeByte >> 2,
                Shift = 1 << (typeByte & 3)
            };
        }

        static BtValue BtInner(BtValue v)
        {
            int off = ReadLe(v.Raw, v.Ptr, v.Len);
            return new BtValue
            {
                Raw = v.Raw,
                Ptr = v.Ptr - off,
                Len = v.Shift,
                Typ = v.Typ,
                Shift = v.Shift
            };
        }

        static List<string> BtKeys(BtValue v)
        {
            int x = ReadLe(v.Raw, v.Ptr - 3 * v.Len, v.Len);
            int basePos = v.Ptr - 3 * v.Len - x;
            int keySize = ReadLe(v.Raw, v.Ptr - 2 * v.Len, v.Len);
            if (keySize < 1 || keySize > 8) throw new InvalidDataException("PackedJson 键宽异常");
            int keyCount = ReadLe(v.Raw, basePos - keySize, keySize);
            if (keyCount < 0 || keyCount > MaxItems) throw new InvalidDataException("PackedJson 键过多");
            var result = new List<string>(keyCount);
            for (int i = 0; i < keyCount; i++)
            {
                int entryPos = basePos + i * keySize;
                int off = ReadLe(v.Raw, entryPos, keySize);
                int keyPos = entryPos - off;
                if (keyPos < 0 || keyPos >= v.Raw.Length) throw new InvalidDataException("PackedJson 键越界");
                int end = Array.IndexOf(v.Raw, (byte)0, keyPos);
                if (end < 0) throw new InvalidDataException("PackedJson 键无结束符");
                result.Add(Encoding.ASCII.GetString(v.Raw, keyPos, end - keyPos));
            }
            return result;
        }

        static string BtString(BtValue v)
        {
            int start = v.Ptr - ReadLe(v.Raw, v.Ptr, v.Len);
            if (start < 0 || start >= v.Raw.Length) throw new InvalidDataException("PackedJson 字符串越界");
            int end = Array.IndexOf(v.Raw, (byte)0, start);
            if (end < 0) throw new InvalidDataException("PackedJson 字符串无结束符");
            return Gbk.GetString(v.Raw, start, end - start);
        }

        static object BtParse(BtValue v, int depth)
        {
            if (depth > MaxDepth) throw new InvalidDataException("PackedJson 嵌套过深");
            // 9/10 的描述本身是「指向容器」的间接层，先解引用再看真实类型。
            if (v.Typ == 9 || v.Typ == 10) v = BtInner(v);

            if (v.Typ == 10)
            {
                int c = BtCount(v);
                var list = new List<object>(c);
                for (int i = 0; i < c; i++) list.Add(BtParse(BtElement(v, i), depth + 1));
                return list;
            }

            if (v.Typ == 9)
            {
                var keys = BtKeys(v);
                int c = BtCount(v);
                var dict = new Dictionary<string, object>(StringComparer.Ordinal);
                for (int i = 0; i < keys.Count && i < c; i++)
                    dict[keys[i]] = BtParse(BtElement(v, i), depth + 1);
                return dict;
            }

            if (v.Typ == 1 || v.Typ == 2) return ReadLe(v.Raw, v.Ptr, v.Len);
            if (v.Typ == 26) return ReadLe(v.Raw, v.Ptr, v.Len) != 0;
            if (v.Typ == 5) return BtString(v);
            return null;
        }

        static JsonNode ToJsonNode(object v)
        {
            switch (v)
            {
                case null: return null;
                case JsonNode jn: return jn;
                case bool b: return JsonValue.Create(b);
                case string s: return JsonValue.Create(s);
                case int i: return JsonValue.Create(i);
                case long l:
                    if (l >= int.MinValue && l <= int.MaxValue) return JsonValue.Create((int)l);
                    return JsonValue.Create(l);
                case Dictionary<string, object> dict:
                    {
                        var o = new JsonObject();
                        foreach (var kv in dict)
                        {
                            var child = ToJsonNode(kv.Value);
                            if (child != null) o[kv.Key] = child;
                        }
                        return o;
                    }
                case List<object> list:
                    {
                        var a = new JsonArray();
                        foreach (var item in list)
                            a.Add(ToJsonNode(item));
                        return a;
                    }
                default:
                    if (v is IConvertible c)
                    {
                        try { return JsonValue.Create(Convert.ToInt32(c, System.Globalization.CultureInfo.InvariantCulture)); }
                        catch { return JsonValue.Create(v.ToString()); }
                    }
                    return JsonValue.Create(v.ToString());
            }
        }

        sealed class BtEmitRef
        {
            public bool IsScalar;
            public int TypeCode;
            public int IntValue;
            public int Pos;
        }

        static void WriteSlot(List<byte> buf, int slot, BtEmitRef r)
        {
            if (r.IsScalar) WriteLe16(buf, slot, r.IntValue);
            else WriteLe16(buf, slot, slot - r.Pos);
        }

        static BtEmitRef EmitValue(List<byte> buf, object value)
        {
            if (value is int i)
                return new BtEmitRef { IsScalar = true, TypeCode = 1, IntValue = i };
            if (value is bool b)
                return new BtEmitRef { IsScalar = true, TypeCode = 26, IntValue = b ? 1 : 0 };
            if (value is string s)
            {
                int start = buf.Count;
                buf.AddRange(Gbk.GetBytes(s ?? ""));
                buf.Add(0);
                return new BtEmitRef { TypeCode = 5, Pos = start };
            }
            if (value is List<object> list)
                return new BtEmitRef { TypeCode = 10, Pos = EmitArray(buf, list) };
            if (value is Dictionary<string, object> dict)
                return new BtEmitRef { TypeCode = 9, Pos = EmitDict(buf, dict) };
            return new BtEmitRef { IsScalar = true, TypeCode = 1, IntValue = 0 };
        }

        static int EmitArray(List<byte> buf, List<object> items)
        {
            var refs = new List<BtEmitRef>(items.Count);
            foreach (var item in items)
                refs.Add(EmitValue(buf, item));

            int count = refs.Count;
            int countPos = buf.Count;
            buf.Add(0); buf.Add(0);
            int descStart = buf.Count;
            for (int i = 0; i < count; i++) { buf.Add(0); buf.Add(0); }
            int typeStart = buf.Count;
            for (int i = 0; i < count; i++) buf.Add(0);

            WriteLe16(buf, countPos, count);
            for (int i = 0; i < count; i++)
            {
                WriteSlot(buf, descStart + i * 2, refs[i]);
                buf[typeStart + i] = (byte)((refs[i].TypeCode << 2) | 1);
            }
            return descStart;
        }

        /// <summary>写出一个对象：先键（ASCII+NUL，保持原顺序），再值，再描述表。返回描述起点。</summary>
        static int EmitDict(List<byte> buf, Dictionary<string, object> dict)
        {
            // 键顺序与解码时一致（原版不是按字母序）。不要排序。
            var keys = new List<string>(dict.Keys);

            var keyStarts = new List<int>(keys.Count);
            foreach (var key in keys)
            {
                keyStarts.Add(buf.Count);
                buf.AddRange(Encoding.ASCII.GetBytes(key));
                buf.Add(0);
            }

            var refs = new List<BtEmitRef>(keys.Count);
            foreach (var key in keys)
                refs.Add(EmitValue(buf, dict[key]));

            int keyCount = keys.Count;
            int keyCountPos = buf.Count;
            buf.Add(0); buf.Add(0);
            int basePos = keyCountPos + 2;
            int keyEntriesStart = buf.Count;
            for (int i = 0; i < keyCount; i++) { buf.Add(0); buf.Add(0); }

            int xPos = buf.Count;
            buf.Add(0); buf.Add(0);
            int keySizePos = buf.Count;
            buf.Add(0); buf.Add(0);
            int countPos = buf.Count;
            buf.Add(0); buf.Add(0);

            int descStart = buf.Count;
            for (int i = 0; i < keyCount; i++) { buf.Add(0); buf.Add(0); }
            int typeStart = buf.Count;
            for (int i = 0; i < keyCount; i++) buf.Add(0);

            WriteLe16(buf, keyCountPos, keyCount);
            for (int i = 0; i < keyCount; i++)
            {
                int entryPos = keyEntriesStart + i * 2;
                WriteLe16(buf, entryPos, entryPos - keyStarts[i]);
            }
            WriteLe16(buf, xPos, (descStart - 6) - basePos);
            WriteLe16(buf, keySizePos, 2);
            WriteLe16(buf, countPos, keyCount);

            for (int i = 0; i < keyCount; i++)
            {
                WriteSlot(buf, descStart + i * 2, refs[i]);
                buf[typeStart + i] = (byte)((refs[i].TypeCode << 2) | 1);
            }
            return descStart;
        }

        static void WriteLe16(List<byte> buf, int pos, int value)
        {
            buf[pos] = (byte)(value & 0xFF);
            buf[pos + 1] = (byte)((value >> 8) & 0xFF);
        }

        static object ToEmit(object v)
        {
            switch (v)
            {
                case null: return 0;
                case Dictionary<string, object> d: return d;
                case List<object> list: return list;
                case bool b: return b;
                case string s: return s;
                case int i: return i;
                case JsonNode jn: return FromJsonNode(jn);
                case JsonElement je: return FromJsonNode(JsonNode.Parse(je.GetRawText()));
                default:
                    if (TryToInt(v, out int n)) return n;
                    return v.ToString() ?? "";
            }
        }

        static object FromJsonNode(JsonNode node)
        {
            if (node == null) return 0;
            if (node is JsonValue jv)
            {
                var kind = jv.GetValueKind();
                if (kind is JsonValueKind.True or JsonValueKind.False)
                    return jv.GetValue<bool>();
                if (kind == JsonValueKind.String)
                    return jv.GetValue<string>() ?? "";
                if (kind == JsonValueKind.Number)
                {
                    if (jv.TryGetValue(out int i)) return i;
                    if (jv.TryGetValue(out long l) && l >= int.MinValue && l <= int.MaxValue) return (int)l;
                    if (jv.TryGetValue(out double d) && d == Math.Truncate(d) &&
                        d >= int.MinValue && d <= int.MaxValue)
                        return (int)d;
                    return 0;
                }
                return 0;
            }
            if (node is JsonArray arr)
            {
                var list = new List<object>(arr.Count);
                foreach (var item in arr)
                    list.Add(FromJsonNode(item));
                return list;
            }
            if (node is JsonObject obj)
            {
                var dict = new Dictionary<string, object>(StringComparer.Ordinal);
                foreach (var kv in obj)
                {
                    if (kv.Value == null || kv.Value.GetValueKind() == JsonValueKind.Null)
                        continue;
                    dict[kv.Key] = FromJsonNode(kv.Value);
                }
                return dict;
            }
            return 0;
        }

        static bool TryToInt(object v, out int n)
        {
            n = 0;
            try
            {
                switch (v)
                {
                    case byte u8: n = u8; return true;
                    case sbyte i8: n = i8; return true;
                    case short s: n = s; return true;
                    case ushort us: n = us; return true;
                    case int i: n = i; return true;
                    case uint ui when ui <= int.MaxValue: n = (int)ui; return true;
                    case long l when l >= int.MinValue && l <= int.MaxValue: n = (int)l; return true;
                    case bool b: n = b ? 1 : 0; return true;
                    default: return false;
                }
            }
            catch
            {
                return false;
            }
        }
    }
}
