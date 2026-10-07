using BtlCore.Front;
using BtlCore.Fb;
using BtlCore.Scripting;
using Xunit;

namespace BtlCore.Front.Tests;

public class GameToolsTests
{
    static BtlFrontDocument Sample()
    {
        var doc = new BtlFrontDocument { Root = BtlFrontJson.NewTable() };
        var map = FrontEdit.EnsureTable(doc.Root, 1);
        FrontEdit.SetField(map, 0, FrontEdit.StructFromFbs("Size", (ushort)3, (ushort)1, (ushort)0, (ushort)0, (ushort)3, (ushort)1));
        var tiles = FrontEdit.EnsureVec(map, 2, "u16");
        tiles.V.Add((ushort)(4 << 8));
        tiles.V.Add((ushort)((8 | 16 | 128) << 8));
        tiles.V.Add((ushort)((4 | 8) << 8));
        var attrs = FrontEdit.EnsureVec(map, 3, "struct");
        foreach (byte id in new byte[] { 41, 7, 8, 42, 9, 99 })
            attrs.V.Add(FrontEdit.StructFromFbs("TileAttr", id, (byte)2, (sbyte)-2, (sbyte)5));
        var agents = FrontEdit.EnsureVec(FrontEdit.EnsureTable(doc.Root, 6), 0, "table");
        for (int i = 0; i < 3; i++)
        {
            var unit = BtlFrontJson.NewTable();
            FrontEdit.SetField(unit, 0, FrontEdit.StructFromFbs("AgentInfo", (ushort)(i == 2 ? 65535 : i),
                (ushort)(i == 1 ? 7 : 3), (ushort)(100 + i), (ushort)101,
                (ushort)((4 << 8) | 2), (ushort)((5 << 8) | 3), (ushort)10, (ushort)20, (ushort)8, (ushort)9));
            FrontEdit.SetScalar(unit, 98, "u16", (ushort)5432);
            var general = FrontEdit.EnsureTable(unit, 11);
            FrontEdit.SetScalar(general, 0, "u16", (ushort)44);
            FrontEdit.SetScalar(general, 1, "bool", true);
            FrontEdit.SetScalar(general, 2, "u8", (byte)7);
            agents.V.Add(unit);
        }
        var factions = FrontEdit.EnsureVec(FrontEdit.EnsureTable(doc.Root, 4), 0, "table");
        for (int i = 0; i < 2; i++)
        {
            var faction = BtlFrontJson.NewTable();
            var info = FrontEdit.NewStruct(new[] { "u16", "u16", "u8", "u8", "u8", "u8", "u32", "u32", "f32", "f32", "f32", "u32", "u16", "u16" });
            info.V[0] = (ushort)(i == 0 ? 3 : 7); info.V[1] = (ushort)1;
            info.V[6] = (uint)100; info.V[7] = (uint)12;
            info.V[8] = 1f; info.V[9] = 1f; info.V[10] = 1f; info.V[11] = (uint)0x11223344;
            FrontEdit.SetField(faction, 0, info); factions.V.Add(faction);
        }
        return doc;
    }

    static ScriptHost Host(BtlFrontDocument doc)
    {
        var host = new ScriptHost();
        var layout = LayoutLoader.Load(Path.Combine(EditorScripts.Root(), "EditorLayout"), null);
        Assert.True(layout.Ok, string.Join("\n", layout.Errors));
        foreach (var script in layout.Bundle.Scripts) host.Execute(script.Text, script.Name);
        host.Load(doc);
        return host;
    }
    static BtlTable Unit(BtlFrontDocument doc, int index) => (BtlTable)((BtlVector)((BtlTable)doc.Root.F[6]).F[0]).V[index];
    static int Hp(BtlFrontDocument doc, int index) => Convert.ToInt32(((BtlStruct)Unit(doc, index).F[0]).V[6]);
    static ScriptArgs Parameters(params (string Key, object Value)[] values) => new() { Input = values.ToDictionary(v => v.Key, v => v.Value) };

    [Fact]
    public void Beginner_declaration_needs_no_paths_types_or_xml_and_heals_only_selected_unit()
    {
        var doc = Sample(); var host = Host(doc);
        host.Execute("""
            editor.tool {title="我的恢复工具", target="selected_unit", run=function(unit) unit.hp=unit.max_hp end}
            """, "我的工具.lua");
        var expected = BtlFrontJson.CloneDocument(doc);
        ((BtlStruct)Unit(expected, 1).F[0]).V[6] = (ushort)20;
        Assert.Equal(1, host.RunTool("我的恢复工具", new ScriptArgs { CellIndex = 1 }));
        Assert.Equal(BtlFrontJson.Serialize(expected), BtlFrontJson.Serialize(doc));
        Assert.Equal(1, host.UndoFrames);
    }

    [Fact]
    public void Explicit_offmap_selection_wins_over_map_cell_and_bulk_includes_reinforcements()
    {
        var doc = Sample(); var host = Host(doc);
        host.RunTool("heal_selected", new ScriptArgs { CellIndex = 0, UnitIndex = 2 });
        Assert.Equal(10, Hp(doc, 0)); Assert.Equal(20, Hp(doc, 2));
        Assert.Equal(3, host.RunTool("heal_all"));
        Assert.Equal(20, Hp(doc, 0)); Assert.Equal(20, Hp(doc, 1));
        Assert.Equal(2, host.UndoFrames);
    }

    [Fact]
    public void Faction_batch_uses_ids_and_preserves_stack_general_and_unknown_fields()
    {
        var doc = Sample(); var host = Host(doc);
        var expected = BtlFrontJson.CloneDocument(doc);
        foreach (int i in new[] { 0, 2 }) ((BtlStruct)Unit(expected, i).F[0]).V[4] = (ushort)((4 << 8) | 17);
        Assert.Equal(2, host.RunTool("faction_level", Parameters(("faction", 3), ("level", 17))));
        Assert.Equal(BtlFrontJson.Serialize(expected), BtlFrontJson.Serialize(doc));
        Assert.Equal(1, host.UndoFrames); Assert.Equal(1, host.SnapshotCount);
    }

    [Fact]
    public void Packed_properties_and_partial_updates_preserve_their_other_halves()
    {
        var doc = Sample(); var host = Host(doc);
        host.Execute("""
            editor.tool {title="局部修改", target="selected_unit", run=function(unit)
              unit:update {level=33, direction=6, hp=12, general_id=88}
            end}
            """, "patch.lua");
        var expected = BtlFrontJson.CloneDocument(doc);
        var info = (BtlStruct)Unit(expected, 0).F[0];
        info.V[4] = (ushort)((4 << 8) | 33); info.V[5] = (ushort)((5 << 8) | 6); info.V[6] = (ushort)12;
        ((BtlScalar)((BtlTable)Unit(expected, 0).F[11]).F[0]).V = (ushort)88;
        host.RunTool("局部修改", new ScriptArgs { CellIndex = 0 });
        Assert.Equal(BtlFrontJson.Serialize(expected), BtlFrontJson.Serialize(doc));
    }

    [Fact]
    public void Faction_color_and_gold_are_local_updates_and_selected_faction_can_come_from_unit()
    {
        var doc = Sample(); var host = Host(doc);
        host.Execute("editor.tool {title='红色', target='selected_faction', run=function(faction) faction.color.r=255 end}", "color.lua");
        var expected = BtlFrontJson.CloneDocument(doc);
        var info = (BtlStruct)((BtlTable)((BtlVector)((BtlTable)expected.Root.F[4]).F[0]).V[1]).F[0];
        info.V[6] = (uint)150; info.V[11] = (uint)0xFF223344;
        host.RunTool("add_gold", new ScriptArgs { CellIndex = 1, Input = new() { ["amount"] = 50 } });
        host.RunTool("红色", new ScriptArgs { FactionIndex = 1 });
        Assert.Equal(BtlFrontJson.Serialize(expected), BtlFrontJson.Serialize(doc));
    }

    [Fact]
    public void Terrain_flag_and_attribute_edits_preserve_other_layers_and_unknown_flags()
    {
        var doc = Sample(); var host = Host(doc);
        var expected = BtlFrontJson.CloneDocument(doc);
        var map = (BtlTable)expected.Root.F[1];
        ((BtlVector)map.F[2]).V[1] = (ushort)((8 | 16 | 128 | 2) << 8);
        ((BtlStruct)((BtlVector)map.F[3]).V[1]).V[2] = (sbyte)6;
        ((BtlStruct)((BtlVector)map.F[3]).V[1]).V[3] = (sbyte)-7;
        host.RunTool("cell_sea", new ScriptArgs { CellIndex = 1 });
        host.RunTool("offset_main", new ScriptArgs { CellIndex = 1, Input = new() { ["dx"] = 6, ["dy"] = -7 } });
        Assert.Equal(BtlFrontJson.Serialize(expected), BtlFrontJson.Serialize(doc));
    }

    [Fact]
    public void Adding_and_clearing_layers_keeps_packed_order_and_unknown_tail_attributes()
    {
        var doc = Sample(); var host = Host(doc);
        host.Execute("""
            editor.tool {title="图层",target="selected_cell",run=function(cell)
              cell.main={id=60,variant=3,dx=-8,dy=9}
              cell.decor=nil
            end}
            """, "layers.lua");
        host.RunTool("图层", new ScriptArgs { CellIndex = 0 });
        var cells = TerrainEdits.Project(doc);
        Assert.Equal(60, Convert.ToInt32(cells[0].Main.V[0]));
        Assert.Equal(-8, Convert.ToInt32(cells[0].Main.V[2]));
        Assert.Equal(7, Convert.ToInt32(cells[1].Main.V[0]));
        Assert.Equal(8, Convert.ToInt32(cells[1].Secondary.V[0]));
        var attrs = (BtlVector)((BtlTable)doc.Root.F[1]).F[3];
        Assert.Equal(6, attrs.V.Count);
        Assert.Equal(99, Convert.ToInt32(((BtlStruct)attrs.V[^1]).V[0]));
    }

    [Fact]
    public void Bulk_terrain_changes_flush_once_and_failure_rolls_back_the_whole_tool()
    {
        var doc = Sample(); var host = Host(doc);
        host.Execute("""
            editor.tool {title="批量贴图",target="all_cells",run=function(cell) cell.main.id=80 end}
            editor.tool {title="失败",target="all_cells",run=function(cell)
              cell.main.id=20
              if cell.index==1 then error("stop") end
            end}
            """, "bulk.lua");
        Assert.Equal(3, host.RunTool("批量贴图"));
        string before = BtlFrontJson.Serialize(doc);
        Assert.Throws<FrontEditException>(() => host.RunTool("失败"));
        Assert.Equal(before, BtlFrontJson.Serialize(doc)); Assert.Equal(1, host.UndoFrames);
        foreach (var cell in TerrainEdits.Project(doc)) Assert.Equal(80, Convert.ToInt32(cell.Main.V[0]));
    }

    [Theory]
    [InlineData("unit.unknown=1")]
    [InlineData("unit.level=256")]
    [InlineData("unit.hp=70000")]
    [InlineData("unit.agent_id=500")]
    public void Typos_ranges_and_readonly_fields_are_reported_and_rolled_back(string statement)
    {
        var doc = Sample(); var host = Host(doc);
        host.Execute("editor.tool {title='错误',target='all_units',run=function(unit) unit.hp=12; " + statement + " end}", "invalid.lua");
        string before = BtlFrontJson.Serialize(doc);
        Assert.Throws<FrontEditException>(() => host.RunTool("错误"));
        Assert.Equal(before, BtlFrontJson.Serialize(doc)); Assert.Equal(0, host.UndoFrames);
    }

    [Fact]
    public void Missing_selection_bad_parameters_and_failed_load_do_not_leak_changes_or_tools()
    {
        var doc = Sample(); var host = Host(doc);
        string before = BtlFrontJson.Serialize(doc);
        Assert.Throws<FrontEditException>(() => host.RunTool("heal_selected"));
        Assert.Throws<FrontEditException>(() => host.RunTool("faction_level", Parameters(("faction", 3), ("level", 300))));
        Assert.Throws<FrontEditException>(() => host.Execute("editor.tool {title='leaked',target='stage',run=function() end}; error('abort')", "bad.lua"));
        Assert.DoesNotContain(host.Tools, t => t.Id == "leaked");
        Assert.Equal(before, BtlFrontJson.Serialize(doc));
        Assert.Equal(1, host.RunTool("heal_selected", new ScriptArgs { CellIndex = 0 }));
    }

    [Fact]
    public void Objects_expire_and_getters_cannot_write_through_game_objects()
    {
        var doc = Sample(); var host = Host(doc);
        host.Execute("""
            local saved
            editor.tool {title='save',target='selected_unit',run=function(unit) saved=unit end}
            editor.tool {title='reuse',target='selected_unit',run=function() saved.hp=20 end}
            editor.action('read_bad',{get=function() game.units.get(0).hp=20; return 0 end})
            """, "lifetime.lua");
        string before = BtlFrontJson.Serialize(doc);
        host.RunTool("save", new ScriptArgs { CellIndex = 0 });
        Assert.Throws<FrontEditException>(() => host.RunTool("reuse", new ScriptArgs { CellIndex = 0 }));
        Assert.Throws<FrontEditException>(() => host.CallGet("read_bad"));
        Assert.Equal(before, BtlFrontJson.Serialize(doc));
    }

    [Fact]
    public void Legacy_structs_gain_known_types_only_on_write_and_keep_existing_values()
    {
        var doc = Sample();
        var unitInfo = (BtlStruct)Unit(doc, 0).F[0];
        var factionInfo = (BtlStruct)((BtlTable)((BtlVector)((BtlTable)doc.Root.F[4]).F[0]).V[0]).F[0];
        var size = (BtlStruct)((BtlTable)doc.Root.F[1]).F[0];
        unitInfo.Layout.Clear(); factionInfo.Layout.Clear(); size.Layout.Clear();
        var host = Host(doc);
        host.Execute("""
            editor.action('read_legacy',{get=function() return game.units.get(0).hp + game.map.width end})
            editor.tool {title='legacy',target='map',run=function(map)
              game.units.get(0):heal()
              game.factions.get(0).color.r=255
              map.left_margin=1
            end}
            """, "legacy.lua");
        string before = BtlFrontJson.Serialize(doc);
        Assert.Equal(13d, host.CallGet("read_legacy"));
        Assert.Equal(before, BtlFrontJson.Serialize(doc));
        object[] unitValues = unitInfo.V.ToArray(), factionValues = factionInfo.V.ToArray(), sizeValues = size.V.ToArray();
        unitValues[6] = (ushort)20; factionValues[11] = (uint)0xFF223344; sizeValues[2] = (ushort)1;
        host.RunTool("legacy");
        Assert.Equal(unitValues, unitInfo.V); Assert.Equal(factionValues, factionInfo.V); Assert.Equal(sizeValues, size.V);
        Assert.Equal(10, unitInfo.Layout.Count); Assert.Equal(14, factionInfo.Layout.Count); Assert.Equal(6, size.Layout.Count);
        Assert.Equal(1, host.UndoFrames);
    }

    [Fact]
    public void Game_object_edits_export_and_read_back_with_correct_packed_values_and_layer_order()
    {
        var doc = Sample();
        FrontEdit.SetScalar(doc.Root, 0, "u16", (ushort)1);
        var host = Host(doc);
        host.Execute("""
            editor.tool {title='export',target='map',run=function(map)
              game.units.get(0):update {level=17, hp=13}
              game.factions.get(0).gold=12345
              map.cell(0,0).main={id=60,variant=3,dx=-8,dy=9}
              map.cell(0,0).decor=nil
            end}
            """, "export.lua");
        host.RunTool("export");
        var built = FrontToBtl.Build(doc);
        Assert.True(built.Ok, string.Join("\n", built.Issues));
        var read = BtlToFront.FromBytes(built.Bytes);
        Assert.True(read.Ok, string.Join("\n", read.Issues));
        var info = (BtlStruct)Unit(read.Document, 0).F[0];
        Assert.Equal(13, Convert.ToInt32(info.V[6])); Assert.Equal((4 << 8) | 17, Convert.ToInt32(info.V[4]));
        var faction = (BtlTable)((BtlVector)((BtlTable)read.Document.Root.F[4]).F[0]).V[0];
        Assert.Equal(12345, Convert.ToInt32(((BtlStruct)faction.F[0]).V[6]));
        var cells = TerrainEdits.Project(read.Document);
        Assert.Equal(60, Convert.ToInt32(cells[0].Main.V[0])); Assert.Equal(-8, Convert.ToInt32(cells[0].Main.V[2]));
        Assert.Equal(7, Convert.ToInt32(cells[1].Main.V[0])); Assert.Equal(8, Convert.ToInt32(cells[1].Secondary.V[0]));
        var attrs = (BtlVector)((BtlTable)read.Document.Root.F[1]).F[3];
        Assert.Equal(99, Convert.ToInt32(((BtlStruct)attrs.V[^1]).V[0]));
    }

    [Theory]
    [InlineData("target='unknown'")]
    [InlineData("params={{id='value',type='choice',choices={{label='bad',value={1,2}}}}}")]
    [InlineData("params={{id='value',min=10,max=0}}")]
    public void Invalid_tool_declarations_fail_without_leaking_a_menu_entry(string declaration)
    {
        var host = Host(Sample());
        Assert.Throws<FrontEditException>(() => host.Execute("editor.tool {title='bad', target='stage',run=function() end," + declaration + "}", "bad-tool.lua"));
        Assert.DoesNotContain(host.Tools, t => t.Id == "bad");
        Assert.Equal(7, host.Tools.Count);
    }

    [Fact]
    public void Malformed_terrain_is_rejected_without_discarding_other_attributes()
    {
        var doc = Sample();
        ((BtlVector)((BtlTable)doc.Root.F[1]).F[3]).V[0] = BtlFrontJson.NewStruct();
        var host = Host(doc);
        string before = BtlFrontJson.Serialize(doc);
        Assert.Throws<FrontEditException>(() => host.RunTool("offset_main", new ScriptArgs { CellIndex = 1 }));
        Assert.Equal(before, BtlFrontJson.Serialize(doc));
    }

    [Fact]
    public void Tool_parameters_use_defaults_and_choice_values()
    {
        var doc = Sample(); var host = Host(doc);
        Assert.Equal(0d, host.Tools.Single(t => t.Id == "cell_climate").Parameters[0].Default);
        host.RunTool("cell_climate", new ScriptArgs { CellIndex = 1, Input = new() { ["climate"] = 2 } });
        Assert.Equal(2, Convert.ToInt32(((BtlVector)((BtlTable)doc.Root.F[1]).F[2]).V[1]) & 7);
        host.RunTool("add_gold", new ScriptArgs { FactionIndex = 0 });
        var info = (BtlStruct)((BtlTable)((BtlVector)((BtlTable)doc.Root.F[4]).F[0]).V[0]).F[0];
        Assert.Equal(200, Convert.ToInt32(info.V[6]));
    }
}
