using BtlCore.Front;
using BtlCore.Scripting;
using Xunit;

namespace BtlCore.Front.Tests;

public class TerrainEditParityTests
{
    static string TerrainLua()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null)
        {
            string path = Path.Combine(dir.FullName, "EditorLayout", "scripts", "terrain.lua");
            if (File.Exists(path)) return File.ReadAllText(path);
            dir = dir.Parent;
        }
        throw new FileNotFoundException("EditorLayout/scripts/terrain.lua");
    }

    static BtlStruct Attr(byte id, byte variant, sbyte dx, sbyte dy) =>
        FrontEdit.StructFromFbs("TileAttr", id, variant, dx, dy);

    static BtlFrontDocument Sample()
    {
        var doc = new BtlFrontDocument { Root = BtlFrontJson.NewTable() };
        var map = FrontEdit.EnsureTable(doc.Root, 1);
        FrontEdit.SetField(map, 0, FrontEdit.StructFromFbs("Size",
            (ushort)2, (ushort)2, (ushort)0, (ushort)0, (ushort)2, (ushort)2));
        var tiles = FrontEdit.EnsureVec(map, 2, "u16");
        tiles.V.Add((ushort)((1 & 7) | ((3 & 0x1F) << 3)));
        tiles.V.Add((ushort)((2 | 4) << 8));
        tiles.V.Add((ushort)((8 | 16) << 8));
        tiles.V.Add((ushort)((4 | 8 | 16) << 8));
        var attrs = FrontEdit.EnsureVec(map, 3, "struct");
        attrs.V.Add(Attr(46, 1, -2, 5));
        attrs.V.Add(Attr(7, 2, 0, 0));
        attrs.V.Add(Attr(8, 0, 1, -1));
        attrs.V.Add(Attr(0, 0, 0, 0));
        attrs.V.Add(Attr(10, 1, 0, 0));
        attrs.V.Add(Attr(11, 4, 3, 4));
        return doc;
    }

    static ScriptHost Host(BtlFrontDocument doc)
    {
        var host = new ScriptHost();
        host.Load(doc);
        host.Execute(TerrainLua(), "terrain.lua");
        return host;
    }

    static Dictionary<string, object> Map(params (string key, object value)[] pairs)
    {
        var d = new Dictionary<string, object>();
        foreach (var (key, value) in pairs) d[key] = value;
        return d;
    }

    [Fact]
    public void Project_then_store_keeps_tiles_and_attributes()
    {
        var doc = Sample();
        string before = BtlFrontJson.Serialize(doc);
        TerrainEdits.Store(doc, TerrainEdits.Project(doc));
        Assert.Equal(before, BtlFrontJson.Serialize(doc));
    }

    [Fact]
    public void Climate_layers_offset_and_paste_match_script()
    {
        var doc = Sample();
        var host = Host(doc);

        host.Run("apply_climate", new ScriptArgs
        {
            CellIndex = 0,
            Item = Map(("t", 2), ("variant", 4), ("sea", false))
        });
        host.Run("set_sea", new ScriptArgs { CellIndex = 0, Value = true });
        host.Run("apply_layer", new ScriptArgs
        {
            CellIndex = 0,
            Item = Map(("layer", "main"), ("terrain_id", 12), ("variant", 1), ("dx", 0), ("dy", -3))
        });
        host.Run("clear_layer", new ScriptArgs { CellIndex = 1, Input = Map(("layer", "decor")) });
        host.Run("clear_layer", new ScriptArgs { CellIndex = 1, Input = Map(("layer", "sea")) });
        host.Run("set_offset", new ScriptArgs
        {
            CellIndex = 2,
            Input = Map(("layer", "main"), ("dx", 6), ("dy", -8))
        });

        string frozen = BtlFrontJson.Serialize(doc);
        int frames = host.UndoFrames;
        host.Run("set_offset", new ScriptArgs
        {
            CellIndex = 3,
            Input = Map(("layer", "decor"), ("dx", 1), ("dy", 1))
        });
        Assert.Equal(frozen, BtlFrontJson.Serialize(doc));
        Assert.Equal(frames, host.UndoFrames);

        ushort pasted = (ushort)((4 | 8) << 8);
        host.Run("paste_terrain", new ScriptArgs
        {
            CellIndex = 3,
            Input = Map(
                ("terrain", pasted),
                ("decor", new object[] { (byte)5, (byte)1, (sbyte)2, (sbyte)3 }),
                ("main", new object[] { (byte)9, (byte)0, (sbyte)-5, (sbyte)6 }),
                ("secondary", null))
        });
        host.Run("apply_climate", new ScriptArgs
        {
            CellIndex = 2,
            Item = Map(("sea", true))
        });
        string outside = BtlFrontJson.Serialize(doc);
        host.Run("apply_climate", new ScriptArgs
        {
            CellIndex = 99,
            Item = Map(("t", 1), ("variant", 0), ("sea", false))
        });
        Assert.Equal(outside, BtlFrontJson.Serialize(doc));

        string packed = BtlFrontJson.Serialize(doc);
        TerrainEdits.Store(doc, TerrainEdits.Project(doc));
        Assert.Equal(packed, BtlFrontJson.Serialize(doc));
        Assert.True(host.UndoFrames >= 1);
    }
}
