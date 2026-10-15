using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using BtlCore.Fb;
using BtlCore.Front;

namespace BtlCore.Scripting;

public sealed record FieldDefinition(string Id, string Owner, string Path, int? Shift, int? Width, string Note,
    string Parent = null, string Kind = "value", string Revision = null, string Confidence = "unknown", string Evidence = null);
public sealed record ResolvedField(FieldDefinition Definition, string StorageType, string DisplayType,
    string StructPath, IReadOnlyList<string> Layout, string Path);

/// <summary>Lua 对象与 XML 共用的字段位置/位段描述。物理类型由文档 schema 提供。</summary>
public sealed class FieldCatalog
{
    sealed record CatalogFile(int Version, List<FieldDefinition> Fields, string Revision = null, string Evidence = null);
    static readonly JsonSerializerOptions Options = new()
    {
        PropertyNameCaseInsensitive = true,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow
    };
    readonly IReadOnlyDictionary<string, FieldDefinition> _fields;
    public string DefinitionJson { get; }
    public string Fingerprint { get; }
    public IReadOnlyCollection<FieldDefinition> Definitions => _fields.Values.ToArray();
    readonly System.Runtime.CompilerServices.ConditionalWeakTable<SchemaContext, System.Collections.Concurrent.ConcurrentDictionary<string, ResolvedField>> _resolved = new();
    public static FieldCatalog Builtin { get; } = LoadBuiltin();

    FieldCatalog(Dictionary<string, FieldDefinition> fields)
    {
        _fields = fields;
        DefinitionJson = JsonSerializer.Serialize(new CatalogFile(1, fields.Values.OrderBy(f => f.Id, StringComparer.Ordinal).ToList()), Options);
        Fingerprint = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(DefinitionJson))).ToLowerInvariant();
    }

    static FieldCatalog LoadBuiltin()
    {
        FieldCatalog result = null;
        foreach (string name in new[] { "unit", "site" })
        {
            using var stream = typeof(FieldCatalog).Assembly.GetManifestResourceStream("fields." + name + ".json");
            using var reader = new StreamReader(stream ?? throw new InvalidDataException("缺少内置字段目录：" + name));
            var catalog = Parse(reader.ReadToEnd());
            result = result == null ? catalog : result.Overlay(catalog);
        }
        return result;
    }

    public static FieldCatalog Parse(string json)
    {
        var file = JsonSerializer.Deserialize<CatalogFile>(json, Options);
        if (file?.Version != 1 || file.Fields == null) throw new InvalidDataException("字段目录 version 必须为 1，并声明 fields。");
        var fields = new Dictionary<string, FieldDefinition>(StringComparer.Ordinal);
        foreach (var source in file.Fields)
        {
            var field = source == null ? null : source with { Revision = source.Revision ?? file.Revision, Evidence = source.Evidence ?? file.Evidence };

            if (field == null || string.IsNullOrWhiteSpace(field.Id) || string.IsNullOrWhiteSpace(field.Owner)
                || field.Path == null || !Regex.IsMatch(field.Path, @"^(?:(?:/\d+)+(?:\.\d+)?|\.\d+)$"))
                throw new InvalidDataException("字段定义需要 id、owner 和相对数字路径。");
            if (field.Width.HasValue != field.Shift.HasValue || field.Width is <= 0 or > 32 || field.Shift is < 0 or > 31)
                throw new InvalidDataException(field.Id + " 的位段需要有效的 shift 和 width（首版最多 32 位）。");
            if (field.Kind is not ("value" or "table" or "struct") || field.Confidence is not ("confirmed" or "tentative" or "unknown"))
                throw new InvalidDataException(field.Id + " 的 kind 或 confidence 无效。");
            if (field.Kind != "value" && field.Width != null) throw new InvalidDataException("配置组不能声明位段。");
            if (!fields.TryAdd(field.Id, field)) throw new InvalidDataException("重复字段：" + field.Id);
        }
        return new FieldCatalog(fields);
    }

    public FieldCatalog Overlay(FieldCatalog other)
    {
        var merged = new Dictionary<string, FieldDefinition>(_fields, StringComparer.Ordinal);
        foreach (var pair in other._fields) merged[pair.Key] = pair.Value;
        return new FieldCatalog(merged);
    }

    public ResolvedField Resolve(string id, SchemaContext context) =>
        _resolved.GetOrCreateValue(context).GetOrAdd(id, key => ResolveCore(key, context));

    public void ValidateReferences()
    {
        foreach (string id in _fields.Keys) _ = EffectivePath(id);
    }

    string EffectivePath(string id, HashSet<string> visiting = null)
    {
        visiting ??= new(StringComparer.Ordinal);
        if (!visiting.Add(id)) throw new FrontEditException("字段目录出现循环父级：" + id);
        if (!_fields.TryGetValue(id, out var field)) throw new FrontEditException("找不到公共字段：" + id);
        if (string.IsNullOrEmpty(field.Parent))
        {
            if (!field.Path.StartsWith('/')) throw new FrontEditException(id + " 的成员路径需要父级结构体。");
            return field.Path;
        }
        if (!_fields.TryGetValue(field.Parent, out var parent) || parent.Owner != field.Owner
            || parent.Kind == "value" || (parent.Kind == "struct") != field.Path.StartsWith('.'))
            throw new FrontEditException(id + " 的父级类型或 owner 不匹配。");
        return EffectivePath(field.Parent, visiting) + field.Path;
    }

    public IReadOnlyList<string> ChangedFields(FieldCatalog previous) => _fields.Keys.Union(previous._fields.Keys)
        .Where(id => Signature(id) != previous.Signature(id)).OrderBy(id => id, StringComparer.Ordinal).ToArray();

    string Signature(string id)
    {
        if (!_fields.TryGetValue(id, out var field)) return null;
        _ = EffectivePath(id); // 检查循环，再追踪全部祖先的变化。
        return JsonSerializer.Serialize(field, Options) + (field.Parent == null ? "" : Signature(field.Parent));
    }

    ResolvedField ResolveCore(string id, SchemaContext context)
    {
        if (!_fields.TryGetValue(id, out var field)) throw new FrontEditException("找不到公共字段：" + id);
        string path = EffectivePath(id);
        string type = field.Owner;
        string[] parts = path.Split('/', StringSplitOptions.RemoveEmptyEntries);
        for (int i = 0; i < parts.Length; i++)
        {
            var part = parts[i].Split('.');
            int slot = int.Parse(part[0], CultureInfo.InvariantCulture);
            if (!context.TryGet(type, slot, out var hint)) throw new FrontEditException(id + " 与当前 schema 不匹配。");
            if (i < parts.Length - 1)
            {
                if (hint.Kind != FieldKind.Table) throw new FrontEditException(id + " 的中间路径必须是表。");
                type = hint.ChildTable;
                continue;
            }
            if (field.Kind != "value")
            {
                bool valid = part.Length == 1 && (field.Kind == "table" ? hint.Kind == FieldKind.Table : hint.Kind == FieldKind.Struct);
                if (!valid) throw new FrontEditException(id + " 的配置组与 schema 不匹配。");
                return new ResolvedField(field, field.Kind, "bool", null,
                    hint.StructMembers?.Select(m => m.T).ToArray(), path);
            }
            string storage;
            string structPath = null;
            string[] layout = null;
            if (part.Length == 2)
            {
                int member = int.Parse(part[1], CultureInfo.InvariantCulture);
                if (hint.Kind != FieldKind.Struct || member >= hint.StructMembers.Length)
                    throw new FrontEditException(id + " 的结构体成员不存在。");
                layout = hint.StructMembers.Select(m => m.T).ToArray();
                storage = layout[member];
                structPath = path[..path.LastIndexOf('.')];
            }
            else storage = hint.Kind switch
            {
                FieldKind.Scalar or FieldKind.Bool => hint.ScalarT,
                FieldKind.String => "string",
                _ => throw new FrontEditException(id + " 必须指向标量或结构体成员。")
            };
            string display = storage;
            if (field.Width is int width)
            {
                int bits = storage switch { "u8" => 8, "u16" => 16, "u32" => 32, _ => 0 };
                if (field.Shift.Value + width > bits) throw new FrontEditException(id + " 的位段超过存储类型。");
                display = width <= 8 ? "u8" : width <= 16 ? "u16" : "u32";
            }
            return new ResolvedField(field, storage, display, structPath, layout, path);
        }
        throw new FrontEditException("字段路径为空。");
    }

    public object Read(BtlFrontDocument document, BtlTable owner, string id)
    {
        var field = Resolve(id, document.Context);
        var row = new BtlFrontDocument { Context = document.Context, Root = owner };
        if (field.Definition.Kind != "value") return NsPath.NodeAt(row, field.Path) != null;
        object value = NsPath.Get(row, field.Path);
        if (value == null || field.Definition.Width == null) return value;
        ulong mask = (1UL << field.Definition.Width.Value) - 1;
        return (Convert.ToUInt64(value, CultureInfo.InvariantCulture) >> field.Definition.Shift.Value) & mask;
    }

    internal void Write(BtlFrontDocument document, BtlTable owner, string id, object value)
    {
        var field = Resolve(id, document.Context);
        var row = new BtlFrontDocument { Context = document.Context, Root = owner };
        if (field.Definition.Kind != "value")
        {
            if (value is not null and not bool) throw new FrontEditException(id + " 的存在开关需要布尔值。");
            if (value is true)
            {
                var existing = NsPath.NodeAt(row, field.Path);
                if (existing != null && (field.Definition.Kind == "table" ? existing is not BtlTable : existing is not BtlStruct))
                    throw new FrontEditException(id + " 的节点类型不匹配。");
                if (existing is BtlStruct structure) ValidateLayout(structure, field.Layout, id);
                NsPath.Ensure(row, field.Path, null, field.Layout?.ToArray());
            }
            else if (NsPath.NodeAt(row, field.Path) != null) NsPath.Set(row, field.Path, "u8", null);
            return;
        }
        if (value == null && field.Definition.Width == null && NsPath.Get(row, field.Path) == null) return;
        if (field.StructPath != null)
        {
            if (NsPath.NodeAt(row, field.StructPath) == null) NsPath.Ensure(row, field.StructPath, null, field.Layout.ToArray());
            var structure = NsPath.NodeAt(row, field.StructPath) as BtlStruct
                ?? throw new FrontEditException(id + " 所在结构体不存在。");
            ValidateLayout(structure, field.Layout, id);
        }
        if (field.Definition.Width is int width)
        {
            object number = FrontPath.Coerce("u32", value ?? throw new FrontEditException(id + " 需要整数。"));
            ulong low = Convert.ToUInt64(number, CultureInfo.InvariantCulture), mask = (1UL << width) - 1;
            if (low > mask) throw new FrontEditException(id + " 超过位段范围。");
            int shift = field.Definition.Shift.Value;
            ulong current = Convert.ToUInt64(NsPath.Get(row, field.Path) ?? 0, CultureInfo.InvariantCulture);
            value = (current & ~(mask << shift)) | (low << shift);
        }
        if (value == null && NsPath.Get(row, field.Path) == null) return;
        NsPath.Set(row, field.Path, field.StorageType, value);
    }

    static void ValidateLayout(BtlStruct structure, IReadOnlyList<string> layout, string id)
    {
        if (structure.Layout.Count == 0)
        {
            if (structure.V.Count > layout.Count) throw new FrontEditException("结构体包含类型未知的额外成员。");
            structure.Layout.AddRange(layout);
            while (structure.V.Count < layout.Count)
                structure.V.Add(SoftSchema.DefaultValue(layout[structure.V.Count]));
        }
        if (!structure.Layout.SequenceEqual(layout))
            throw new FrontEditException(id + " 的文档布局与 schema 不一致；请先明确研究覆盖。");
    }

    public void Bind(LayoutPage page, SchemaContext context)
    {
        foreach (var column in page.Sections.SelectMany(s => s.Columns).Where(c => !string.IsNullOrEmpty(c.FieldKey)))
            column.Bind = Resolve(column.FieldKey, context).Path;
        foreach (var field in page.Sections.SelectMany(s => s.Fields).Where(f => !string.IsNullOrEmpty(f.FieldKey)))
        {
            var resolved = Resolve(field.FieldKey, context);
            field.Bind = resolved.Path;
            field.Type = resolved.DisplayType;
            field.Shift = resolved.Definition.Shift;
            field.Width = resolved.Definition.Width;
        }
    }
}
