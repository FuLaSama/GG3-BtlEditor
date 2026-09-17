using BtlCore.Front;
using BtldMapEditor;

namespace BtldMapEditor.Front
{
    /// <summary>
    /// 贴图绘制入口：把 Front 格子工作视图拆成 DecodedTerrainMap（几何 + 建筑/工事列表）。
    /// 不改 Document。地形码低 8 位 = 气候 3bit + 变种，高 8 位 = 海/阻拦/A1A2A3 标志。
    /// </summary>
    public static class FrontTerrain
    {
        public static DecodedTerrainMap FromCells(BtlFrontDocument doc, List<MapCell> cells)
        {
            var decoded = new DecodedTerrainMap();
            if (doc == null || cells == null) return decoded;

            int width = FrontNav.MapWidth(doc);
            int height = FrontNav.MapHeight(doc);
            decoded.Width = width;
            decoded.Height = height;
            var size = FrontNav.MapSize(doc);
            decoded.PlayableFlag = (byte)FrontNav.ScalarI64(FrontNav.Map(doc), 1, 1);

            int left = (int)FrontNav.MemberU16(size, 2);
            int top = (int)FrontNav.MemberU16(size, 3);
            int pw = (int)FrontNav.MemberU16(size, 4);
            int ph = (int)FrontNav.MemberU16(size, 5);
            if (pw <= 0) pw = width;
            if (ph <= 0) ph = height;

            for (int i = 0; i < cells.Count; i++)
            {
                var item = cells[i];
                TerrainGeometry.CellCenter(item.X, item.Y, out int x, out int y);
                ushort code = item.Terrain;
                int lo = code & 0xFF;
                int hi = code >> 8;

                var cell = new TerrainCell
                {
                    Col = item.X,
                    Row = item.Y,
                    Index = item.Index,
                    X = x,
                    Y = y,
                    T = lo & 7,
                    V = lo >> 3,
                    Sea = (hi & 0x02) != 0,
                    CoastB = ((hi & 0x02) != 0) && ((hi & 0x20) != 0),
                    Blocked = (hi & 0x01) != 0,
                    Playable = item.X >= left && item.X < left + pw && item.Y >= top && item.Y < top + ph,
                    A1 = (hi & 0x04) != 0 ? SlotFromAttr(item.Attr) : (TerrainSlot?)null,
                    A2 = (hi & 0x08) != 0 ? SlotFromAttr(item.AttrA2) : (TerrainSlot?)null,
                    A3 = (hi & 0x10) != 0 ? SlotFromAttr(item.AttrA3) : (TerrainSlot?)null,
                };
                decoded.Cells.Add(cell);

                var bData = FrontNav.BuildingData(item.TriggerBldg);
                int buildingId = (int)FrontNav.MemberU16(bData, 1);
                if (buildingId != 0)
                {
                    decoded.Buildings.Add(new TerrainMapBuilding
                    {
                        Index = item.Index,
                        BuildingId = buildingId,
                        Variant = (int)FrontNav.MemberI64(bData, 3),
                        Val1 = (int)FrontNav.MemberU16(bData, 0),
                        Val2 = (int)FrontNav.MemberI64(bData, 2),
                        Dx = (sbyte)FrontNav.MemberI64(bData, 4),
                        Dy = (sbyte)FrontNav.MemberI64(bData, 5),
                    });
                }

                var fort = FrontNav.FortDetail(item.TriggerFort);
                int fortId = (int)FrontNav.ScalarI64(fort, 0);
                if (fortId != 0)
                {
                    bool faceLeft = (FrontNav.AgentU16(item.Unit, 5) & 0xFF) == 0;
                    decoded.Forts.Add(new TerrainMapFort
                    {
                        Index = item.Index,
                        FortId = fortId,
                        FaceLeft = faceLeft,
                    });
                }
            }

            return decoded;
        }

        static TerrainSlot? SlotFromAttr(BtlStruct attr)
        {
            // TileAttr：地形 id、变种、渲染 dx/dy。没有 struct 就当没这个装饰槽。
            if (attr == null) return null;
            return new TerrainSlot(
                FrontNav.AttrU8(attr, 0),
                FrontNav.AttrU8(attr, 1),
                FrontNav.AttrI8(attr, 2),
                FrontNav.AttrI8(attr, 3));
        }
    }
}
