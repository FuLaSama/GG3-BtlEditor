/*
 * TerrainDecode.cs
 *
 * 贴图绘制用的几何，不是关卡文档：
 *   TerrainSlot     一格上的地表/装饰（id、变种、像素偏移）
 *   TerrainCell     画布格：气候 T、海、海岸、A1/A2/A3
 *   DecodedTerrainMap  FrontTerrain.FromCells 的输出，给 TerrainSpriteRenderer
 *
 * 六角：偶/奇行用两套邻居偏移；像素坐标 CellCenter，尺寸 ColW=69 RowH=80（再 ×Scale=2）。
 * CellIsSnow：气候 T==3 或建筑 extraFlag==2 时走雪地切片名。
 */
namespace BtldMapEditor
{
    /// <summary>地块属性槽：对应 TileAttr 四个成员。</summary>
    public readonly struct TerrainSlot
    {
        public readonly int TerrainId;
        public readonly int Variant;
        public readonly int Dx;
        public readonly int Dy;

        public TerrainSlot(int terrainId, int variant, int dx, int dy)
        {
            TerrainId = terrainId;
            Variant = variant;
            Dx = dx;
            Dy = dy;
        }

        public bool IsEmpty => TerrainId == 0 || Variant == 0xFF;
    }

    public sealed class TerrainCell
    {
        public int Col;
        public int Row;
        public int Index;
        public int X;
        public int Y;
        public int T;
        public int V;
        public bool Sea;
        public bool CoastB;
        public bool Blocked;
        public bool Playable = true;
        public TerrainSlot? A1;
        public TerrainSlot? A2;
        public TerrainSlot? A3;

        public int CoastMask(IReadOnlyList<TerrainCell> cells, int width, int height)
        {
            int bits = 0;
            for (int d = 0; d < 6; d++)
            {
                int n = TerrainGeometry.NeighborIndex(width, height, Index, d);
                if (n < 0 || cells[n].Sea)
                    bits |= 1 << d;
            }
            return bits;
        }

        public bool IsCoast(IReadOnlyList<TerrainCell> cells, int width, int height)
        {
            if (!Sea) return false;
            for (int d = 0; d < 6; d++)
            {
                int n = TerrainGeometry.NeighborIndex(width, height, Index, d);
                if (n >= 0 && !cells[n].Sea)
                    return true;
            }
            return false;
        }

        public bool ClimateDiffers(IReadOnlyList<TerrainCell> cells, int width, int height)
        {
            for (int d = 0; d < 6; d++)
            {
                int n = TerrainGeometry.NeighborIndex(width, height, Index, d);
                if (n >= 0 && cells[n].T != T)
                    return true;
            }
            return false;
        }
    }

    public sealed class TerrainMapBuilding
    {
        public int Index;
        public int BuildingId;
        public int Variant;
        public int Val1;
        public int Val2;
        public int Dx;
        public int Dy;
    }

    public sealed class TerrainMapFort
    {
        public int Index;
        public int FortId;
        public bool FaceLeft;
    }

    public sealed class DecodedTerrainMap
    {
        public int Width;
        public int Height;
        public int PlayableFlag;
        public List<TerrainCell> Cells = new List<TerrainCell>();
        public List<TerrainMapBuilding> Buildings = new List<TerrainMapBuilding>();
        public List<TerrainMapFort> Forts = new List<TerrainMapFort>();
    }

    /// <summary>游戏六角格像素公式。数值来自客户端，不要随手改，否则贴图对不齐。</summary>
    public static class TerrainGeometry
    {
        public const int ColW = 69;
        public const int RowH = 80;
        public const int OriginX = 46;
        public const int OriginY = 40;
        /// <summary>奇行相对偶行的水平错位（未缩放）。</summary>
        public const int OddRowShift = 40;
        public const int Scale = 2;
        /// <summary>画布四周留白，避免贴图画到负坐标被裁掉。</summary>
        public const int Pad = 256;

        public static readonly int[] HexFill = { -23, 40, -46, 0, -23, -40, 23, -40, 46, 0, 23, 40 };
        public static readonly int[] HexVerts = { -23, 39, -45, 0, -23, -39, 23, -39, 45, 0, 23, 39 };

        static readonly int[] EvenDelta = { 0, -1, 1, -1, 1, 0, 0, 1, -1, 0, -1, -1 };
        static readonly int[] OddDelta = { 0, -1, 1, 0, 1, 1, 0, 1, -1, 1, -1, 0 };

        public static void CellCenter(int col, int row, out int x, out int y)
        {
            x = col * ColW + OriginX;
            y = row * RowH + OriginY + ((col & 1) != 0 ? OddRowShift : 0);
        }

        public static int NeighborIndex(int width, int height, int index, int direction)
        {
            int col = index % width;
            int row = index / width;
            int[] delta = (col & 1) != 0 ? OddDelta : EvenDelta;
            int nc = col + delta[direction * 2];
            int nr = row + delta[direction * 2 + 1];
            if (nc < 0 || nr < 0 || nc >= width || nr >= height)
                return -1;
            return nr * width + nc;
        }

        public static int CanvasWidth(int mapWidth)
        {
            return Pad * 2 + ((mapWidth - 1) * ColW + OriginX + 45) * Scale;
        }

        public static int CanvasHeight(int mapHeight)
        {
            return Pad * 2 + ((mapHeight - 1) * RowH + OriginY + OddRowShift + 39) * Scale;
        }
    }

    public static class TerrainDecode
    {
        /// <summary>雪地切片：建筑 Val2==2 强制雪，否则看格子气候是否为 3。</summary>
        public static bool CellIsSnow(TerrainCell cell, int extraFlag = 0)
        {
            if (extraFlag == 2) return true;
            return cell != null && cell.T == 3;
        }
    }
}
