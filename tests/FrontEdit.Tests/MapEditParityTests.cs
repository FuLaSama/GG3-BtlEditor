using BtlCore.Front;
using BtlCore.Scripting;
using Xunit;

namespace BtlCore.Front.Tests;

public class MapEditParityTests
{
    static string MapLua()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null)
        {
            string path = Path.Combine(dir.FullName, "EditorLayout", "scripts", "map.lua");
            if (File.Exists(path)) return File.ReadAllText(path);
            dir = dir.Parent;
        }
        throw new FileNotFoundException("EditorLayout/scripts/map.lua");
    }

    static BtlFrontDocument Sample()
    {
        var doc = new BtlFrontDocument { Root = BtlFrontJson.NewTable() };
        var map = FrontEdit.EnsureTable(doc.Root, 1);
        FrontEdit.SetField(map, 0, FrontEdit.StructFromFbs("Size",
            (ushort)2, (ushort)2, (ushort)2, (ushort)0, (ushort)2, (ushort)2));
        var tiles = FrontEdit.EnsureVec(map, 2, "u16");
        tiles.V.Add((ushort)((4 << 8) | 3));
        tiles.V.Add((ushort)0);
        tiles.V.Add((ushort)0);
        tiles.V.Add((ushort)0);
        var attrs = FrontEdit.EnsureVec(map, 3, "struct");
        attrs.V.Add(FrontEdit.StructFromFbs("TileAttr", (byte)5, (byte)1, (sbyte)-2, (sbyte)3));

        var agents = FrontEdit.EnsureVec(FrontEdit.EnsureTable(doc.Root, 6), 0, "table");
        agents.V.Add(UnitEdits.Create(0, 0, 1, 10, 0, 0, 8, 8));
        agents.V.Add(UnitEdits.Create(5, 0, 4, 11, 0, 0, 1, 1));
        agents.V.Add(UnitEdits.Create(65535, 1, 9, 3, 0, 0, 1, 1));

        var events = FrontEdit.EnsureVec(FrontEdit.EnsureTable(doc.Root, 5), 0, "table");
        events.V.Add(SiteEdits.CreateBuilding(1, 12, 3, 1, 2, -2, 4, null));
        events.V.Add(SiteEdits.CreateFort(2, 2, 0));
        var script = BtlFrontJson.NewTable();
        FrontEdit.SetScalar(script, 0, "u16", (ushort)3);
        events.V.Add(script);
        var outside = BtlFrontJson.NewTable();
        FrontEdit.SetScalar(outside, 0, "u16", (ushort)4);
        events.V.Add(outside);

        var points = FrontEdit.EnsureVec(FrontEdit.EnsureTable(doc.Root, 3), 1, "table");
        var near = BtlFrontJson.NewTable();
        FrontEdit.SetScalar(near, 0, "u16", (ushort)0);
        points.V.Add(near);
        var far = BtlFrontJson.NewTable();
        FrontEdit.SetScalar(far, 0, "u16", (ushort)4);
        points.V.Add(far);
        return doc;
    }

    static ScriptHost Host(BtlFrontDocument doc)
    {
        var host = new ScriptHost();
        host.Load(doc);
        host.Execute(MapLua(), "map.lua");
        return host;
    }

    static Dictionary<string, object> Map(params (string key, object value)[] pairs)
    {
        var d = new Dictionary<string, object>();
        foreach (var (key, value) in pairs) d[key] = value;
        return d;
    }

    static void Resync(BtlFrontDocument doc)
    {
        TerrainEdits.Store(doc, TerrainEdits.Project(doc));
        var size = (BtlStruct)((BtlTable)doc.Root.F[1]).F[0];
        int total = Convert.ToInt32(size.V[0]) * Convert.ToInt32(size.V[1]);
        UnitEdits.SyncAgents(doc, UnitEdits.Project(doc, total));
        var buildings = new BtlTable[total];
        var forts = new BtlTable[total];
        var events = (BtlVector)((BtlTable)doc.Root.F[5]).F[0];
        foreach (var item in events.V)
        {
            if (item is not BtlTable ev || !ev.F.TryGetValue(0, out var cellNode) || cellNode is not BtlScalar sc) continue;
            int idx = Convert.ToInt32(sc.V);
            if (idx < 0 || idx >= total) continue;
            if (ev.F.TryGetValue(3, out var b) && b is BtlTable) buildings[idx] = ev;
            if (ev.F.TryGetValue(4, out var f) && f is BtlTable) forts[idx] = ev;
        }
        SiteEdits.SyncEvents(doc, buildings, forts);
    }

    [Fact]
    public void Resize_matches_script_and_stays_idempotent()
    {
        var doc = Sample();
        var host = Host(doc);

        string before = BtlFrontJson.Serialize(doc);
        host.Run("resize_map", new ScriptArgs { Input = Map(("left", 0), ("right", 0), ("up", 0), ("down", 0)) });
        Assert.Equal(before, BtlFrontJson.Serialize(doc));
        Assert.Equal(0, host.UndoFrames);

        host.Run("resize_map", new ScriptArgs { Input = Map(("left", 500), ("right", 0), ("up", 0), ("down", 0)) });
        Assert.Equal(before, BtlFrontJson.Serialize(doc));

        host.Run("resize_map", new ScriptArgs { Input = Map(("left", 1), ("right", 0), ("up", 0), ("down", 0)) });

        var size = (BtlStruct)((BtlTable)doc.Root.F[1]).F[0];
        Assert.Equal(3, Convert.ToInt32(size.V[0]));
        Assert.Equal(1, Convert.ToInt32(size.V[4]));
        var agents = (BtlVector)((BtlTable)doc.Root.F[6]).F[0];
        Assert.Equal(2, agents.V.Count);
        Assert.Equal(1, Convert.ToInt32(((BtlStruct)((BtlTable)agents.V[0]).F[0]).V[0]));
        Assert.Equal(65535, Convert.ToInt32(((BtlStruct)((BtlTable)agents.V[1]).F[0]).V[0]));
        var events = (BtlVector)((BtlTable)doc.Root.F[5]).F[0];
        Assert.Equal((ushort)4, ((BtlScalar)((BtlTable)events.V[3]).F[0]).V);
        var points = (BtlVector)((BtlTable)doc.Root.F[3]).F[1];
        Assert.Single(points.V);
        Assert.Equal((ushort)1, ((BtlScalar)((BtlTable)points.V[0]).F[0]).V);

        string packed = BtlFrontJson.Serialize(doc);
        Resync(doc);
        Assert.Equal(packed, BtlFrontJson.Serialize(doc));
    }

    [Fact]
    public void Paste_and_route_cells_match_script()
    {
        var doc = Sample();
        var host = Host(doc);

        host.Run("copy_unit", new ScriptArgs { CellIndex = 0 });
        host.Run("paste_unit", new ScriptArgs { CellIndex = 1 });
        var agents = (BtlVector)((BtlTable)doc.Root.F[6]).F[0];
        Assert.Equal(10, Convert.ToInt32(((BtlStruct)((BtlTable)agents.V[agents.V.Count - 1]).F[0]).V[2]));

        host.Run("copy_unit", new ScriptArgs { CellIndex = 2 });
        host.Run("paste_unit", new ScriptArgs { CellIndex = 0 });

        host.Run("copy_landmarks", new ScriptArgs { CellIndex = 1 });
        host.Run("paste_landmarks", new ScriptArgs { CellIndex = 0 });
        host.Run("copy_landmarks", new ScriptArgs { CellIndex = 2 });
        host.Run("paste_landmarks", new ScriptArgs { CellIndex = 1 });

        host.Run("add_route", new ScriptArgs { Input = Map(("flag", null), ("cells", new object[] { 4, 5 })) });
        host.Run("set_route", new ScriptArgs
        {
            ObjectPath = "Root.ai_info.behaviors",
            ObjectIndex = 0,
            Input = Map(("flag", 4), ("cells", Array.Empty<object>()))
        });
        var routes = (BtlVector)((BtlTable)doc.Root.F[6]).F[1];
        Assert.Empty(((BtlVector)((BtlTable)routes.V[0]).F[1]).V);
        Assert.Equal((ushort)4, ((BtlScalar)((BtlTable)routes.V[0]).F[0]).V);

        host.Run("add_route", new ScriptArgs { Input = Map(("flag", 2), ("cells", new object[] { 1 })) });
        host.Run("delete_route", new ScriptArgs { Index = 0 });
        Assert.Single(routes.V);
    }
}
