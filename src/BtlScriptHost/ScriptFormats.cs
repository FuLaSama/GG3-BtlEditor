using BtlCore.Fb;
using BtlCore.Front;
using MoonSharp.Interpreter;

namespace BtlCore.Scripting;

public sealed partial class ScriptHost
{
    sealed record PayloadCodec(string Id, string Revision, string SourceHash, DynValue Decode, DynValue Encode);
    readonly Dictionary<string, string> _formatSchemas = new(StringComparer.Ordinal);
    readonly Dictionary<string, PayloadCodec> _payloadCodecs = new(StringComparer.Ordinal);
    readonly Dictionary<SchemaContext, SchemaContext> _formatContexts = new();
    string _formatSourceHash;
    bool _insideCodec;
    const int MaxPayloadBytes = 2_000_000;

    /// <summary>在打开文件前构造固定解释；不修改当前文档或全局默认 schema。</summary>
    public SchemaContext CreateSchemaContext(SchemaContext basis = null)
    {
        EnsureIdle();
        return FormatContext(basis ?? SoftSchema.DefaultContext);
    }

    SchemaContext FormatContext(SchemaContext context)
    {
        var basis = context.BaseContext ?? context;
        if (!_formatContexts.TryGetValue(basis, out var result))
            _formatContexts[basis] = result = basis.WithOverlays(_formatSchemas.Values);
        return result;
    }

    void InstallFormatApi(Table editor)
    {
        var format = new Table(_script);
        format.Set("schema", Api("editor.format.schema", (_, args) =>
        {
            RequireRegistration();
            var table = Declaration(args);
            CheckKeys(table, "格式声明", "id", "definition");
            string id = Text(table, "id", true), definition = Text(table, "definition", true);
            if (_formatSchemas.ContainsKey(id)) throw new FrontEditException("格式声明 id 重复：" + id);
            // 完整引用验证在全部脚本加载后进行，允许引用后加载的类型。
            if (definition.Length > 1_000_000) throw new FrontEditException("格式声明过大");
            _formatSchemas.Add(id, definition);
            _formatContexts.Clear();
            return DynValue.Nil;
        }));
        format.Set("codec", Api("editor.format.codec", (_, args) =>
        {
            RequireRegistration();
            var table = Declaration(args);
            CheckKeys(table, "编解码器", "id", "revision", "decode", "encode");
            string id = Text(table, "id", true), revision = Text(table, "revision", true);
            if (_payloadCodecs.ContainsKey(id)) throw new FrontEditException("编解码器 id 重复：" + id);
            var decode = table.Get("decode"); var encode = table.Get("encode");
            if (decode.Type != DataType.Function || encode.Type != DataType.Function)
                throw new FrontEditException("编解码器需要 decode 和 encode 函数");
            _payloadCodecs.Add(id, new(id, revision, _formatSourceHash, decode, encode));
            return DynValue.Nil;
        }));

        // 回调保持在同一个受预算控制的 Lua 协程内执行，避免从 C# 回调重入 Invoke。
        var wrappers = _script.LoadString("""
            local begin_read, begin_write, finish, commit = ...
            local pcall, error, type = pcall, error, type
            local function decode(id, path)
              local fn, bytes = begin_read(id, path)
              local ok, value = pcall(fn, bytes)
              finish()
              if not ok then error('decode [' .. id .. '] ' .. path .. ': ' .. (type(value)=='string' and value or '编解码器返回了非文本错误'), 2) end
              return value
            end
            local function encode(id, path, value)
              local fn = begin_write(id, path)
              local ok, bytes = pcall(fn, value)
              finish()
              if not ok then error('encode [' .. id .. '] ' .. path .. ': ' .. (type(bytes)=='string' and bytes or '编解码器返回了非文本错误'), 2) end
              commit(id, path, bytes)
            end
            return {read=decode, write=encode}
            """, codeFriendlyName: "payload codec wrappers");
        var functions = _script.Call(wrappers,
            Api("editor.format.read", (_, a) => BeginPayload(a, write: false)),
            Api("editor.format.write", (_, a) => BeginPayload(a, write: true)),
            DynValue.NewCallback((_, _) => { _insideCodec = false; return DynValue.Nil; }),
            Api("editor.format.write", (_, a) => CommitPayload(a))).Table;
        format.Set("read", functions.Get("read"));
        format.Set("write", functions.Get("write"));
        editor.Set("format", DynValue.NewTable(format));
        InstallBinaryApi(editor);
    }

    static Table Declaration(CallbackArguments args) => args.Count == 1 && args[0].Type == DataType.Table
        ? args[0].Table : throw new FrontEditException("需要一个声明表");

    PayloadCodec FindCodec(string id, string path)
    {
        if (Document?.Root == null) throw new FrontEditException("没有可读取的关卡");
        if (FormatContext(Document.Context).Fingerprint != Document.Context.Fingerprint)
            throw new FrontEditException("格式声明已变化，请重新打开关卡以应用新的解释");
        if (!_payloadCodecs.TryGetValue(id, out var codec)) throw new FrontEditException("未登记编解码器：" + id);
        if (!NsPath.IsNs(path)) throw new FrontEditException("载荷需要数字路径");
        var previous = Document.PayloadCodecUses.LastOrDefault(use => use.Path == path);
        if (previous != null && (previous.CodecId != id || previous.Revision != codec.Revision || previous.SourceSha256 != codec.SourceHash))
            throw new FrontEditException("载荷的编解码器版本不匹配，请加载原脚本或重新打开原始 BTL");
        return codec;
    }

    BtlVector PayloadVector(string path)
    {
        if (NsPath.NodeAt(Document, path) is not BtlVector vector || vector.Elem != "u8" || !string.IsNullOrEmpty(vector.Enc))
            throw new FrontEditException("载荷必须是已存在的普通 u8 向量：" + path);
        if (vector.V.Count > MaxPayloadBytes) throw new FrontEditException("载荷超过字节预算");
        return vector;
    }

    DynValue BeginPayload(CallbackArguments args, bool write)
    {
        if (args.Count != 2) throw new FrontEditException("需要编解码器 id 和数字路径");
        if (write) RequireWrite();
        string id = ArgString(args, 0), path = NsPath.CanonicalPath(ArgString(args, 1));
        var codec = FindCodec(id, path);
        var vector = PayloadVector(path);
        var bytes = new Table(_script);
        if (!write)
            for (int i = 0; i < vector.V.Count; i++) bytes.Set(i + 1, ToDyn(FrontPath.Coerce("u8", vector.V[i])));
        _insideCodec = true;
        return write ? codec.Encode : DynValue.NewTuple(codec.Decode, DynValue.NewTable(bytes));
    }

    DynValue CommitPayload(CallbackArguments args)
    {
        RequireWrite();
        string id = ArgString(args, 0), path = NsPath.CanonicalPath(ArgString(args, 1));
        var codec = FindCodec(id, path);
        var vector = PayloadVector(path);
        byte[] bytes = ReadBytes(args[2]); // 完整验证后才修改文档，Lua pcall 捕获错误也不会留下半次写入。
        if (bytes.Length == vector.V.Count && bytes.Cast<object>().Zip(vector.V).All(p => Convert.ToDecimal(p.First) == Convert.ToDecimal(p.Second)))
            return DynValue.Nil;
        FrontEdit.Fill(vector, bytes.Cast<object>());
        Document.Editing.BeforeCommit(() =>
        {
            var uses = Document.PayloadCodecUses.Where(use => use.Path != path).ToList();
            uses.Add(new(path, id, codec.Revision, codec.SourceHash));
            Document.PayloadCodecUses = uses.AsReadOnly();
        });
        return DynValue.Nil;
    }

    static byte[] ReadBytes(DynValue value)
    {
        if (value.Type != DataType.Table) throw new FrontEditException("字节数据必须是连续的 1 起数组");
        var table = value.Table;
        int length = table.Length;
        if (length > MaxPayloadBytes) throw new FrontEditException("载荷超过字节预算");
        if (table.Pairs.Any(p => p.Key.Type != DataType.Number || p.Key.Number < 1 || p.Key.Number > length || p.Key.Number != Math.Truncate(p.Key.Number)))
            throw new FrontEditException("字节数组不能有空洞或额外键");
        var result = new byte[length];
        for (int i = 0; i < result.Length; i++) result[i] = ByteValue(table.Get(i + 1));
        return result;
    }

    static byte ByteValue(DynValue value) => value.Type == DataType.Number && value.Number >= 0 && value.Number <= 255 && value.Number == Math.Truncate(value.Number)
        ? (byte)value.Number : throw new FrontEditException("字节必须是 0–255 的整数");
}
