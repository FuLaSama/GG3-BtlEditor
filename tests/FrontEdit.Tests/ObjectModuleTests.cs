using BtlCore.Fb;
using BtlCore.Scripting;
using Xunit;

namespace BtlCore.Front.Tests;

public class ObjectModuleTests
{
    static ScriptHost Host(string code = "", FieldCatalog catalog = null)
    {
        var host = new ScriptHost();
        host.ConfigureFields(catalog ?? FieldCatalog.Builtin);
        EditorScripts.Load(host);
        if (code.Length != 0) host.Execute(code, "extension.lua");
        var doc = new BtlFrontDocument { Context = host.CreateSchemaContext(), Root = BtlFrontJson.NewTable() };
        var map = FrontEdit.EnsureTable(doc.Root, 1);
        FrontEdit.SetField(map, 0, FrontEdit.NewStruct(new[] {"u16","u16","u16","u16","u16","u16"}));
        var size = (BtlStruct)map.F[0]; size.V[0]=(ushort)3; size.V[1]=(ushort)1; size.V[4]=(ushort)3; size.V[5]=(ushort)1;
        FrontEdit.Fill(FrontEdit.EnsureVec(map, 2, "u16"), new object[] {(ushort)0,(ushort)0,(ushort)0});
        var units = FrontEdit.EnsureVec(FrontEdit.EnsureTable(doc.Root, 6), 0, "table");
        for (ushort i=0; i<3; i++) units.V.Add(UnitEdits.Create(i, 0, (ushort)(10+i), 101, 0, 0, 3, 20,doc.Context));
        host.Load(doc);
        return host;
    }

    static BtlVector Rows(BtlFrontDocument doc, int table, int field) => (BtlVector)((BtlTable)doc.Root.F[table]).F[field];

    [Fact]
    public void One_extension_file_adds_property_object_and_tool_with_binary_roundtrip()
    {
        var code = File.ReadAllText(Path.Combine(EditorScripts.Root(),"EditorLayout/examples/research_objects.lua"));
        var host = Host(code);
        host.RunTool("example.research_promote", new ScriptArgs {Input=new() {["delta"]=4}});
        Assert.Equal(3, ((BtlVector)host.Document.Root.F[35]).V.Count);
        foreach (BtlTable unit in Rows(host.Document,6,0).V) Assert.Equal((ushort)4, ((BtlScalar)unit.F[33]).V);
        var built = FrontToBtl.Build(host.Document); Assert.True(built.Ok, string.Join("; ",built.Issues));
        var read = BtlToFront.FromBytes(built.Bytes,host.Document.Context); Assert.False(read.HasUnresolvedData);
        host.Load(read.Document);
        host.RunTool("example.research_promote", new ScriptArgs {Input=new() {["delta"]=2}});
        Assert.Equal(3, ((BtlVector)host.Document.Root.F[35]).V.Count); // 每个部队的记录更新，不重复追加。
        foreach (BtlTable record in ((BtlVector)host.Document.Root.F[35]).V) Assert.Equal((ushort)6, ((BtlScalar)record.F[1]).V);
        Assert.True(host.Document.Editing.Undo());
        foreach (BtlTable unit in Rows(host.Document,6,0).V) Assert.Equal((ushort)4, ((BtlScalar)unit.F[33]).V);
    }

    [Fact]
    public void Definitions_are_loading_only_and_failed_source_does_not_leave_fields_or_methods()
    {
        var host = Host("""
            editor.action('read',{get=function() return game.units.get(0).hp end})
            editor.action('late',function()
              game.units.define_field('late',editor.modules.objects.scalar('/33','u16'))
            end)
            editor.action('absent',{get=function() return game.units.get(0).leaked end})
            """);
        string before=BtlFrontJson.Serialize(host.Document);
        Assert.Throws<FrontEditException>(() => host.Execute("""
            game.units.define_field('leaked',editor.modules.objects.scalar('/33','u16'))
            game.units.define_method('leaked_method',function(unit) unit.hp=99 end)
            error('reject extension')
            ""","bad-extension.lua"));
        Assert.Throws<FrontEditException>(()=>host.CallGet("absent"));
        Assert.Throws<FrontEditException>(()=>host.Run("late",null));
        Assert.Equal(3d,host.CallGet("read"));
        Assert.Equal(before,BtlFrontJson.Serialize(host.Document));
    }

    [Fact]
    public void Optional_helpers_preserve_existing_members_and_clear_only_the_requested_group()
    {
        var host=Host("""
            editor.action('ensure',function()
              local unit=game.units.get(0)
              local general=unit:ensure('general',{id=0,active=false})
              assert(general.id==0 and general.active==false)
              assert(not pcall(function() unit:ensure('hp') end))
              assert(not pcall(function() unit:clear('hp') end))
            end)
            editor.action('clear',function() game.units.get(0):clear('general') end)
            """);
        var unit=(BtlTable)Rows(host.Document,6,0).V[0];
        var general=FrontEdit.EnsureTable(unit,11); FrontEdit.SetScalar(general,2,"u8",(byte)91);
        var special=FrontEdit.EnsureTable(unit,10); FrontEdit.SetScalar(special,0,"u16",(ushort)77);
        host.Run("ensure",null);
        Assert.Same(general,unit.F[11]); Assert.Equal((byte)91,((BtlScalar)general.F[2]).V);
        string ensured=BtlFrontJson.Serialize(host.Document);
        host.Run("ensure",null); Assert.Equal(ensured,BtlFrontJson.Serialize(host.Document)); Assert.Equal(1,host.UndoFrames);
        host.Run("clear",null); Assert.False(unit.F.ContainsKey(11)); Assert.Same(special,unit.F[10]);
        Assert.True(host.Document.Editing.Undo()); Assert.Equal(ensured,BtlFrontJson.Serialize(host.Document));
    }

    [Fact]
    public void Bulk_remove_handles_adjacent_rows_and_keeps_faction_related_rows_consistent()
    {
        var host=Host("""
            editor.action('create',function()
              for i=0,2 do local f=game.factions.create {gold=10+i}; assert(f.id==i) end
            end)
            editor.action('remove',function()
              local survivor=game.factions.get(2)
              assert(game.factions.remove_where(function(f) return f.id<2 end)==2)
              assert(survivor.index==0 and survivor.id==2)
              survivor.gold=99
            end)
            editor.action('failed',function()
              game.factions.remove_where(function(f)
                if f.id==1 then error('bad predicate') end
                return true
              end)
            end)
            """);
        host.Run("create",null); string before=BtlFrontJson.Serialize(host.Document);
        Assert.Throws<FrontEditException>(()=>host.Run("failed",null)); Assert.Equal(before,BtlFrontJson.Serialize(host.Document));
        host.Run("remove",null);
        var faction=Assert.IsType<BtlTable>(Assert.Single(Rows(host.Document,4,0).V));
        Assert.Equal((uint)99,((BtlStruct)faction.F[0]).V[6]);
        foreach (int relation in new[] {1,3})
        {
            var row=Assert.IsType<BtlTable>(Assert.Single(Rows(host.Document,4,relation).V));
            Assert.Equal((ushort)2,((BtlScalar)row.F[0]).V);
        }
        Assert.True(host.Document.Editing.Undo()); Assert.Equal(before,BtlFrontJson.Serialize(host.Document));
    }

    [Fact]
    public void Resize_uses_overridden_landmark_fields_and_preserves_terrain_layouts_and_tail()
    {
        var catalog=FieldCatalog.Builtin.Overlay(FieldCatalog.Parse("""
            {"version":1,"fields":[
              {"id":"building","owner":"TriggerEvent","path":"/8","kind":"table"},
              {"id":"fort","owner":"TriggerEvent","path":"/9","kind":"table"}]}
            """));
        var host=Host("""
            editor.format.schema {id='moved_landmarks',definition=[[
              table TriggerEvent { moved_building:TriggerEventBldg (id:8); moved_fort:TriggerEventFort (id:9); }
              struct TileAttr { id:ubyte; variant:ubyte; dx:byte; dy:byte; research:ushort; }
            ]]}
            editor.action('create',function()
              game.events.create {cell_index=0,building={flag=0}}
              game.events.create {cell_index=0,building={flag=1}}
              game.events.create {cell_index=1,fort={id=0}}
            end)
            editor.action('resize',function()
              game.map.cell(1,0).main={id=7,dx=-3}
              game.map.resize {left=1}
              game.map.cell(2,0).main.dy=9
            end)
            """,catalog);
        var map=(BtlTable)host.Document.Root.F[1];
        ((BtlVector)map.F[2]).V[0]=(ushort)2048;
        var attr=FrontEdit.NewStruct(new[] {"u8","u8","i8","i8","u16"}); attr.V[0]=(byte)6; attr.V[4]=(ushort)444;
        var tail=FrontEdit.NewStruct(new[] {"u8","u8","i8","i8","u16"}); tail.V[0]=(byte)88; tail.V[4]=(ushort)555;
        var attributes=FrontEdit.EnsureVec(map,3,"struct"); attributes.StructLayout.AddRange(attr.Layout);
        attributes.V.Add(attr); attributes.V.Add(tail);
        host.Run("create",null); string before=BtlFrontJson.Serialize(host.Document);
        host.Run("resize",null);
        var events=Rows(host.Document,5,0);
        Assert.Equal(2,events.V.Count); // 同格旧建筑剔除；新路径下的建筑、工事仍被识别。
        Assert.Equal((ushort)1,((BtlScalar)((BtlTable)events.V[0]).F[0]).V);
        Assert.Equal((ushort)2,((BtlScalar)((BtlTable)events.V[1]).F[0]).V);
        var cells=TerrainEdits.Project(host.Document);
        Assert.Equal((ushort)444,cells[1].Main.V[4]); Assert.Equal((byte)7,cells[2].Main.V[0]); Assert.Equal((sbyte)9,cells[2].Main.V[3]);
        Assert.Same(tail,((BtlVector)map.F[3]).V.Last()); Assert.Equal((ushort)555,tail.V[4]);
        var built=FrontToBtl.Build(host.Document); Assert.True(built.Ok,string.Join("; ",built.Issues));
        var reopened=BtlToFront.FromBytes(built.Bytes,host.Document.Context); Assert.False(reopened.HasUnresolvedData);
        Assert.Equal((ushort)444,TerrainEdits.Project(reopened.Document)[1].Main.V[4]);
        Assert.Equal((ushort)555,((BtlStruct)Rows(reopened.Document,1,3).V.Last()).V[4]);
        Assert.True(host.Document.Editing.Undo()); Assert.Equal(before,BtlFrontJson.Serialize(host.Document));
    }
}
