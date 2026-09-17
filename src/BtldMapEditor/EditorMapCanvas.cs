/*
 * EditorMapCanvas.cs  （BtldMapEditor）
 *
 * 六角格画布。工作视图是 MapCell 列表，Document 是 BtlFront：
 *   cell.Unit / TriggerBldg / TriggerFort 与 Document 向量里是同一张表。
 *
 * 两种外观：线框（快，调逻辑）和 Sprite（TerrainSpriteRenderer 位图）。
 * 鼠标：选格、刷地形、拖部队、拉触发连线。改完通知主窗体写回 Document / 记撤销。
 *
 * 滚动与缩放只动视图，不改格子数据。
 */
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Windows.Forms;
using BtlCore.Front;
using BtldMapEditor.Front;

namespace BtldMapEditor
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

    public enum MapVisualStyle
    {
        Sketch,
        Sprite
    }

    public class EditorMapCanvas : Panel
    {
        private BtlFrontDocument _doc;
        private List<MapCell> _cells = new List<MapCell>();
        private int _selectedCellIndex = -1;
        private int _dropPreviewIndex = -1;
        private int _radius = 25; // 默认六角格半径
        private MapRenderMode _renderMode = MapRenderMode.All;
        private MapVisualStyle _visualStyle = MapVisualStyle.Sketch;
        private bool _showCellCoordinates = true;
        private bool _showCellOffsets = true;
        private TerrainSpriteRenderer _spriteRenderer;
        private int[] _spriteCellHash;
        private int _spriteMetaKey = int.MinValue;
        private string _spriteLoadError;
        private Bitmap _viewCache;
        private int _viewCacheRadius;
        private int _viewCacheSrcX, _viewCacheSrcY, _viewCacheSrcW, _viewCacheSrcH;
        private Point _lastPaintScroll = new Point(int.MinValue, int.MinValue);
        private readonly Dictionary<int, int> _factionCamp = new Dictionary<int, int>();
        private int _playerFactionId = -1;
        private int _playerCamp = -1;

        public bool ShowCellCoordinates
        {
            get => _showCellCoordinates;
            set
            {
                _showCellCoordinates = value;
                Invalidate();
            }
        }

        public bool ShowCellOffsets
        {
            get => _showCellOffsets;
            set
            {
                _showCellOffsets = value;
                Invalidate();
            }
        }

        public event Action<int> CellSelected;
        public event Action<int> CellDoubleClicked;
        public event MouseEventHandler MapMouseDown;
        public event MouseEventHandler MapMouseMove;
        public event MouseEventHandler MapMouseUp;

        public bool BrushMode { get; set; } = false;
        public Action<int> PaintCellRequested { get; set; }

        public MapRenderMode RenderMode
        {
            get => _renderMode;
            set
            {
                _renderMode = value;
                Invalidate();
            }
        }

        public MapVisualStyle VisualStyle
        {
            get => _visualStyle;
            set
            {
                if (_visualStyle == value) return;
                _visualStyle = value;
                Invalidate();
            }
        }

        public string SpriteLoadError => _spriteLoadError;

        public BtlFrontDocument Document
        {
            get => _doc;
            set
            {
                _doc = value;
                _selectedCellIndex = -1;
                _dropPreviewIndex = -1;
                RebuildCells();
                UpdateScrollSize();
                _spriteCellHash = null;
                _spriteMetaKey = int.MinValue;
                Invalidate();
            }
        }

        public List<MapCell> Cells => _cells;

        public int SelectedCellIndex
        {
            get => _selectedCellIndex;
            set
            {
                _selectedCellIndex = value;
                Invalidate();
            }
        }

        public int DropPreviewIndex
        {
            get => _dropPreviewIndex;
            set
            {
                if (_dropPreviewIndex == value) return;
                _dropPreviewIndex = value;
                Invalidate();
            }
        }

        public int Radius
        {
            get => _radius;
            set
            {
                _radius = Math.Max(5, Math.Min(300, value));
                UpdateScrollSize();
                Invalidate();
            }
        }

        private PointF[] _hexTemplate = new PointF[6];

        public EditorMapCanvas()
        {
            DoubleBuffered = true;
            AutoScroll = true;
            BackColor = Color.FromArgb(248, 250, 252);
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint
                | ControlStyles.OptimizedDoubleBuffer | ControlStyles.StandardDoubleClick, true);
            UpdateHexTemplate();
        }

        private void UpdateHexTemplate()
        {
            for (int j = 0; j < 6; j++)
            {
                double angle = j * Math.PI / 3.0;
                _hexTemplate[j] = new PointF(
                    _radius * (float)Math.Cos(angle),
                    _radius * (float)Math.Sin(angle)
                );
            }
        }

        /// <summary>Document 变了（打开/撤销）时重建格子列表并刷新势力颜色。</summary>
        public void RebuildCells()
        {
            _cells = FrontNav.RebuildCells(_doc);
            RefreshFactionRoles();
        }

        /// <summary>撤销/重做时更新 Document，保留 sprite 缓存并对变化格做脏区重绘。</summary>
        public void ApplyDocumentFromHistory(BtlFrontDocument doc)
        {
            _doc = doc;
            var newCells = FrontNav.RebuildCells(doc);
            bool showBldg = _renderMode == MapRenderMode.All || _renderMode == MapRenderMode.BuildingsOnly;
            bool showFort = _renderMode == MapRenderMode.All || _renderMode == MapRenderMode.FortificationsOnly;

            if (_spriteCellHash != null
                && _cells != null
                && _spriteCellHash.Length == newCells.Count
                && _cells.Count == newCells.Count)
            {
                for (int i = 0; i < newCells.Count; i++)
                {
                    int oldHash = HashSpriteCell(_cells[i], showBldg, showFort);
                    int newHash = HashSpriteCell(newCells[i], showBldg, showFort);
                    if (oldHash != newHash)
                        _spriteCellHash[i] = int.MinValue;
                }
            }
            else
            {
                _spriteCellHash = null;
                _spriteMetaKey = int.MinValue;
            }

            _cells = newCells;
            RefreshFactionRoles();
            UpdateScrollSize();
            Invalidate();
        }

        int MapCols => FrontNav.MapWidth(_doc);
        int MapRows => FrontNav.MapHeight(_doc);
        bool HasMap => FrontNav.MapSize(_doc) != null;

        public void UpdateScrollSize()
        {
            if (!HasMap)
            {
                AutoScrollMinSize = Size.Empty;
                return;
            }

            int cols = MapCols;
            int rows = MapRows;

            float hDist = _radius * 1.5f;
            float vDist = (float)Math.Sqrt(3) * _radius;

            int width = (int)((cols - 1) * hDist + _radius * 2) + 40;
            int height = (int)(rows * vDist + vDist / 2f) + 40;

            AutoScrollMinSize = new Size(width, height);
            UpdateHexTemplate();
        }

        /// <summary>
        /// 开屏介绍文本 (未加载 BTL/地图时渲染在背景上的极简纯文本，无边框与外框)
        /// 您可以直接在此修改占位符文本内容。
        /// </summary>
        public string WelcomeText { get; set; } =
            "将军的荣耀3 地图编辑器\n\n" +
            "【简介】\n" +
            "版本：0.2.2\n本编辑器处于早期开发阶段，使用时如遇到bug欢迎反馈\n\n" +
            "【使用说明】\n" +
            "点击顶部菜单【文件】->【打开...】或【新建关卡地图...】开始使用\n\n" +
            "【快捷键操作】\n" +
            "• 鼠标右键  : 拖动地图\n• Ctrl + Z  : 撤销修改\n" +
            "• Ctrl + Y  : 重做修改\n" +
            "• Ctrl + C  : 复制当前选中地块完整属性至剪贴板\n" +
            "• Ctrl + V  : 粘贴剪贴板属性至目标格子\n" +
            "• B    : 切换画笔涂色模式（长按左键拖动涂色，批量粘贴剪贴板数据）\n• V    : 地形贴图随机变种\n• O    : 地形贴图随机偏移\n" +
            "• 双击地块  : 在注册表界面内显示\n\n" +
            "【更新日志】\n" +
            "1.地图游戏贴图模式\n2.bug修复\n3.其他小改动\n\n" +
            "" +
            "";

        private struct LinkRegion
        {
            public RectangleF Bounds;
            public string Url;
        }

        private List<LinkRegion> _welcomeLinkRegions = new List<LinkRegion>();

        private void DrawWelcomeText(Graphics g)
        {
            _welcomeLinkRegions.Clear();
            if (string.IsNullOrWhiteSpace(WelcomeText)) return;

            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;

            using (Font titleFont = new Font("Microsoft YaHei", 22f, FontStyle.Bold))
            using (Font sectionFont = new Font("Microsoft YaHei", 14f, FontStyle.Bold))
            using (Font bodyFont = new Font("Microsoft YaHei", 12f, FontStyle.Regular))
            using (Font linkFont = new Font("Microsoft YaHei", 12f, FontStyle.Underline))
            using (Brush titleBrush = new SolidBrush(Color.FromArgb(30, 41, 59)))
            using (Brush sectionBrush = new SolidBrush(Color.FromArgb(51, 65, 85)))
            using (Brush bodyBrush = new SolidBrush(Color.FromArgb(100, 116, 139)))
            using (Brush linkBrush = new SolidBrush(Color.FromArgb(37, 99, 235)))
            {
                float x = 45;
                float y = 45;

                string[] lines = WelcomeText.Split('\n');
                for (int i = 0; i < lines.Length; i++)
                {
                    string line = lines[i];
                    if (string.IsNullOrEmpty(line))
                    {
                        y += 14;
                        continue;
                    }

                    if (i == 0)
                    {
                        g.DrawString(line, titleFont, titleBrush, x, y);
                        y += 52;
                    }
                    else if (line.StartsWith("【") && line.EndsWith("】"))
                    {
                        g.DrawString(line, sectionFont, sectionBrush, x, y);
                        y += 34;
                    }
                    else
                    {
                        int httpIdx = line.IndexOf("http://");
                        if (httpIdx < 0) httpIdx = line.IndexOf("https://");

                        if (httpIdx >= 0)
                        {
                            string prefix = line.Substring(0, httpIdx);
                            string rest = line.Substring(httpIdx);
                            int endIdx = rest.IndexOf(' ');
                            string url = endIdx > 0 ? rest.Substring(0, endIdx) : rest;
                            string suffix = endIdx > 0 ? rest.Substring(endIdx) : "";

                            float curX = x;
                            if (!string.IsNullOrEmpty(prefix))
                            {
                                g.DrawString(prefix, bodyFont, bodyBrush, curX, y);
                                SizeF pSize = g.MeasureString(prefix, bodyFont);
                                curX += pSize.Width - 5;
                            }

                            g.DrawString(url, linkFont, linkBrush, curX, y);
                            SizeF urlSize = g.MeasureString(url, linkFont);

                            _welcomeLinkRegions.Add(new LinkRegion
                            {
                                Bounds = new RectangleF(curX, y, urlSize.Width, urlSize.Height),
                                Url = url
                            });

                            curX += urlSize.Width - 5;

                            if (!string.IsNullOrEmpty(suffix))
                            {
                                g.DrawString(suffix, bodyFont, bodyBrush, curX, y);
                            }
                        }
                        else
                        {
                            g.DrawString(line, bodyFont, bodyBrush, x, y);
                        }
                        y += 28;
                    }
                }
            }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            if (!HasMap || _cells.Count == 0)
            {
                DrawWelcomeText(e.Graphics);
                return;
            }

            RefreshFactionRoles();

            // Sprite 模式关闭抗锯齿，避免六角边缘糊成游戏里没有的颜色。
            int cols = MapCols;
            int rows = MapRows;

            Graphics g = e.Graphics;
            bool wantSprites = _visualStyle == MapVisualStyle.Sprite;
            g.SmoothingMode = wantSprites ? SmoothingMode.None : SmoothingMode.AntiAlias;
            g.PixelOffsetMode = wantSprites ? PixelOffsetMode.Half : PixelOffsetMode.Default;
            g.InterpolationMode = wantSprites ? InterpolationMode.NearestNeighbor : InterpolationMode.Default;

            // 应用滚动平移
            g.TranslateTransform(AutoScrollPosition.X, AutoScrollPosition.Y);

            float hDist = _radius * 1.5f;
            float vDist = (float)Math.Sqrt(3) * _radius;

            Rectangle clipRect = e.ClipRectangle;
            float clipLeft = clipRect.Left - AutoScrollPosition.X;
            float clipTop = clipRect.Top - AutoScrollPosition.Y;
            float clipRight = clipRect.Right - AutoScrollPosition.X;
            float clipBottom = clipRect.Bottom - AutoScrollPosition.Y;

            bool spriteMode = _visualStyle == MapVisualStyle.Sprite && EnsureSpriteCache();
            if (spriteMode)
                DrawSpriteViewport(g, clipLeft, clipTop, clipRight, clipBottom);
            else if (_visualStyle == MapVisualStyle.Sprite && !string.IsNullOrEmpty(_spriteLoadError))
            {
                using var font = new Font("Microsoft YaHei", 10f);
                using var brush = new SolidBrush(Color.FromArgb(180, 180, 50, 50));
                g.DrawString("贴图模式加载失败，已回退简笔：\n" + _spriteLoadError, font, brush, 20, 20);
            }

            // 1. 视口裁剪 (Viewport Culling): 仅计算在当前可视矩形中的列和行

            int minCol = Math.Max(0, (int)((clipLeft - _radius * 2 - 20) / hDist));
            int maxCol = Math.Min(cols - 1, (int)((clipRight + _radius * 2 + 20) / hDist));

            int minRow = Math.Max(0, (int)((clipTop - vDist * 2 - 20) / vDist));
            int maxRow = Math.Min(rows - 1, (int)((clipBottom + vDist * 2 + 20) / vDist));

            using (var borderPen = new Pen(Color.FromArgb(60, 15, 23, 42), 1))
            using (var selectedPen = new Pen(Color.FromArgb(20, 184, 166), 3))
            using (var dropPreviewBrush = new SolidBrush(Color.FromArgb(130, 255, 255, 255)))
            using (var cityPen = new Pen(Color.FromArgb(120, 0, 0, 0), 1.5f) { DashStyle = DashStyle.Dash })
            using (var playableBorderPen = new Pen(Color.FromArgb(220, 245, 158, 11), 2.5f) { DashStyle = DashStyle.Dash })
            using (var riverPen = new Pen(Color.FromArgb(34, 211, 238), 3.5f) { LineJoin = LineJoin.Round, StartCap = LineCap.Round, EndCap = LineCap.Round })
            using (var coordFont = new Font("Segoe UI", 7, FontStyle.Regular))
            using (var unitFont = new Font("Segoe UI", 8, FontStyle.Bold))
            using (var textBrush = new SolidBrush(Color.FromArgb(15, 23, 42)))
            {
                // 地形画刷缓存
                var brushCache = new Dictionary<Color, SolidBrush>();

                SolidBrush GetCachedBrush(Color color)
                {
                    if (!brushCache.TryGetValue(color, out var b))
                    {
                        b = new SolidBrush(color);
                        brushCache[color] = b;
                    }
                    return b;
                }

                PointF[] points = new PointF[6];

                // ----------------------------------------------------
                // Pass 1: 绘制所有地块的地形背景底色（简笔模式）
                // ----------------------------------------------------
                if (!spriteMode)
                {
                for (int col = minCol; col <= maxCol; col++)
                {
                    float cx = col * hDist + _radius + 10;
                    float cyOffset = (col % 2 == 1 ? vDist / 2f : 0f) + 10;

                    for (int row = minRow; row <= maxRow; row++)
                    {
                        int cellIndex = row * cols + col;
                        if (cellIndex < 0 || cellIndex >= _cells.Count) continue;

                        MapCell cell = _cells[cellIndex];
                        float cy = row * vDist + cyOffset;

                        for (int j = 0; j < 6; j++)
                        {
                            points[j] = new PointF(cx + _hexTemplate[j].X, cy + _hexTemplate[j].Y);
                        }

                        Color terrainColor = GetTerrainBgColor(cell);
                        g.FillPolygon(GetCachedBrush(terrainColor), points);
                    }
                }

                // ----------------------------------------------------
                // Pass 2: 绘制所有河流线段 (位于遮罩图层下方)
                // ----------------------------------------------------
                if (_renderMode != MapRenderMode.Reinforcements)
                {
                    for (int col = minCol; col <= maxCol; col++)
                    {
                        float cx = col * hDist + _radius + 10;
                        float cyOffset = (col % 2 == 1 ? vDist / 2f : 0f) + 10;

                        for (int row = minRow; row <= maxRow; row++)
                        {
                            int cellIndex = row * cols + col;
                            if (cellIndex < 0 || cellIndex >= _cells.Count) continue;

                            MapCell cell = _cells[cellIndex];
                            if (!IsRiver(cell)) continue;

                            float cy = row * vDist + cyOffset;
                            Point[] neighbors = GetNeighbors(cell.X, cell.Y);
                            foreach (var n in neighbors)
                            {
                                if (n.X >= 0 && n.X < cols && n.Y >= 0 && n.Y < rows)
                                {
                                    int nIndex = n.Y * cols + n.X;
                                    if (nIndex > cellIndex && nIndex < _cells.Count && IsRiver(_cells[nIndex]))
                                    {
                                        float ncx = n.X * hDist + _radius + 10;
                                        float ncy = n.Y * vDist + (n.X % 2 == 1 ? vDist / 2f : 0f) + 10;
                                        g.DrawLine(riverPen, cx, cy, ncx, ncy);
                                    }
                                }
                            }
                        }
                    }
                }
                }

                // ----------------------------------------------------
                // Pass 3: 绘制不可游玩区域半透明遮罩与边界框 (覆盖在地形与河流上方)
                // 贴图模式贴近游戏画面，不叠编辑器辅助层。
                // ----------------------------------------------------
                if (!spriteMode && HasMap)
                {
                    var sz = FrontNav.MapSize(_doc);
                    ushort leftM = FrontNav.MemberU16(sz, 2);
                    ushort topM = FrontNav.MemberU16(sz, 3);
                    ushort playW = FrontNav.MemberU16(sz, 4);
                    ushort playH = FrontNav.MemberU16(sz, 5);

                    if (playW > 0 && playH > 0)
                    {
                        for (int col = minCol; col <= maxCol; col++)
                        {
                            float cx = col * hDist + _radius + 10;
                            float cyOffset = (col % 2 == 1 ? vDist / 2f : 0f) + 10;

                            for (int row = minRow; row <= maxRow; row++)
                            {
                                int cellIndex = row * cols + col;
                                if (cellIndex < 0 || cellIndex >= _cells.Count) continue;

                                MapCell cell = _cells[cellIndex];
                                float cy = row * vDist + cyOffset;

                                for (int j = 0; j < 6; j++)
                                {
                                    points[j] = new PointF(cx + _hexTemplate[j].X, cy + _hexTemplate[j].Y);
                                }

                                bool isPlayable = cell.X >= leftM && cell.X < leftM + playW &&
                                                  cell.Y >= topM && cell.Y < topM + playH;

                                if (!isPlayable)
                                {
                                    g.FillPolygon(GetCachedBrush(Color.FromArgb(100, 15, 23, 42)), points);
                                }
                                else if (cell.X == leftM || cell.X == leftM + playW - 1 || cell.Y == topM || cell.Y == topM + playH - 1)
                                {
                                    g.DrawPolygon(playableBorderPen, points);
                                }
                            }
                        }
                    }
                }

                // ----------------------------------------------------
                // Pass 4: 绘制网格边框、坐标、建筑、工事、部队等上层元素
                // ----------------------------------------------------
                for (int col = minCol; col <= maxCol; col++)
                {
                    float cx = col * hDist + _radius + 10;
                    float cyOffset = (col % 2 == 1 ? vDist / 2f : 0f) + 10;

                    for (int row = minRow; row <= maxRow; row++)
                    {
                        int cellIndex = row * cols + col;
                        if (cellIndex < 0 || cellIndex >= _cells.Count) continue;

                        MapCell cell = _cells[cellIndex];
                        float cy = row * vDist + cyOffset;

                        for (int j = 0; j < 6; j++)
                        {
                            points[j] = new PointF(cx + _hexTemplate[j].X, cy + _hexTemplate[j].Y);
                        }

                        // 3. 绘制拖放预览 / 选中边框
                        if (_dropPreviewIndex == cell.Index)
                            g.FillPolygon(dropPreviewBrush, points);
                        if (_selectedCellIndex == cell.Index)
                        {
                            g.DrawPolygon(selectedPen, points);
                        }
                        else if (!spriteMode)
                        {
                            g.DrawPolygon(borderPen, points);
                        }

                        // 突出显示建筑/城市的虚线边框
                        if (!spriteMode)
                        {
                            bool hasBldg = cell.TriggerBldg != null;
                            bool hasFort = cell.TriggerFort != null;
                            bool showBldgBorder = (_renderMode == MapRenderMode.All || _renderMode == MapRenderMode.BuildingsOnly) && hasBldg;
                            bool showFortBorder = (_renderMode == MapRenderMode.All || _renderMode == MapRenderMode.FortificationsOnly) && hasFort;
                            if (showBldgBorder || showFortBorder)
                            {
                                g.DrawPolygon(cityPen, points);
                            }
                        }

                        // 4. 绘制坐标标签 (X,Y)
                        if (!spriteMode && _showCellCoordinates)
                        {
                            string coordText = $"{cell.X},{cell.Y}";
                            SizeF size = g.MeasureString(coordText, coordFont);
                            g.DrawString(coordText, coordFont, textBrush, cx - size.Width / 2f, cy + _radius * 0.45f);
                        }

                        // 5. 独立绘制建筑物与工事标签（贴图模式已经画了精灵，不再叠文字徽章）
                        if (!spriteMode && cell.TriggerBldg != null && (_renderMode == MapRenderMode.All || _renderMode == MapRenderMode.BuildingsOnly))
                        {
                            var bData = FrontNav.BuildingData(cell.TriggerBldg);
                            int bId = (int)FrontNav.MemberU16(bData, 1);
                            int owner = (int)FrontNav.MemberI64(bData, 3);
                            string bName = GameSettings.GetBuildingName(bId);
                            string bSymbol = bName.Length > 0 ? bName.Substring(0, Math.Min(3, bName.Length)) : "建";
                            string bText = $"{bSymbol}{owner}";

                            using (var bFont = new Font("Segoe UI", 7.5f, FontStyle.Bold))
                            using (var bBrush = new SolidBrush(Color.FromArgb(255, 30, 41, 59)))
                            {
                                SizeF bSize = g.MeasureString(bText, bFont);
                                var rect = new RectangleF(cx - bSize.Width / 2f - 2, cy - _radius * 0.48f - bSize.Height / 2f, bSize.Width + 4, bSize.Height);

                                Color factionBgColor = GetFactionBgColor(owner);
                                using (var bgBrush = new SolidBrush(Color.FromArgb(230, factionBgColor)))
                                using (var badgePen = new Pen(Color.FromArgb(120, Color.DimGray), 0.8f))
                                {
                                    g.FillRectangle(bgBrush, rect);
                                    g.DrawRectangle(badgePen, rect.X, rect.Y, rect.Width, rect.Height);
                                }
                                g.DrawString(bText, bFont, bBrush, cx - bSize.Width / 2f, cy - _radius * 0.48f - bSize.Height / 2f);
                            }
                        }

                        if (!spriteMode && cell.TriggerFort != null && (_renderMode == MapRenderMode.All || _renderMode == MapRenderMode.FortificationsOnly))
                        {
                            string fName = GameSettings.GetFortName((int)FrontNav.ScalarI64(FrontNav.FortDetail(cell.TriggerFort), 0));
                            string fSymbol = fName.Length > 0 ? fName.Substring(0, Math.Min(2, fName.Length)) : "工";

                            using (var fFont = new Font("Segoe UI", 6.5f, FontStyle.Bold))
                            using (var fBrush = new SolidBrush(Color.FromArgb(220, 71, 85, 105)))
                            {
                                SizeF fSize = g.MeasureString(fSymbol, fFont);
                                float fY = cy - _radius * 0.25f;

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

                        if (!spriteMode && _renderMode == MapRenderMode.DecorationsOnly)
                        {
                            byte decId = GetPrimaryDecorationId(cell);
                            if (decId > 0)
                            {
                                byte tileIdx = GetPrimaryDecorationTileIndex(cell);
                                string decSymbol = "";
                                if (decId == 49) decSymbol = "出";
                                else if (decId == 50) decSymbol = "滩";
                                else if (decId == 51) decSymbol = "田";
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

                        if (!spriteMode && _renderMode != MapRenderMode.UnitsOnly && _renderMode != MapRenderMode.Reinforcements)
                        {
                            bool hasOffset = false;
                            sbyte dx = 0, dy = 0;
                            if (cell.TriggerBldg != null)
                            {
                                var bData = FrontNav.BuildingData(cell.TriggerBldg);
                                dx = (sbyte)FrontNav.MemberI64(bData, 4);
                                dy = (sbyte)FrontNav.MemberI64(bData, 5);
                                if (dx != 0 || dy != 0) hasOffset = true;
                            }
                            else
                            {
                                var slots = new[] { cell.Attr, cell.AttrA2, cell.AttrA3 };
                                foreach (var s in slots)
                                {
                                    if (s != null)
                                    {
                                        sbyte sDx = FrontNav.AttrI8(s, 2);
                                        sbyte sDy = FrontNav.AttrI8(s, 3);
                                        if (sDx != 0 || sDy != 0)
                                        {
                                            dx = sDx;
                                            dy = sDy;
                                            hasOffset = true;
                                            break;
                                        }
                                    }
                                }
                            }

                            if (hasOffset && _showCellOffsets)
                            {
                                string offText = $"o:{dx},{dy}";
                                using (var fFont = new Font("Segoe UI", 6f, FontStyle.Regular))
                                using (var fBrush = new SolidBrush(Color.FromArgb(140, 100, 116, 139)))
                                {
                                    SizeF fSize = g.MeasureString(offText, fFont);
                                    float oY = cy + _radius * 0.18f;
                                    if (cell.TriggerFort != null || (cell.Attr != null && FrontNav.AttrU8(cell.Attr, 0) > 0))
                                    {
                                        oY = cy - _radius * 0.35f;
                                    }
                                    g.DrawString(offText, fFont, fBrush, cx - fSize.Width / 2f, oY - fSize.Height / 2f);
                                }
                            }
                        }

                        // 6. 绘制部队 Unit
                        if ((_renderMode == MapRenderMode.All || _renderMode == MapRenderMode.UnitsOnly) && cell.Unit != null)
                        {
                            DrawUnitItem(g, cell.Unit, cx, cy, unitFont);
                        }

                        // 7. 绘制增兵点 Badge
                        if (_renderMode == MapRenderMode.Reinforcements)
                        {
                            BtlTable rpHit = null;
                            foreach (var rp in FrontNav.TableItems(FrontNav.ReinforcePoints(_doc)))
                            {
                                if ((int)FrontNav.ScalarI64(rp, 0) == cell.Index)
                                {
                                    rpHit = rp;
                                    break;
                                }
                            }

                            if (rpHit != null)
                            {
                                int rpFaction = (int)FrontNav.ScalarI64(rpHit, 1);
                                float rBadge = _radius * 0.45f;
                                RectangleF badgeRect = new RectangleF(cx - rBadge, cy - rBadge, rBadge * 2, rBadge * 2);
                                using (var badgeBrush = new SolidBrush(Color.FromArgb(249, 115, 22)))
                                using (var whiteBorderPen = new Pen(Color.White, 1.8f))
                                {
                                    g.FillEllipse(badgeBrush, badgeRect);
                                    g.DrawEllipse(whiteBorderPen, badgeRect);
                                }

                                string rText = $"R{rpFaction}";
                                using (var rFont = new Font("Segoe UI", 7.5f, FontStyle.Bold))
                                using (var rBrush = new SolidBrush(Color.White))
                                {
                                    SizeF rSize = g.MeasureString(rText, rFont);
                                    g.DrawString(rText, rFont, rBrush, cx - rSize.Width / 2f, cy - rSize.Height / 2f);
                                }

                                if (FrontNav.ScalarI64(rpHit, 2) != 0)
                                {
                                    using (var starFont = new Font("Segoe UI", 9, FontStyle.Bold))
                                    using (var starBrush = new SolidBrush(Color.FromArgb(234, 179, 8)))
                                    {
                                        g.DrawString("★", starFont, starBrush, cx - rBadge * 1.3f, cy - rBadge * 1.3f);
                                    }
                                }
                            }
                        }
                    }
                }

                // 释放地形画刷缓存
                foreach (var b in brushCache.Values)
                {
                    b.Dispose();
                }
            }
        }

        private string GetDoodadName(int id)
        {
            if (id == 0) return "";
            if (GameSettings.MapTerrains.TryGetValue(id, out var terrain))
                return terrain.Name;
            if (GameSettings.Buildings.TryGetValue(id, out var bldg))
                return bldg.Name;
            if (GameSettings.Fortifications.TryGetValue(id, out var fort))
                return fort.Name;
            if (GameSettings.TerrainTypes.TryGetValue(id, out var typeName))
                return typeName;
            return $"D{id}";
        }

        private void DrawUnitItem(Graphics g, BtlTable agent, float cx, float cy, Font font)
        {
            if (agent == null || FrontNav.ChildStruct(agent, 0) == null) return;

            int unitId = FrontNav.AgentU16(agent, 3);
            int factionId = FrontNav.AgentU16(agent, 1);
            int facing = FrontNav.AgentU16(agent, 5) & 0xFF;
            bool faceLeft = facing == 0;
            UnitIconRole role = ClassifyUnitRole(factionId);

            Image icon = UnitIconCache.Get(unitId, role);
            float size = Math.Max(16f, _radius * 0.9f);

            if (icon != null)
            {
                var dest = new RectangleF(
                    faceLeft ? cx + size / 2f : cx - size / 2f,
                    cy - size / 2f,
                    faceLeft ? -size : size,
                    size);
                g.DrawImage(icon, dest);
            }
            else
            {
                float uRadius = _radius * 0.65f;
                RectangleF unitRect = new RectangleF(cx - uRadius, cy - uRadius, uRadius * 2, uRadius * 2);
                Color factionColor = GetUnitRoleColor(role);
                using (var unitBaseBrush = new SolidBrush(Color.FromArgb(200, 10, 13, 24)))
                using (var factionPen = new Pen(factionColor, 2.5f))
                {
                    g.FillEllipse(unitBaseBrush, unitRect);
                    g.DrawEllipse(factionPen, unitRect);
                }

                string unitName = GameSettings.GetUnitName(unitId);
                string idText = unitName.Length > 0 ? unitName.Substring(0, Math.Min(2, unitName.Length)) : "兵";
                SizeF textSize = g.MeasureString(idText, font);
                using (var textBrush = new SolidBrush(Color.White))
                    g.DrawString(idText, font, textBrush, cx - textSize.Width / 2f, cy - textSize.Height / 2f);
            }

            bool hasGeneral = FrontNav.Has(agent, 11);
            if (hasGeneral)
            {
                using (var starFont = new Font("Segoe UI", 9, FontStyle.Bold))
                using (var starBrush = new SolidBrush(Color.FromArgb(251, 191, 36)))
                {
                    g.DrawString("★", starFont, starBrush, cx - size * 0.55f, cy - size * 0.55f);
                }
            }
        }

        static byte Attr0(BtlStruct st) => st == null ? (byte)0 : FrontNav.AttrU8(st, 0);
        static byte Attr1(BtlStruct st) => st == null ? (byte)0 : FrontNav.AttrU8(st, 1);

        private byte GetPrimaryDecorationId(MapCell cell)
        {
            if (cell.Attr != null && Attr0(cell.Attr) >= 49 && Attr0(cell.Attr) < 100)
                return Attr0(cell.Attr);
            if (cell.AttrA2 != null && Attr0(cell.AttrA2) >= 49 && Attr0(cell.AttrA2) < 100)
                return Attr0(cell.AttrA2);
            if (cell.AttrA3 != null && Attr0(cell.AttrA3) >= 49 && Attr0(cell.AttrA3) < 100)
                return Attr0(cell.AttrA3);
            return 0;
        }

        private byte GetPrimaryDecorationTileIndex(MapCell cell)
        {
            if (cell.Attr != null && Attr0(cell.Attr) >= 49 && Attr0(cell.Attr) < 100)
                return Attr1(cell.Attr);
            if (cell.AttrA2 != null && Attr0(cell.AttrA2) >= 49 && Attr0(cell.AttrA2) < 100)
                return Attr1(cell.AttrA2);
            if (cell.AttrA3 != null && Attr0(cell.AttrA3) >= 49 && Attr0(cell.AttrA3) < 100)
                return Attr1(cell.AttrA3);
            return 0;
        }

        private int GetFinalTerrainType(MapCell cell)
        {
            ushort terrain = cell.Terrain;
            byte low = (byte)(terrain & 0xFF);
            byte high = (byte)(terrain >> 8);
            int v7 = low & 7;

            if ((high & 2) != 0) return 1;

            byte slot2Val = 0;
            if (cell.AttrA2 != null)
                slot2Val = Attr0(cell.AttrA2);

            if (slot2Val == 0)
            {
                if (v7 == 2) return 3;
                if (v7 == 3) return 2;
                return 0;
            }

            if (GameSettings.MapTerrains.TryGetValue(slot2Val, out var setting))
            {
                return setting.Type;
            }

            switch (slot2Val)
            {
                case 11: case 12: case 13: case 14: return 4;
                case 21: case 22: case 23: case 24: case 25: return 6;
                case 31: case 34: case 37: case 40: return 7;
                case 32: case 35: case 38: case 41: return 8;
                case 33: case 36: case 39: case 42: case 44: return 9;
                case 46: case 47: case 48: return 10;
                default:
                    if (v7 == 2) return 3;
                    if (v7 == 3) return 2;
                    return 0;
            }
        }

        private Color GetTerrainBgColor(MapCell cell)
        {
            if (_renderMode == MapRenderMode.DecorationsOnly)
            {
                byte decId = GetPrimaryDecorationId(cell);
                if (decId > 0)
                {
                    if (decId == 49) return Color.FromArgb(204, 251, 241);
                    if (decId == 50) return Color.FromArgb(254, 243, 199);
                    if (decId == 51) return Color.FromArgb(217, 249, 157);
                    if (decId == 52) return Color.FromArgb(254, 240, 138);
                    if (decId == 53) return Color.FromArgb(254, 215, 170);
                    if (decId == 54) return Color.FromArgb(187, 247, 208);
                    if (decId == 55) return Color.FromArgb(224, 242, 254);
                    if (decId == 56) return Color.FromArgb(253, 230, 138);
                    return Color.FromArgb(233, 213, 255);
                }
                else
                {
                    return Color.FromArgb(248, 250, 252);
                }
            }

            int type = GetFinalTerrainType(cell);
            ushort terrain = cell.Terrain;
            byte low = (byte)(terrain & 0xFF);
            int v7 = low & 7;

            switch (type)
            {
                case 0: return Color.FromArgb(220, 252, 231);
                case 1: return Color.FromArgb(191, 219, 254);
                case 2: return Color.FromArgb(248, 250, 252);
                case 3: return Color.FromArgb(254, 240, 138);
                case 4: return Color.FromArgb(163, 230, 53);
                case 6: return Color.FromArgb(34, 197, 94);
                case 7: return Color.FromArgb(253, 186, 116);
                case 8: return Color.FromArgb(156, 163, 175);
                case 9: return Color.FromArgb(75, 85, 99);
                case 10:
                    if (v7 == 2) return Color.FromArgb(254, 240, 138);
                    if (v7 == 3) return Color.FromArgb(248, 250, 252);
                    return Color.FromArgb(220, 252, 231);
                default: return Color.FromArgb(241, 245, 249);
            }
        }

        private bool IsRiver(MapCell cell)
        {
            return GetFinalTerrainType(cell) == 10;
        }

        private Point[] GetNeighbors(int x, int y)
        {
            Point[] neighbors = new Point[6];
            neighbors[0] = new Point(x, y - 1);
            neighbors[1] = new Point(x, y + 1);

            if (x % 2 == 0)
            {
                neighbors[2] = new Point(x + 1, y - 1);
                neighbors[3] = new Point(x + 1, y);
                neighbors[4] = new Point(x - 1, y - 1);
                neighbors[5] = new Point(x - 1, y);
            }
            else
            {
                neighbors[2] = new Point(x + 1, y);
                neighbors[3] = new Point(x + 1, y + 1);
                neighbors[4] = new Point(x - 1, y);
                neighbors[5] = new Point(x - 1, y + 1);
            }
            return neighbors;
        }

        private void RefreshFactionRoles()
        {
            _factionCamp.Clear();
            _playerFactionId = -1;
            _playerCamp = -1;
            var factions = FrontNav.TableItems(FrontNav.FactionList(_doc));
            if (factions.Count == 0) return;

            foreach (var faction in factions)
            {
                var info = FrontNav.ChildStruct(faction, 0);
                if (info == null) continue;
                int id = (int)FrontNav.MemberU16(info, 0);
                int camp = (int)FrontNav.MemberI64(info, 2);
                _factionCamp[id] = camp;
                if (_playerFactionId < 0 && FrontNav.MemberI64(info, 3) == 0)
                {
                    _playerFactionId = id;
                    _playerCamp = camp;
                }
            }
        }

        private UnitIconRole ClassifyUnitRole(int factionId)
        {
            if (factionId == 0) return UnitIconRole.Neutral;
            if (!_factionCamp.TryGetValue(factionId, out int camp) || camp == 0)
                return UnitIconRole.Neutral;
            if (factionId == _playerFactionId) return UnitIconRole.Player;
            if (_playerCamp >= 0 && camp == _playerCamp) return UnitIconRole.Ally;
            return UnitIconRole.Enemy;
        }

        private Color GetUnitRoleColor(UnitIconRole role)
        {
            switch (role)
            {
                case UnitIconRole.Player:
                case UnitIconRole.Ally:
                    return Color.FromArgb(59, 130, 246);
                case UnitIconRole.Enemy:
                    return Color.FromArgb(239, 68, 68);
                default:
                    return Color.FromArgb(148, 163, 184);
            }
        }

        private Color GetFactionBgColor(int factionId)
        {
            switch (factionId)
            {
                case 1: return Color.FromArgb(254, 242, 242);
                case 2: return Color.FromArgb(239, 246, 255);
                case 3: return Color.FromArgb(240, 253, 244);
                case 4: return Color.FromArgb(254, 243, 199);
                default: return Color.FromArgb(248, 250, 252);
            }
        }

        bool EnsureSpriteCache()
        {
            if (_spriteRenderer == null && _spriteLoadError == null)
            {
                Cursor = Cursors.WaitCursor;
                try
                {
                    if (!TerrainSpriteRenderer.TryCreate(out _spriteRenderer, out _spriteLoadError))
                        _spriteRenderer = null;
                }
                finally
                {
                    Cursor = Cursors.Default;
                }
            }
            if (_spriteRenderer == null)
                return false;

            bool showBldg = _renderMode == MapRenderMode.All || _renderMode == MapRenderMode.BuildingsOnly;
            bool showFort = _renderMode == MapRenderMode.All || _renderMode == MapRenderMode.FortificationsOnly;
            int meta = SpriteMetaKey(showBldg, showFort);
            Point scroll = AutoScrollPosition;
            bool scrolled = scroll != _lastPaintScroll;
            _lastPaintScroll = scroll;
            if (scrolled
                && _spriteCellHash != null
                && _spriteCellHash.Length == _cells.Count
                && meta == _spriteMetaKey
                && _spriteRenderer.MapBitmap != null)
                return true;

            bool sizeChanged = _spriteCellHash == null || _spriteCellHash.Length != _cells.Count || meta != _spriteMetaKey;

            var dirty = sizeChanged ? null : new List<int>();
            if (!sizeChanged)
            {
                for (int i = 0; i < _cells.Count; i++)
                {
                    int h = HashSpriteCell(_cells[i], showBldg, showFort);
                    if (h != _spriteCellHash[i])
                        dirty.Add(i);
                }
                if (dirty.Count == 0 && _spriteRenderer.MapBitmap != null)
                    return true;
            }

            try
            {
                var decoded = FrontTerrain.FromCells(_doc, _cells);
                bool full = sizeChanged || dirty.Count > Math.Max(48, _cells.Count / 5);
                if (full)
                {
                    Cursor = Cursors.WaitCursor;
                    try { _spriteRenderer.RenderFull(decoded, showBldg, showFort); }
                    finally { Cursor = Cursors.Default; }
                }
                else
                {
                    _spriteRenderer.RenderDirty(decoded, showBldg, showFort, dirty);
                }

                if (_spriteCellHash == null || _spriteCellHash.Length != _cells.Count)
                    _spriteCellHash = new int[_cells.Count];
                for (int i = 0; i < _cells.Count; i++)
                    _spriteCellHash[i] = HashSpriteCell(_cells[i], showBldg, showFort);
                _spriteMetaKey = meta;
                DropViewCache();
                return _spriteRenderer.MapBitmap != null;
            }
            catch (Exception ex)
            {
                _spriteLoadError = ex.Message;
                return false;
            }
        }

        void DrawSpriteViewport(Graphics g, float clipLeft, float clipTop, float clipRight, float clipBottom)
        {
            var bmp = _spriteRenderer?.MapBitmap;
            if (bmp == null) return;

            float scale = _radius * 1.5f / TerrainGeometry.ColW;
            float k = scale / TerrainGeometry.Scale;
            float ox = _radius + 10 - TerrainGeometry.OriginX * scale - TerrainGeometry.Pad * k;
            float oy = 10 - TerrainGeometry.OriginY * scale - TerrainGeometry.Pad * k;

            float visL = Math.Max(clipLeft, ox);
            float visT = Math.Max(clipTop, oy);
            float visR = Math.Min(clipRight, ox + bmp.Width * k);
            float visB = Math.Min(clipBottom, oy + bmp.Height * k);
            if (visR <= visL || visB <= visT) return;

            int srcX = Math.Max(0, (int)Math.Floor((visL - ox) / k) - 1);
            int srcY = Math.Max(0, (int)Math.Floor((visT - oy) / k) - 1);
            int srcR = Math.Min(bmp.Width, (int)Math.Ceiling((visR - ox) / k) + 1);
            int srcB = Math.Min(bmp.Height, (int)Math.Ceiling((visB - oy) / k) + 1);
            if (srcR <= srcX || srcB <= srcY) return;

            if (!ViewCacheCovers(srcX, srcY, srcR, srcB))
                RebuildViewCache(bmp, k, srcX, srcY, srcR, srcB, visR - visL, visB - visT);

            if (_viewCache != null)
            {
                float destL = ox + _viewCacheSrcX * k;
                float destT = oy + _viewCacheSrcY * k;
                float blitL = Math.Max(visL, destL);
                float blitT = Math.Max(visT, destT);
                float blitR = Math.Min(visR, destL + _viewCache.Width);
                float blitB = Math.Min(visB, destT + _viewCache.Height);
                if (blitR <= blitL || blitB <= blitT) return;

                int cacheX = Math.Max(0, (int)Math.Floor(blitL - destL));
                int cacheY = Math.Max(0, (int)Math.Floor(blitT - destT));
                int cacheW = Math.Min(_viewCache.Width - cacheX, (int)Math.Ceiling(blitR - destL) - cacheX);
                int cacheH = Math.Min(_viewCache.Height - cacheY, (int)Math.Ceiling(blitB - destT) - cacheY);
                if (cacheW <= 0 || cacheH <= 0) return;

                var destRect = new RectangleF(destL + cacheX, destT + cacheY, cacheW, cacheH);
                var srcRect = new Rectangle(cacheX, cacheY, cacheW, cacheH);
                var oldComp = g.CompositingMode;
                g.CompositingMode = CompositingMode.SourceCopy;
                g.DrawImage(_viewCache, destRect, srcRect, GraphicsUnit.Pixel);
                g.CompositingMode = oldComp;
                return;
            }

            var oldInterp = g.InterpolationMode;
            var oldMode = g.CompositingMode;
            g.CompositingMode = CompositingMode.SourceCopy;
            g.InterpolationMode = InterpolationMode.Bilinear;
            g.DrawImage(bmp,
                new RectangleF(ox + srcX * k, oy + srcY * k, (srcR - srcX) * k, (srcB - srcY) * k),
                new Rectangle(srcX, srcY, srcR - srcX, srcB - srcY),
                GraphicsUnit.Pixel);
            g.InterpolationMode = oldInterp;
            g.CompositingMode = oldMode;
        }

        bool ViewCacheCovers(int srcX, int srcY, int srcR, int srcB)
        {
            if (_viewCache == null || _viewCacheRadius != _radius)
                return false;
            if (srcX < _viewCacheSrcX || srcY < _viewCacheSrcY)
                return false;
            if (srcR > _viewCacheSrcX + _viewCacheSrcW || srcB > _viewCacheSrcY + _viewCacheSrcH)
                return false;
            return true;
        }

        void RebuildViewCache(Bitmap src, float k, int srcX, int srcY, int srcR, int srcB, float visW, float visH)
        {
            int extraX = Math.Max(64, (int)(visW / k * 0.55f));
            int extraY = Math.Max(64, (int)(visH / k * 0.55f));
            int x0 = Math.Max(0, srcX - extraX);
            int y0 = Math.Max(0, srcY - extraY);
            int x1 = Math.Min(src.Width, srcR + extraX);
            int y1 = Math.Min(src.Height, srcB + extraY);
            int sw = x1 - x0;
            int sh = y1 - y0;
            if (sw <= 0 || sh <= 0)
            {
                DropViewCache();
                return;
            }

            int dw = Math.Max(1, (int)Math.Ceiling(sw * k));
            int dh = Math.Max(1, (int)Math.Ceiling(sh * k));
            const int maxEdge = 4096;
            if (dw > maxEdge || dh > maxEdge)
            {
                x0 = srcX;
                y0 = srcY;
                sw = srcR - srcX;
                sh = srcB - srcY;
                dw = Math.Max(1, (int)Math.Ceiling(sw * k));
                dh = Math.Max(1, (int)Math.Ceiling(sh * k));
                if (dw > maxEdge || dh > maxEdge)
                {
                    DropViewCache();
                    return;
                }
            }

            DropViewCache();
            _viewCache = new Bitmap(dw, dh, PixelFormat.Format32bppPArgb);
            using (var vg = Graphics.FromImage(_viewCache))
            {
                vg.CompositingMode = CompositingMode.SourceCopy;
                vg.CompositingQuality = CompositingQuality.HighSpeed;
                vg.PixelOffsetMode = PixelOffsetMode.Half;
                vg.InterpolationMode = Math.Abs(k - 1f) < 0.02f
                    ? InterpolationMode.NearestNeighbor
                    : InterpolationMode.Bilinear;
                vg.DrawImage(src, new Rectangle(0, 0, dw, dh), new Rectangle(x0, y0, sw, sh), GraphicsUnit.Pixel);
            }
            _viewCacheRadius = _radius;
            _viewCacheSrcX = x0;
            _viewCacheSrcY = y0;
            _viewCacheSrcW = sw;
            _viewCacheSrcH = sh;
        }

        void DropViewCache()
        {
            _viewCache?.Dispose();
            _viewCache = null;
        }

        int SpriteMetaKey(bool showBldg, bool showFort)
        {
            unchecked
            {
                int h = 17;
                h = h * 31 + _cells.Count;
                h = h * 31 + MapCols;
                h = h * 31 + MapRows;
                h = h * 31 + (int)FrontNav.ScalarI64(FrontNav.Map(_doc), 1);
                h = h * 31 + (showBldg ? 1 : 0);
                h = h * 31 + (showFort ? 2 : 0);
                return h;
            }
        }

        int HashSpriteCell(MapCell c, bool showBldg, bool showFort)
        {
            unchecked
            {
                int h = c.Terrain;
                h = h * 31 + HashAttr(c.Attr);
                h = h * 31 + HashAttr(c.AttrA2);
                h = h * 31 + HashAttr(c.AttrA3);
                if (showBldg && FrontNav.BuildingData(c.TriggerBldg) != null)
                {
                    var b = FrontNav.BuildingData(c.TriggerBldg);
                    h = h * 31 + (int)FrontNav.MemberU16(b, 1);
                    h = h * 31 + (int)FrontNav.MemberI64(b, 3);
                    h = h * 31 + (int)FrontNav.MemberI64(b, 4);
                    h = h * 31 + (int)FrontNav.MemberI64(b, 5);
                    h = h * 31 + (int)FrontNav.MemberI64(b, 2);
                }
                if (showFort && FrontNav.FortDetail(c.TriggerFort) != null)
                {
                    int fid = (int)FrontNav.ScalarI64(FrontNav.FortDetail(c.TriggerFort), 0);
                    h = h * 31 + fid;
                    if (fid >= 3 && fid <= 5)
                    {
                        int facing = FrontNav.AgentU16(c.Unit, 5) & 0xFF;
                        h = h * 31 + facing;
                    }
                }
                return h;
            }
        }

        static int HashAttr(BtlStruct a)
        {
            if (a == null) return 0;
            return FrontNav.AttrU8(a, 0) | (FrontNav.AttrU8(a, 1) << 8)
                | ((byte)FrontNav.AttrI8(a, 2) << 16) | ((byte)FrontNav.AttrI8(a, 3) << 24);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _spriteRenderer?.Dispose();
                _spriteRenderer = null;
                _spriteCellHash = null;
                DropViewCache();
            }
            base.Dispose(disposing);
        }

        protected override void OnMouseWheel(MouseEventArgs e)
        {
            ((HandledMouseEventArgs)e).Handled = true;
            if (e.Delta > 0)
                Radius += 3;
            else
                Radius -= 3;
        }

        public int HitTestCell(Point client)
        {
            return GetCellAtPoint(client.X - AutoScrollPosition.X, client.Y - AutoScrollPosition.Y);
        }

        private bool _isMouseDown = false;
        private int _lastPaintedIndex = -1;
        private bool _isPanning;
        private Point _panStartClient;
        private Point _panStartScroll;

        /// <summary>
        /// O(1) 级别的鼠标命中检测算法（取代传统的 O(n) 全局格子循环比对）
        /// </summary>
        public int GetCellAtPoint(float clickX, float clickY)
        {
            return GetCellAtPointInternal(clickX, clickY);
        }

        public bool TryGetCellCenter(int cellIndex, out float cx, out float cy)
        {
            cx = cy = 0;
            if (!HasMap || cellIndex < 0 || cellIndex >= _cells.Count)
                return false;
            int cols = MapCols;
            var cell = _cells[cellIndex];
            float hDist = _radius * 1.5f;
            float vDist = (float)Math.Sqrt(3) * _radius;
            cx = cell.X * hDist + _radius + 10;
            cy = cell.Y * vDist + ((cell.X % 2 == 1) ? vDist / 2f : 0f) + 10;
            return true;
        }

        private int GetCellAtPointInternal(float clickX, float clickY)
        {
            if (!HasMap || _cells.Count == 0) return -1;

            int cols = MapCols;
            int rows = MapRows;

            float hDist = _radius * 1.5f;
            float vDist = (float)Math.Sqrt(3) * _radius;

            // 估算最近的 X
            int approxX = (int)Math.Round((clickX - _radius - 10) / hDist);
            approxX = Math.Max(0, Math.Min(cols - 1, approxX));

            // 估算最近的 Y
            float rowOffsetY = (approxX % 2 == 1) ? vDist / 2f : 0f;
            int approxY = (int)Math.Round((clickY - rowOffsetY - _radius - 10) / vDist);
            approxY = Math.Max(0, Math.Min(rows - 1, approxY));

            int bestIndex = -1;
            float minDistSq = float.MaxValue;

            // 仅搜索估算坐标周边 3x3 邻域（至多 9 个单元格），实现绝对 O(1) 耗时
            for (int dx = -1; dx <= 1; dx++)
            {
                int cxIndex = approxX + dx;
                if (cxIndex < 0 || cxIndex >= cols) continue;

                for (int dy = -1; dy <= 1; dy++)
                {
                    int cyIndex = approxY + dy;
                    if (cyIndex < 0 || cyIndex >= rows) continue;

                    int idx = cyIndex * cols + cxIndex;
                    if (idx < 0 || idx >= _cells.Count) continue;

                    float cellCx = cxIndex * hDist + _radius + 10;
                    float cellCy = cyIndex * vDist + (cxIndex % 2 == 1 ? vDist / 2f : 0f) + 10;

                    float distSq = (clickX - cellCx) * (clickX - cellCx) + (clickY - cellCy) * (clickY - cellCy);
                    if (distSq < minDistSq)
                    {
                        minDistSq = distSq;
                        bestIndex = idx;
                    }
                }
            }

            if (bestIndex != -1 && minDistSq < _radius * _radius * 0.9f)
            {
                return bestIndex;
            }

            return -1;
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (!HasMap || _cells.Count == 0)
            {
                if (e.Button == MouseButtons.Left)
                {
                    foreach (var link in _welcomeLinkRegions)
                    {
                        if (link.Bounds.Contains(e.Location))
                        {
                            try
                            {
                                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                                {
                                    FileName = link.Url,
                                    UseShellExecute = true
                                });
                            }
                            catch (Exception ex)
                            {
                                MessageBox.Show($"无法打开链接: {ex.Message}", "提示", MessageBoxButtons.OK, MessageBoxIcon.Error);
                            }
                            break;
                        }
                    }
                }
                return;
            }
            if (e.Button == MouseButtons.Right)
            {
                _isPanning = true;
                _panStartClient = e.Location;
                _panStartScroll = new Point(-AutoScrollPosition.X, -AutoScrollPosition.Y);
                Cursor = Cursors.SizeAll;
                Capture = true;
                return;
            }
            if (e.Button != MouseButtons.Left) return;

            _isMouseDown = true;
            HandleMousePaintOrSelect(e);
            MapMouseDown?.Invoke(this, e);
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            MapMouseMove?.Invoke(this, e);
            if (!HasMap || _cells.Count == 0)
            {
                bool isOverLink = false;
                foreach (var link in _welcomeLinkRegions)
                {
                    if (link.Bounds.Contains(e.Location))
                    {
                        isOverLink = true;
                        break;
                    }
                }
                Cursor = isOverLink ? Cursors.Hand : Cursors.Default;
                return;
            }

            if (_isPanning)
            {
                int dx = e.X - _panStartClient.X;
                int dy = e.Y - _panStartClient.Y;
                AutoScrollPosition = new Point(_panStartScroll.X - dx, _panStartScroll.Y - dy);
                return;
            }

            if (_isMouseDown && BrushMode)
            {
                HandleMousePaintOrSelect(e);
            }
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            if (e.Button == MouseButtons.Right && _isPanning)
            {
                _isPanning = false;
                Capture = false;
                Cursor = Cursors.Default;
            }
            if (e.Button == MouseButtons.Left)
            {
                _isMouseDown = false;
                _lastPaintedIndex = -1;
            }
            MapMouseUp?.Invoke(this, e);
        }

        private void HandleMousePaintOrSelect(MouseEventArgs e)
        {
            if (!HasMap) return;

            float clickX = e.X - AutoScrollPosition.X;
            float clickY = e.Y - AutoScrollPosition.Y;

            int closestIndex = GetCellAtPointInternal(clickX, clickY);

            if (closestIndex != -1)
            {
                if (BrushMode && (Control.ModifierKeys & Keys.Shift) == 0)
                {
                    if (closestIndex != _lastPaintedIndex)
                    {
                        _lastPaintedIndex = closestIndex;
                        _selectedCellIndex = closestIndex;
                        Invalidate();
                        PaintCellRequested?.Invoke(closestIndex);
                    }
                }
                else
                {
                    if (_selectedCellIndex != closestIndex)
                    {
                        _selectedCellIndex = closestIndex;
                        Invalidate();
                        CellSelected?.Invoke(closestIndex);
                    }
                }
            }
        }

        protected override void OnMouseDoubleClick(MouseEventArgs e)
        {
            base.OnMouseDoubleClick(e);
            if (!HasMap) return;

            float clickX = e.X - AutoScrollPosition.X;
            float clickY = e.Y - AutoScrollPosition.Y;

            int closestIndex = GetCellAtPointInternal(clickX, clickY);

            if (closestIndex != -1)
            {
                CellDoubleClicked?.Invoke(closestIndex);
            }
        }
    }
}
