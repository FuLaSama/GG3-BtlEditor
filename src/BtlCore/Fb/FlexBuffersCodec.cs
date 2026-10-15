using System.Globalization;
using System.Numerics;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace BtlCore.Fb;

/// <summary>FlexBuffers JSON 值子集。游戏 String 使用严格 GB18030，Key 使用 UTF-8。
/// 不支持的类型拒绝解包，由调用方原样保存字节，不猜测 Blob 或未知类型。</summary>
internal static class FlexBuffersCodec
{
    internal const int MaxItems = 200_000;
    const int MaxDepth = 64;
    static readonly Encoding TextEncoding = GameEncoding();
    static readonly Encoding KeyEncoding = new UTF8Encoding(false, true);
    static Encoding GameEncoding()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        return Encoding.GetEncoding(54936, EncoderFallback.ExceptionFallback, DecoderFallback.ExceptionFallback);
    }
    internal static JsonNode Decode(byte[] bytes) => new Reader(bytes).Root();
    internal static byte[] Encode(JsonNode value) => new Writer().Root(value);
    static bool Width(int width) => width is 1 or 2 or 4 or 8;
    static int Bits(int width) => width switch { 1 => 0, 2 => 1, 4 => 2, 8 => 3, _ => throw new InvalidDataException("FlexBuffers 非法宽度") };
    static int UnsignedWidth(ulong value) => value <= byte.MaxValue ? 1 : value <= ushort.MaxValue ? 2 : value <= uint.MaxValue ? 4 : 8;
    static int SignedWidth(long value) => value is >= sbyte.MinValue and <= sbyte.MaxValue ? 1
        : value is >= short.MinValue and <= short.MaxValue ? 2 : value is >= int.MinValue and <= int.MaxValue ? 4 : 8;
    static (BigInteger Coefficient, int Exponent) DecimalValue(string text)
    {
        if (text.Length > 512) throw new InvalidDataException("FlexBuffers 数字文本过长");
        int e = text.IndexOfAny(new[] {'e','E'}), exponent = 0;
        if (e >= 0)
        {
            if (!int.TryParse(text[(e+1)..], NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out exponent))
                throw new InvalidDataException("FlexBuffers 数值指数越界");
            text = text[..e];
        }
        int dot = text.IndexOf('.');
        if (dot >= 0) { exponent = checked(exponent - (text.Length - dot - 1)); text = text.Remove(dot, 1); }
        var coefficient = BigInteger.Parse(text, CultureInfo.InvariantCulture);
        if (coefficient.IsZero) return (BigInteger.Zero, 0);
        while (coefficient % 10 == 0) { coefficient /= 10; exponent = checked(exponent + 1); }
        return (coefficient, exponent);
    }

    sealed class Reader(byte[] data)
    {
        int remaining = MaxItems, textBudget = 8_000_000;
        void Check(long pos, long size)
        {
            if (pos < 0 || size < 0 || pos > data.Length || size > data.Length - pos)
                throw new InvalidDataException("FlexBuffers 内容越界或截断");
        }
        ulong UInt(int pos, int width)
        {
            if (!Width(width)) throw new InvalidDataException("FlexBuffers 非法宽度");
            Check(pos, width);
            ulong value = 0;
            for (int i = 0; i < width; i++) value |= (ulong)data[pos + i] << (8 * i);
            return value;
        }
        long Int(int pos, int width)
        {
            ulong value = UInt(pos, width);
            return unchecked((long)(value << (64 - width * 8))) >> (64 - width * 8);
        }
        int Target(int pos, int width)
        {
            ulong offset = UInt(pos, width);
            if (offset > (ulong)pos) throw new InvalidDataException("FlexBuffers 反向偏移越界");
            return pos - (int)offset;
        }
        int Count(int pos, int width)
        {
            ulong count = UInt(pos, width);
            if (count > MaxItems) throw new InvalidDataException("FlexBuffers 容器超过元素预算");
            return (int)count;
        }
        string Text(int pos, int width, bool key)
        {
            Check(pos, 0);
            int length;
            if (key)
            {
                int end = Array.IndexOf(data, (byte)0, pos);
                if (end < 0) throw new InvalidDataException("FlexBuffers Key 缺少终止符");
                length = end - pos;
            }
            else
            {
                ulong count = UInt(pos - width, width);
                if (count > int.MaxValue) throw new InvalidDataException("FlexBuffers 字符串长度越界");
                length = (int)count;
                Check(pos, (long)length + 1);
                if (data[pos + length] != 0) throw new InvalidDataException("FlexBuffers String 缺少终止符");
            }
            textBudget -= length;
            if (textBudget < 0) throw new InvalidDataException("FlexBuffers 文本超过总预算");
            return (key ? KeyEncoding : TextEncoding).GetString(data, pos, length);
        }
        internal JsonNode Root()
        {
            if (data.Length < 3 || data.Length > PackedJsonStream.MaxPayloadBytes) throw new InvalidDataException("FlexBuffers 长度非法");
            int width = data[^1];
            if (!Width(width)) throw new InvalidDataException("FlexBuffers 根槽宽非法");
            return Value(data.Length - 2 - width, width, data[^2], 0);
        }
        JsonNode Signed(int pos, int width)
        {
            long value = Int(pos, width);
            return value is >= int.MinValue and <= int.MaxValue ? JsonValue.Create((int)value) : JsonValue.Create(value);
        }
        JsonNode Unsigned(int pos, int width)
        {
            ulong value = UInt(pos, width);
            return value <= int.MaxValue ? JsonValue.Create((int)value) : value <= long.MaxValue ? JsonValue.Create((long)value) : JsonValue.Create(value);
        }
        JsonNode Float(int pos, int width)
        {
            double value = width switch
            {
                4 => BitConverter.Int32BitsToSingle(unchecked((int)UInt(pos, 4))),
                8 => BitConverter.Int64BitsToDouble(unchecked((long)UInt(pos, 8))),
                _ => throw new InvalidDataException("FlexBuffers 浮点宽度非法")
            };
            if (!double.IsFinite(value)) throw new InvalidDataException("FlexBuffers 非有限浮点无法表示为 JSON");
            return JsonValue.Create(value);
        }
        JsonNode Value(int pos, int parentWidth, int packed, int depth)
        {
            if (--remaining < 0 || depth > MaxDepth) throw new InvalidDataException("FlexBuffers 超过节点或深度预算");
            Check(pos, parentWidth);
            int type = packed >> 2, width = 1 << (packed & 3);
            switch (type)
            {
                case 0: return null;
                case 1: return Signed(pos, parentWidth);
                case 2: return Unsigned(pos, parentWidth);
                case 3: return Float(pos, parentWidth);
                case 26: return JsonValue.Create(UInt(pos, parentWidth) != 0);
            }
            int target = Target(pos, parentWidth);
            if (type is 4 or 5) return JsonValue.Create(Text(target, width, type == 4));
            if (type == 6) return Signed(target, width);
            if (type == 7) return Unsigned(target, width);
            if (type == 8) return Float(target, width);
            if (type is 9 or 10) return Container(target, width, type == 9, depth);
            int elementType = type switch { 11 => 1, 12 => 2, 13 => 3, 14 => 4, 36 => 26, _ => -1 };
            int count = -1;
            if (type is >= 16 and <= 24) { elementType = 1 + (type - 16) % 3; count = 2 + (type - 16) / 3; }
            if (elementType < 0) throw new InvalidDataException("FlexBuffers 类型暂不支持：" + type);
            if (count < 0) count = Count(target - width, width);
            if (count > remaining) throw new InvalidDataException("FlexBuffers 超过节点预算");
            Check(target, (long)count * width);
            var array = new JsonArray();
            for (int i = 0; i < count; i++) array.Add(Value(target + i * width, width, elementType << 2, depth + 1));
            return array;
        }
        JsonNode Container(int target, int width, bool map, int depth)
        {
            int count = Count(target - width, width);
            if (count > remaining) throw new InvalidDataException("FlexBuffers 超过节点预算");
            Check(target, (long)count * (width + 1));
            if (!map)
            {
                var array = new JsonArray();
                for (int i = 0; i < count; i++) array.Add(Value(target + i * width, width, data[target + count * width + i], depth + 1));
                return array;
            }
            int keys = Target(target - 3 * width, width);
            ulong keyWidthValue = UInt(target - 2 * width, width);
            if (keyWidthValue > 8 || !Width((int)keyWidthValue)) throw new InvalidDataException("FlexBuffers Map 键槽宽非法");
            int keyWidth = (int)keyWidthValue;
            if (Count(keys - keyWidth, keyWidth) != count) throw new InvalidDataException("FlexBuffers Map 键值数量不同");
            Check(keys, (long)count * keyWidth);
            var obj = new JsonObject();
            byte[] previous = null;
            for (int i = 0; i < count; i++)
            {
                string key = Text(Target(keys + i * keyWidth, keyWidth), 1, true);
                byte[] encoded = KeyEncoding.GetBytes(key);
                if (previous != null && previous.AsSpan().SequenceCompareTo(encoded) >= 0)
                    throw new InvalidDataException("FlexBuffers Map 键未排序或重复");
                previous = encoded;
                obj[key] = Value(target + i * width, width, data[target + count * width + i], depth + 1);
            }
            return obj;
        }
    }

    sealed record Ref(int Type, int Width, ulong Number = 0, double Real = 0, int Position = -1)
    {
        internal byte Packed => (byte)((Type << 2) | Bits(Width));
    }
    sealed class Writer
    {
        readonly List<byte> buffer = new();
        int remaining = MaxItems, textBudget = 8_000_000;
        void Add(byte value)
        {
            if (buffer.Count >= PackedJsonStream.MaxPayloadBytes) throw new InvalidDataException("FlexBuffers 输出超过字节预算");
            buffer.Add(value);
        }
        void UInt(ulong value, int width) { for (int i = 0; i < width; i++) Add((byte)(value >> (8 * i))); }
        void Align(int width) { while (buffer.Count % width != 0) Add(0); }
        static int Aligned(int pos, int width) => checked((pos + width - 1) & -width);
        void Bytes(byte[] bytes)
        {
            textBudget -= bytes.Length;
            if (textBudget < 0) throw new InvalidDataException("FlexBuffers 文本超过总预算");
            foreach (byte value in bytes) Add(value);
        }
        void Slot(Ref value, int width)
        {
            if (value.Position >= 0) UInt(checked((ulong)(buffer.Count - value.Position)), width);
            else if (value.Type == 3) UInt(width == 4 ? unchecked((uint)BitConverter.SingleToInt32Bits((float)value.Real))
                : unchecked((ulong)BitConverter.DoubleToInt64Bits(value.Real)), width);
            else UInt(value.Number, width);
        }
        int ContainerWidth(IReadOnlyList<Ref> values, Ref keys = null)
        {
            foreach (int width in new[] {1, 2, 4, 8})
            {
                if (UnsignedWidth((ulong)values.Count) > width || values.Any(v => v.Position < 0 && v.Width > width)) continue;
                int start = Aligned(buffer.Count, width), data = checked(start + (keys == null ? 1 : 3) * width);
                if (keys != null && UnsignedWidth(checked((ulong)(start - keys.Position))) > width) continue;
                bool fit = true;
                for (int i = 0; i < values.Count; i++)
                    if (values[i].Position >= 0 && UnsignedWidth(checked((ulong)(data + i * width - values[i].Position))) > width) { fit = false; break; }
                if (fit) return width;
            }
            throw new InvalidDataException("FlexBuffers 无法确定容器宽度");
        }
        Ref Vector(IReadOnlyList<Ref> values, bool typedKeys = false, Ref keys = null)
        {
            int width = ContainerWidth(values, keys);
            Align(width);
            if (keys != null) { UInt(checked((ulong)(buffer.Count - keys.Position)), width); UInt((ulong)keys.Width, width); }
            UInt((ulong)values.Count, width);
            int data = buffer.Count;
            foreach (var value in values) Slot(value, width);
            if (!typedKeys) foreach (var value in values)
                Add((byte)((value.Type << 2) | Bits(value.Position < 0 ? width : value.Width)));
            return new Ref(keys == null ? 10 : 9, width, Position: data);
        }
        internal byte[] Root(JsonNode node)
        {
            Ref value = Value(node, 0);
            int width = value.Width;
            if (value.Position >= 0)
                foreach (int candidate in new[] {1, 2, 4, 8})
                    if (UnsignedWidth(checked((ulong)(Aligned(buffer.Count, candidate) - value.Position))) <= candidate) { width = candidate; break; }
            Align(width); Slot(value, width); Add(value.Packed); Add((byte)width);
            return buffer.ToArray();
        }
        Ref Value(JsonNode node, int depth)
        {
            if (--remaining < 0 || depth > MaxDepth) throw new InvalidDataException("FlexBuffers 超过节点或深度预算");
            if (node == null) return new Ref(0, 1);
            if (node is JsonArray array)
            {
                if (array.Count > remaining) throw new InvalidDataException("FlexBuffers 超过节点预算");
                return Vector(array.Select(n => Value(n, depth + 1)).ToArray());
            }
            if (node is JsonObject obj)
            {
                if (obj.Count > remaining) throw new InvalidDataException("FlexBuffers 超过节点预算");
                var entries = obj.Select(p => (Key: KeyEncoding.GetBytes(p.Key), Value: p.Value)).ToList();
                entries.Sort((a,b) => a.Key.AsSpan().SequenceCompareTo(b.Key));
                var keyRefs = new List<Ref>();
                foreach (var entry in entries)
                {
                    if (entry.Key.Contains((byte)0)) throw new InvalidDataException("FlexBuffers Key 不能包含 NUL");
                    int start = buffer.Count; Bytes(entry.Key); Add(0); keyRefs.Add(new Ref(4, 1, Position: start));
                }
                Ref keys = Vector(keyRefs, typedKeys: true);
                return Vector(entries.Select(p => Value(p.Value, depth + 1)).ToArray(), keys: keys);
            }
            if (node.GetValueKind() == JsonValueKind.String)
            {
                byte[] bytes = TextEncoding.GetBytes(node.GetValue<string>());
                int width = UnsignedWidth((ulong)bytes.Length);
                Align(width); UInt((ulong)bytes.Length, width);
                int start = buffer.Count; Bytes(bytes); Add(0);
                return new Ref(5, width, Position: start);
            }
            if (node.GetValueKind() is JsonValueKind.True or JsonValueKind.False) return new Ref(26, 1, node.GetValue<bool>() ? 1UL : 0UL);
            if (node.GetValueKind() == JsonValueKind.Null) return new Ref(0, 1);
            if (node.GetValueKind() != JsonValueKind.Number) throw new InvalidDataException("FlexBuffers 不支持的 JSON 值");
            string text = node.ToJsonString();
            bool storedFloat = node is JsonValue scalar && !scalar.TryGetValue<JsonElement>(out _)
                && (scalar.TryGetValue<double>(out _) || scalar.TryGetValue<float>(out _));
            if (!storedFloat && long.TryParse(text, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out long signed))
                return new Ref(1, SignedWidth(signed), unchecked((ulong)signed));
            if (!storedFloat && ulong.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out ulong unsigned))
                return new Ref(2, UnsignedWidth(unsigned), unsigned);
            if (!double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out double real) || !double.IsFinite(real))
                throw new InvalidDataException("FlexBuffers 数值超出范围");
            if (!storedFloat && !text.Contains('.') && !text.Contains('e') && !text.Contains('E')) throw new InvalidDataException("FlexBuffers 整数超过 64 位范围");
            if (DecimalValue(text) != DecimalValue(real.ToString("R", CultureInfo.InvariantCulture)))
                throw new InvalidDataException("FlexBuffers 浮点转换会丢失 JSON 数值精度");
            return new Ref(3, (double)(float)real == real ? 4 : 8, Real: real);
        }
    }
}
