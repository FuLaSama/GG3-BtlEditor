/*
 * TerrainPalettePanel.cs
 *
 * 左侧分类贴图栏：缩略图单击选中，按住拖到画布。
 * 数据来自 EditorPaletteCatalog（catalog.json），不写 BTL。
 * 拖放的是 PaletteItem（地形 id / 建筑 id），落点由 EditorMapCanvas 转成改格子。
 */
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Reflection;
using System.Windows.Forms;

namespace BtldMapEditor
{
    public sealed class TerrainPalettePanel : Panel
    {
        PaletteTarget _target;
        readonly int _thumbSize;
        readonly FlowLayoutPanel _host;
        readonly Dictionary<PaletteItem, PaletteTile> _tiles = new Dictionary<PaletteItem, PaletteTile>();
        PaletteItem _selected;
        Point _dragStart;
        bool _dragging;
        int _rowWidth = -1;

        public event Action<PaletteItem, PaletteTarget> ItemSelected;
        public event Action<PaletteItem, PaletteTarget> ItemActivated;
        public event Action ClearLayerRequested;

        public PaletteTarget Target
        {
            get => _target;
            set => _target = value;
        }
        public PaletteItem SelectedItem => _selected;

        public TerrainPalettePanel(PaletteTarget target, IReadOnlyList<PaletteItem> items, int thumbSize, bool showClear)
        {
            _target = target;
            _thumbSize = thumbSize;
            DoubleBuffered = true;
            SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint, true);
            Dock = DockStyle.Fill;
            BackColor = Color.FromArgb(248, 250, 252);

            _host = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                AutoScroll = true,
                WrapContents = false,
                FlowDirection = FlowDirection.TopDown,
                Padding = new Padding(6),
                BackColor = Color.FromArgb(248, 250, 252)
            };
            EnableDoubleBuffer(_host);
            _host.Resize += (s, e) => RelayoutRows();
            Controls.Add(_host);

            if (showClear)
            {
                var clear = new LinkLabel
                {
                    Text = target == PaletteTarget.Climate ? "清除海洋" : "清除此层",
                    AutoSize = true,
                    Margin = new Padding(4, 2, 8, 8),
                    LinkColor = Color.FromArgb(71, 85, 105)
                };
                clear.Click += (s, e) => ClearLayerRequested?.Invoke();
                _host.Controls.Add(clear);
                _host.SetFlowBreak(clear, true);
            }

            if (items == null || items.Count == 0)
            {
                _host.Controls.Add(new Label
                {
                    Text = "未找到编辑器贴图。需要 GameData/EditorPalette/catalog.json",
                    AutoSize = true,
                    ForeColor = Color.FromArgb(180, 50, 50),
                    Margin = new Padding(4)
                });
                return;
            }

            string lastGroup = null;
            FlowLayoutPanel rowPanel = null;
            foreach (var item in items)
            {
                string group = item.GroupName;
                if (rowPanel == null || group != lastGroup)
                {
                    lastGroup = group;
                    var header = new Label
                    {
                        Text = group,
                        AutoSize = true,
                        Font = new Font("Segoe UI", 8.5f, FontStyle.Bold),
                        ForeColor = Color.FromArgb(51, 65, 85),
                        Margin = new Padding(4, 10, 4, 2)
                    };
                    _host.Controls.Add(header);
                    _host.SetFlowBreak(header, true);
                    rowPanel = MakeRow();
                    _host.Controls.Add(rowPanel);
                }
                AddTile(rowPanel, item, showCaption: false);
            }
            RelayoutRows();
        }

        static void EnableDoubleBuffer(Control c)
        {
            typeof(Control).GetProperty("DoubleBuffered", BindingFlags.Instance | BindingFlags.NonPublic)
                ?.SetValue(c, true);
        }

        FlowLayoutPanel MakeRow()
        {
            var row = new FlowLayoutPanel
            {
                AutoSize = true,
                WrapContents = true,
                Margin = new Padding(0, 0, 0, 4),
                Padding = new Padding(0),
                Width = Math.Max(120, _host.ClientSize.Width - 24)
            };
            EnableDoubleBuffer(row);
            return row;
        }

        void RelayoutRows()
        {
            int w = Math.Max(120, _host.ClientSize.Width - 24);
            if (w == _rowWidth) return;
            _rowWidth = w;
            _host.SuspendLayout();
            foreach (Control c in _host.Controls)
            {
                if (c is FlowLayoutPanel row)
                    row.Width = w;
            }
            _host.ResumeLayout(true);
        }

        void AddTile(FlowLayoutPanel row, PaletteItem item, bool showCaption)
        {
            var tile = new PaletteTile(item, EditorPaletteCatalog.LoadThumb(item, _thumbSize), _thumbSize, showCaption);
            tile.MouseDown += TileMouseDown;
            tile.MouseMove += TileMouseMove;
            tile.MouseUp += TileMouseUp;
            tile.DoubleClick += (s, e) => ItemActivated?.Invoke(item, _target);
            row.Controls.Add(tile);
            _tiles[item] = tile;
        }

        void TileMouseDown(object sender, MouseEventArgs e)
        {
            if (e.Button != MouseButtons.Left) return;
            var tile = (PaletteTile)sender;
            SelectItem(tile.Item, notify: true);
            _dragStart = e.Location;
            _dragging = false;
        }

        void TileMouseMove(object sender, MouseEventArgs e)
        {
            if (e.Button != MouseButtons.Left || _selected == null) return;
            if (_dragging) return;
            if (Math.Abs(e.X - _dragStart.X) < 4 && Math.Abs(e.Y - _dragStart.Y) < 4)
                return;
            _dragging = true;
            DoDragDrop(new PaletteDrag { Item = _selected, Target = _target }, DragDropEffects.Copy);
            _dragging = false;
        }

        void TileMouseUp(object sender, MouseEventArgs e)
        {
            _dragging = false;
        }

        public void SelectItem(PaletteItem item, bool notify)
        {
            if (_selected == item)
            {
                if (notify && item != null)
                    ItemSelected?.Invoke(item, _target);
                return;
            }
            _selected = item;
            foreach (var kv in _tiles)
                kv.Value.Selected = kv.Key == item;
            if (notify && item != null)
                ItemSelected?.Invoke(item, _target);
        }

        public void HighlightMatch(Func<PaletteItem, bool> match)
        {
            foreach (var kv in _tiles)
                kv.Value.OnMap = match != null && match(kv.Key);
        }

        sealed class PaletteTile : Panel
        {
            readonly Label _caption;
            public PaletteItem Item { get; }
            bool _selected;
            bool _onMap;

            public bool Selected
            {
                get => _selected;
                set
                {
                    if (_selected == value) return;
                    _selected = value;
                    Invalidate();
                }
            }

            public bool OnMap
            {
                get => _onMap;
                set
                {
                    if (_onMap == value) return;
                    _onMap = value;
                    Invalidate();
                }
            }

            public PaletteTile(PaletteItem item, Image thumb, int size, bool showCaption)
            {
                Item = item;
                Width = size + 8;
                Height = showCaption ? size + 28 : size + 8;
                Margin = new Padding(3);
                Cursor = Cursors.Hand;
                DoubleBuffered = true;
                SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint, true);
                BackgroundImage = thumb;
                BackgroundImageLayout = ImageLayout.Center;
                BackColor = Color.White;
                var tip = new ToolTip();
                tip.SetToolTip(this, item.TooltipText);

                if (showCaption)
                {
                    _caption = new Label
                    {
                        Text = item.Label ?? "",
                        Dock = DockStyle.Bottom,
                        Height = 18,
                        TextAlign = ContentAlignment.MiddleCenter,
                        Font = new Font("Segoe UI", 7.5f),
                        ForeColor = Color.FromArgb(51, 65, 85)
                    };
                    Controls.Add(_caption);
                }
            }

            protected override void OnPaint(PaintEventArgs e)
            {
                base.OnPaint(e);
                Color border = _selected
                    ? Color.FromArgb(13, 148, 136)
                    : _onMap
                        ? Color.FromArgb(59, 130, 246)
                        : Color.FromArgb(203, 213, 225);
                int w = _selected ? 2 : 1;
                using var pen = new Pen(border, w);
                e.Graphics.DrawRectangle(pen, w / 2, w / 2, Width - w - 1, Height - w - 1);
            }
        }
    }
}
