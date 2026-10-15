using BtlCore.Fb;
using BtlCore.Scripting;
using Xunit;

namespace BtlCore.Front.Tests;

public class OptionalFieldWriteTests
{
    static Dictionary<string, object> Input(params (string Key, object Value)[] pairs) => pairs.ToDictionary(p => p.Key, p => p.Value);
    static BtlFrontDocument Blank() => new() { Root = BtlFrontJson.NewTable(), Context=EditorScripts.LegacyContext };
    static ScriptHost Host(BtlFrontDocument doc, params string[] scripts)
    {
        var host = new ScriptHost(); host.Load(doc); EditorScripts.Load(host, scripts); return host;
    }
    static BtlBinaryInspection Written(BtlFrontDocument doc)
    {
        var built = FrontToBtl.Build(doc);
        Assert.True(built.Ok, string.Join("\n", built.Issues));
        return new BtlBinaryInspection(built.Bytes, doc.Context.Schema);
    }

    [Fact]
    public void Reinforcement_missing_false_true_and_zero_remain_distinct_in_binary()
    {
        var doc = Blank();
        var points = FrontEdit.EnsureVec(FrontEdit.EnsureTable(doc.Root, 3), 1, "table");
        for (int i = 0; i < 3; i++) points.V.Add(BtlFrontJson.NewTable());
        var host = Host(doc, "stage/增兵部署点.lua");
        for (int i = 0; i < 3; i++)
            host.Run("update_reinforce", new ScriptArgs { Index = i,
                Input = Input(("cell", i), ("is_key", i == 0 ? null : i == 2), ("flag", i == 0 ? null : 0)) });
        var binary = Written(doc);
        Assert.Equal(BinaryValueState.Absent, binary.Find("/3/1/i0/2").State);
        Assert.Equal(BinaryValueState.Present, binary.Find("/3/1/i1/2").State);
        Assert.Equal(false, binary.Find("/3/1/i1/2").Value);
        Assert.Equal(true, binary.Find("/3/1/i2/2").Value);
        Assert.Equal(BinaryValueState.Absent, binary.Find("/3/1/i0/3").State);
        Assert.Equal((byte)0, binary.Find("/3/1/i1/3").Value);
        host.Run("update_reinforce", new ScriptArgs { Index = 1, Input = Input(("is_key", null), ("flag", null)) });
        Assert.False(((BtlTable)points.V[1]).F.ContainsKey(2));
        Assert.False(((BtlTable)points.V[1]).F.ContainsKey(3));
    }

    [Fact]
    public void Fort_can_clear_optional_scalars_without_disappearing_from_selection()
    {
        var doc = Blank(); var events = FrontEdit.EnsureVec(FrontEdit.EnsureTable(doc.Root, 5), 0, "table");
        var ev = BtlFrontJson.NewTable(); events.V.Add(ev);
        FrontEdit.SetScalar(ev, 0, "u16", (ushort)0);
        var fort = FrontEdit.EnsureTable(ev, 4);
        FrontEdit.SetScalar(fort, 0, "u8", (byte)2);
        var hidden = FrontEdit.EnsureTable(fort, 2);
        FrontEdit.SetScalar(hidden, 0, "u8", (byte)7); // 未展示子表应保留，类型与 schema 一致。
        var host = Host(doc, "site/军事工事配置.lua");
        host.Run("apply_fort", new ScriptArgs { Index = 0, Input = Input(("fort_id", null), ("field1", null), ("field3", null)) });
        Assert.False(ev.F.ContainsKey(1)); Assert.False(fort.F.ContainsKey(0)); Assert.False(fort.F.ContainsKey(3));
        Assert.Same(hidden, fort.F[2]);
        Assert.Equal((byte)7, ((BtlScalar)hidden.F[0]).V);
        Assert.Equal(0, host.ResolveIndex("fort", 0));
        var binary = Written(doc);
        Assert.Equal(BinaryValueState.Absent, binary.Find("/5/0/i0/1").State);
        Assert.Equal(BinaryValueState.Absent, binary.Find("/5/0/i0/4/0").State);
        Assert.Equal((byte)7, binary.Find("/5/0/i0/4/2/0").Value);
        host.Run("apply_fort", new ScriptArgs { Index = 0, Input = Input(("fort_id", 0), ("field1", 0), ("field3", 0)) });
        Assert.Equal(BinaryValueState.Present, Written(doc).Find("/5/0/i0/4/0").State);
    }

    [Fact]
    public void General_and_special_empty_fields_do_not_acquire_default_slots()
    {
        var doc = Blank(); var agents = FrontEdit.EnsureVec(FrontEdit.EnsureTable(doc.Root, 6), 0, "table");
        var unit = UnitEdits.Create(0, 0, 1, 101, 1, 0, 10, 20,doc.Context); agents.V.Add(unit);
        var general = FrontEdit.EnsureTable(unit, 11); FrontEdit.SetScalar(general, 2, "u8", (byte)9);
        var special = FrontEdit.EnsureTable(unit, 10); FrontEdit.SetScalar(special, 1, "u8", (byte)8);
        var host = Host(doc, "unit/配置表单.lua", "unit/基础部队信息.lua");
        var input = Input(("general", true), ("ex", true), ("general_id", null), ("general_active", null),
            ("general_param2", null), ("ex_id", null), ("ex_1", 8));
        host.Run("apply_unit", new ScriptArgs { Index = 0, Input = input });
        Assert.Empty(general.F); Assert.False(special.F.ContainsKey(0)); Assert.Equal((byte)8, ((BtlScalar)special.F[1]).V);
        var binary = Written(doc);
        Assert.Equal(BinaryValueState.Present, binary.Find("/6/0/i0/11").State);
        Assert.Equal(BinaryValueState.Absent, binary.Find("/6/0/i0/11/2").State);
        input["general_active"] = false; input["general_param2"] = 0; input["ex_id"] = 0;
        host.Run("apply_unit", new ScriptArgs { Index = 0, Input = input });
        binary = Written(doc);
        Assert.Equal(false, binary.Find("/6/0/i0/11/1").Value);
        Assert.Equal((byte)0, binary.Find("/6/0/i0/11/2").Value);
        Assert.Equal((ushort)0, binary.Find("/6/0/i0/10/0").Value);
    }

    [Fact]
    public void Route_nil_empty_and_zero_element_remain_distinct()
    {
        var doc = Blank(); var routes = FrontEdit.EnsureVec(FrontEdit.EnsureTable(doc.Root, 6), 1, "table");
        routes.V.Add(BtlFrontJson.NewTable()); var host = Host(doc, "faction/路线.lua");
        foreach (object cells in new object[] { null, Array.Empty<object>(), new object[] { 0 }, null })
        {
            host.Run("set_route", new ScriptArgs { Index = 0, Input = Input(("flag", null), ("cells", cells)) });
            var route = (BtlTable)routes.V[0]; Assert.False(route.F.ContainsKey(0));
            var binary = Written(doc); var vector = binary.Find("/6/1/i0/1");
            Assert.Equal(cells == null ? BinaryValueState.Absent : BinaryValueState.Present, vector.State);
            if (cells != null) Assert.Equal(((object[])cells).Length, vector.ChildCount);
        }
        host.Execute("editor.action('check_route_presence', function() local route=game.routes.get(0); assert(route.cells==nil); route.cells={}; assert(#route.cells==0) end)", "route-presence");
        host.Run("check_route_presence", new ScriptArgs());
        Assert.True(((BtlTable)routes.V[0]).F.ContainsKey(1));
    }
}
