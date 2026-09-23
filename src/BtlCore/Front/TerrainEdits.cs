/*
 * TerrainEdits.cs
 *
 * 画布投影：从 tiles / attributes 摊开格子，再按同一顺序写回。
 * 刷地质、分层、偏移和粘贴在 scripts/terrain.lua。
 *
 * 高字节：bit1 海，bit2 装饰，bit3 主地形，bit4 次地形。
 * attributes 按格子顺序只收有标志的层，一层一个 TileAttr，顺序是装饰、主地形、次地形。
 * 装饰在视图里总有一份 struct；标志关掉时不写进向量。主地形、次地形关掉就是 null。
 */
using System.Globalization;

namespace BtlCore.Front
{
    public static class TerrainEdits
    {
        public sealed class Cell
        {
            public ushort Terrain;
            public BtlStruct Decor;
            public BtlStruct Main;
            public BtlStruct Secondary;
        }

        /// <summary>与 RebuildCells 的地形部分相同。格子数是宽乘高。tiles 不够的格用 9001。</summary>
        public static List<Cell> Project(BtlFrontDocument doc)
        {
            var cells = new List<Cell>();
            if (doc?.Root == null) return cells;
            if (doc.Root.F.TryGetValue(1, out var mapNode) == false || mapNode is not BtlTable map) return cells;
            if (map.F.TryGetValue(0, out var sizeNode) == false || sizeNode is not BtlStruct size) return cells;
            int width = (int)U16(size, 0);
            int height = (int)U16(size, 1);
            if (width <= 0 || height <= 0) return cells;

            map.F.TryGetValue(2, out var tilesNode);
            map.F.TryGetValue(3, out var attrsNode);
            var tiles = tilesNode as BtlVector;
            var attrs = attrsNode as BtlVector;
            int attrIndex = 0;
            int attrCount = attrs?.V?.Count ?? 0;
            int tileCount = tiles?.V?.Count ?? 0;
            int total = width * height;
            for (int i = 0; i < total; i++)
            {
                ushort terrain = 9001;
                if (i < tileCount) terrain = (ushort)AsLong(tiles.V[i]);
                int flags = terrain >> 8;
                BtlStruct decor = null, main = null, secondary = null;
                if ((flags & 4) != 0 && attrIndex < attrCount)
                    decor = attrs.V[attrIndex++] as BtlStruct;
                if ((flags & 8) != 0 && attrIndex < attrCount)
                    main = attrs.V[attrIndex++] as BtlStruct;
                if ((flags & 16) != 0 && attrIndex < attrCount)
                    secondary = attrs.V[attrIndex++] as BtlStruct;
                cells.Add(new Cell
                {
                    Terrain = terrain,
                    Decor = decor ?? NewAttr(0, 0, 0, 0),
                    Main = main,
                    Secondary = secondary,
                });
            }
            return cells;
        }

        /// <summary>与 SyncGrid 里重写 tiles / attributes 的那一段相同。不碰部队和事件。</summary>
        public static void Store(BtlFrontDocument doc, IReadOnlyList<Cell> cells)
        {
            if (doc?.Root == null || cells == null || cells.Count == 0) return;
            var map = FrontEdit.EnsureTable(doc.Root, 1);
            var tiles = FrontEdit.EnsureVec(map, 2, "u16");
            var attrs = FrontEdit.EnsureVec(map, 3, "struct");
            tiles.V.Clear();
            attrs.V.Clear();
            foreach (var cell in cells)
            {
                tiles.V.Add(cell.Terrain);
                int flags = cell.Terrain >> 8;
                if ((flags & 4) != 0) attrs.V.Add(cell.Decor ?? NewAttr(0, 0, 0, 0));
                if ((flags & 8) != 0) attrs.V.Add(cell.Main ?? NewAttr(0, 0, 0, 0));
                if ((flags & 16) != 0) attrs.V.Add(cell.Secondary ?? NewAttr(0, 0, 0, 0));
            }
        }

        public static bool HasContent(Cell cell, string layer)
        {
            if (cell == null) return false;
            int bit = LayerBit(layer);
            if (((cell.Terrain >> 8) & bit) == 0) return false;
            var attr = Of(cell, layer);
            return attr != null && U8(attr, 0) != 0;
        }

        static int LayerBit(string layer) => layer switch
        {
            "decor" => 4,
            "main" => 8,
            "secondary" => 16,
            _ => throw new FrontEditException("未知地形层")
        };

        static BtlStruct Of(Cell cell, string layer) => layer switch
        {
            "decor" => cell.Decor,
            "main" => cell.Main,
            "secondary" => cell.Secondary,
            _ => throw new FrontEditException("未知地形层")
        };

        static BtlStruct NewAttr(byte id, byte variant, sbyte dx, sbyte dy) =>
            FrontEdit.StructFromFbs("TileAttr", id, variant, dx, dy);

        static ushort U16(BtlStruct st, int index) => (ushort)AsLong(st != null && index >= 0 && index < st.V.Count ? st.V[index] : null);

        static byte U8(BtlStruct st, int index) => (byte)AsLong(st != null && index >= 0 && index < st.V.Count ? st.V[index] : null);

        static long AsLong(object v)
        {
            if (v == null) return 0;
            if (v is BtlScalar s) return AsLong(s.V);
            try { return Convert.ToInt64(v, CultureInfo.InvariantCulture); }
            catch { return 0; }
        }
    }
}
