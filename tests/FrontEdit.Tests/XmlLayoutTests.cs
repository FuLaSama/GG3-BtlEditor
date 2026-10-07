using BtlCore.Front;
using BtlCore.Scripting;
using Xunit;

namespace BtlCore.Front.Tests;

public class XmlLayoutTests
{
    static LayoutBundle Bundle()
    {
        var loaded = LayoutLoader.Load(Path.Combine(EditorScripts.Root(), "EditorLayout"), null);
        Assert.True(loaded.Ok, string.Join("\n", loaded.Errors));
        return loaded.Bundle;
    }

    [Fact]
    public void All_edit_pages_and_new_map_dialog_have_xml_and_available_actions()
    {
        var bundle = Bundle();
        var host = new ScriptHost();
        foreach (var script in bundle.Scripts) host.Execute(script.Text, script.Name);
        Assert.Equal(6, bundle.Tabs.Count(t => t.Placement != "dialog"));
        Assert.Contains(bundle.Tabs, t => t.Id == "newMap" && t.Placement == "dialog");
        foreach (var tab in bundle.Tabs)
        {
            Assert.False(tab.Builtin);
            Assert.NotNull(tab.Page);
            LayoutLoader.MarkMissingActions(tab.Page, host.CanRun, host.CanGet);
            foreach (var section in tab.Page.Sections)
            {
                Assert.All(section.Commands, c => Assert.True(c.Enabled, c.Script ?? c.Handler));
                Assert.All(section.Fields, f => Assert.True(f.Enabled, f.Action));
            }
        }
    }

    [Fact]
    public void Contextual_getters_do_not_write_and_layer_setter_preserves_other_members()
    {
        var doc = new BtlFrontDocument { Root = BtlFrontJson.NewTable() };
        var map = FrontEdit.EnsureTable(doc.Root, 1);
        FrontEdit.SetField(map, 0, FrontEdit.StructFromFbs("Size", (ushort)2, (ushort)1, (ushort)0, (ushort)0, (ushort)2, (ushort)1));
        var tiles = FrontEdit.EnsureVec(map, 2, "u16");
        tiles.V.Add((ushort)(4 << 8)); // 先前格子的装饰占用一个 attr。
        tiles.V.Add((ushort)((8 | 16) << 8));
        var attrs = FrontEdit.EnsureVec(map, 3, "struct");
        attrs.V.Add(FrontEdit.StructFromFbs("TileAttr", (byte)46, (byte)1, (sbyte)-2, (sbyte)5));
        attrs.V.Add(FrontEdit.StructFromFbs("TileAttr", (byte)7, (byte)2, (sbyte)3, (sbyte)-4));
        attrs.V.Add(FrontEdit.StructFromFbs("TileAttr", (byte)8, (byte)0, (sbyte)6, (sbyte)9));
        var host = new ScriptHost();
        foreach (var script in Bundle().Scripts) host.Execute(script.Text, script.Name);
        host.Load(doc);
        string before = BtlFrontJson.Serialize(doc);
        var selected = new ScriptArgs { CellIndex = 1 };
        Assert.Equal(3, Convert.ToInt32(host.CallGet("ui_main_dx", selected)));
        Assert.Equal(9, Convert.ToInt32(host.CallGet("ui_secondary_dy", selected)));
        Assert.Null(host.CallGet("ui_main_dx", new ScriptArgs()));
        Assert.Equal(before, BtlFrontJson.Serialize(doc));
        Assert.Equal(0, host.UndoFrames);
        host.Run("ui_main_dx", new ScriptArgs { CellIndex = 1, Value = -12 });
        var cells = TerrainEdits.Project(doc);
        Assert.Equal(-12, Convert.ToInt32(cells[1].Main.V[2]));
        Assert.Equal(-4, Convert.ToInt32(cells[1].Main.V[3]));
        Assert.Equal(9, Convert.ToInt32(cells[1].Secondary.V[3]));
        Assert.Equal(1, host.UndoFrames);
    }

    [Fact]
    public void Absolute_form_bind_and_resolved_cell_bind_are_distinct()
    {
        Assert.Equal("/1/0.0", LayoutRead.Compose(null, "/1/0.0", null, null));
        Assert.Equal("/6/0[2]/0.6", LayoutRead.Compose("/6/0", "/0.6", 2, 4));
        Assert.Null(LayoutRead.Compose("/6/0", "/0.6", null, 4));
        Assert.Null(LayoutRead.Compose(null, "/1/2[{cell}]", null, null));
    }

    [Fact]
    public void Terrain_fields_share_one_attribute_scan_per_refresh_without_snapshots()
    {
        var doc = new BtlFrontDocument { Root = BtlFrontJson.NewTable() };
        var map = FrontEdit.EnsureTable(doc.Root, 1);
        var tiles = FrontEdit.EnsureVec(map, 2, "u16");
        FrontEdit.SetField(map, 0, FrontEdit.StructFromFbs("Size", (ushort)4, (ushort)1, (ushort)0, (ushort)0, (ushort)4, (ushort)1));
        var attrs = FrontEdit.EnsureVec(map, 3, "struct");
        for (int i = 0; i < 4; i++)
        {
            tiles.V.Add((ushort)(8 << 8));
            attrs.V.Add(FrontEdit.StructFromFbs("TileAttr", (byte)(i + 1), (byte)2, (sbyte)3, (sbyte)4));
        }
        var host = new ScriptHost();
        foreach (var script in Bundle().Scripts) host.Execute(script.Text, script.Name);
        host.Load(doc);
        using (host.BeginRead())
        {
            foreach (string layer in new[] { "main", "secondary", "decor" })
                foreach (string field in new[] { "id", "variant", "dx", "dy" })
                    host.CallGet("ui_" + layer + "_" + field, new ScriptArgs { CellIndex = 3 });
            Assert.Equal(1, host.TerrainProjectionCount);
        }
        Assert.Equal(4d, host.CallGet("ui_main_id", new ScriptArgs { CellIndex = 3 }));
        Assert.Equal(2, host.TerrainProjectionCount);
        Assert.Equal(0, host.SnapshotCount);
    }
}
