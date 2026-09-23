using BtlCore.Front;
using BtlCore.Scripting;
using Xunit;

namespace BtlCore.Front.Tests;

public class UnitEditParityTests
{
    static string UnitLua()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null)
        {
            string path = Path.Combine(dir.FullName, "EditorLayout", "scripts", "unit.lua");
            if (File.Exists(path)) return File.ReadAllText(path);
            dir = dir.Parent;
        }
        throw new FileNotFoundException("EditorLayout/scripts/unit.lua");
    }

    static BtlFrontDocument Sample()
    {
        var doc = new BtlFrontDocument { Root = BtlFrontJson.NewTable() };
        var map = FrontEdit.EnsureTable(doc.Root, 1);
        FrontEdit.SetField(map, 0, FrontEdit.StructFromFbs("Size",
            (ushort)2, (ushort)2, (ushort)0, (ushort)0, (ushort)2, (ushort)2));
        var tiles = FrontEdit.EnsureVec(map, 2, "u16");
        for (int i = 0; i < 4; i++) tiles.V.Add((ushort)0);
        FrontEdit.EnsureVec(map, 3, "struct");

        var ai = FrontEdit.EnsureTable(doc.Root, 6);
        var agents = FrontEdit.EnsureVec(ai, 0, "table");
        agents.V.Add(UnitEdits.Create(0, 0, 1, 10, 0, 0, 10, 10));

        var armed = UnitEdits.Create(1, 0, 2, 8, 0, 0, 20, 20);
        var beh = FrontEdit.EnsureTable(armed, 3);
        FrontEdit.SetScalar(beh, 1, "u16", (ushort)4);
        var gen = FrontEdit.EnsureTable(armed, 11);
        FrontEdit.SetScalar(gen, 0, "u16", (ushort)7);
        FrontEdit.SetScalar(gen, 1, "bool", true);
        FrontEdit.SetScalar(gen, 2, "u8", (byte)4);
        var ex = FrontEdit.EnsureTable(armed, 10);
        FrontEdit.SetScalar(ex, 0, "u16", (ushort)5);
        FrontEdit.SetScalar(ex, 1, "u8", (byte)1);
        agents.V.Add(armed);

        agents.V.Add(UnitEdits.Create(65535, 1, 9, 3, 0, 0, 1, 1));
        return doc;
    }

    static ScriptHost Host(BtlFrontDocument doc)
    {
        var host = new ScriptHost();
        host.Load(doc);
        host.Execute(UnitLua(), "unit.lua");
        return host;
    }

    static Dictionary<string, object> Map(params (string key, object value)[] pairs)
    {
        var d = new Dictionary<string, object>();
        foreach (var (key, value) in pairs) d[key] = value;
        return d;
    }

    static Dictionary<string, object> UnitInput(ushort faction, ushort agent, ushort unit, ushort? hp, ushort maxHp) => Map(
        ("faction", faction), ("agent", agent), ("unit", unit),
        ("hp", hp), ("max_hp", maxHp),
        ("behavior", false), ("general", false), ("ex", false));

    static BtlTable AgentAt(BtlFrontDocument doc, int index)
    {
        var ai = (BtlTable)doc.Root.F[6];
        var vec = (BtlVector)ai.F[0];
        return (BtlTable)vec.V[index];
    }

    [Fact]
    public void Project_then_sync_keeps_on_map_and_off_map_units()
    {
        var doc = Sample();
        string before = BtlFrontJson.Serialize(doc);
        UnitEdits.SyncAgents(doc, UnitEdits.Project(doc, 4));
        Assert.Equal(before, BtlFrontJson.Serialize(doc));
    }

    [Fact]
    public void Stats_optional_tables_place_and_delete_match_script()
    {
        var doc = Sample();
        var host = Host(doc);

        host.Run("apply_unit", new ScriptArgs
        {
            ObjectPath = "Root.ai_info.agents",
            ObjectIndex = 2,
            Input = UnitInput(1, 9, 3, 4, 1)
        });
        Assert.Equal(65535, Convert.ToInt32(((BtlStruct)AgentAt(doc, 2).F[0]).V[0]));

        string missed = BtlFrontJson.Serialize(doc);
        host.Run("apply_unit", new ScriptArgs
        {
            ResolveName = "unit",
            CellIndex = 2,
            Input = UnitInput(0, 1, 10, 1, 1)
        });
        Assert.Equal(missed, BtlFrontJson.Serialize(doc));

        var stats = UnitInput(0, 1, 10, null, 10);
        stats["stack"] = 3;
        stats["mobility"] = 2;
        host.Run("apply_unit", new ScriptArgs
        {
            ResolveName = "unit",
            CellIndex = 0,
            Input = stats
        });
        var info = (BtlStruct)AgentAt(doc, 0).F[0];
        Assert.True(AgentAt(doc, 0).F.ContainsKey(0));
        Assert.Equal(0, Convert.ToInt32(info.V[6]));
        Assert.Equal(3 << 8, Convert.ToInt32(info.V[4]));
        Assert.Equal(2 << 8, Convert.ToInt32(info.V[5]));

        var keep = UnitInput(0, 2, 8, 20, 20);
        keep["behavior"] = true;
        keep["behavior_id"] = 4;
        keep["general"] = true;
        keep["general_id"] = 7;
        keep["general_active"] = false;
        keep["ex"] = true;
        keep["ex_id"] = 5;
        keep["ex_1"] = 1;
        host.Run("apply_unit", new ScriptArgs
        {
            ResolveName = "unit",
            CellIndex = 1,
            Input = keep
        });
        var gen = (BtlTable)AgentAt(doc, 1).F[11];
        Assert.Equal(false, gen.F.ContainsKey(1) ? ((BtlScalar)gen.F[1]).V : null);
        Assert.Equal((byte)0, ((BtlScalar)gen.F[2]).V);

        host.Run("apply_unit", new ScriptArgs
        {
            ResolveName = "unit",
            CellIndex = 1,
            Input = UnitInput(0, 2, 8, 20, 20)
        });
        Assert.False(AgentAt(doc, 1).F.ContainsKey(3));
        Assert.False(AgentAt(doc, 1).F.ContainsKey(11));
        Assert.False(AgentAt(doc, 1).F.ContainsKey(10));

        host.Run("place_unit", new ScriptArgs
        {
            CellIndex = 2,
            Input = Map(
                ("faction", 1), ("agent", 20), ("unit", 101),
                ("level", 1), ("stack", 2), ("mobility", 0), ("direction", 4),
                ("hp", 5), ("max_hp", 6))
        });
        string packed = BtlFrontJson.Serialize(doc);
        UnitEdits.SyncAgents(doc, UnitEdits.Project(doc, 4));
        Assert.Equal(packed, BtlFrontJson.Serialize(doc));

        host.Run("delete_unit", new ScriptArgs { CellIndex = 0 });
        Assert.Equal(65535, Convert.ToInt32(((BtlStruct)AgentAt(doc, 1).F[0]).V[0]));
    }
}
