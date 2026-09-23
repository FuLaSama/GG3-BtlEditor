using BtlCore.Front;
using BtlCore.Scripting;
using Xunit;

namespace BtlCore.Front.Tests;

public class SiteEditParityTests
{
    static string SiteLua()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null)
        {
            string path = Path.Combine(dir.FullName, "EditorLayout", "scripts", "site.lua");
            if (File.Exists(path)) return File.ReadAllText(path);
            dir = dir.Parent;
        }
        throw new FileNotFoundException("EditorLayout/scripts/site.lua");
    }

    static BtlTable Relation(ushort factionId, ushort item)
    {
        var row = BtlFrontJson.NewTable();
        FrontEdit.SetScalar(row, 0, "u16", factionId);
        var vec = BtlFrontJson.NewVector("u16");
        vec.V.Add(item);
        row.F[1] = vec;
        return row;
    }

    static BtlTable Faction(ushort id, uint color)
    {
        var tbl = BtlFrontJson.NewTable();
        tbl.F[0] = FrontEdit.StructFromFbs("FactionMetadata",
            id, (ushort)1, (byte)1, (byte)0, (byte)0, (byte)0,
            100u, 0u, 1f, 1f, 1f, color, (ushort)0, (ushort)0);
        FrontEdit.SetScalar(tbl, 7, "u8", (byte)1);
        FrontEdit.SetScalar(tbl, 8, "u16", (ushort)1);
        return tbl;
    }

    static BtlFrontDocument Sample()
    {
        var doc = new BtlFrontDocument { Root = BtlFrontJson.NewTable() };
        var events = FrontEdit.EnsureVec(FrontEdit.EnsureTable(doc.Root, 5), 0, "table");
        var script = BtlFrontJson.NewTable();
        FrontEdit.SetScalar(script, 0, "u16", (ushort)9);
        events.V.Add(script);
        events.V.Add(SiteEdits.CreateBuilding(0, 12, 3, 1, 2, -2, 4, 7));
        var fort = SiteEdits.CreateFort(1, 2, 0);
        FrontEdit.SetScalar(fort, 1, "u16", (ushort)6);
        events.V.Add(fort);

        var info = FrontEdit.EnsureTable(doc.Root, 4);
        var factions = FrontEdit.EnsureVec(info, 0, "table");
        var cards = FrontEdit.EnsureVec(info, 1, "table");
        var limits = FrontEdit.EnsureVec(info, 3, "table");
        foreach (ushort id in new ushort[] { 0, 1, 2 })
        {
            factions.V.Add(Faction(id, id == 0 ? 0x11223344u : 0xFFFFFFFFu));
            cards.V.Add(Relation((ushort)(2 - id), (ushort)(10 + id)));
            limits.V.Add(Relation(id, (ushort)(20 + id)));
        }
        return doc;
    }

    static ScriptHost Host(BtlFrontDocument doc)
    {
        var host = new ScriptHost();
        host.Load(doc);
        host.Execute(SiteLua(), "site.lua");
        return host;
    }

    static Dictionary<string, object> Map(params (string key, object value)[] pairs)
    {
        var d = new Dictionary<string, object>();
        foreach (var (key, value) in pairs) d[key] = value;
        return d;
    }

    static BtlVector Events(BtlFrontDocument doc) => (BtlVector)((BtlTable)doc.Root.F[5]).F[0];

    static BtlTable EventAt(BtlFrontDocument doc, int index) => (BtlTable)Events(doc).V[index];

    [Fact]
    public void Event_sync_keeps_script_events_and_is_idempotent()
    {
        var doc = Sample();
        string before = BtlFrontJson.Serialize(doc);
        var buildings = new BtlTable[4];
        var forts = new BtlTable[4];
        buildings[0] = EventAt(doc, 1);
        forts[1] = EventAt(doc, 2);
        SiteEdits.SyncEvents(doc, buildings, forts);
        Assert.Equal(before, BtlFrontJson.Serialize(doc));
    }

    [Fact]
    public void Building_fort_and_faction_writes_match_script()
    {
        var doc = Sample();
        var host = Host(doc);

        host.Run("apply_building", new ScriptArgs
        {
            ObjectPath = "Root.trigger_info.events",
            ObjectIndex = 1,
            Input = Map(
                ("type", null), ("owner", null), ("flag", null), ("extra", null),
                ("dx", null), ("dy", null), ("field6", null))
        });
        var detail = (BtlTable)EventAt(doc, 1).F[3];
        Assert.False(detail.F.ContainsKey(6));
        Assert.Equal(0, Convert.ToInt32(((BtlStruct)detail.F[0]).V[1]));

        host.Run("apply_fort", new ScriptArgs
        {
            ObjectPath = "Root.trigger_info.events",
            ObjectIndex = 2,
            Input = Map(("fort_id", null), ("field1", null), ("field3", null))
        });
        Assert.Equal((byte)0, ((BtlScalar)((BtlTable)EventAt(doc, 2).F[4]).F[0]).V);
        Assert.Equal((ushort)0, ((BtlScalar)EventAt(doc, 2).F[1]).V);

        host.Run("place_building", new ScriptArgs
        {
            CellIndex = 2,
            Input = Map(
                ("type", 101), ("flag", 1), ("extra", 1),
                ("owner", null), ("dx", null), ("dy", null), ("field6", null))
        });
        var placed = EventAt(doc, Events(doc).V.Count - 1);
        var gridB = new List<BtlTable> { EventAt(doc, 1), null, placed, null };
        var gridF = new List<BtlTable> { null, EventAt(doc, 2), null, null };
        string packed = BtlFrontJson.Serialize(doc);
        SiteEdits.SyncEvents(doc, gridB, gridF);
        Assert.Equal(packed, BtlFrontJson.Serialize(doc));

        host.Run("place_fort", new ScriptArgs
        {
            CellIndex = 3,
            Input = Map(("fort_id", 2), ("field3", 0), ("field1", 0))
        });
        host.Run("delete_event", new ScriptArgs { Index = 1 });
        Assert.Equal((ushort)9, ((BtlScalar)EventAt(doc, 0).F[0]).V);

        host.Run("apply_faction", new ScriptArgs
        {
            ObjectPath = "Root.faction_info.factions",
            ObjectIndex = 0,
            Input = Map(
                ("id", 0), ("camp", 2), ("country", 4), ("is_ai", null), ("val5", null), ("align1", null),
                ("gold", 50), ("tech", null), ("income", null), ("damage", null), ("hp", null),
                ("r", 1), ("g", 2), ("b", 3), ("a", null),
                ("align2", null), ("config_id", null), ("general_flag", null), ("config_ref", 9))
        });
        var factions = (BtlVector)((BtlTable)doc.Root.F[4]).F[0];
        var info = (BtlStruct)((BtlTable)factions.V[0]).F[0];
        Assert.Equal((1u << 24) | (2u << 16) | (3u << 8) | 255u, Convert.ToUInt32(info.V[11]));
        Assert.False(((BtlTable)factions.V[0]).F.ContainsKey(7));

        host.Run("apply_faction", new ScriptArgs
        {
            ObjectPath = "Root.faction_info.factions",
            ObjectIndex = 1,
            Input = Map(
                ("id", 1), ("camp", null), ("country", null), ("is_ai", null), ("val5", null), ("align1", null),
                ("gold", null), ("tech", null), ("income", null), ("damage", null), ("hp", null),
                ("r", null), ("g", null), ("b", null), ("a", null),
                ("align2", null), ("config_id", null), ("general_flag", null), ("config_ref", null))
        });
        Assert.Equal(0xFFFFFFFFu, Convert.ToUInt32(((BtlStruct)((BtlTable)factions.V[1]).F[0]).V[11]));

        host.Run("move_faction", new ScriptArgs { Input = Map(("from", 0), ("to", 2)) });
        host.Run("add_faction", new ScriptArgs());
        host.Run("delete_faction", new ScriptArgs { Index = 1 });
        Assert.Equal(3, factions.V.Count);
    }
}
