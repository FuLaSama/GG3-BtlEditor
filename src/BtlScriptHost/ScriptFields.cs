using BtlCore.Front;
using MoonSharp.Interpreter;

namespace BtlCore.Scripting;

public sealed partial class ScriptHost
{
    public FieldCatalog Fields { get; private set; } = FieldCatalog.Builtin;
    HashSet<string> _fieldUses;

    void RecordFieldUses(HashSet<string> used)
    {
        if (used.Count == 0) return;
        var records = Document.FieldCatalogUses.ToList();
        int index = records.FindIndex(r => r.Sha256 == Fields.Fingerprint);
        var ids = used.Concat(index < 0 ? Array.Empty<string>() : records[index].Fields)
            .Distinct().OrderBy(id => id, StringComparer.Ordinal).ToArray();
        var record = new FieldCatalogUse(Fields.Fingerprint, Fields.DefinitionJson, Array.AsReadOnly(ids));
        if (index < 0) records.Add(record); else records[index] = record;
        Document.FieldCatalogUses = records.AsReadOnly();
    }

    public void ConfigureFields(FieldCatalog fields)
    {
        EnsureIdle();
        if (_readDepth != 0 || _sources.Count != 0)
            throw new FrontEditException("请在加载源码之前安装字段目录；热加载应使用新宿主。");
        Fields = fields ?? throw new ArgumentNullException(nameof(fields));
    }

    void InstallFieldsApi(Table bridge)
    {
        bridge.Set("field_path", Api("editor.internal.field_path", (_, args) =>
            DynValue.NewString(Fields.Resolve(ArgString(args, 0), Document.Context).Path)));
        bridge.Set("field_get", Api("editor.internal.field_get", (_, args) =>
            ToDyn(Fields.Read(Document, Unwrap(args[0]).Table ?? throw new FrontEditException("需要对象表"), ArgString(args, 1)))));
        bridge.Set("field_set", Api("editor.internal.field_set", (_, args) =>
        {
            RequireWrite();
            Fields.Write(Document, Unwrap(args[0]).Table ?? throw new FrontEditException("需要对象表"),
                ArgString(args, 1), FromDyn(args[2]));
            _fieldUses?.Add(ArgString(args, 1));
            return DynValue.Nil;
        }));
    }
}
