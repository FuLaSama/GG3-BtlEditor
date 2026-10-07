using BtlCore.Front;
using BtlCore.Scripting;
using Xunit;

namespace BtlCore.Front.Tests;

public class LayoutReadTests
{
    static BtlFrontDocument DocWithAgents()
    {
        var doc = new BtlFrontDocument { Root = BtlFrontJson.NewTable() };
        var ai = FrontEdit.EnsureTable(doc.Root, 6);
        var agents = FrontEdit.EnsureVec(ai, 0, "table");
        agents.V.Add(BtlFrontJson.NewTable());
        var packed = BtlFrontJson.NewTable();
        var info = FrontEdit.NewStruct(new[] { "u16", "u16", "u16", "u16", "u16", "u16", "u16", "u16", "u16", "u16" });
        info.V[0] = (ushort)1;
        info.V[4] = (ushort)((5 << 8) | 3);
        info.V[6] = (ushort)0;
        FrontEdit.SetField(packed, 0, info);
        agents.V.Add(packed);
        return doc;
    }

    [Fact]
    public void Missing_member_is_null_and_written_zero_stays()
    {
        var doc = DocWithAgents();
        Assert.Null(LayoutRead.Value(doc, "/6/0", "/0.6", 0, null, "number", null, null));
        Assert.Equal(0L, Convert.ToInt64(LayoutRead.Value(doc, "/6/0", "/0.6", 1, null, "number", null, null)));
    }

    [Fact]
    public void Bit_slice_matches_level_pack()
    {
        var doc = DocWithAgents();
        Assert.Equal(3L, Convert.ToInt64(LayoutRead.Value(doc, "/6/0", "/0.4", 1, null, "number", 0, 8)));
        Assert.Equal(5L, Convert.ToInt64(LayoutRead.Value(doc, "/6/0", "/0.4", 1, null, "number", 8, 8)));
    }

    [Fact]
    public void Resolve_unit_reads_hp_and_empty_cell_is_null()
    {
        var doc = DocWithAgents();
        var host = new ScriptHost();
        host.Load(doc);
        EditorScripts.Load(host, "unit/基础部队信息.lua");
        Assert.Equal(1, host.ResolveIndex("unit", 1));
        Assert.Null(host.ResolveIndex("unit", 4));
        Assert.Equal(0L, Convert.ToInt64(host.ReadBind("/6/0", "/0.6", host.ResolveIndex("unit", 1), 1, "number", null, null)));
        Assert.Null(host.ReadBind("/6/0", "/0.6", host.ResolveIndex("unit", 4), 4, "number", null, null));
    }

    [Fact]
    public void Resolve_building_and_fort_follow_the_cell()
    {
        var doc = new BtlFrontDocument { Root = BtlFrontJson.NewTable() };
        var events = FrontEdit.EnsureVec(FrontEdit.EnsureTable(doc.Root, 5), 0, "table");
        events.V.Add(Building(1, 9));
        events.V.Add(Fort(2, 4));

        var host = new ScriptHost();
        host.Load(doc);
        EditorScripts.Load(host, "site/建筑配置.lua", "site/军事工事配置.lua");
        Assert.Equal(0, host.ResolveIndex("building", 1));
        Assert.Null(host.ResolveIndex("building", 2));
        Assert.Equal(1, host.ResolveIndex("fort", 2));
        Assert.Null(host.ResolveIndex("fort", 0));
        Assert.Equal(9L, Convert.ToInt64(host.ReadBind("/5/0", "/3/0.1", 0, null, "number", null, null)));
        Assert.Null(host.ReadBind("/5/0", "/3/0.1", 1, null, "number", null, null));
    }

    static BtlTable Building(ushort cell, ushort type)
    {
        var ev = BtlFrontJson.NewTable();
        FrontEdit.SetScalar(ev, 0, "u16", cell);
        var data = FrontEdit.NewStruct(new[] { "u16", "u16", "u8", "u8", "i8", "i8" });
        data.V[1] = type;
        FrontEdit.SetField(FrontEdit.EnsureTable(ev, 3), 0, data);
        return ev;
    }

    static BtlTable Fort(ushort cell, byte id)
    {
        var ev = BtlFrontJson.NewTable();
        FrontEdit.SetScalar(ev, 0, "u16", cell);
        FrontEdit.SetScalar(FrontEdit.EnsureTable(ev, 4), 0, "u8", id);
        return ev;
    }
}
