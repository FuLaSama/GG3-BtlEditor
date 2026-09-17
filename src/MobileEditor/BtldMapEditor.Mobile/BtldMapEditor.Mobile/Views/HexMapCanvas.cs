using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using BtldMapEditor;

namespace BtldMapEditor.Mobile.Views
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

    public class HexMapCanvas : Control
    {
        private StageModel _stage;
        private List<CellItem> _cells = new List<CellItem>();
        private int _selectedCellIndex = -1;
        private int _radius = 35; // 手机端默认半径稍大，更适合手指触控
        private MapRenderMode _renderMode = MapRenderMode.All;
        private ScrollViewer? _attachedScrollViewer;
        private EventHandler<AvaloniaPropertyChangedEventArgs>? _scrollPropertyChangedHandler;
        private bool _showCellCoordinates = true;
        private bool _showCellOffsets = true;

        private PathGeometry _hexagonPrototype;
        private readonly Dictionary<int, FormattedText> _coordTextCache = new Dictionary<int, FormattedText>();
        private readonly Dictionary<int, FormattedText> _bldgTextCache = new Dictionary<int, FormattedText>();
        private readonly Dictionary<int, FormattedText> _fortTextCache = new Dictionary<int, FormattedText>();
        private readonly Dictionary<int, FormattedText> _unitTextCache = new Dictionary<int, FormattedText>();
        private readonly Dictionary<int, FormattedText> _reinforceTextCache = new Dictionary<int, FormattedText>();
        private readonly Dictionary<Color, SolidColorBrush> _brushCache = new Dictionary<Color, SolidColorBrush>();
        private readonly Dictionary<(Color, double), Pen> _penCache = new Dictionary<(Color, double), Pen>();

        // Pre-allocated static/readonly pens and brushes to prevent GC churn
        private static readonly Pen _borderPen = new Pen(new SolidColorBrush(Color.FromArgb(60, 15, 23, 42)), 1);
        private static readonly Pen _selectedPen = new Pen(new SolidColorBrush(Color.FromArgb(255, 20, 184, 166)), 3);
        private static readonly Pen _cityPen = new Pen(new SolidColorBrush(Color.FromArgb(120, 0, 0, 0)), 1.5f) { DashStyle = DashStyle.Dash };
        private static readonly SolidColorBrush _textBrush = new SolidColorBrush(Color.FromArgb(255, 15, 23, 42));
        private static readonly Pen _riverPen = new Pen(new SolidColorBrush(Color.FromArgb(255, 34, 211, 238)), 3.5f)
        {
            LineCap = PenLineCap.Round,
            LineJoin = PenLineJoin.Round
        };
        private static readonly SolidColorBrush _unitBaseBrush = new SolidColorBrush(Color.FromArgb(200, 10, 13, 24));
        private static readonly SolidColorBrush _whiteBrush = new SolidColorBrush(Colors.White);
        private static readonly Pen _whiteBorderPen = new Pen(Brushes.White, 1.8f);
        private static readonly Pen _borderPenGray = new Pen(Brushes.DimGray, 0.8f);
        private static readonly Pen _borderPenFort = new Pen(Brushes.Gray, 0.5f);
        private static readonly SolidColorBrush _nonPlayableMaskBrush = new SolidColorBrush(Color.FromArgb(100, 15, 23, 42));
        private static readonly Pen _playableBorderPen = new Pen(new SolidColorBrush(Color.FromArgb(220, 245, 158, 11)), 2.5f) { DashStyle = DashStyle.Dash };
        private static readonly Typeface _segoeTypeface = new Typeface(new FontFamily("Segoe UI"));
        private static readonly Typeface _segoeBoldTypeface = new Typeface(new FontFamily("Segoe UI"), FontStyle.Normal, FontWeight.Bold);

        private static readonly SolidColorBrush _starBrush = new SolidColorBrush(Color.FromArgb(255, 251, 191, 36));
        private static readonly FormattedText _starFormattedText = new FormattedText(
            "★",
            System.Globalization.CultureInfo.CurrentCulture,
            FlowDirection.LeftToRight,
            _segoeBoldTypeface,
            10,
            _starBrush
        );

        private int[] _cellTerrainTypes;
        private SolidColorBrush[] _cellTerrainBrushes;
        private List<int>[] _cellNeighbors;

        private bool _isPinching = false;
        private int _startRadius = 35;
        private Vector _startScrollOffset;
        private Point _startScaleOrigin;

        private Point[] _cellCenters;
        private Dictionary<int, ReinforcePointModel> _reinforceByCell;

        private static readonly Point[] _hexAngles = new Point[6];
        static HexMapCanvas()
        {
            for (int j = 0; j < 6; j++)
            {
                double angle = j * Math.PI / 3.0;
                _hexAngles[j] = new Point(Math.Cos(angle), Math.Sin(angle));
            }
        }

        private readonly StreamGeometry[] _terrainBatchGeometries = new StreamGeometry[12];
        private StreamGeometry _riverBatchGeometry;
        private StreamGeometry _borderBatchGeometry;
        private bool _isBatchDirty = true;

        private void ClearCaches()
        {
            _hexagonPrototype = null;
            _coordTextCache.Clear();
            _bldgTextCache.Clear();
            _fortTextCache.Clear();
            _unitTextCache.Clear();
            _reinforceTextCache.Clear();
            _brushCache.Clear();
            _penCache.Clear();
            _isBatchDirty = true;
        }

        public void InvalidateCellCache(int cellIndex)
        {
            if (_cellTerrainTypes != null && cellIndex >= 0 && cellIndex < _cellTerrainTypes.Length)
            {
                _cellTerrainTypes[cellIndex] = -1;
            }
            _bldgTextCache.Remove(cellIndex);
            _fortTextCache.Remove(cellIndex);
            _unitTextCache.Remove(cellIndex);
            _reinforceTextCache.Remove(cellIndex);
            _isBatchDirty = true;
        }

        private SolidColorBrush GetSolidBrush(Color color)
        {
            if (!_brushCache.TryGetValue(color, out var brush))
            {
                brush = new SolidColorBrush(color);
                _brushCache[color] = brush;
            }
            return brush;
        }

        private Pen GetPen(Color color, double thickness)
        {
            var key = (color, thickness);
            if (!_penCache.TryGetValue(key, out var pen))
            {
                pen = new Pen(GetSolidBrush(color), (float)thickness);
                _penCache[key] = pen;
            }
            return pen;
        }

        public static readonly DirectProperty<HexMapCanvas, StageModel> StageProperty =
            AvaloniaProperty.RegisterDirect<HexMapCanvas, StageModel>(
                nameof(Stage),
                o => o.Stage,
                (o, v) => o.Stage = v);

        public static readonly DirectProperty<HexMapCanvas, int> SelectedCellIndexProperty =
            AvaloniaProperty.RegisterDirect<HexMapCanvas, int>(
                nameof(SelectedCellIndex),
                o => o.SelectedCellIndex,
                (o, v) => o.SelectedCellIndex = v);

        public StageModel Stage
        {
            get => _stage;
            set
            {
                SetAndRaise(StageProperty, ref _stage, value);
                _selectedCellIndex = -1;
                ClearCaches();
                RebuildCells();
                InvalidateMeasure();
                InvalidateVisual();
            }
        }

        public int SelectedCellIndex
        {
            get => _selectedCellIndex;
            set
            {
                SetAndRaise(SelectedCellIndexProperty, ref _selectedCellIndex, value);
                InvalidateVisual();
            }
        }

        public int Radius
        {
            get => _radius;
            set
            {
                _radius = Math.Max(10, Math.Min(200, value));
                ClearCaches();
                RebuildCellCenters();
                InvalidateMeasure();
                InvalidateVisual();
            }
        }

        public MapRenderMode RenderMode
        {
            get => _renderMode;
            set
            {
                _renderMode = value;
                InvalidateVisual();
            }
        }

        public bool ShowCellCoordinates
        {
            get => _showCellCoordinates;
            set
            {
                _showCellCoordinates = value;
                InvalidateVisual();
            }
        }

        public bool ShowCellOffsets
        {
            get => _showCellOffsets;
            set
            {
                _showCellOffsets = value;
                InvalidateVisual();
            }
        }

        private bool _brushMode = false;
        private Vector _lockedScrollOffset;
        public bool BrushMode
        {
            get => _brushMode;
            set
            {
                _brushMode = value;
                if (_brushMode)
                {
                    var scrollViewer = Parent as ScrollViewer;
                    if (scrollViewer != null)
                    {
                        _lockedScrollOffset = scrollViewer.Offset;
                    }
                }
            }
        }

        public event Action<int> CellSelected;
        public Action<int> PaintCellRequested { get; set; }

        public List<CellItem> Cells => _cells;

        public HexMapCanvas()
        {
            ClipToBounds = true;

            // Pinch-to-zoom support
            GestureRecognizers.Add(new PinchGestureRecognizer());
            AddHandler(Gestures.PinchEvent, OnPinch);
            AddHandler(Gestures.PinchEndedEvent, OnPinchEnded);
        }

        private void OnPinch(object? sender, PinchEventArgs e)
        {
            var scrollViewer = Parent as ScrollViewer;
            if (scrollViewer == null) return;

            if (!_isPinching)
            {
                _isPinching = true;
                _startRadius = _radius;
                _startScrollOffset = scrollViewer.Offset;
                _startScaleOrigin = e.ScaleOrigin;
            }

            int newRadius = (int)(_startRadius * e.Scale);
            newRadius = Math.Max(10, Math.Min(200, newRadius));

            if (newRadius != _radius)
            {
                Radius = newRadius;

                // Force layout update to refresh ScrollViewer extent
                scrollViewer.UpdateLayout();

                double ratio = (double)newRadius / _startRadius;
                double targetX = _startScrollOffset.X + _startScaleOrigin.X * (ratio - 1);
                double targetY = _startScrollOffset.Y + _startScaleOrigin.Y * (ratio - 1);

                double maxOffsetX = Math.Max(0, scrollViewer.Extent.Width - scrollViewer.Viewport.Width);
                double maxOffsetY = Math.Max(0, scrollViewer.Extent.Height - scrollViewer.Viewport.Height);

                double clampedX = Math.Max(0, Math.Min(maxOffsetX, targetX));
                double clampedY = Math.Max(0, Math.Min(maxOffsetY, targetY));

                scrollViewer.Offset = new Vector(clampedX, clampedY);
            }
        }

        private void OnPinchEnded(object? sender, PinchEndedEventArgs e)
        {
            _isPinching = false;
        }

        protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
        {
            base.OnAttachedToVisualTree(e);
            DetachScrollViewerListener();

            _attachedScrollViewer = Parent as ScrollViewer;
            if (_attachedScrollViewer != null)
            {
                _scrollPropertyChangedHandler = (s, ev) =>
                {
                    if (ev.Property == ScrollViewer.OffsetProperty)
                    {
                        if (BrushMode)
                        {
                            _attachedScrollViewer.Offset = _lockedScrollOffset;
                        }
                    }
                    else if (ev.Property == ScrollViewer.ViewportProperty)
                    {
                        InvalidateVisual();
                    }
                };
                _attachedScrollViewer.PropertyChanged += _scrollPropertyChangedHandler;
            }
            _isPointerDown = false;
        }

        protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
        {
            base.OnDetachedFromVisualTree(e);
            DetachScrollViewerListener();
            _isPointerDown = false;
            _lastPaintedIndex = -1;
            ClearCaches();
        }

        private void DetachScrollViewerListener()
        {
            if (_attachedScrollViewer != null && _scrollPropertyChangedHandler != null)
            {
                _attachedScrollViewer.PropertyChanged -= _scrollPropertyChangedHandler;
                _attachedScrollViewer = null;
                _scrollPropertyChangedHandler = null;
            }
        }

        public void RebuildCells()
        {
            ClearCaches();
            _cellTerrainBrushes = null;
            _cellTerrainTypes = null;
            _cellNeighbors = null;
            _cells.Clear();
            if (_stage?.MapTerrain?.Size == null) return;

            ushort width = _stage.MapTerrain.Size.Width;
            ushort height = _stage.MapTerrain.Size.Height;
            var tiles = _stage.MapTerrain.Tiles ?? new List<ushort>();
            var attributes = _stage.MapTerrain.Attributes ?? new List<TileAttrModel>();

            var agentsByCell = new Dictionary<int, AIAgentModel>();
            if (_stage.AIInfo?.Agents != null)
            {
                foreach (var agent in _stage.AIInfo.Agents)
                {
                    if (agent.AgentInfo != null && agent.AgentInfo.CellIdx.HasValue)
                    {
                        agentsByCell[agent.AgentInfo.CellIdx.Value] = agent;
                    }
                }
            }

            var triggerBldgByCell = new Dictionary<int, TriggerEventModel>();
            var triggerFortByCell = new Dictionary<int, TriggerEventModel>();
            if (_stage.TriggerInfo?.Events != null)
            {
                foreach (var evt in _stage.TriggerInfo.Events)
                {
                    if (evt.DetailBldg != null && evt.DetailBldg.BuildingData != null)
                        triggerBldgByCell[evt.TileIndex] = evt;
                    if (evt.DetailFort != null)
                        triggerFortByCell[evt.TileIndex] = evt;
                }
            }

            int attrIndex = 0;
            for (int i = 0; i < width * height; i++)
            {
                int x = i % width;
                int y = i / width;
                ushort terrain = i < tiles.Count ? tiles[i] : (ushort)9001;
                byte v6 = (byte)(terrain >> 8);

                TileAttrModel attrA1 = null;
                TileAttrModel attrA2 = null;
                TileAttrModel attrA3 = null;

                if ((v6 & 4) != 0 && attrIndex < attributes.Count)
                {
                    attrA1 = attributes[attrIndex++];
                }
                if ((v6 & 8) != 0 && attrIndex < attributes.Count)
                {
                    attrA2 = attributes[attrIndex++];
                }
                if ((v6 & 0x10) != 0 && attrIndex < attributes.Count)
                {
                    attrA3 = attributes[attrIndex++];
                }

                if (attrA1 == null)
                {
                    attrA1 = new TileAttrModel { Byte0 = 0, Byte1 = 0, Byte2 = 0, Byte3 = 0 };
                }

                AIAgentModel unit;
                agentsByCell.TryGetValue(i, out unit);

                TriggerEventModel bldg;
                triggerBldgByCell.TryGetValue(i, out bldg);

                TriggerEventModel fort;
                triggerFortByCell.TryGetValue(i, out fort);

                _cells.Add(new CellItem
                {
                    Index = i,
                    X = x,
                    Y = y,
                    Terrain = terrain,
                    Attr = attrA1,
                    AttrA2 = attrA2,
                    AttrA3 = attrA3,
                    Unit = unit,
                    TriggerBldg = bldg,
                    TriggerFort = fort,
                });
            }

            // Pre-calculate neighbor indices
            _cellNeighbors = new List<int>[width * height];
            for (int i = 0; i < width * height; i++)
            {
                int x = i % width;
                int y = i / width;
                var list = new List<int>();
                var neighbors = GetNeighborsRaw(x, y);
                foreach (var n in neighbors)
                {
                    if (n.Item1 >= 0 && n.Item1 < width && n.Item2 >= 0 && n.Item2 < height)
                    {
                        list.Add(n.Item2 * width + n.Item1);
                    }
                }
                _cellNeighbors[i] = list;
            }

            var reinforceByCell = new Dictionary<int, ReinforcePointModel>();
            if (_stage.BattleInfo?.ReinforcePoints != null)
            {
                foreach (var rp in _stage.BattleInfo.ReinforcePoints)
                {
                    if (rp.CellIdx.HasValue)
                    {
                        reinforceByCell[Convert.ToInt32(rp.CellIdx.Value)] = rp;
                    }
                }
            }
            _reinforceByCell = reinforceByCell;

            RebuildCellCenters();
            _isBatchDirty = true;
        }

        private void RebuildCellCenters()
        {
            if (_cells.Count == 0 || _stage?.MapTerrain?.Size == null) return;
            int cellCount = _cells.Count;
            if (_cellCenters == null || _cellCenters.Length != cellCount)
            {
                _cellCenters = new Point[cellCount];
            }

            float hDist = _radius * 1.5f;
            float vDist = (float)Math.Sqrt(3) * _radius;

            for (int i = 0; i < cellCount; i++)
            {
                var cell = _cells[i];
                float cx = cell.X * hDist + _radius + 10;
                float cy = cell.Y * vDist + (cell.X % 2 == 1 ? vDist / 2f : 0f) + 10;
                _cellCenters[i] = new Point(cx, cy);
            }
        }

        private void RebuildBatchGeometries()
        {
            for (int i = 0; i < 12; i++)
            {
                _terrainBatchGeometries[i] = null;
            }
            _riverBatchGeometry = null;
            _borderBatchGeometry = null;

            if (_cells.Count == 0 || _stage?.MapTerrain?.Size == null) return;

            int cellCount = _cells.Count;
            RebuildCellCenters();

            if (_cellTerrainBrushes == null || _cellTerrainBrushes.Length != cellCount)
            {
                _cellTerrainBrushes = new SolidColorBrush[cellCount];
                _cellTerrainTypes = new int[cellCount];
                for (int i = 0; i < cellCount; i++)
                {
                    _cellTerrainTypes[i] = -1;
                }
            }

            var terrainContexts = new StreamGeometryContext[12];
            var terrainGeoms = new StreamGeometry[12];

            var borderGeom = new StreamGeometry();
            var borderCtx = borderGeom.Open();

            var riverGeom = new StreamGeometry();
            var riverCtx = riverGeom.Open();

            for (int i = 0; i < cellCount; i++)
            {
                var cell = _cells[i];
                Point center = _cellCenters[i];
                double cx = center.X;
                double cy = center.Y;

                if (_cellTerrainTypes[i] == -1)
                {
                    int type = GetFinalTerrainType(cell);
                    _cellTerrainTypes[i] = type;
                    _cellTerrainBrushes[i] = GetSolidBrush(GetTerrainBgColor(cell));
                }

                int terrainType = _cellTerrainTypes[i];
                if (terrainType < 0 || terrainType >= 12) terrainType = 0;

                if (terrainContexts[terrainType] == null)
                {
                    var tGeom = new StreamGeometry();
                    terrainContexts[terrainType] = tGeom.Open();
                    terrainGeoms[terrainType] = tGeom;
                }

                var tCtx = terrainContexts[terrainType];

                Point p0 = new Point(cx + _radius * _hexAngles[0].X, cy + _radius * _hexAngles[0].Y);

                // Terrain Fill polygon
                tCtx.BeginFigure(p0, isFilled: true);
                for (int j = 1; j < 6; j++)
                {
                    tCtx.LineTo(new Point(cx + _radius * _hexAngles[j].X, cy + _radius * _hexAngles[j].Y));
                }
                tCtx.EndFigure(isClosed: true);

                // Hex Border stroke
                borderCtx.BeginFigure(p0, isFilled: false);
                for (int j = 1; j < 6; j++)
                {
                    borderCtx.LineTo(new Point(cx + _radius * _hexAngles[j].X, cy + _radius * _hexAngles[j].Y));
                }
                borderCtx.EndFigure(isClosed: true);

                // River centerline segment (deduplicated: nIndex > i)
                if (terrainType == 10 && _cellNeighbors != null && i < _cellNeighbors.Length)
                {
                    var neighbors = _cellNeighbors[i];
                    if (neighbors != null)
                    {
                        foreach (int nIndex in neighbors)
                        {
                            if (nIndex > i && nIndex < cellCount)
                            {
                                if (_cellTerrainTypes[nIndex] == -1)
                                {
                                    _cellTerrainTypes[nIndex] = GetFinalTerrainType(_cells[nIndex]);
                                    _cellTerrainBrushes[nIndex] = GetSolidBrush(GetTerrainBgColor(_cells[nIndex]));
                                }

                                if (_cellTerrainTypes[nIndex] == 10)
                                {
                                    Point nCenter = _cellCenters[nIndex];
                                    riverCtx.BeginFigure(center, isFilled: false);
                                    riverCtx.LineTo(nCenter);
                                    riverCtx.EndFigure(isClosed: false);
                                }
                            }
                        }
                    }
                }
            }

            for (int k = 0; k < 12; k++)
            {
                if (terrainContexts[k] != null)
                {
                    terrainContexts[k].Dispose();
                    _terrainBatchGeometries[k] = terrainGeoms[k];
                }
            }
            borderCtx.Dispose();
            _borderBatchGeometry = borderGeom;

            riverCtx.Dispose();
            _riverBatchGeometry = riverGeom;

            _isBatchDirty = false;
        }

        protected override Size MeasureOverride(Size availableSize)
        {
            if (_stage?.MapTerrain?.Size == null)
                return new Size(100, 100);

            int cols = _stage.MapTerrain.Size.Width;
            int rows = _stage.MapTerrain.Size.Height;

            float hDist = _radius * 1.5f;
            float vDist = (float)Math.Sqrt(3) * _radius;

            double width = (cols - 1) * hDist + _radius * 2 + 40;
            double height = rows * vDist + vDist / 2.0 + 40;

            return new Size(width, height);
        }

        public override void Render(DrawingContext context)
        {
            base.Render(context);
            if (_stage?.MapTerrain?.Size == null || _cells.Count == 0) return;

            if (_isBatchDirty)
            {
                RebuildBatchGeometries();
            }

            // 1. Draw Static Terrain Fills (Batched into ~10 Draw calls for the ENTIRE map)
            for (int k = 0; k < 12; k++)
            {
                var geom = _terrainBatchGeometries[k];
                if (geom != null)
                {
                    var brush = GetSolidBrush(GetTerrainBgColorByType(k));
                    context.DrawGeometry(brush, null, geom);
                }
            }

            // 2. Draw Rivers (Batched into 1 Draw call)
            if (_renderMode != MapRenderMode.Reinforcements && _riverBatchGeometry != null)
            {
                context.DrawGeometry(null, _riverPen, _riverBatchGeometry);
            }

            // 3. Draw Grid Borders (Batched into 1 Draw call)
            if (_borderBatchGeometry != null)
            {
                context.DrawGeometry(null, _borderPen, _borderBatchGeometry);
            }

            // 4. Dynamic Overlay Layer (Selection, City borders, Buildings, Forts, Units, Reinforcements, Coordinates)
            ushort width = _stage.MapTerrain.Size.Width;
            ushort height = _stage.MapTerrain.Size.Height;
            int cols = width;
            int rows = height;
            int cellCount = _cells.Count;

            float hDist = _radius * 1.5f;
            float vDist = (float)Math.Sqrt(3) * _radius;

            if (_hexagonPrototype == null)
            {
                var points = new List<Point>();
                for (int j = 0; j < 6; j++)
                {
                    double angle = j * Math.PI / 3.0;
                    points.Add(new Point(
                        _radius * Math.Cos(angle),
                        _radius * Math.Sin(angle)
                    ));
                }

                var geom = new PathGeometry();
                var figure = new PathFigure { StartPoint = points[0], IsClosed = true };
                for (int j = 1; j < 6; j++)
                {
                    figure.Segments.Add(new LineSegment { Point = points[j] });
                }
                geom.Figures.Add(figure);
                _hexagonPrototype = geom;
            }

            // Viewport Range Slicing (O(1) visible area determination)
            int minCol = 0;
            int maxCol = cols - 1;
            int minRow = 0;
            int maxRow = rows - 1;

            var scrollViewer = Parent as ScrollViewer;
            if (scrollViewer != null)
            {
                double margin = _radius * 2.5; // safety offset to render slightly outside screen
                double viewportMinX = scrollViewer.Offset.X - margin;
                double viewportMaxX = scrollViewer.Offset.X + scrollViewer.Viewport.Width + margin;
                double viewportMinY = scrollViewer.Offset.Y - margin;
                double viewportMaxY = scrollViewer.Offset.Y + scrollViewer.Viewport.Height + margin;

                minCol = Math.Max(0, (int)((viewportMinX - _radius) / hDist));
                maxCol = Math.Min(cols - 1, (int)((viewportMaxX + _radius) / hDist) + 1);
                minRow = Math.Max(0, (int)((viewportMinY - vDist) / vDist));
                maxRow = Math.Min(rows - 1, (int)((viewportMaxY + vDist) / vDist) + 1);
            }

            // LOD (Level of Detail) thresholds
            bool showCoordsLOD = _showCellCoordinates && _radius >= 22;
            bool showDetailsLOD = _radius >= 18;

            for (int r = minRow; r <= maxRow; r++)
            {
                for (int c = minCol; c <= maxCol; c++)
                {
                    int cellIndex = r * cols + c;
                    if (cellIndex < 0 || cellIndex >= cellCount) continue;

                    var cell = _cells[cellIndex];
                    Point center = _cellCenters != null && cellIndex < _cellCenters.Length
                        ? _cellCenters[cellIndex]
                        : new Point(cell.X * hDist + _radius + 10, cell.Y * vDist + (cell.X % 2 == 1 ? vDist / 2f : 0f) + 10);

                    float cx = (float)center.X;
                    float cy = (float)center.Y;

                    // Combined PushTransform for Hex Overlay Geometries (Selection & City/Fort Borders)
                    bool isSelected = _selectedCellIndex == cell.Index;
                    bool hasBldg = cell.TriggerBldg != null;
                    bool hasFort = cell.TriggerFort != null;
                    bool showBldgBorder = (_renderMode == MapRenderMode.All || _renderMode == MapRenderMode.BuildingsOnly) && hasBldg;
                    bool showFortBorder = (_renderMode == MapRenderMode.All || _renderMode == MapRenderMode.FortificationsOnly) && hasFort;

                    // Playable bounds overlay
                    if (_stage?.MapTerrain?.Size != null)
                    {
                        ushort leftM = _stage.MapTerrain.Size.LeftMargin;
                        ushort topM = _stage.MapTerrain.Size.TopMargin;
                        ushort playW = _stage.MapTerrain.Size.PlayableWidth;
                        ushort playH = _stage.MapTerrain.Size.PlayableHeight;

                        if (playW > 0 && playH > 0)
                        {
                            bool isPlayable = cell.X >= leftM && cell.X < leftM + playW &&
                                              cell.Y >= topM && cell.Y < topM + playH;

                            if (!isPlayable)
                            {
                                using (context.PushTransform(Matrix.CreateTranslation(cx, cy)))
                                {
                                    context.DrawGeometry(_nonPlayableMaskBrush, null, _hexagonPrototype);
                                }
                            }
                            else if (cell.X == leftM || cell.X == leftM + playW - 1 || cell.Y == topM || cell.Y == topM + playH - 1)
                            {
                                using (context.PushTransform(Matrix.CreateTranslation(cx, cy)))
                                {
                                    context.DrawGeometry(null, _playableBorderPen, _hexagonPrototype);
                                }
                            }
                        }
                    }

                    if (isSelected || showBldgBorder || showFortBorder)
                    {
                        using (context.PushTransform(Matrix.CreateTranslation(cx, cy)))
                        {
                            if (isSelected)
                            {
                                context.DrawGeometry(null, _selectedPen, _hexagonPrototype);
                            }
                            if (showBldgBorder || showFortBorder)
                            {
                                context.DrawGeometry(null, _cityPen, _hexagonPrototype);
                            }
                        }
                    }

                    // Coordinates Text (Cached + LOD)
                    if (showCoordsLOD)
                    {
                        if (!_coordTextCache.TryGetValue(cell.Index, out var formattedText))
                        {
                            string coordText = $"{cell.X},{cell.Y}";
                            formattedText = new FormattedText(
                                coordText,
                                System.Globalization.CultureInfo.CurrentCulture,
                                FlowDirection.LeftToRight,
                                _segoeTypeface,
                                8,
                                _textBrush
                            );
                            _coordTextCache[cell.Index] = formattedText;
                        }
                        context.DrawText(formattedText, new Point(cx - formattedText.Width / 2, cy + _radius * 0.4f));
                    }

                    // Buildings / Fortifications (Cached + LOD)
                    if (cell.TriggerBldg != null && (_renderMode == MapRenderMode.All || _renderMode == MapRenderMode.BuildingsOnly) && showDetailsLOD)
                    {
                        if (!_bldgTextCache.TryGetValue(cell.Index, out var formattedBText))
                        {
                            var bData = cell.TriggerBldg.DetailBldg.BuildingData;
                            string bName = GameSettings.GetBuildingName(bData.BuildingId ?? 0);
                            string bSymbol = bName.Length > 0 ? bName.Substring(0, Math.Min(3, bName.Length)) : "建";
                            string bText = $"{bSymbol}{bData.Owner}";

                            formattedBText = new FormattedText(
                                bText,
                                System.Globalization.CultureInfo.CurrentCulture,
                                FlowDirection.LeftToRight,
                                _segoeBoldTypeface,
                                9,
                                _textBrush
                            );
                            _bldgTextCache[cell.Index] = formattedBText;
                        }

                        float rectW = (float)formattedBText.Width + 4;
                        float rectH = (float)formattedBText.Height + 2;
                        float rectX = cx - rectW / 2f;
                        float rectY = cy - _radius * 0.5f - rectH / 2f;

                        Color factionBgColor = GetFactionBgColor(cell.TriggerBldg.DetailBldg.BuildingData.Owner ?? 0);
                        var bgBrush = GetSolidBrush(factionBgColor);

                        context.DrawRectangle(bgBrush, _borderPenGray, new Rect(rectX, rectY, rectW, rectH));
                        context.DrawText(formattedBText, new Point(cx - formattedBText.Width / 2, rectY + 1));
                    }

                    if (cell.TriggerFort != null && (_renderMode == MapRenderMode.All || _renderMode == MapRenderMode.FortificationsOnly) && showDetailsLOD)
                    {
                        if (!_fortTextCache.TryGetValue(cell.Index, out var formattedFText))
                        {
                            string fName = GameSettings.GetFortName(cell.TriggerFort.DetailFort.FortId ?? 0);
                            string fSymbol = fName.Length > 0 ? fName.Substring(0, Math.Min(2, fName.Length)) : "工";

                            formattedFText = new FormattedText(
                                fSymbol,
                                System.Globalization.CultureInfo.CurrentCulture,
                                FlowDirection.LeftToRight,
                                _segoeBoldTypeface,
                                8,
                                _textBrush
                            );
                            _fortTextCache[cell.Index] = formattedFText;
                        }

                        float rectW = (float)formattedFText.Width + 4;
                        float rectH = (float)formattedFText.Height + 2;
                        float rectX = cx - rectW / 2f;
                        float rectY = cy - _radius * 0.2f - rectH / 2f;

                        context.DrawRectangle(GetSolidBrush(Color.FromArgb(180, 203, 213, 225)), _borderPenFort, new Rect(rectX, rectY, rectW, rectH));
                        context.DrawText(formattedFText, new Point(cx - formattedFText.Width / 2, rectY + 1));
                    }

                    // Dynamic Units Layer
                    if ((_renderMode == MapRenderMode.All || _renderMode == MapRenderMode.UnitsOnly) && cell.Unit != null)
                    {
                        DrawUnitItemAt(context, cell, cx, cy);
                    }

                    // Dynamic Reinforcements Layer (Indexed Lookup _reinforceByCell)
                    if (_renderMode == MapRenderMode.Reinforcements)
                    {
                        if (_reinforceByCell != null && _reinforceByCell.TryGetValue(cell.Index, out var cellRP))
                        {
                            float rBadge = _radius * 0.45f;
                            var badgeBrush = GetSolidBrush(Color.FromArgb(255, 249, 115, 22));
                            context.DrawEllipse(badgeBrush, _whiteBorderPen, new Point(cx, cy), rBadge, rBadge);

                            if (!_reinforceTextCache.TryGetValue(cell.Index, out var formattedRText))
                            {
                                string rText = $"R{cellRP.FactionId}";
                                formattedRText = new FormattedText(
                                    rText,
                                    System.Globalization.CultureInfo.CurrentCulture,
                                    FlowDirection.LeftToRight,
                                    _segoeBoldTypeface,
                                    8,
                                    _whiteBrush
                                );
                                _reinforceTextCache[cell.Index] = formattedRText;
                            }
                            context.DrawText(formattedRText, new Point(cx - formattedRText.Width / 2, cy - formattedRText.Height / 2));

                            if (Convert.ToBoolean(cellRP.IsKeyUnit))
                            {
                                context.DrawText(_starFormattedText, new Point(cx - rBadge * 1.3f, cy - rBadge * 1.3f));
                            }
                        }
                    }
                }
            }
        }

        private void DrawUnitItemAt(DrawingContext context, CellItem cell, float cx, float cy)
        {
            var agent = cell.Unit;
            if (agent?.AgentInfo == null) return;

            float uRadius = _radius * 0.6f;
            Color factionColor = GetFactionColor(agent.AgentInfo.FactionId ?? 0);
            var factionPen = GetPen(factionColor, 2.5f);

            context.DrawEllipse(_unitBaseBrush, factionPen, new Point(cx, cy), uRadius, uRadius);

            if (!_unitTextCache.TryGetValue(cell.Index, out var formattedText))
            {
                string unitName = GameSettings.GetUnitName(agent.AgentInfo.UnitId ?? 0);
                string idText = unitName.Length > 0 ? unitName.Substring(0, Math.Min(2, unitName.Length)) : "兵";

                formattedText = new FormattedText(
                    idText,
                    System.Globalization.CultureInfo.CurrentCulture,
                    FlowDirection.LeftToRight,
                    _segoeBoldTypeface,
                    9,
                    _whiteBrush
                );
                _unitTextCache[cell.Index] = formattedText;
            }

            context.DrawText(formattedText, new Point(cx - formattedText.Width / 2, cy - formattedText.Height / 2));

            // General Star
            bool hasGeneral = agent.ExtensionData != null && agent.ExtensionData.ContainsKey("extra_table_11");
            if (hasGeneral)
            {
                context.DrawText(_starFormattedText, new Point(cx - uRadius * 1.1f, cy - uRadius * 1.1f));
            }
        }

        private Color GetTerrainBgColorByType(int type, ushort terrain = 0)
        {
            byte low = (byte)(terrain & 0xFF);
            int v7 = low & 7;

            switch (type)
            {
                case 0: return Color.FromArgb(255, 220, 252, 231); // 平原
                case 1: return Color.FromArgb(255, 191, 219, 254); // 海洋
                case 2: return Color.FromArgb(255, 248, 250, 252); // 雪地
                case 3: return Color.FromArgb(255, 254, 240, 138); // 沙漠
                case 4: return Color.FromArgb(255, 163, 230, 53);  // 沼泽
                case 6: return Color.FromArgb(255, 34, 197, 94);   // 森林
                case 7: return Color.FromArgb(255, 253, 186, 116); // 丘陵
                case 8: return Color.FromArgb(255, 156, 163, 175); // 矮山
                case 9: return Color.FromArgb(255, 75, 85, 99);    // 高山
                case 10:
                    if (v7 == 2) return Color.FromArgb(255, 254, 240, 138);
                    if (v7 == 3) return Color.FromArgb(255, 248, 250, 252);
                    return Color.FromArgb(255, 220, 252, 231);
                default: return Color.FromArgb(255, 241, 245, 249);
            }
        }

        private int GetFinalTerrainType(CellItem cell)
        {
            ushort terrain = cell.Terrain;
            byte low = (byte)(terrain & 0xFF);
            byte high = (byte)(terrain >> 8);
            int v7 = low & 7;

            if ((high & 2) != 0) return 1; // 海洋

            byte slot2Val = 0;
            if (cell.AttrA2 != null)
                slot2Val = (byte)cell.AttrA2.Byte0;

            if (slot2Val == 0)
            {
                if (v7 == 2) return 3; // 沙漠
                if (v7 == 3) return 2; // 雪地
                return 0; // 平原
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

        private Color GetTerrainBgColor(CellItem cell)
        {
            int type = GetFinalTerrainType(cell);
            ushort terrain = cell.Terrain;
            byte low = (byte)(terrain & 0xFF);
            int v7 = low & 7;

            switch (type)
            {
                case 0: return Color.FromArgb(255, 220, 252, 231); // 平原
                case 1: return Color.FromArgb(255, 191, 219, 254); // 海洋
                case 2: return Color.FromArgb(255, 248, 250, 252); // 雪地
                case 3: return Color.FromArgb(255, 254, 240, 138); // 沙漠
                case 4: return Color.FromArgb(255, 163, 230, 53);  // 沼泽
                case 6: return Color.FromArgb(255, 34, 197, 94);   // 森林
                case 7: return Color.FromArgb(255, 253, 186, 116); // 丘陵
                case 8: return Color.FromArgb(255, 156, 163, 175); // 矮山
                case 9: return Color.FromArgb(255, 75, 85, 99);    // 高山
                case 10:
                    if (v7 == 2) return Color.FromArgb(255, 254, 240, 138);
                    if (v7 == 3) return Color.FromArgb(255, 248, 250, 252);
                    return Color.FromArgb(255, 220, 252, 231);
                default: return Color.FromArgb(255, 241, 245, 249);
            }
        }

        private bool IsRiver(CellItem cell)
        {
            return GetFinalTerrainType(cell) == 10;
        }

        private Tuple<int, int>[] GetNeighborsRaw(int x, int y)
        {
            var neighbors = new Tuple<int, int>[6];
            neighbors[0] = Tuple.Create(x, y - 1);
            neighbors[1] = Tuple.Create(x, y + 1);

            if (x % 2 == 0)
            {
                neighbors[2] = Tuple.Create(x + 1, y - 1);
                neighbors[3] = Tuple.Create(x + 1, y);
                neighbors[4] = Tuple.Create(x - 1, y - 1);
                neighbors[5] = Tuple.Create(x - 1, y);
            }
            else
            {
                neighbors[2] = Tuple.Create(x + 1, y);
                neighbors[3] = Tuple.Create(x + 1, y + 1);
                neighbors[4] = Tuple.Create(x - 1, y);
                neighbors[5] = Tuple.Create(x - 1, y + 1);
            }
            return neighbors;
        }

        private Color GetFactionColor(int factionId)
        {
            switch (factionId)
            {
                case 1: return Color.FromArgb(255, 239, 68, 68);
                case 2: return Color.FromArgb(255, 59, 130, 246);
                case 3: return Color.FromArgb(255, 16, 185, 129);
                case 4: return Color.FromArgb(255, 245, 158, 11);
                default: return Colors.White;
            }
        }

        private Color GetFactionBgColor(int factionId)
        {
            switch (factionId)
            {
                case 1: return Color.FromArgb(255, 254, 242, 242);
                case 2: return Color.FromArgb(255, 239, 246, 255);
                case 3: return Color.FromArgb(255, 240, 253, 244);
                case 4: return Color.FromArgb(255, 254, 243, 199);
                default: return Color.FromArgb(255, 248, 250, 252);
            }
        }

        private bool _isPointerDown = false;
        private int _lastPaintedIndex = -1;

        protected override void OnPointerPressed(PointerPressedEventArgs e)
        {
            base.OnPointerPressed(e);
            var point = e.GetCurrentPoint(this);
            if (point.Properties.IsLeftButtonPressed || e.Pointer.Type == PointerType.Touch)
            {
                _isPointerDown = true;
                e.Pointer.Capture(this);
                HandlePointerAction(point.Position);
                if (BrushMode)
                {
                    e.Handled = true;
                }
            }
        }

        protected override void OnPointerMoved(PointerEventArgs e)
        {
            base.OnPointerMoved(e);
            var point = e.GetCurrentPoint(this);
            if (_isPointerDown && (point.Properties.IsLeftButtonPressed || e.Pointer.Type == PointerType.Touch))
            {
                HandlePointerAction(point.Position);
                if (BrushMode)
                {
                    e.Handled = true;
                }
            }
        }

        protected override void OnPointerReleased(PointerReleasedEventArgs e)
        {
            base.OnPointerReleased(e);
            _isPointerDown = false;
            _lastPaintedIndex = -1;
            e.Pointer.Capture(null);
            if (BrushMode)
            {
                e.Handled = true;
            }
        }

        protected override void OnPointerCaptureLost(PointerCaptureLostEventArgs e)
        {
            base.OnPointerCaptureLost(e);
            _isPointerDown = false;
            _lastPaintedIndex = -1;
        }

        private int FastGetCellIndexAt(Point clickPos)
        {
            if (_stage?.MapTerrain?.Size == null || _cells.Count == 0) return -1;

            ushort width = _stage.MapTerrain.Size.Width;
            ushort height = _stage.MapTerrain.Size.Height;

            float hDist = _radius * 1.5f;
            float vDist = (float)Math.Sqrt(3) * _radius;

            float clickX = (float)clickPos.X;
            float clickY = (float)clickPos.Y;

            int approxCol = (int)Math.Round((clickX - _radius - 10) / hDist);
            if (approxCol < 0 || approxCol >= width) return -1;

            float cyBase = clickY - 10 - (approxCol % 2 == 1 ? vDist / 2f : 0f);
            int approxRow = (int)Math.Round(cyBase / vDist);
            if (approxRow < 0 || approxRow >= height) return -1;

            int centerIndex = approxRow * width + approxCol;

            int closestIndex = centerIndex;
            float minDistSq = GetDistSq(clickX, clickY, centerIndex, width, hDist, vDist);

            if (_cellNeighbors != null && centerIndex >= 0 && centerIndex < _cellNeighbors.Length)
            {
                var neighbors = _cellNeighbors[centerIndex];
                if (neighbors != null)
                {
                    foreach (int nIndex in neighbors)
                    {
                        if (nIndex >= 0 && nIndex < _cells.Count)
                        {
                            float dSq = GetDistSq(clickX, clickY, nIndex, width, hDist, vDist);
                            if (dSq < minDistSq)
                            {
                                minDistSq = dSq;
                                closestIndex = nIndex;
                            }
                        }
                    }
                }
            }

            return closestIndex;
        }

        private float GetDistSq(float clickX, float clickY, int cellIndex, ushort width, float hDist, float vDist)
        {
            if (_cellCenters != null && cellIndex >= 0 && cellIndex < _cellCenters.Length)
            {
                Point center = _cellCenters[cellIndex];
                float dxCenter = clickX - (float)center.X;
                float dyCenter = clickY - (float)center.Y;
                return dxCenter * dxCenter + dyCenter * dyCenter;
            }
            int cellX = cellIndex % width;
            int cellY = cellIndex / width;
            float cx = cellX * hDist + _radius + 10;
            float cy = cellY * vDist + (cellX % 2 == 1 ? vDist / 2f : 0f) + 10;
            float dx = clickX - cx;
            float dy = clickY - cy;
            return dx * dx + dy * dy;
        }

        private void HandlePointerAction(Point clickPos)
        {
            if (_stage?.MapTerrain?.Size == null) return;

            int closestIndex = FastGetCellIndexAt(clickPos);

            if (closestIndex != -1)
            {
                ushort width = _stage.MapTerrain.Size.Width;
                float hDist = _radius * 1.5f;
                float vDist = (float)Math.Sqrt(3) * _radius;
                float minDistSq = GetDistSq((float)clickPos.X, (float)clickPos.Y, closestIndex, width, hDist, vDist);

                bool insideCell = minDistSq < _radius * _radius * 0.9f;
                if (BrushMode)
                {
                    if (minDistSq < _radius * _radius * 1.5f && closestIndex != _lastPaintedIndex)
                    {
                        _lastPaintedIndex = closestIndex;
                        SelectedCellIndex = closestIndex;
                        PaintCellRequested?.Invoke(closestIndex);
                    }
                }
                else
                {
                    if (insideCell && _selectedCellIndex != closestIndex)
                    {
                        SelectedCellIndex = closestIndex;
                        CellSelected?.Invoke(closestIndex);
                    }
                }
            }
        }
    }
}
