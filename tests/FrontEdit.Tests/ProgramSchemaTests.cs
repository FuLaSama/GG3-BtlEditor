using BtlCore.Fb;
using BtlCore.Scripting;
using Xunit;

namespace BtlCore.Front.Tests;

public class ProgramSchemaTests
{
    [Fact]
    public void Core_unit_factory_places_split_bytes_and_health_in_their_declared_members()
    {
        var unit=UnitEdits.Create(65535,2,3,101,0x0911,0x0702,100,500);
        var info=(BtlStruct)unit.F[0];
        Assert.Equal(-1,Convert.ToInt32(info.V[0]));
        Assert.Equal(new[]{17,9,2,7,100,500},info.V.Skip(4).Take(6).Select(Convert.ToInt32));
    }

    [Fact]
    public void Default_schema_is_the_file_beside_program_and_core_has_no_embedded_fbs()
    {
        string path=Path.Combine(AppContext.BaseDirectory,"battle.fbs");
        Assert.Equal(path,FbsSchema.ProgramSchemaPath);
        Assert.Equal(Path.GetFullPath(path),SoftSchema.Schema.SourcePath);
        Assert.Equal(FbsSchema.LoadFile(path).ContentHash,SoftSchema.Schema.ContentHash);
        Assert.DoesNotContain(typeof(SoftSchema).Assembly.GetManifestResourceNames(),
            name=>name.EndsWith(".fbs",StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Split_byte_fbs_loads_before_compatibility_binding_and_edits_export_correct_bytes()
    {
        // 程序旁的新版 FBS 有 12 个成员；它本身不能套用旧版的 16 位字段映射。
        var basis=SoftSchema.DefaultContext;
        Assert.True(basis.TryStructMembers("AgentInfo",out var split));
        Assert.Equal(12,split.Length);Assert.Equal("u8",split[4].T);
        Assert.Throws<FrontEditException>(()=>FieldCatalog.Builtin.Resolve("unit.stack",basis));
        var loaded=LayoutLoader.Load(Path.Combine(EditorScripts.Root(),"EditorLayout"),null);
        Assert.True(loaded.Ok,string.Join(";",loaded.Errors));
        var host=new ScriptHost();host.ConfigureFields(loaded.Bundle.Fields);
        foreach(var script in loaded.Bundle.Scripts)host.Execute(script.Text,script.Name);
        var context=host.CreateSchemaContext(basis);
        foreach(var tab in loaded.Bundle.Tabs)host.Fields.Bind(tab.Page,context);
        var doc=new BtlFrontDocument{Root=BtlFrontJson.NewTable(),Context=context};
        FrontEdit.EnsureVec(FrontEdit.EnsureTable(doc.Root,6),0,"table")
            .V.Add(UnitEdits.Create(0,1,2,101,0,0,123,456,context));
        host.Load(doc);
        host.Execute("""
            editor.action('edit',function()
              local unit=game.units.get(0)
              unit.level=17;unit.stack=9;unit.direction=2;unit.mobility=7;unit.hp=100;unit.max_hp=500
            end)
            ""","split-member-regression.lua");
        host.Run("edit",new());
        var built=FrontToBtl.Build(doc);Assert.True(built.Ok,string.Join(";",built.Issues));
        // 用未经 Lua 合并的新版 FBS 独立重新读取，逐项核对四个字节及后续血量的位置。
        var read=BtlToFront.FromBytes(built.Bytes,basis);Assert.True(read.Ok,string.Join(";",read.Issues));
        var info=(BtlStruct)((BtlTable)((BtlVector)((BtlTable)read.Document.Root.F[6]).F[0]).V[0]).F[0];
        Assert.Equal(new[]{17,9,2,7,100,500},info.V.Skip(4).Take(6).Select(Convert.ToInt32));
        Assert.Equal(1,doc.Editing.CommitCount);Assert.True(doc.Editing.Undo());
    }
}
