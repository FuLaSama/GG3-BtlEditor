/*
 * SiteEdits.cs
 *
 * 画布投影：建筑和工事按格子写回 events，没有这两张子表的脚本事件原样保留。
 * 改建筑、工事和势力在 scripts/site.lua。
 */
namespace BtlCore.Front
{
    public static class SiteEdits
    {
        public static BtlTable CreateBuilding(ushort tile, ushort buildingId, ushort val1, byte val2,
            byte? owner, sbyte? dx, sbyte? dy, byte? field6)
        {
            var ev = BtlFrontJson.NewTable();
            FrontEdit.SetScalar(ev, 0, "u16", tile);
            var detail = BtlFrontJson.NewTable();
            detail.F[0] = FrontEdit.StructFromFbs("BuildingData", val1, buildingId, val2, owner ?? (byte)0, dx ?? (sbyte)0, dy ?? (sbyte)0);
            detail.F[2] = BtlFrontJson.NewTable();
            detail.F[4] = BtlFrontJson.NewTable();
            if (field6 != null) FrontEdit.SetScalar(detail, 6, "u8", field6);
            ev.F[3] = detail;
            return ev;
        }

        public static BtlTable CreateFort(ushort tile, byte fortId, byte field3)
        {
            var ev = BtlFrontJson.NewTable();
            FrontEdit.SetScalar(ev, 0, "u16", tile);
            var detail = BtlFrontJson.NewTable();
            FrontEdit.SetScalar(detail, 0, "u8", fortId);
            FrontEdit.SetScalar(detail, 3, "u8", field3);
            ev.F[4] = detail;
            return ev;
        }

        public static void SyncEvents(BtlFrontDocument doc, IList<BtlTable> buildings, IList<BtlTable> forts)
        {
            if (doc?.Root == null || buildings == null || forts == null) return;
            var vec = Events(doc);
            var kept = new List<BtlTable>();
            var onGrid = new HashSet<BtlTable>();
            int n = Math.Min(buildings.Count, forts.Count);
            for (int i = 0; i < n; i++)
            {
                if (buildings[i] != null)
                {
                    FrontEdit.SetScalar(buildings[i], 0, "u16", (ushort)i);
                    onGrid.Add(buildings[i]);
                }
                if (forts[i] != null)
                {
                    FrontEdit.SetScalar(forts[i], 0, "u16", (ushort)i);
                    onGrid.Add(forts[i]);
                }
            }
            foreach (var item in vec.V.ToList())
            {
                if (item is not BtlTable ev) continue;
                bool on = onGrid.Contains(ev);
                bool script = AsTable(ev, 3) == null && AsTable(ev, 4) == null;
                if (on || script)
                {
                    kept.Add(ev);
                    onGrid.Remove(ev);
                }
            }
            foreach (var extra in onGrid) kept.Add(extra);
            vec.V.Clear();
            foreach (var ev in kept) vec.V.Add(ev);
        }

        static BtlTable AsTable(BtlTable tbl, int id)
        {
            if (tbl == null || !tbl.F.TryGetValue(id, out var node)) return null;
            return node as BtlTable;
        }

        static BtlVector Events(BtlFrontDocument doc)
        {
            var trig = FrontEdit.EnsureTable(doc.Root, 5);
            return FrontEdit.EnsureVec(trig, 0, "table");
        }
    }
}
