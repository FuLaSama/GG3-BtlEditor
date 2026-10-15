using BtlCore.Fb;
using BtlCore.Scripting;
using Xunit;

namespace BtlCore.Front.Tests;

public class FieldEvolutionTests
{
    static (BtlFrontDocument Doc, BtlTable Unit, ScriptHost Host) Sample(FieldCatalog catalog = null, SchemaContext context = null)
    {
        var doc = new BtlFrontDocument { Context = context ?? EditorScripts.LegacyContext, Root = BtlFrontJson.NewTable() };
        var unit = UnitEdits.Create(0, 0, 1, 101, 1, 0, 10, 30,doc.Context);
        FrontEdit.EnsureVec(FrontEdit.EnsureTable(doc.Root, 6), 0, "table").V.Add(unit);
        var host = new ScriptHost(); host.ConfigureFields(catalog ?? FieldCatalog.Builtin); host.Load(doc); EditorScripts.Load(host);
        return (doc, unit, host);
    }

    [Fact]
    public void Optional_group_override_moves_presence_and_all_children_in_xml_and_lua()
    {
        var context = new SchemaContext(FbsSchema.LoadFromText(File.ReadAllText(EditorScripts.LegacySchemaPath)
            .Replace("extra_table_12:AIAgentTable12", "extra_table_12:AIAgentTable11")));
        var catalog = FieldCatalog.Builtin.Overlay(FieldCatalog.Parse("""
            {"version":1,"revision":"experiment.general.2","fields":[
              {"id":"unit.general","owner":"AIAgent","path":"/12","kind":"table","confidence":"tentative","evidence":"test fixture"}
            ]}
            """));
        var (doc, unit, host) = Sample(catalog, context);
        var old = FrontEdit.EnsureTable(unit, 11); FrontEdit.SetScalar(old, 0, "u16", (ushort)77);
        var moved = FrontEdit.EnsureTable(unit, 12); FrontEdit.SetScalar(moved, 99, "u8", (byte)88);
        var layout = LayoutLoader.Load(Path.Combine(EditorScripts.Root(), "EditorLayout"), null, context);
        Assert.True(layout.Ok, string.Join("\n", layout.Errors));
        var page = layout.Bundle.Tabs.Single(t => t.Id == "unit").Page;
        catalog.Bind(page, context);
        foreach (var section in page.Sections.Where(s=>s.Fields.Any(f=>f.Id=="general")))
        {
            Assert.Equal("/12", section.Fields.Single(f => f.Id == "general").Bind);
            Assert.Equal("/12/1", section.Fields.Single(f => f.Id == "general_active").Bind);
        }
        host.Execute("""
            editor.action('change', function() local u=game.units.get(0); u.general={id=0,active=false} end)
            editor.action('clear_value', function() game.units.get(0).general.active=nil end)
            editor.action('remove_group', function() game.units.get(0).general=nil end)
            """, "override.lua");
        host.Run("change", new());
        Assert.Same(old, unit.F[11]);
        Assert.Equal((ushort)77, ((BtlScalar)old.F[0]).V);
        Assert.Equal((ushort)0, ((BtlScalar)moved.F[0]).V);
        Assert.Equal(false, ((BtlScalar)moved.F[1]).V);
        Assert.Equal((byte)88, ((BtlScalar)moved.F[99]).V);
        var active = page.Sections[0].Fields.Single(f => f.Id == "general_active");
        Assert.Equal(false, host.ReadBind("/6/0", active.Bind, 0, null, active.Widget, null, null));
        host.Run("clear_value", new()); Assert.False(moved.F.ContainsKey(1)); Assert.True(unit.F.ContainsKey(12));
        host.Run("remove_group", new()); Assert.False(unit.F.ContainsKey(12)); Assert.Same(old, unit.F[11]);
        Assert.True(doc.Editing.Undo());
        Assert.True(((BtlTable)((BtlVector)((BtlTable)doc.Root.F[6]).F[0]).V[0]).F.ContainsKey(12));
    }

    [Fact]
    public void Building_member_override_applies_to_creation_and_preserves_other_members()
    {
        var catalog = FieldCatalog.Builtin.Overlay(FieldCatalog.Parse("""
            {"version":1,"fields":[{"id":"building.dx","owner":"TriggerEvent","parent":"building.data","path":".5"}]}
            """));
        var (doc, _, host) = Sample(catalog);
        host.Execute("""
            editor.action('create',function() game.events.create {cell_index=0,building={dx=-7}} end)
            editor.action('set',function() game.events.get(0).building.dx=-12 end)
            """, "building.lua");
        host.Run("create", new());
        var ev = (BtlTable)((BtlVector)((BtlTable)doc.Root.F[5]).F[0]).V[0];
        var data = (BtlStruct)((BtlTable)ev.F[3]).F[0];
        Assert.Equal((sbyte)0, data.V[4]); Assert.Equal((sbyte)-7, data.V[5]);
        FrontEdit.SetScalar((BtlTable)ev.F[3], 90, "u8", (byte)9);
        host.Run("set", new());
        Assert.Equal((sbyte)0, data.V[4]); Assert.Equal((sbyte)-12, data.V[5]);
        Assert.Equal((byte)9, ((BtlScalar)((BtlTable)ev.F[3]).F[90]).V);
        var page = LayoutLoader.Load(Path.Combine(EditorScripts.Root(), "EditorLayout"), null).Bundle.Tabs.Single(t => t.Id == "site").Page;
        catalog.Bind(page, doc.Context);
        Assert.Equal("/3/0.5", page.Sections[0].Fields.Single(f => f.Id == "dx").Bind);
    }

    [Fact]
    public void Field_provenance_survives_work_json_and_history_but_not_failure_or_noop()
    {
        var (doc, _, host) = Sample();
        host.Execute("""
            editor.action('change',function() game.units.get(0).hp=15 end)
            editor.action('bad',function() game.units.get(0).hp=20; error('fail') end)
            """, "provenance.lua");
        string initial = BtlFrontJson.Serialize(doc);
        Assert.ThrowsAny<Exception>(() => host.Run("bad", new()));
        Assert.Equal(initial, BtlFrontJson.Serialize(doc));
        host.Run("change", new());
        var used = Assert.Single(doc.FieldCatalogUses);
        Assert.Equal(host.Fields.Fingerprint, used.Sha256);
        Assert.Contains("unit.hp", used.Fields);
        Assert.Equal(used.Sha256, FieldCatalog.Parse(used.DefinitionJson).Fingerprint);
        string edited = BtlFrontJson.Serialize(doc);
        host.Run("change", new()); Assert.Equal(edited, BtlFrontJson.Serialize(doc));
        Assert.Equal(edited, BtlFrontJson.Serialize(BtlFrontJson.Parse(edited,doc.Context)));
        Assert.Equal(edited, BtlFrontJson.Serialize(BtlFrontJson.CloneDocument(doc)));
        doc.Editing.Undo(); Assert.Equal(initial, BtlFrontJson.Serialize(doc));
        doc.Editing.Redo(); Assert.Equal(edited, BtlFrontJson.Serialize(doc));
        doc.Editing.Undo();
        using (doc.Editing.BeginHistoryGroup())
        {
            host.Run("change", new());
            doc.Editing.Execute(() => FrontEdit.SetMember((BtlStruct)((BtlTable)((BtlVector)((BtlTable)doc.Root.F[6]).F[0]).V[0]).F[0], 6, (ushort)10));
        }
        Assert.Equal(initial, BtlFrontJson.Serialize(doc));
    }

    [Fact]
    public void Nested_script_metadata_is_committed_once_with_the_outer_transaction()
    {
        var (doc, _, host) = Sample();
        host.Execute("editor.action('set',function(ctx) game.units.get(0).hp=ctx.value end)", "nested.lua");
        string before = BtlFrontJson.Serialize(doc);
        Assert.Throws<InvalidOperationException>(() => doc.Editing.Execute(() =>
        {
            host.Run("set", new() { Value = 11 });
            throw new InvalidOperationException("outer failed");
        }));
        Assert.Equal(before, BtlFrontJson.Serialize(doc));
        doc.Editing.Execute(() => { host.Run("set", new() { Value = 11 }); host.Run("set", new() { Value = 12 }); });
        Assert.Single(doc.FieldCatalogUses); Assert.Equal(1, doc.Editing.CommitCount);
        doc.Editing.Undo(); Assert.Equal(before, BtlFrontJson.Serialize(doc));
    }

    [Fact]
    public void Catalog_fingerprints_are_order_independent_and_parent_changes_report_dependents()
    {
        var first = FieldCatalog.Parse("""
            {"version":1,"revision":"r1","fields":[
              {"id":"g","owner":"Root","path":"/1","kind":"table"},
              {"id":"v","owner":"Root","path":"/0","parent":"g"}]}
            """);
        var same = FieldCatalog.Parse("""
            {"version":1,"revision":"r1","fields":[
              {"id":"v","owner":"Root","path":"/0","parent":"g"},
              {"id":"g","owner":"Root","path":"/1","kind":"table"}]}
            """);
        Assert.Equal(first.Fingerprint, same.Fingerprint);
        var next = first.Overlay(FieldCatalog.Parse("""
            {"version":1,"revision":"r2","fields":[{"id":"g","owner":"Root","path":"/2","kind":"table"}]}
            """));
        Assert.Equal(new[] { "g", "v" }, next.ChangedFields(first));
        Assert.NotEqual(first.Fingerprint, next.Fingerprint);
        Assert.Throws<FrontEditException>(() => first.Overlay(FieldCatalog.Parse("""
            {"version":1,"fields":[{"id":"g","owner":"Root","path":"/0","kind":"table","parent":"g"}]}
            """)).ValidateReferences());
    }
}
