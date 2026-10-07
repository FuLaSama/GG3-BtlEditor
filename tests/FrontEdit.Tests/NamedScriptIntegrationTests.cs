using BtlCore.Fb;
using BtlCore.Front;
using BtlCore.Scripting;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Xunit;

namespace BtlCore.Front.Tests;

public class NamedScriptIntegrationTests
{
    static ScriptHost Host(BtlFrontDocument doc)
    {
        var loaded = LayoutLoader.Load(Path.Combine(EditorScripts.Root(), "EditorLayout"), null);
        Assert.True(loaded.Ok, string.Join("\n", loaded.Errors));
        var host = new ScriptHost();
        foreach (var script in loaded.Bundle.Scripts) host.Execute(script.Text, script.Name);
        host.Load(doc);
        return host;
    }
    static BtlFrontDocument Sample() => StageJson.LoadFile(Path.Combine(EditorScripts.Root(), "src/BtldMapEditor/samples/stage10101.json"), SoftSchema.Schema);
    static BtlVector Rows(BtlFrontDocument doc, int table, int field) => (BtlVector)((BtlTable)doc.Root.F[table]).F[field];
    static BtlStruct Info(BtlFrontDocument doc, int table, int row) => (BtlStruct)((BtlTable)Rows(doc, table, 0).V[row]).F[0];
    static ScriptArgs Args(int? index = null, int? cell = null, params (string Key, object Value)[] values) =>
        new() { Index = index, CellIndex = cell, Input = values.ToDictionary(v => v.Key, v => v.Value) };
    static BtlFrontDocument Export(BtlFrontDocument doc, string filename)
    {
        var built = FrontToBtl.Build(doc);
        Assert.True(built.Ok, string.Join("\n", built.Issues));
        var read = BtlToFront.FromBytes(built.Bytes);
        Assert.True(read.Ok, string.Join("\n", read.Issues));
        string output = Path.Combine(EditorScripts.Root(), "artifacts", "lua-objects");
        Directory.CreateDirectory(output);
        File.WriteAllBytes(Path.Combine(output, filename), built.Bytes);
        File.WriteAllText(Path.Combine(output, Path.ChangeExtension(filename, ".front.json")), BtlFrontJson.Serialize(read.Document));
        return read.Document;
    }

    [Fact]
    public void Business_scripts_use_named_objects_without_embedding_field_paths()
    {
        string directory = Path.Combine(EditorScripts.Root(), "EditorLayout", "scripts");
        var scripts = Directory.GetFiles(directory, "*.lua", SearchOption.AllDirectories)
            .Where(path => !Path.GetRelativePath(directory, path).StartsWith("common" + Path.DirectorySeparatorChar)).ToArray();
        Assert.NotEmpty(scripts);
        foreach (string path in scripts)
            Assert.False(Regex.IsMatch(File.ReadAllText(path), "[\"']/[0-9]"), "业务脚本仍包含字段路径：" + path);
    }

    [Fact]
    public void Json_import_decodes_empty_and_nonempty_packed_behavior_trees_at_load_time()
    {
        byte[][] streams = { PackedJsonStream.EmptyHeader,
            PackedJsonStream.Encode(new object[] { JsonNode.Parse("{\"btid\":7,\"name\":\"import-test\",\"class\":\"bt\",\"node\":[]}") }) };
        for (int index = 0; index < streams.Length; index++)
        {
            var raw = new BtlFrontDocument { Root = BtlFrontJson.NewTable() };
            var vector = FrontEdit.EnsureVec(raw.Root, 10, "u8");
            vector.V.AddRange(streams[index].Select(b => (object)b));
            var doc = StageJson.LoadJson(BtlFrontJson.Serialize(raw), SoftSchema.Schema);
            Assert.Equal(PackedJsonStream.EncName, ((BtlVector)doc.Root.F[10]).Enc);
            var host = Host(doc); string before = BtlFrontJson.Serialize(doc);
            host.Execute("editor.action('tree_count',{get=function() return game.behavior_trees.count() end})", "read-import.lua");
            Assert.Equal((double)index, host.CallGet("tree_count"));
            Assert.Equal(before, BtlFrontJson.Serialize(doc)); Assert.Equal(0, host.SnapshotCount);
        }
    }

    [Fact]
    public void Real_sample_page_edits_and_tools_work_together_and_export_correct_values()
    {
        var doc = Sample(); var host = Host(doc);
        Assert.Equal(588, Rows(doc, 1, 2).V.Count); Assert.Equal(71, Rows(doc, 6, 0).V.Count);
        var info = Info(doc, 6, 0);
        int faction = Convert.ToInt32(info.V[1]), maxHp = Convert.ToInt32(info.V[7]);
        host.Run("apply_unit", Args(0, null,
            ("agent", info.V[2]), ("unit", info.V[3]), ("faction", info.V[1]), ("level", 17), ("stack", 9),
            ("direction", Convert.ToInt32(info.V[5]) & 255), ("mobility", Convert.ToInt32(info.V[5]) >> 8),
            ("hp", 0), ("max_hp", info.V[7]), ("val8", info.V[8]), ("val9", info.V[9])));
        Assert.Equal(0, Convert.ToInt32(info.V[6]));
        Assert.Equal((9 << 8) | 17, Convert.ToInt32(info.V[4]));
        int frames = host.UndoFrames;
        host.RunTool("heal_selected", new ScriptArgs { UnitIndex = 0 });
        Assert.Equal(maxHp, Convert.ToInt32(info.V[6])); Assert.Equal(frames + 1, host.UndoFrames);
        var stacks = Rows(doc, 6, 0).V.Cast<BtlTable>().Select(u => Convert.ToInt32(((BtlStruct)u.F[0]).V[4]) >> 8).ToArray();
        host.RunTool("faction_level", Args(null, null, ("faction", faction), ("level", 25)));
        Assert.Equal(stacks, Rows(doc, 6, 0).V.Cast<BtlTable>().Select(u => Convert.ToInt32(((BtlStruct)u.F[0]).V[4]) >> 8).ToArray());
        Assert.Equal((9 << 8) | 25, Convert.ToInt32(info.V[4]));

        var factionInfo = Info(doc, 4, 0); long gold = Convert.ToInt64(factionInfo.V[6]);
        host.RunTool("add_gold", new ScriptArgs { FactionIndex = 0, Input = new() { ["amount"] = 123 } });
        Assert.Equal(gold + 123, Convert.ToInt64(factionInfo.V[6]));
        int cell = TerrainEdits.Project(doc).FindIndex(c => c.Main != null);
        var originalLayer = TerrainEdits.Project(doc)[cell].Main;
        object id = originalLayer.V[0], variation = originalLayer.V[1], dy = originalLayer.V[3];
        host.Run("ui_main_dx", new ScriptArgs { CellIndex = cell, Value = -11 });
        var editedLayer = TerrainEdits.Project(doc)[cell].Main;
        Assert.Equal(id, editedLayer.V[0]); Assert.Equal(variation, editedLayer.V[1]); Assert.Equal(dy, editedLayer.V[3]);
        Assert.Equal(-11, Convert.ToInt32(editedLayer.V[2]));
        host.Run("apply_layer", new ScriptArgs { CellIndex = cell, Item = new() { ["layer"] = "secondary", ["terrain_id"] = 12, ["dx"] = -3 } });
        host.RunTool("cell_climate", Args(null, cell, ("climate", 3)));
        Assert.Equal(12, Convert.ToInt32(TerrainEdits.Project(doc)[cell].Secondary.V[0]));
        Assert.Equal(3, TerrainEdits.Project(doc)[cell].Terrain & 7);
        host.Run("set_round_limit", new ScriptArgs { Value = 44 });
        host.Run("ui_fog", new ScriptArgs { Value = true });
        int targets = host.Count("/2/0"), weather = host.Count("/9/0"), routes = host.Count("/6/1"), trees = host.Count("/10");
        host.Run("add_target", Args(null, null, ("type", 6), ("value", -3), ("param1", 1)));
        host.Run("add_weather", Args(null, null, ("type", 2), ("start", 1), ("duration", 3)));
        host.Run("add_route", Args(null, null, ("cells", new object[] { 0, 1, 2 })));
        host.Run("add_tree", new ScriptArgs());
        host.Run("add_root", Args(trees));
        host.Run("add_child", Args(trees, null, ("path", new object[] { 0 })));
        host.Run("save_bt", Args(trees, null, ("btid", 999), ("id", 999), ("name", "Lua对象试验"), ("agent", "CBTCountryAgent"),
            ("class", "bt"), ("path", new object[] { 0, 0 }), ("node_class", "act"), ("method", "LuaExperiment"), ("params", new object[] { 3, 4 })));
        Assert.Equal(targets + 1, host.Count("/2/0")); Assert.Equal(weather + 1, host.Count("/9/0"));
        Assert.Equal(routes + 1, host.Count("/6/1")); Assert.Equal(trees + 1, host.Count("/10"));
        var exported = Export(doc, "stage10101-对象与工具试验.btl");
        Assert.Equal(maxHp, Convert.ToInt32(Info(exported, 6, 0).V[6]));
        Assert.Equal((9 << 8) | 25, Convert.ToInt32(Info(exported, 6, 0).V[4]));
        Assert.Equal(gold + 123, Convert.ToInt64(Info(exported, 4, 0).V[6]));
        Assert.Equal(-11, Convert.ToInt32(TerrainEdits.Project(exported)[cell].Main.V[2]));
        Assert.Equal(12, Convert.ToInt32(TerrainEdits.Project(exported)[cell].Secondary.V[0]));
        var readHost = Host(exported);
        readHost.Execute("editor.action('tree_method',{get=function() return game.behavior_trees.get(" + trees + "):node({0,0}).method end})", "read-export.lua");
        Assert.Equal("LuaExperiment", readHost.CallGet("tree_method"));
    }

    [Fact]
    public void Legacy_sample_resize_keeps_full_unit_stats_and_pending_layer_edits()
    {
        var doc = Sample(); var host = Host(doc);
        var originalInfo = Info(doc, 6, 0).V.ToArray();
        int width = Convert.ToInt32(((BtlStruct)((BtlTable)doc.Root.F[1]).F[0]).V[0]);
        int height = Convert.ToInt32(((BtlStruct)((BtlTable)doc.Root.F[1]).F[0]).V[1]);
        host.Execute("""
            editor.tool {title='扩图试验',target='map',run=function(map)
              map.cell(0,0).main={id=19,dx=-7,dy=6}
              map.resize {right=1}
            end}
            """, "resize-experiment.lua");
        host.RunTool("扩图试验");
        Assert.Equal(1, host.UndoFrames);
        var resized = Info(doc, 6, 0);
        Assert.Equal(originalInfo.Skip(1), resized.V.Skip(1)); Assert.Equal(10, resized.Layout.Count);
        var exported = Export(doc, "stage10101-扩图试验.btl");
        Assert.Equal((width + 1) * height, Rows(exported, 1, 2).V.Count);
        Assert.Equal(originalInfo.Skip(1).Select(Convert.ToInt64), Info(exported, 6, 0).V.Skip(1).Select(Convert.ToInt64));
        Assert.Equal(19, Convert.ToInt32(TerrainEdits.Project(exported)[0].Main.V[0]));
        Assert.Equal(-7, Convert.ToInt32(TerrainEdits.Project(exported)[0].Main.V[2]));
    }

    [Fact]
    public void Stable_objects_follow_row_moves_and_removed_objects_reject_writes()
    {
        var doc = Sample(); var host = Host(doc);
        host.Execute("""
            editor.tool {title='对象寿命试验',target='stage',run=function()
              local removed=game.weather.create {type=1,start=2,duration=3}
              local retained=game.weather.create {type=2,start=4,duration=5}
              game.weather.remove(removed)
              retained.duration=9
              assert(retained.index==game.weather.count()-1)
              local ok,message=pcall(function() removed.duration=99 end)
              assert(not ok and string.find(message,'移除'))
              local tree=game.behavior_trees.create()
              local child=tree:add_child {class='act',params={1,2,3}}
              assert(child.params[2]==2)
            end}
            """, "object-lifetime.lua");
        host.RunTool("对象寿命试验");
        var weather = Rows(doc, 9, 0);
        Assert.Equal(9, Convert.ToInt32(((BtlScalar)((BtlTable)weather.V[^1]).F[2]).V));
    }

    [Fact]
    public void Ordinary_action_failure_rolls_back_new_optional_objects_and_unflushed_layers()
    {
        var doc = Sample(); var host = Host(doc); string before = BtlFrontJson.Serialize(doc);
        host.Execute("""
            editor.action('对象回滚试验',function()
              game.units.get(0).general={id=88,active=true}
              game.map.cell(0,0).secondary={id=19,dx=-7}
              game.weather.create {type=2,duration=10}
              error('实验故意失败')
            end)
            """, "rollback-experiment.lua");
        Assert.Throws<FrontEditException>(() => host.Run("对象回滚试验", new ScriptArgs()));
        Assert.Equal(before, BtlFrontJson.Serialize(doc)); Assert.Equal(0, host.UndoFrames);
        host.RunTool("heal_selected", new ScriptArgs { UnitIndex = 0 });
    }

    [Fact]
    public void Readonly_named_objects_do_not_materialize_legacy_types_or_modify_json()
    {
        var doc = Sample(); var host = Host(doc); string before = BtlFrontJson.Serialize(doc);
        host.Execute("""
            editor.action('只读试验',{get=function()
              local unit=game.units.get(0)
              if unit.general then local id=unit.general.id end
              if unit.behavior then local radius=unit.behavior.radius end
              local count=game.targets.count()+game.weather.count()+game.routes.count()+game.events.count()
              if game.behavior_trees.count()>0 then local tree=game.behavior_trees.get(0); local name=tree.name end
              return count
            end})
            """, "readonly-experiment.lua");
        host.CallGet("只读试验");
        Assert.Equal(before, BtlFrontJson.Serialize(doc)); Assert.Equal(0, host.SnapshotCount);
    }
}
