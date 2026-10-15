/*
 * TerrainEdits.cs
 *
 * 画布投影：从 tiles / attributes 摊开格子，再按同一顺序写回。
 * 刷地质、分层、偏移和粘贴在 scripts/terrain/。
 *
 * 高字节：bit1 海，bit2 装饰，bit3 主地形，bit4 次地形。
 * attributes 按格子顺序只收有标志的层，一层一个 TileAttr，顺序是装饰、主地形、次地形。
 * 没有属性的层使用 null，写入缺失的属性时才创建 struct。
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
            cells.Capacity = total;
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
                    Decor = decor,
                    Main = main,
                    Secondary = secondary,
                });
            }
            return cells;
        }

        /// <summary>只修改指定格子的地质变种位，不重建地形、属性或实体向量。</summary>
        public static void SetVariant(BtlFrontDocument doc, int index, int variant)
        {
            if (variant is < 0 or > 31) throw new FrontEditException("地质变种必须在 0 到 31 之间。");
            if (doc?.Root?.F.GetValueOrDefault(1) is not BtlTable map
                || map.F.GetValueOrDefault(0) is not BtlStruct size
                || index < 0 || index >= (long)U16(size, 0) * U16(size, 1))
                throw new FrontEditException("地块索引超出地图范围。");
            var tiles = FrontEdit.EnsureVec(map, 2, "u16");
            while (tiles.V.Count <= index) tiles.V.Add((ushort)9001);
            ushort terrain = (ushort)AsLong(tiles.V[index]);
            FrontEdit.SetAt(tiles, index, (ushort)((terrain & ~0xF8) | (variant << 3)));
        }

        /// <summary>显式重建 tiles / attributes。不碰部队和事件；局部编辑应直接修改目标字段。</summary>
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

        /// <summary>按坐标搬运地形，并保留完整属性及未关联尾部；实体重映射规则由调用方处理。</summary>
        public static void Resize(BtlFrontDocument doc, int left, int up, int width, int height)
        {
            if (width is < 1 or > 500 || height is < 1 or > 500)
                throw new FrontEditException("地图宽高必须在 1 到 500 之间。");
            if (doc?.Root?.F.GetValueOrDefault(1) is not BtlTable map
                || map.F.GetValueOrDefault(0) is not BtlStruct size)
                throw new FrontEditException("地图缺少尺寸结构。");
            int oldWidth = U16(size, 0), oldHeight = U16(size, 1);
            if (oldWidth <= 0 || oldHeight <= 0 || (long)oldWidth * oldHeight > 1_000_000)
                throw new FrontEditException("原地图尺寸无效或超过预算。");
            if (size.Layout.Count == 0 && size.V.Count > 6)
                throw new FrontEditException("地图尺寸结构包含类型未知的额外成员。");
            var cells = Project(doc);
            int used = cells.Sum(c => ((c.Terrain & 1024) != 0 ? 1 : 0)
                + ((c.Terrain & 2048) != 0 ? 1 : 0) + ((c.Terrain & 4096) != 0 ? 1 : 0));
            var extras = map.F.GetValueOrDefault(3) is BtlVector attributes
                ? attributes.V.Skip(used).ToArray() : Array.Empty<object>();
            var resized = new List<Cell>(width * height);
            for (int y = 0; y < height; y++)
                for (int x = 0; x < width; x++)
                {
                    long oldX = (long)x - left, oldY = (long)y - up;
                    resized.Add(oldX >= 0 && oldX < oldWidth && oldY >= 0 && oldY < oldHeight
                        ? cells[(int)oldY * oldWidth + (int)oldX] : new Cell());
                }
            size = FrontEdit.EnsureStruct(map, 0, new[] { "u16", "u16", "u16", "u16", "u16", "u16" });
            FrontEdit.SetMember(size, 0, (ushort)width);
            FrontEdit.SetMember(size, 1, (ushort)height);
            FrontEdit.SetMember(size, 4, (ushort)Math.Min(U16(size, 4), Math.Max(0, width - U16(size, 2))));
            FrontEdit.SetMember(size, 5, (ushort)Math.Min(U16(size, 5), Math.Max(0, height - U16(size, 3))));
            Store(doc, resized);
            ((BtlVector)map.F[3]).V.AddRange(extras);
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
