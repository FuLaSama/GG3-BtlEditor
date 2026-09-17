/*
 * HexMapCanvas.cs
 * 
 * 本文件是一个只读的自定义 WinForms 六角格地图画布网格控件：
 * - 多图层渲染切换 (RenderMode): 支持“地形底色”、“仅部队”、“仅建筑”、“仅工事”、“仅装饰”和“仅增员”等七种可视化图层控制。
 * - 六角形几何绘制: 依据格子坐标系、半径参数计算绘制六边形顶点，解析 Tile 属性位掩码并映射成对应的游戏地貌（海洋、沼泽、森林、高山、河流连接线）和城市徽章，标明各势力的驻军及微调偏移位移。
 * - 交互交互: 内置鼠标滚轮控制半径缩放、滚动条平移计算、点击/双击单元格坐标重定位和事件通知（CellSelected、CellDoubleClicked）。
 */
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace BtlVerifier
{
    public enum MapRenderMode
    {
        All,
        TerrainOnly,
        UnitsOnly,
        BuildingsOnly,
        FortificationsOnly,
        DecorationsOnly,
        Reinforcements
    }

    // 只读地图网格画布控件 (中文注释版)
    public class HexMapCanvas : Panel
    {
        private StageModel _stage;
        private int _selectedCellIndex = -1;
        private int _radius = 25; // 默认六边形半径
        private MapRenderMode _renderMode = MapRenderMode.All;

        // 选中单元格的外部回调事件
        public event Action<int> CellSelected;
        public event Action<int> CellDoubleClicked;

        public MapRenderMode RenderMode
        {
            get => _renderMode;
            set
            {
                _renderMode = value;
                Invalidate();
            }
        }

        public StageModel Stage
        {
            get => _stage;
            set
            {
                _stage = value;
                _selectedCellIndex = -1;
                UpdateScrollSize();
                Invalidate();
            }
        }

        public int SelectedCellIndex
        {
            get => _selectedCellIndex;
            set
            {
                _selectedCellIndex = value;
                Invalidate();
            }
        }

        public int Radius
        {
            get => _radius;
            set
            {
                _radius = Math.Max(10, Math.Min(60, value));
                UpdateScrollSize();
                Invalidate();
            }
        }

        public HexMapCanvas()
        {
            DoubleBuffered = true;
            AutoScroll = true;
            BackColor = Color.FromArgb(248, 250, 252); // 极浅背景
            SetStyle(ControlStyles.StandardDoubleClick, true);
        }

        // 计算滚动范围，支撑大地图滑动
        public void UpdateScrollSize()
        {
            if (_stage == null)
            {
                AutoScrollMinSize = Size.Empty;
                return;
            }

            int cols = _stage.MapTerrain.Size.Width;
            int rows = _stage.MapTerrain.Size.Height;

            float hDist = _radius * 1.5f;
            float vDist = (float)Math.Sqrt(3) * _radius;

            int width = (int)((cols - 1) * hDist + _radius * 2) + 40;
            int height = (int)(rows * vDist + vDist / 2f) + 40;

            AutoScrollMinSize = new Size(width, height);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            if (_stage == null) return;

            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;

            // 根据滚动位置对绘图上下文进行物理平移
            g.TranslateTransform(AutoScrollPosition.X, AutoScrollPosition.Y);

            float hDist = _radius * 1.5f;
            float vDist = (float)Math.Sqrt(3) * _radius;

            using (var borderPen = new Pen(Color.FromArgb(60, 15, 23, 42), 1)) // 暗色系地块网格边框
            using (var selectedPen = new Pen(Color.FromArgb(20, 184, 166), 3))
            using (var coordFont = new Font("Segoe UI", 7, FontStyle.Regular))
            using (var unitFont = new Font("Segoe UI", 8, FontStyle.Bold))
            using (var textBrush = new SolidBrush(Color.FromArgb(15, 23, 42))) // 坐标文本置黑/暗灰色
            {
                foreach (var cell in _stage.Cells)
                {
                    float cx = cell.X * hDist + _radius + 10;
                    float cy = cell.Y * vDist + (cell.X % 2 == 1 ? vDist / 2f : 0f) + 10;

                    // 计算六边形的 6 个顶点
                    PointF[] points = new PointF[6];
                    for (int j = 0; j < 6; j++)
                    {
                        double angle = j * Math.PI / 3.0;
                        points[j] = new PointF(
                            cx + _radius * (float)Math.Cos(angle),
                            cy + _radius * (float)Math.Sin(angle)
                        );
                    }

                    // 获取并填充地形颜色
                    // 获取并填充地形颜色
                    Color terrainColor;
                    var cellStruct = GetCellStructureInfo(cell);
                    bool hasBldg = cellStruct?.Building != null;
                    bool hasFort = cellStruct?.Fortification != null;
                    bool isCityOrFort = hasBldg || hasFort;
                    if (_renderMode == MapRenderMode.UnitsOnly)
                    {
                        terrainColor = Color.FromArgb(241, 245, 249);
                    }
                    else if (_renderMode == MapRenderMode.Reinforcements)
                    {
                        // 检查本格子是否是增兵点
                        bool isReinforce = false;
                        if (_stage.BattleInfo?.ReinforcePoints != null)
                        {
                            foreach (var rp in _stage.BattleInfo.ReinforcePoints)
                            {
                                if (rp.CellIdx == cell.Index)
                                {
                                    isReinforce = true;
                                    break;
                                }
                            }
                        }

                        if (isReinforce)
                        {
                            terrainColor = Color.FromArgb(255, 237, 213); // 浅橙色背景底色
                        }
                        else
                        {
                            terrainColor = Color.FromArgb(248, 250, 252); // 非增兵点用地块底色
                        }
                    }
                    else if (_renderMode == MapRenderMode.BuildingsOnly)
                    {
                        // 仅建筑模式：只着色城市/工厂/港口/机场/油田，不包含工事
                        if (hasBldg)
                        {
                            int bType = cellStruct.Building.Type;
                            if (bType == 1) // 城市
                                terrainColor = Color.FromArgb(254, 240, 138); // 城市 (淡金色)
                            else if (bType == 2) // 工厂
                                terrainColor = Color.FromArgb(254, 205, 211); // 工厂 (淡红褐色)
                            else if (bType == 3 || bType == 4) // 机场/港口
                                terrainColor = Color.FromArgb(207, 250, 254); // 港口/机场 (淡青色)
                            else if (bType == 5) // 油田/煤矿/矿区
                                terrainColor = Color.FromArgb(253, 186, 116); // 油田/煤矿 (淡橙色)
                            else
                                terrainColor = Color.FromArgb(226, 232, 240); // 其它建筑 (淡灰色)
                        }
                        else
                        {
                            terrainColor = Color.FromArgb(248, 250, 252); // 无建筑的地块使用极浅的底色作为背景，避免视觉干扰
                        }
                    }
                    else if (_renderMode == MapRenderMode.FortificationsOnly)
                    {
                        // 仅工事模式：只着色工事地块
                        if (hasFort)
                        {
                            string fName = cellStruct.Fortification.Name ?? "";
                            if (fName.Contains("Trench") || fName.Contains("战壕") || fName.Contains("Bunker") || fName.Contains("地堡"))
                                terrainColor = Color.FromArgb(203, 213, 225); // 战壕/地堡 (淡灰蓝色)
                            else if (fName.Contains("Fortress") || fName.Contains("要塞") || fName.Contains("Coastal") || fName.Contains("海岸"))
                                terrainColor = Color.FromArgb(254, 205, 211); // 要塞炮/海岸炮 (淡玫瑰色)
                            else if (fName.Contains("Radar") || fName.Contains("雷达"))
                                terrainColor = Color.FromArgb(186, 230, 253); // 雷达 (淡天蓝色)
                            else
                                terrainColor = Color.FromArgb(226, 232, 240); // 工事 (淡灰色)
                        }
                        else
                        {
                            terrainColor = Color.FromArgb(248, 250, 252); // 非工事区域以极浅底色作为背景
                        }
                    }
                    else if (_renderMode == MapRenderMode.DecorationsOnly)
                    {
                        byte decId = GetPrimaryDecorationId(cell);
                        if (decId > 0)
                        {
                            if (decId == 51) // 农田
                                terrainColor = Color.FromArgb(217, 249, 157); // 淡黄绿色 (农田)
                            else if (decId == 52) // 沙丘
                                terrainColor = Color.FromArgb(254, 240, 138); // 淡黄色
                            else if (decId == 53) // 土地装饰
                                terrainColor = Color.FromArgb(254, 215, 170); // 淡褐/橙色
                            else if (decId == 54) // 草地装饰
                                terrainColor = Color.FromArgb(187, 247, 208); // 淡绿色
                            else if (decId == 55) // 雪地装饰
                                terrainColor = Color.FromArgb(224, 242, 254); // 淡冰蓝色
                            else if (decId == 56) // 沙漠装饰
                                terrainColor = Color.FromArgb(253, 230, 138); // 淡金橙色
                            else
                                terrainColor = Color.FromArgb(233, 213, 255); // 其它装饰 (淡紫色)
                        }
                        else
                        {
                            terrainColor = Color.FromArgb(248, 250, 252); // 无装饰地块使用极浅灰色
                        }
                    }
                    else
                    {
                        terrainColor = GetTerrainColor(cell);
                    }

                    using (var fillBrush = new SolidBrush(terrainColor))
                    {
                        g.FillPolygon(fillBrush, points);
                    }

                    // 如果是河流且当前模式不是仅显示部队，则绘制中心连接线
                    if (_renderMode != MapRenderMode.UnitsOnly && _renderMode != MapRenderMode.Reinforcements && IsRiver(cell))
                    {
                        int w = _stage.MapTerrain.Size.Width;
                        int h = _stage.MapTerrain.Size.Height;
                        Point[] neighbors = GetNeighbors(cell.X, cell.Y);

                        using (var riverPen = new Pen(Color.FromArgb(34, 211, 238), 3.5f))
                        {
                            riverPen.LineJoin = LineJoin.Round;
                            riverPen.StartCap = LineCap.Round;
                            riverPen.EndCap = LineCap.Round;

                            foreach (var n in neighbors)
                            {
                                if (n.X >= 0 && n.X < w && n.Y >= 0 && n.Y < h)
                                {
                                    var neighborCell = _stage.Cells[n.Y * w + n.X];
                                    if (IsRiver(neighborCell))
                                    {
                                        float ncx = n.X * hDist + _radius + 10;
                                        float ncy = n.Y * vDist + (n.X % 2 == 1 ? vDist / 2f : 0f) + 10;
                                        g.DrawLine(riverPen, cx, cy, ncx, ncy);
                                    }
                                }
                            }
                        }
                    }

                    // 绘制边框或选中标识
                    if (_selectedCellIndex == cell.Index)
                    {
                        g.DrawPolygon(selectedPen, points);
                    }
                    else
                    {
                        g.DrawPolygon(borderPen, points);
                    }

                    // 突出显示建筑/城市的特殊边框
                    if (_renderMode != MapRenderMode.UnitsOnly && _renderMode != MapRenderMode.Reinforcements && isCityOrFort)
                    {
                        using (var cityPen = new Pen(Color.FromArgb(120, 0, 0, 0), 1.5f))
                        {
                            cityPen.DashStyle = DashStyle.Dash;
                            g.DrawPolygon(cityPen, points);
                        }
                    }

                    // 绘制坐标标签 (X,Y)
                    string coordText = $"{cell.X},{cell.Y}";
                    SizeF size = g.MeasureString(coordText, coordFont);
                    g.DrawString(coordText, coordFont, textBrush, cx - size.Width / 2f, cy + _radius * 0.45f);

                    // 独立绘制建筑物与工事标签
                    if (_renderMode != MapRenderMode.UnitsOnly && _renderMode != MapRenderMode.Reinforcements)
                    {
                        var structInfo = GetCellStructureInfo(cell);
                        if (structInfo != null)
                        {
                            // 1. 绘制建筑 (Upper Center)
                            if (structInfo.Building != null && _renderMode != MapRenderMode.FortificationsOnly && _renderMode != MapRenderMode.DecorationsOnly)
                            {
                                string bSymbol = "";
                                if (structInfo.Building.Type == 1) bSymbol = "C";
                                else if (structInfo.Building.Type == 2) bSymbol = "F";
                                else if (structInfo.Building.Type == 3) bSymbol = "A";
                                else if (structInfo.Building.Type == 4) bSymbol = "P";
                                else if (structInfo.Building.Type == 5) bSymbol = "Oil";
                                else bSymbol = structInfo.Building.Name.Substring(0, 1);

                                string bText = $"{bSymbol}{structInfo.BuildingFactionId}";
                                using (var bFont = new Font("Segoe UI", 7.5f, FontStyle.Bold))
                                using (var bBrush = new SolidBrush(Color.FromArgb(255, 30, 41, 59))) // 深色加粗
                                {
                                    SizeF bSize = g.MeasureString(bText, bFont);
                                    var rect = new RectangleF(cx - bSize.Width / 2f - 2, cy - _radius * 0.48f - bSize.Height / 2f, bSize.Width + 4, bSize.Height);
                                    
                                    Color factionBgColor = GetFactionBgColor(structInfo.BuildingFactionId);
                                    using (var bgBrush = new SolidBrush(Color.FromArgb(230, factionBgColor)))
                                    using (var badgePen = new Pen(Color.FromArgb(120, Color.DimGray), 0.8f))
                                    {
                                        g.FillRectangle(bgBrush, rect);
                                        g.DrawRectangle(badgePen, rect.X, rect.Y, rect.Width, rect.Height);
                                    }
                                    g.DrawString(bText, bFont, bBrush, cx - bSize.Width / 2f, cy - _radius * 0.48f - bSize.Height / 2f);
                                }
                            }

                            // 1.5 绘制工事标记 (Center, 在建筑标记下方)
                            if (structInfo.Fortification != null && _renderMode != MapRenderMode.BuildingsOnly && _renderMode != MapRenderMode.DecorationsOnly)
                            {
                                string fSymbol = "";
                                string fName = structInfo.Fortification.Name ?? "";
                                if (fName.Contains("Trench") || fName.Contains("战壕")) fSymbol = "Tn";
                                else if (fName.Contains("Bunker") || fName.Contains("地堡")) fSymbol = "Bk";
                                else if (fName.Contains("Fortress") || fName.Contains("要塞")) fSymbol = "Fg";
                                else if (fName.Contains("Coastal") || fName.Contains("海岸")) fSymbol = "Cg";
                                else if (fName.Contains("Radar") || fName.Contains("雷达")) fSymbol = "Rd";
                                else fSymbol = "Ft";

                                using (var fFont = new Font("Segoe UI", 6.5f, FontStyle.Bold))
                                using (var fBrush = new SolidBrush(Color.FromArgb(220, 71, 85, 105)))
                                {
                                    SizeF fSize = g.MeasureString(fSymbol, fFont);
                                    // 若不渲染建筑（或没有建筑），把工事标签位置往上移一点，视觉效果更好
                                    float fY = (structInfo.Building != null && _renderMode != MapRenderMode.FortificationsOnly) 
                                        ? cy - _radius * 0.12f 
                                        : cy - _radius * 0.25f;
                                    var fRect = new RectangleF(cx - fSize.Width / 2f - 1, fY - fSize.Height / 2f, fSize.Width + 2, fSize.Height);
                                    using (var fBgBrush = new SolidBrush(Color.FromArgb(180, 203, 213, 225)))
                                    using (var fPen = new Pen(Color.FromArgb(100, Color.Gray), 0.5f))
                                    {
                                        g.FillRectangle(fBgBrush, fRect);
                                        g.DrawRectangle(fPen, fRect.X, fRect.Y, fRect.Width, fRect.Height);
                                    }
                                    g.DrawString(fSymbol, fFont, fBrush, cx - fSize.Width / 2f, fY - fSize.Height / 2f);
                                }
                            }

                            // 1.6 绘制装饰标签 (仅在“仅装饰”模式下绘制)
                            if (_renderMode == MapRenderMode.DecorationsOnly)
                            {
                                byte decId = GetPrimaryDecorationId(cell);
                                if (decId > 0)
                                {
                                    byte tileIdx = GetPrimaryDecorationTileIndex(cell);
                                    string decSymbol = "";
                                    if (decId == 51) decSymbol = "田";
                                    else if (decId == 52) decSymbol = "丘";
                                    else if (decId == 53) decSymbol = "土";
                                    else if (decId == 54) decSymbol = "草";
                                    else if (decId == 55) decSymbol = "雪";
                                    else if (decId == 56) decSymbol = "沙";
                                    else decSymbol = $"D{decId}";

                                    string decText = $"{decSymbol}{tileIdx}";
                                    using (var dFont = new Font("Segoe UI", 7.5f, FontStyle.Bold))
                                    using (var dBrush = new SolidBrush(Color.FromArgb(255, 30, 41, 59)))
                                    {
                                        SizeF dSize = g.MeasureString(decText, dFont);
                                        var rect = new RectangleF(cx - dSize.Width / 2f - 2, cy - _radius * 0.25f - dSize.Height / 2f, dSize.Width + 4, dSize.Height);
                                        using (var bgBrush = new SolidBrush(Color.FromArgb(220, 241, 245, 249)))
                                        using (var badgePen = new Pen(Color.FromArgb(120, Color.DimGray), 0.8f))
                                        {
                                            g.FillRectangle(bgBrush, rect);
                                            g.DrawRectangle(badgePen, rect.X, rect.Y, rect.Width, rect.Height);
                                        }
                                        g.DrawString(decText, dFont, dBrush, cx - dSize.Width / 2f, cy - _radius * 0.25f - dSize.Height / 2f);
                                    }
                                }
                            }

                            // 2. 绘制微调偏移位移 (仅在格子有建筑、工事，或处于仅装饰模式且有偏移时才显示)
                            if (structInfo.HasDisplacement && (structInfo.Building != null || structInfo.Fortification != null || _renderMode == MapRenderMode.DecorationsOnly))
                            {
                                string offText = $"off: {structInfo.Dx},{structInfo.Dy}";
                                using (var fFont = new Font("Segoe UI", 5.8f, FontStyle.Regular))
                                using (var fBrush = new SolidBrush(Color.FromArgb(140, 100, 116, 139))) // 浅色偏灰，不干扰主视图
                                {
                                    SizeF fSize = g.MeasureString(offText, fFont);
                                    g.DrawString(offText, fFont, fBrush, cx - fSize.Width / 2f, cy + _radius * 0.18f - fSize.Height / 2f);
                                }
                            }
                        }
                    }

                    // 绘制部署的部队单元
                    if (_renderMode != MapRenderMode.TerrainOnly && _renderMode != MapRenderMode.BuildingsOnly && _renderMode != MapRenderMode.FortificationsOnly && _renderMode != MapRenderMode.DecorationsOnly && _renderMode != MapRenderMode.Reinforcements && cell.Unit != null)
                    {
                        DrawUnit(g, cell.Unit, cx, cy, unitFont);
                    }

                    // 绘制增员部署点徽章
                    if (_renderMode == MapRenderMode.Reinforcements)
                    {
                        ReinforcePointModel cellRP = null;
                        if (_stage.BattleInfo?.ReinforcePoints != null)
                        {
                            foreach (var rp in _stage.BattleInfo.ReinforcePoints)
                            {
                                if (rp.CellIdx == cell.Index)
                                {
                                    cellRP = rp;
                                    break;
                                }
                            }
                        }

                        if (cellRP != null)
                        {
                            // 绘制增兵点的大号橙色徽章
                            float rBadge = _radius * 0.45f;
                            RectangleF badgeRect = new RectangleF(cx - rBadge, cy - rBadge, rBadge * 2, rBadge * 2);
                            using (var badgeBrush = new SolidBrush(Color.FromArgb(249, 115, 22))) // 鲜艳的橙色 #f97316
                            using (var whiteBorderPen = new Pen(Color.White, 1.8f))
                            {
                                g.FillEllipse(badgeBrush, badgeRect);
                                g.DrawEllipse(whiteBorderPen, badgeRect);
                            }

                            // 绘制增兵点阵营号，例如 "R1"
                            string rText = $"R{cellRP.FactionId}";
                            using (var rFont = new Font("Segoe UI", 7.5f, FontStyle.Bold))
                            using (var rBrush = new SolidBrush(Color.White))
                            {
                                SizeF rSize = g.MeasureString(rText, rFont);
                                g.DrawString(rText, rFont, rBrush, cx - rSize.Width / 2f, cy - rSize.Height / 2f);
                            }

                            // 如果是关键增兵点，额外在其上方绘制星号标记
                            if (cellRP.IsKeyUnit)
                            {
                                using (var starFont = new Font("Segoe UI", 9, FontStyle.Bold))
                                using (var starBrush = new SolidBrush(Color.FromArgb(234, 179, 8))) // 金黄色 #eab308
                                {
                                    g.DrawString("★", starFont, starBrush, cx - rBadge * 1.3f, cy - rBadge * 1.3f);
                                }
                            }
                        }
                    }
                }
            }
        }

        private void DrawUnit(Graphics g, UnitModel unit, float cx, float cy, Font font)
        {
            float uRadius = _radius * 0.65f;
            RectangleF unitRect = new RectangleF(cx - uRadius, cy - uRadius, uRadius * 2, uRadius * 2);

            using (var unitBaseBrush = new SolidBrush(Color.FromArgb(200, 10, 13, 24)))
            using (var factionPen = new Pen(GetFactionColor(unit.FactionId), 2.5f))
            {
                g.FillEllipse(unitBaseBrush, unitRect);
                g.DrawEllipse(factionPen, unitRect);
            }

            string idText = unit.DisplayIndex.ToString();
            SizeF size = g.MeasureString(idText, font);
            using (var textBrush = new SolidBrush(Color.White))
            {
                g.DrawString(idText, font, textBrush, cx - size.Width / 2f, cy - size.Height / 2f);
            }

            // 绘制将领 Flag 标识
            if (unit.Flag != 0)
            {
                using (var starFont = new Font("Segoe UI", 9, FontStyle.Bold))
                using (var starBrush = new SolidBrush(Color.FromArgb(251, 191, 36)))
                {
                    g.DrawString("★", starFont, starBrush, cx - uRadius * 1.1f, cy - uRadius * 1.1f);
                }
            }
        }

        private int GetFinalTerrainType(CellModel cell)
        {
            if (cell == null) return 0;
            ushort terrain = cell.Terrain;
            byte low = (byte)(terrain & 0xFF);
            byte high = (byte)(terrain >> 8);
            int v7 = low & 7;

            // 1. 如果高字节 Bit 1 (high & 2) 被设置，代表海洋
            if ((high & 2) != 0)
            {
                return 1; // 海洋
            }

            // 2. 检查是否有 Slot 2 属性（高级地形覆盖）
            byte v14 = 0;
            if (cell.AttrA2 != null && cell.AttrA2.Count >= 4)
            {
                v14 = cell.AttrA2[0];
            }

            if (v14 == 0)
            {
                // 没有覆盖，退化到基础地貌底色
                if (v7 == 2) return 3; // 沙漠
                if (v7 == 3) return 2; // 雪地
                return 0; // 平原
            }

            // 3. 有 Slot 2 属性覆盖，根据 def_mapterrain.xml 的 ID 进行映射
            switch (v14)
            {
                // 沼泽 (type 4)
                case 11:
                case 12:
                case 13:
                case 14:
                    return 4;

                // 森林 (type 6)
                case 21:
                case 22:
                case 23:
                case 24:
                case 25:
                    return 6;

                // 丘陵 (type 7)
                case 31:
                case 34:
                case 37:
                case 40:
                    return 7;

                // 矮山 (type 8)
                case 32:
                case 35:
                case 38:
                case 41:
                    return 8;

                // 高山 (type 9)
                case 33:
                case 36:
                case 39:
                case 42:
                case 44:
                    return 9;

                // 河流 (type 10)
                case 46:
                case 47:
                case 48:
                    return 10;

                // 其它覆盖物 (视为装饰或平原底色)
                default:
                    // 城市/工厂地基等
                    if (v14 == 54 || v14 == 47 || v14 == 35 || v14 == 12)
                    {
                        return 0; // 平原
                    }
                    if (v7 == 2) return 3;
                    if (v7 == 3) return 2;
                    return 0;
            }
        }

        private Color GetTerrainColor(CellModel cell)
        {
            int type = GetFinalTerrainType(cell);
            ushort terrain = cell.Terrain;
            byte low = (byte)(terrain & 0xFF);
            int v7 = low & 7;

            switch (type)
            {
                case 0: return Color.FromArgb(220, 252, 231); // 平原 (浅绿 #dcfce7)
                case 1: return Color.FromArgb(191, 219, 254); // 海洋 (深浅蓝 #bfdbfe)
                case 2: return Color.FromArgb(248, 250, 252); // 雪地 (白色 #f8fafc)
                case 3: return Color.FromArgb(254, 240, 138); // 沙漠 (金黄 #fef08a)
                case 4: return Color.FromArgb(163, 230, 53);  // 沼泽 (黄绿 #a3e635)
                case 6: return Color.FromArgb(34, 197, 94);   // 森林 (草绿 #22c55e)
                case 7: return Color.FromArgb(253, 186, 116); // 丘陵 (浅褐 #fdbaf8)
                case 8: return Color.FromArgb(156, 163, 175); // 矮山 (中灰 #9ca3af)
                case 9: return Color.FromArgb(75, 85, 99);    // 高山 (深灰 #4b5563)
                case 10:
                    // 河流背景色：退化到本格子的基础地貌底色 (平原/雪地/沙漠)
                    if (v7 == 2) return Color.FromArgb(254, 240, 138); // 沙漠
                    if (v7 == 3) return Color.FromArgb(248, 250, 252); // 雪地
                    return Color.FromArgb(220, 252, 231); // 平原
                default: return Color.FromArgb(241, 245, 249); // 默认浅灰色
            }
        }

        private bool IsRiver(CellModel cell)
        {
            return GetFinalTerrainType(cell) == 10;
        }

        private Point[] GetNeighbors(int x, int y)
        {
            Point[] neighbors = new Point[6];
            neighbors[0] = new Point(x, y - 1); // Top
            neighbors[1] = new Point(x, y + 1); // Bottom

            if (x % 2 == 0) // Even column
            {
                neighbors[2] = new Point(x + 1, y - 1); // Top-Right
                neighbors[3] = new Point(x + 1, y);     // Bottom-Right
                neighbors[4] = new Point(x - 1, y - 1); // Top-Left
                neighbors[5] = new Point(x - 1, y);     // Bottom-Left
            }
            else // Odd column
            {
                neighbors[2] = new Point(x + 1, y);     // Top-Right
                neighbors[3] = new Point(x + 1, y + 1); // Bottom-Right
                neighbors[4] = new Point(x - 1, y);     // Top-Left
                neighbors[5] = new Point(x - 1, y + 1); // Bottom-Left
            }
            return neighbors;
        }

        private Color GetFactionColor(byte factionId)
        {
            switch (factionId)
            {
                case 1: return Color.FromArgb(239, 68, 68);    // 阵营 1 (红色)
                case 2: return Color.FromArgb(59, 130, 246);   // 阵营 2 (蓝色)
                case 3: return Color.FromArgb(16, 185, 129);   // 阵营 3 (绿色)
                case 4: return Color.FromArgb(245, 158, 11);   // 阵营 4 (黄色)
                default: return Color.White;
            }
        }

        protected override void OnMouseWheel(MouseEventArgs e)
        {
            // 将滚轮滚动事件重映射为六角格半径的增减，实现缩放
            ((HandledMouseEventArgs)e).Handled = true;
            if (e.Delta > 0)
                Radius += 3;
            else
                Radius -= 3;
        }

        private Color GetFactionBgColor(byte factionId)
        {
            switch (factionId)
            {
                case 1: return Color.FromArgb(254, 242, 242);   // 阵营 1 (浅红色)
                case 2: return Color.FromArgb(239, 246, 255);   // 阵营 2 (浅蓝色)
                case 3: return Color.FromArgb(240, 253, 244);   // 阵营 3 (浅绿色)
                case 4: return Color.FromArgb(254, 243, 199);   // 阵营 4 (浅黄色)
                default: return Color.FromArgb(248, 250, 252); // 其他阵营/默认 (极浅灰)
            }
        }

        public class StructureInfo
        {
            public BuildingSetting Building { get; set; }
            public byte BuildingFactionId { get; set; }
            public FortificationSetting Fortification { get; set; }
            public sbyte Dx { get; set; }
            public sbyte Dy { get; set; }
            public bool HasDisplacement { get; set; }
        }

        private StructureInfo GetCellStructureInfo(CellModel cell)
        {
            if (cell == null) return null;

            var info = new StructureInfo();

            // 从触发器事件中获取建筑信息 (Type=4)
            if (cell.TriggerBuilding != null)
            {
                var bSetting = GameSettings.GetBuilding(cell.TriggerBuilding.BuildingId);
                if (bSetting != null)
                {
                    info.Building = bSetting;
                    info.BuildingFactionId = cell.TriggerBuilding.Owner;
                }

                if (cell.TriggerBuilding.Dx != 0 || cell.TriggerBuilding.Dy != 0)
                {
                    info.Dx = cell.TriggerBuilding.Dx;
                    info.Dy = cell.TriggerBuilding.Dy;
                    info.HasDisplacement = true;
                }
            }

            // 从触发器事件中获取工事信息 (Type=0)
            if (cell.TriggerFort != null)
            {
                var fSetting = GameSettings.GetFortification(cell.TriggerFort.FortId);
                if (fSetting != null)
                {
                    info.Fortification = fSetting;
                }
            }

            // 兜底：如果触发器中没有找到，再从属性槽中尝试读取位移信息
            if (!info.HasDisplacement)
            {
                var slots = new List<List<byte>> { cell.Attr, cell.AttrA2, cell.AttrA3 };
                foreach (var slot in slots)
                {
                    if (slot != null && slot.Count >= 4)
                    {
                        sbyte dx = (sbyte)slot[2];
                        sbyte dy = (sbyte)slot[3];
                        if (dx != 0 || dy != 0)
                        {
                            info.Dx = dx;
                            info.Dy = dy;
                            info.HasDisplacement = true;
                            break;
                        }
                    }
                }
            }

            return info;
        }

        private byte GetCountryIdFromSlots(CellModel cell)
        {
            var slots = new List<List<byte>> { cell.Attr, cell.AttrA2, cell.AttrA3 };
            foreach (var slot in slots)
            {
                if (slot != null && slot.Count >= 4 && slot[1] != 0 && slot[1] != 255)
                    return slot[1];
            }
            return 0;
        }


        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (_stage == null) return;

            // 考虑滚动条物理平移
            float clickX = e.X - AutoScrollPosition.X;
            float clickY = e.Y - AutoScrollPosition.Y;

            float hDist = _radius * 1.5f;
            float vDist = (float)Math.Sqrt(3) * _radius;

            int closestIndex = -1;
            float minDist = float.MaxValue;

            foreach (var cell in _stage.Cells)
            {
                float cx = cell.X * hDist + _radius + 10;
                float cy = cell.Y * vDist + (cell.X % 2 == 1 ? vDist / 2f : 0f) + 10;

                float dist = (clickX - cx) * (clickX - cx) + (clickY - cy) * (clickY - cy);
                if (dist < minDist)
                {
                    minDist = dist;
                    closestIndex = cell.Index;
                }
            }

            // 限制点击有效半径
            if (closestIndex != -1 && minDist < _radius * _radius * 0.9f)
            {
                _selectedCellIndex = closestIndex;
                Invalidate();
                CellSelected?.Invoke(closestIndex);
            }
        }

        protected override void OnMouseDoubleClick(MouseEventArgs e)
        {
            base.OnMouseDoubleClick(e);
            if (_stage == null) return;

            // 考虑滚动条物理平移
            float clickX = e.X - AutoScrollPosition.X;
            float clickY = e.Y - AutoScrollPosition.Y;

            float hDist = _radius * 1.5f;
            float vDist = (float)Math.Sqrt(3) * _radius;

            int closestIndex = -1;
            float minDist = float.MaxValue;

            foreach (var cell in _stage.Cells)
            {
                float cx = cell.X * hDist + _radius + 10;
                float cy = cell.Y * vDist + (cell.X % 2 == 1 ? vDist / 2f : 0f) + 10;

                float dist = (clickX - cx) * (clickX - cx) + (clickY - cy) * (clickY - cy);
                if (dist < minDist)
                {
                    minDist = dist;
                    closestIndex = cell.Index;
                }
            }

            // 限制双击有效半径
            if (closestIndex != -1 && minDist < _radius * _radius * 0.9f)
            {
                CellDoubleClicked?.Invoke(closestIndex);
            }
        }

        private byte GetPrimaryDecorationId(CellModel cell)
        {
            if (cell.Attr != null && cell.Attr.Count >= 4 && cell.Attr[0] >= 50 && cell.Attr[0] < 100)
                return cell.Attr[0];
            if (cell.AttrA2 != null && cell.AttrA2.Count >= 4 && cell.AttrA2[0] >= 50 && cell.AttrA2[0] < 100)
                return cell.AttrA2[0];
            if (cell.AttrA3 != null && cell.AttrA3.Count >= 4 && cell.AttrA3[0] >= 50 && cell.AttrA3[0] < 100)
                return cell.AttrA3[0];
            return 0;
        }

        private byte GetPrimaryDecorationTileIndex(CellModel cell)
        {
            if (cell.Attr != null && cell.Attr.Count >= 4 && cell.Attr[0] >= 50 && cell.Attr[0] < 100)
                return cell.Attr[1];
            if (cell.AttrA2 != null && cell.AttrA2.Count >= 4 && cell.AttrA2[0] >= 50 && cell.AttrA2[0] < 100)
                return cell.AttrA2[1];
            if (cell.AttrA3 != null && cell.AttrA3.Count >= 4 && cell.AttrA3[0] >= 50 && cell.AttrA3[0] < 100)
                return cell.AttrA3[1];
            return 0;
        }
    }
}
