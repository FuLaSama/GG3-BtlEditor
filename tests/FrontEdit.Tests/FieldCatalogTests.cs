using BtlCore.Fb;
using BtlCore.Scripting;
using Xunit;

namespace BtlCore.Front.Tests;

public class FieldCatalogTests
{
    static (BtlFrontDocument Doc, BtlTable Unit) Sample()
    {
        var doc = new BtlFrontDocument { Root = BtlFrontJson.NewTable() };
        var units = FrontEdit.EnsureVec(FrontEdit.EnsureTable(doc.Root, 6), 0, "table");
        var unit = UnitEdits.Create(0, 0, 1, 101, 0xA5, 0, 10, 30);
        FrontEdit.SetScalar(unit, 98, "u16", (ushort)1234);
        units.V.Add(unit);
        return (doc, unit);
    }

    [Fact]
    public void One_catalog_override_changes_both_xml_reads_and_lua_writes_and_preserves_other_bits()
    {
        string user = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(user);
        try
        {
            File.WriteAllText(Path.Combine(user, "layout.xml"), "<editorLayout format='1'><fields src='fields.json'/></editorLayout>");
            File.WriteAllText(Path.Combine(user, "fields.json"), """
                {"version":1,"fields":[
                  {"id":"unit.hp","owner":"AIAgent","path":"/0.7"},
                  {"id":"unit.level","owner":"AIAgent","path":"/0.4","shift":0,"width":4}
                ]}
                """);
            var loaded = LayoutLoader.Load(Path.Combine(EditorScripts.Root(), "EditorLayout"), user);
            Assert.True(loaded.Ok, string.Join("\n", loaded.Errors));
            var section = loaded.Bundle.Tabs.Single(t => t.Id == "unit").Page.Sections.First();
            var hp = section.Fields.Single(f => f.Id == "hp");
            var level = section.Fields.Single(f => f.Id == "level");
            Assert.Equal("/0.7", hp.Bind);
            Assert.Equal(4, level.Width);
            var (doc, unit) = Sample();
            var host = new ScriptHost();
            host.ConfigureFields(loaded.Bundle.Fields);
            host.Load(doc);
            EditorScripts.Load(host);
            string before = BtlFrontJson.Serialize(doc);
            Assert.Equal(30, Convert.ToInt32(host.ReadBind(section.Bind, hp.Bind, 0, null, hp.Widget, hp.Shift, hp.Width)));
            Assert.Equal(before, BtlFrontJson.Serialize(doc));
            host.Execute("editor.action('edit', function() local u=game.units.get(0); u.hp=45; u.level=3 end)", "test.lua");
            host.Run("edit", new());
            var info = (BtlStruct)unit.F[0];
            Assert.Equal(10, Convert.ToInt32(info.V[6]));
            Assert.Equal(45, Convert.ToInt32(info.V[7]));
            Assert.Equal(0xA3, Convert.ToInt32(info.V[4]));
            Assert.Equal((ushort)1234, ((BtlScalar)unit.F[98]).V);
            Assert.Equal(3, Convert.ToInt32(host.ReadBind(section.Bind, level.Bind, 0, null, level.Widget, level.Shift, level.Width)));
            Assert.Equal(1, doc.Editing.CommitCount);
        }
        finally { Directory.Delete(user, true); }
    }

    [Fact]
    public void Out_of_range_bitfield_rolls_back_prior_writes_and_does_not_add_history()
    {
        var (doc, _) = Sample();
        var host = new ScriptHost(); host.Load(doc); EditorScripts.Load(host);
        host.Execute("editor.action('bad', function() local u=game.units.get(0); u.hp=50; u.level=256 end)", "bad.lua");
        string before = BtlFrontJson.Serialize(doc);
        Assert.ThrowsAny<Exception>(() => host.Run("bad", new()));
        Assert.Equal(before, BtlFrontJson.Serialize(doc));
        Assert.Equal(0, doc.Editing.CommitCount);
        Assert.Equal(0, doc.Editing.HistoryCount);
    }

    [Fact]
    public void Catalog_types_come_from_the_supplied_schema_and_conflicting_layouts_are_not_rewritten()
    {
        var catalog = FieldCatalog.Parse("""
            {"version":1,"fields":[{"id":"probe.value","owner":"Row","path":"/0.0"}]}
            """);
        var a = new SchemaContext(FbsSchema.LoadFromText("struct Info { value:ushort; } table Row { info:Info; } root_type Row;"));
        var b = new SchemaContext(FbsSchema.LoadFromText("struct Info { value:short; } table Row { info:Info; } root_type Row;"));
        Assert.Equal("u16", catalog.Resolve("probe.value", a).StorageType);
        Assert.Equal("i16", catalog.Resolve("probe.value", b).StorageType);
        var (doc, unit) = Sample();
        ((BtlStruct)unit.F[0]).Layout[6] = "u8";
        var host = new ScriptHost(); host.Load(doc); EditorScripts.Load(host);
        host.Execute("editor.action('edit', function() game.units.get(0).hp=20 end)", "edit.lua");
        string before = BtlFrontJson.Serialize(doc);
        Assert.ThrowsAny<Exception>(() => host.Run("edit", new()));
        Assert.Equal(before, BtlFrontJson.Serialize(doc));
    }

    [Fact]
    public void Catalog_conflicts_in_xml_fail_loading_instead_of_silently_overriding_a_binding()
    {
        string user = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(user);
        try
        {
            File.WriteAllText(Path.Combine(user, "layout.xml"), "<editorLayout format='1'><tabs><tab id='unit' page='unit.xml'/></tabs></editorLayout>");
            File.WriteAllText(Path.Combine(user, "unit.xml"), "<page id='unit'><section kind='form'><field id='hp' field='unit.hp' bind='/0.6'/></section></page>");
            var result = LayoutLoader.Load(Path.Combine(EditorScripts.Root(), "EditorLayout"), user);
            Assert.False(result.Ok);
            Assert.Null(result.Bundle);
            Assert.Contains(result.Errors, e => e.Contains("不能与"));
        }
        finally { Directory.Delete(user, true); }
    }
}
