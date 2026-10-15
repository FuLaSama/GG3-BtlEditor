using System.Text.Json;
using System.Text.Json.Nodes;
using BtlCore.Front;

namespace BtlCore.Fb;

/// <summary>Root/10 的共用行为树库。旧类名和 enc 保留兼容；实际格式为 FlexBuffers。</summary>
public static class PackedJsonStream
{
    public const string EncName = "country_ai_bt";
    public const int MaxPayloadBytes = 16_000_000;
    // 有效的 FlexBuffers 空 Vector，不是专用文件头。
    public static readonly byte[] EmptyHeader = { 0, 0, 0x28, 1 };
    public static bool EncIsPackedJson(string enc) => string.Equals(enc, EncName, StringComparison.OrdinalIgnoreCase);
    public static bool IsEmptyHeader(IList<byte> bytes) => bytes != null && bytes.SequenceEqual(EmptyHeader);

    // 仅用于查看器显示尝试解码入口，不是格式合法性判断。
    public static bool LooksLike(IList<byte> bytes)
    {
        if (bytes == null || bytes.Count < 3) return false;
        if (bytes[^2] >> 2 == 10) return true;
        for (int i = 0; i + 4 < bytes.Count; i++)
            if (bytes[i] == 'b' && bytes[i + 1] == 't' && bytes[i + 2] == 'i' && bytes[i + 3] == 'd' && bytes[i + 4] == 0) return true;
        return false;
    }
    public static bool TryDecode(IList<byte> bytes, out List<JsonNode> nodes)
    {
        nodes = null;
        if (bytes == null || bytes.Count < 3 || bytes.Count > MaxPayloadBytes) return false;
        try
        {
            var value = FlexBuffersCodec.Decode(bytes.ToArray());
            if (value is not JsonArray array || array.Any(n => n is not JsonObject)) return false;
            nodes = array.Select(n => n.DeepClone()).ToList();
            return true;
        }
        catch (Exception ex) when (ex is InvalidDataException or OverflowException or System.Text.DecoderFallbackException)
        { return false; }
    }
    public static bool TryUnpackVector(BtlVector vec)
    {
        if (vec == null) return false;
        if (!string.IsNullOrEmpty(vec.Enc)) return EncIsPackedJson(vec.Enc);
        if (vec.V.Count == 0 || vec.V.Count > MaxPayloadBytes) return false;
        var raw = new byte[vec.V.Count];
        for (int i = 0; i < raw.Length; i++) if (!TryByte(vec.V[i], out raw[i])) return false;
        if (!TryDecode(raw, out var nodes)) return false;
        vec.PackedSource = raw;
        vec.Enc = EncName;
        vec.Elem = "u8";
        vec.V.Clear(); vec.V.AddRange(nodes);
        return true;
    }
    public static bool ShouldEncode(BtlVector vec, out byte[] packed)
    {
        packed = null;
        if (vec == null || !EncIsPackedJson(vec.Enc)) return false;
        var current = ToLibrary(vec.V);
        // 比较实际内容，不依赖写接口 dirty；Lua、注册表和 JSON 修改都能被识别。
        if (!vec.PackedSource.IsEmpty && TryDecode(vec.PackedSource.ToArray(), out var original)
            && JsonNode.DeepEquals(current, new JsonArray(original.ToArray())))
            packed = vec.PackedSource.ToArray();
        else packed = FlexBuffersCodec.Encode(current);
        // 错误向上传播，不能回退为空树库。
        return true;
    }
    public static byte[] Encode(IList<object> items) => FlexBuffersCodec.Encode(ToLibrary(items));
    static JsonArray ToLibrary(IList<object> items)
    {
        var array = new JsonArray();
        if (items == null) return array;
        if (items.Count > FlexBuffersCodec.MaxItems) throw new InvalidDataException("行为树库超过元素预算");
        foreach (var item in items)
        {
            JsonNode node = item is JsonNode json ? json.DeepClone() : JsonSerializer.SerializeToNode(item);
            if (node is not JsonObject) throw new InvalidDataException("行为树库元素必须是对象");
            array.Add(node);
        }
        return array;
    }
    static bool TryByte(object value, out byte result)
    {
        result = 0;
        if (value is JsonValue json && json.TryGetValue(out int n) && n is >= 0 and <= 255) { result = (byte)n; return true; }
        if (value is not (byte or sbyte or short or ushort or int or uint or long or ulong)) return false;
        decimal number = Convert.ToDecimal(value);
        if (number is < 0 or > 255) return false;
        result = (byte)number; return true;
    }
}
