using System.Text.Json;
using BtlCore.Fb;
using BtlCore.Front;
using BtlCore.Scripting;
using BtldMapEditor.Front;

namespace BtldMapEditor
{
    // WinForms 只负责窗口、菜单和地图容器；编辑页全部由 EditorLayout XML 创建。
    public partial class MainEditorForm : Form
    {
        BtlFrontDocument Doc => _front?.Document;
        bool HasDoc => Doc?.Root != null;
        FrontSession _front;
        string _loadedFilePath;
        int _selectedCellIdx = -1;
        readonly List<BtlFrontDocument> _history = new();
        readonly List<long> _historySizes = new();
        long _historyBytes;
        const long HistoryBudget = 128L * 1024 * 1024;
        bool _openingFile;
        List<MapCell> _preparedCells;
        BtlFrontDocument _preparedHistory;
        long _preparedHistoryBytes;
        int _historyIndex = -1;
        bool _isUndoRedoAction, _isInitialLoading;
        readonly bool _strictLayout;
        MenuStrip menuStrip;
        ToolStripMenuItem fileMenu, newMapItem, openItem, exportItem, exitItem;
        StatusStrip statusStrip;
        ToolStripStatusLabel statusLabel;
        SplitContainer splitMain;
        EditorMapCanvas mapCanvas;
        TabControl tabControlLeft, tabControlRight;
        FrontRegistryViewer fbRegistryViewer;
        JsonViewer txtJsonEditor;
        bool _registryEdited, _suppressRegistryHistory;
        ComboBox cbRenderMode, cbVisualStyle;
        CheckBox chkBrushMode;
        PaletteDrag _terrainBrush;
        bool _terrainOffsetDragging;
        ushort? _copiedTerrain;
        BtlStruct _copiedAttr, _copiedAttrA2, _copiedAttrA3;

        public MainEditorForm() : this(false) { }

        public MainEditorForm(bool strictLayout)
        {
            _strictLayout = strictLayout;
            Text = "BTLD 关卡与地图逻辑编辑器 - GG3 Mod Tools";
            Size = new Size(1280, 800);
            MinimumSize = new Size(1050, 650);
            StartPosition = FormStartPosition.CenterScreen;
            InitializeComponent();
            UpdateSaveButtonState();
        }

        private void InitializeComponent()

        {

            // 菜单栏

            menuStrip = new MenuStrip();

            fileMenu = new ToolStripMenuItem("文件");

            newMapItem = new ToolStripMenuItem("新建关卡地图...", null, NewMapClick);

            openItem = new ToolStripMenuItem("打开...", null, OpenFileClick);

            exportItem = new ToolStripMenuItem("导出为...", null, ExportAsClick);

            exitItem = new ToolStripMenuItem("退出", null, (s, e) => Close());

            fileMenu.DropDownItems.Add(newMapItem);
            fileMenu.DropDownItems.Add(new ToolStripSeparator());
            fileMenu.DropDownItems.Add(openItem);
            fileMenu.DropDownItems.Add(exportItem);
            fileMenu.DropDownItems.Add(new ToolStripSeparator());
            fileMenu.DropDownItems.Add(new ToolStripMenuItem("重新加载布局", null, ReloadLayoutClick));
            fileMenu.DropDownItems.Add(new ToolStripSeparator());
            fileMenu.DropDownItems.Add(exitItem);

            menuStrip.Items.Add(fileMenu);

            Controls.Add(menuStrip);

            // 状态栏

            statusStrip = new StatusStrip();

            statusLabel = new ToolStripStatusLabel("未加载文件");

            statusStrip.Items.Add(statusLabel);

            Controls.Add(statusStrip);

            // 分割视口

            splitMain = new SplitContainer();

            splitMain.Dock = DockStyle.Fill;

            splitMain.SplitterWidth = 5;

            splitMain.SplitterDistance = 800;

            Controls.Add(splitMain);

            splitMain.Size = ClientSize;
            splitMain.FixedPanel = FixedPanel.Panel2;
            splitMain.Panel1MinSize = 300;
            splitMain.Panel2MinSize = 460;
            splitMain.SplitterDistance = Math.Max(300, ClientSize.Width - 500);

            splitMain.BringToFront();

            menuStrip.SendToBack();

            statusStrip.SendToBack();

            // 左侧 Tab 容器

            tabControlLeft = new TabControl();

            tabControlLeft.Dock = DockStyle.Fill;

            splitMain.Panel1.Controls.Add(tabControlLeft);

            // Tab 1: 六边形地图模式

            TabPage tabMapMode = new TabPage("六边形地图模式");

            tabMapMode.Padding = new Padding(3);

            tabControlLeft.TabPages.Add(tabMapMode);

            Panel pnlLeft = new Panel();

            pnlLeft.Dock = DockStyle.Fill;

            tabMapMode.Controls.Add(pnlLeft);

            // 工具栏

            FlowLayoutPanel pnlToolbar = new FlowLayoutPanel

            {

                Dock = DockStyle.Top,

                Height = 35,

                BackColor = Color.FromArgb(226, 232, 240),

                FlowDirection = FlowDirection.LeftToRight,

                WrapContents = true,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,

                Padding = new Padding(10, 4, 10, 4)

            };

            pnlLeft.Controls.Add(pnlToolbar);

            Label lblRenderMode = new Label { Text = "图层:", AutoSize = true, Margin = new Padding(0, 6, 5, 0) };

            cbRenderMode = new ComboBox { Width = 140, DropDownStyle = ComboBoxStyle.DropDownList, Margin = new Padding(0, 2, 12, 0) };

            cbRenderMode.Items.AddRange(Enum.GetNames(typeof(MapRenderMode)));

            cbRenderMode.SelectedIndex = 0;

            cbRenderMode.SelectedIndexChanged += (s, e) =>

            {

                if (mapCanvas != null)

                {

                    mapCanvas.RenderMode = (MapRenderMode)Enum.Parse(typeof(MapRenderMode), cbRenderMode.SelectedItem.ToString());
                    mapCanvas.Focus();
                }

            };

            Label lblVisualStyle = new Label { Text = "外观:", AutoSize = true, Margin = new Padding(0, 6, 5, 0) };
            cbVisualStyle = new ComboBox { Width = 110, DropDownStyle = ComboBoxStyle.DropDownList, Margin = new Padding(0, 2, 15, 0) };
            cbVisualStyle.Items.Add("简笔六边形");
            cbVisualStyle.Items.Add("游戏贴图");
            cbVisualStyle.SelectedIndex = 0;
            cbVisualStyle.SelectedIndexChanged += (s, e) =>
            {
                if (mapCanvas == null) return;
                mapCanvas.VisualStyle = cbVisualStyle.SelectedIndex == 1 ? MapVisualStyle.Sprite : MapVisualStyle.Sketch;
                if (mapCanvas.VisualStyle == MapVisualStyle.Sprite && !string.IsNullOrEmpty(mapCanvas.SpriteLoadError))
                    statusLabel.Text = "贴图模式：" + mapCanvas.SpriteLoadError;
                mapCanvas.Focus();
            };

            CheckBox chkShowCoords = new CheckBox { Text = "显示地块坐标", AutoSize = true, Checked = true, Margin = new Padding(0, 4, 15, 0) };

            chkShowCoords.CheckedChanged += (s, e) =>

            {

                if (mapCanvas != null)

                {

                    mapCanvas.ShowCellCoordinates = chkShowCoords.Checked;

                }

            };

            CheckBox chkShowOffsets = new CheckBox { Text = "显示贴图偏移", AutoSize = true, Checked = true, Margin = new Padding(0, 4, 15, 0) };

            chkShowOffsets.CheckedChanged += (s, e) =>

            {

                if (mapCanvas != null)

                {

                    mapCanvas.ShowCellOffsets = chkShowOffsets.Checked;

                }

            };

            chkBrushMode = new CheckBox { Text = "画笔工具 (B)", AutoSize = true, Checked = false, Margin = new Padding(0, 4, 15, 0) };

            chkBrushMode.CheckedChanged += (s, e) =>

            {

                if (mapCanvas != null)

                {

                    mapCanvas.BrushMode = chkBrushMode.Checked;

                    statusLabel.Text = chkBrushMode.Checked ? "画笔工具已激活：长按左键在地图上拖动即可涂色覆盖（按Ctrl+C复制完整数据）" : "画笔工具已关闭";

                }

            };

            pnlToolbar.Controls.Add(lblRenderMode);

            pnlToolbar.Controls.Add(cbRenderMode);

            pnlToolbar.Controls.Add(lblVisualStyle);
            pnlToolbar.Controls.Add(cbVisualStyle);

            pnlToolbar.Controls.Add(chkShowCoords);

            pnlToolbar.Controls.Add(chkShowOffsets);

            pnlToolbar.Controls.Add(chkBrushMode);

            // 画布

            mapCanvas = new EditorMapCanvas();

            mapCanvas.Dock = DockStyle.Fill;

            mapCanvas.CellSelected += idx =>
            {
                ClearTerrainBrush();
                CellSelectedClick(idx);
            };

            mapCanvas.CellDoubleClicked += CellDoubleClickedClick;

            mapCanvas.PaintCellRequested += PaintCell;
            mapCanvas.MapMouseDown += MapCanvasMouseDown;
            mapCanvas.MapMouseMove += MapCanvasMouseMove;
            mapCanvas.MapMouseUp += MapCanvasMouseUp;
            mapCanvas.AllowDrop = true;
            mapCanvas.DragEnter += MapCanvasPaletteDragEnter;
            mapCanvas.DragOver += MapCanvasPaletteDragOver;
            mapCanvas.DragDrop += MapCanvasPaletteDragDrop;
            mapCanvas.DragLeave += (s, e) => { mapCanvas.DropPreviewIndex = -1; };
            mapCanvas.MouseUp += (s, e) => {
                if (mapCanvas.BrushMode)
                {
                    AddHistoryState();
                }
            };

            pnlLeft.Controls.Add(mapCanvas);

            mapCanvas.BringToFront();

            // Tab 2: JSON 展示模式 (代码保留，默认隐藏)
            TabPage tabJsonMode = new TabPage("JSON 展示模式");
            tabJsonMode.Padding = new Padding(5);
            // tabControlLeft.TabPages.Add(tabJsonMode); // 隐藏 JSON 展示模式 Tab

            txtJsonEditor = new JsonViewer
            {
                Dock = DockStyle.Fill,
                ReadOnly = true,
                ScrollBars = RichTextBoxScrollBars.Both,
                Font = new Font("Consolas", 10f),
                WordWrap = false,
                HideSelection = false
            };
            tabJsonMode.Controls.Add(txtJsonEditor);

            // Tab 2: 注册表模式（直接钻探 BtlFront Document，不经 AST / .btl）
            TabPage tabFbRegistryMode = new TabPage("注册表模式");
            tabFbRegistryMode.Padding = new Padding(2);
            fbRegistryViewer = new FrontRegistryViewer { Dock = DockStyle.Fill };
            fbRegistryViewer.DataChanged += (s, e) =>
            {
                if (_front == null) return;
                _registryEdited = true;
                _suppressRegistryHistory = true;
                try
                {
                    _front.ReplaceDocument(_front.Document);
                    OnDocumentLoaded();
                }
                finally
                {
                    _suppressRegistryHistory = false;
                }
                AddHistoryState();
            };
            fbRegistryViewer.UndoRequested += () => PerformUndoAction();
            fbRegistryViewer.RedoRequested += () => PerformRedoAction();
            tabFbRegistryMode.Controls.Add(fbRegistryViewer);
            tabControlLeft.TabPages.Add(tabFbRegistryMode);

            TabPage _lastSelectedTab = null;

            // 提前拦截 Tab 切页事件（在原生控件擦除背景画板前冻结屏幕，防止切页白屏/闪烁）
            tabControlLeft.Selecting += (s, e) =>
            {
                SuspendDrawing(this);
            };

            // 左侧 Tab：地图 ↔ 注册表 双向同步（Document 为真相源）
            tabControlLeft.SelectedIndexChanged += (s, e) =>
            {
                try
                {
                    // 1. 从注册表切出：提交内联编辑；若改过则再投影一次（DataChanged 多半已刷过）
                    if (_lastSelectedTab == tabFbRegistryMode && tabControlLeft.SelectedTab != tabFbRegistryMode)
                    {
                        if (fbRegistryViewer != null)
                        {
                            fbRegistryViewer.CommitInlineEdit();
                            if (_registryEdited && _front != null)
                            {
                                OnDocumentLoaded();
                            }
                            _registryEdited = false;
                        }
                    }

                    // 2. 切入注册表：先把地图改动 Merge 进 Document，再绑树
                    if (tabControlLeft.SelectedTab == tabFbRegistryMode)
                    {
                        splitMain.Panel2Collapsed = true;
                        FrontNav.SyncGrid(Doc, mapCanvas.Cells);
                        if (_front?.Document != null)
                            fbRegistryViewer.LoadDocument(_front.Document, collapseAll: true);
                        _registryEdited = false;
                    }
                    else
                    {
                        splitMain.Panel2Collapsed = false;
                        if (tabControlLeft.SelectedTab == tabJsonMode)
                        {
                            RefreshJsonEditor();
                        }
                    }
                }
                finally
                {
                    _lastSelectedTab = tabControlLeft.SelectedTab;
                    ResumeDrawing(this);
                }
            };

            tabControlRight = new TabControl { Dock = DockStyle.Fill, Multiline = true };
            tabControlRight.SelectedIndexChanged += (_, _) => RefreshActiveEditorPage();
            splitMain.Panel2.Controls.Add(tabControlRight);
            InstallEditorLayout();
        }

        private async void OpenFileClick(object sender, EventArgs e)
        {
            using (var ofd = new OpenFileDialog
            {
                Title = "打开关卡",
                Filter =
                    "关卡文件 (*.btl;*.json)|*.btl;*.json|" +
                    "BTL (*.btl)|*.btl|" +
                    "JSON (*.json)|*.json|" +
                    "所有文件 (*.*)|*.*"
            })
            {
                if (ofd.ShowDialog() != DialogResult.OK) return;
                await OpenPath(ofd.FileName);
            }
        }

        async Task OpenPath(string path)
        {
            if (_openingFile) return;
            _openingFile = true;
            try
            {
                UseWaitCursor = true;
                menuStrip.Enabled = false;
                splitMain.Enabled = false;
                statusLabel.Text = "正在读取关卡并准备地图：" + Path.GetFileName(path);
                var loaded = await Task.Run(() =>
                {
                    var session = IsBtlFile(path) ? FrontSession.FromBtl(path)
                        : IsJsonFile(path) ? FrontSession.FromJsonFile(path)
                        : throw new InvalidDataException("无法识别后缀。请使用 .btl 或 .json");
                    var cells = FrontNav.RebuildCells(session.Document);
                    long allocated = GC.GetAllocatedBytesForCurrentThread();
                    var history = BtlFrontJson.CloneDocument(session.Document);
                    return (Session: session, Cells: cells, History: history,
                        HistoryBytes: GC.GetAllocatedBytesForCurrentThread() - allocated);
                });
                if (IsDisposed) return;
                _front = loaded.Session;
                _preparedCells = loaded.Cells;
                _preparedHistory = loaded.History;
                _preparedHistoryBytes = loaded.HistoryBytes;

                _loadedFilePath = path;
                statusLabel.Text = $"已打开 ({_front.OpenedKind}): {Path.GetFileName(path)}";
                _isInitialLoading = true;
                try
                {
                    OnDocumentLoaded();
                    InitHistory();
                }
                finally
                {
                    _isInitialLoading = false;
                }
            }
            catch (Exception ex)
            {
                if (!IsDisposed) MessageBox.Show($"打开失败:\n{ex.Message}", "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            finally
            {
                _openingFile = false;
                _preparedCells = null;
                _preparedHistory = null;
                if (!IsDisposed)
                {
                    UseWaitCursor = false;
                    menuStrip.Enabled = true;
                    splitMain.Enabled = true;
                }
            }
        }

        static bool IsBtlFile(string path) =>
            !string.IsNullOrEmpty(path)
            && path.EndsWith(".btl", StringComparison.OrdinalIgnoreCase)
            && !path.EndsWith(".btlf", StringComparison.OrdinalIgnoreCase);

        static bool IsJsonFile(string path)
        {
            if (string.IsNullOrEmpty(path)) return false;
            return path.EndsWith(".json", StringComparison.OrdinalIgnoreCase);
        }

        void ApplyBtlFile(string path)
        {
            _front = FrontSession.FromBtl(path);
        }

        void ApplyJsonFile(string path)
        {
            _front = FrontSession.FromJsonFile(path);
        }

        void ApplyFrontJson(string json)
        {
            _front = FrontSession.FromJson(json);
        }

        string SnapshotFrontJson()
        {
            if (!HasDoc) return null;
            FrontNav.SyncGrid(Doc, mapCanvas.Cells);
            return _front.ToJson();
        }

        void SaveBtlFile(string path)
        {
            if (!HasDoc)
                throw new InvalidOperationException("没有可导出的关卡数据");
            FrontNav.SyncGrid(Doc, mapCanvas.Cells);
            _front.SaveBtl(path);
        }

        private void UpdateSaveButtonState()
        {
            if (exportItem != null)
                exportItem.Enabled = HasDoc;
            if (_toolsMenu != null)
                foreach (ToolStripItem item in _toolsMenu.DropDownItems) item.Enabled = HasDoc && item.Tag is ScriptTool;
        }

        void RefreshRegistryViewIfVisible()
        {
            if (fbRegistryViewer == null || _front?.Document == null) return;
            // 注册表内联编辑触发的 Reproject：树已就地更新，不要整树重建
            if (_suppressRegistryHistory) return;
            if (tabControlLeft?.SelectedTab?.Text != "注册表模式")
            {
                fbRegistryViewer.NotifyExternalDataChanged();
                return;
            }
            fbRegistryViewer.LoadDocument(_front.Document, collapseAll: false);
        }

        private void InitHistory()
        {
            _history.Clear();
            _historySizes.Clear();
            _historyBytes = 0;
            if (HasDoc)
            {
                StoreHistory(_preparedHistory);
                _preparedHistory = null;
            }
            _historyIndex = 0;
        }

        private void AddHistoryState()
        {
            if (!HasDoc) return;
            mapCanvas.NotifyContentChanged();
            FrontNav.SyncGrid(Doc, mapCanvas.Cells);

            // Prevent duplicate adjacent states
            if (_historyIndex >= 0 && _historyIndex < _history.Count && BtlFrontJson.ContentEquals(_history[_historyIndex], Doc))
            {
                return;
            }

            // Remove any redo states
            if (_historyIndex < _history.Count - 1)
            {
                for (int i = _historyIndex + 1; i < _historySizes.Count; i++) _historyBytes -= _historySizes[i];
                _historySizes.RemoveRange(_historyIndex + 1, _historySizes.Count - _historyIndex - 1);
                _history.RemoveRange(_historyIndex + 1, _history.Count - (_historyIndex + 1));
            }

            StoreHistory();
            _historyIndex = _history.Count - 1;

            // 同时限制步骤与快照分配量，至少保留当前和上一帧。
            while (_history.Count > 100 || _history.Count > 2 && _historyBytes > HistoryBudget)
            {
                _historyBytes -= _historySizes[0];
                _historySizes.RemoveAt(0);
                _history.RemoveAt(0);
                _historyIndex--;
            }
        }

        void StoreHistory(BtlFrontDocument prepared = null)
        {
            long start = GC.GetAllocatedBytesForCurrentThread();
            var snapshot = prepared ?? BtlFrontJson.CloneDocument(Doc);
            long bytes = prepared != null ? _preparedHistoryBytes : GC.GetAllocatedBytesForCurrentThread() - start;
            _history.Add(snapshot);
            _historySizes.Add(bytes);
            _historyBytes += bytes;
        }

        private void LoadHistoryState(BtlFrontDocument snapshot)
        {
            try
            {
                _isUndoRedoAction = true;

                _front = FrontSession.FromDocument(BtlFrontJson.CloneDocument(snapshot));
                OnDocumentLoaded();
            }
            finally
            {
                _isUndoRedoAction = false;

            }
        }

        private void PerformUndoAction()
        {
            if (!HasDoc) return;
            if (_historyIndex <= 0)
            {
                statusLabel.Text = "撤销失败：已到达历史记录最前";
                return;
            }

            _historyIndex--;
            LoadHistoryState(_history[_historyIndex]);
            statusLabel.Text = $"撤销成功：已回退到步骤 {_historyIndex + 1}/{_history.Count}";
        }

        private void PerformRedoAction()
        {
            if (!HasDoc) return;
            if (_historyIndex >= _history.Count - 1)
            {
                statusLabel.Text = "重做失败：已到达历史记录最后";
                return;
            }

            _historyIndex++;
            LoadHistoryState(_history[_historyIndex]);
            statusLabel.Text = $"重做成功：已前进到步骤 {_historyIndex + 1}/{_history.Count}";
        }

        private void ExportAsClick(object sender, EventArgs e)
        {
            if (!HasDoc) return;

            using (var sfd = new SaveFileDialog
            {
                Title = "导出为",
                FileName = SuggestExportFileName(),
                Filter =
                    "BTL 关卡 (*.btl)|*.btl|" +
                    "JSON 字段名 (*.json)|*.json|" +
                    "JSON 原始字段ID (*.json)|*.json",
                FilterIndex = 1
            })
            {
                if (sfd.ShowDialog() != DialogResult.OK) return;

                try
                {
                    Cursor = Cursors.WaitCursor;
                    FrontNav.SyncGrid(Doc, mapCanvas.Cells);

                    string outPath = sfd.FileName;
                    int fmt = sfd.FilterIndex;
                    if (IsBtlFile(outPath)) fmt = 1;

                    switch (fmt)
                    {
                        case 1:
                            if (!IsBtlFile(outPath))
                                outPath += ".btl";
                            SaveBtlFile(outPath);
                            break;
                        case 2:
                            if (!outPath.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
                                outPath += ".json";
                            ExportStageJson(outPath, rawFieldIds: false);
                            break;
                        case 3:
                            if (!outPath.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
                                outPath += ".json";
                            ExportStageJson(outPath, rawFieldIds: true);
                            break;
                        default:
                            SaveBtlFile(outPath);
                            break;
                    }

                    statusLabel.Text = $"已导出: {Path.GetFileName(outPath)}";
                    MessageBox.Show("导出成功！", "成功", MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"导出失败:\n{ex.Message}", "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
                finally
                {
                    Cursor = Cursors.Default;
                }
            }
        }

        string SuggestExportFileName()
        {
            if (!string.IsNullOrEmpty(_loadedFilePath))
                return Path.GetFileNameWithoutExtension(_loadedFilePath);
            return "stage";
        }

        string ResolveFbsPath(string exportPath)
        {
            var dirs = new List<string>();
            if (!string.IsNullOrEmpty(_loadedFilePath))
                dirs.Add(Path.GetDirectoryName(_loadedFilePath));
            if (!string.IsNullOrEmpty(exportPath))
                dirs.Add(Path.GetDirectoryName(exportPath));
            dirs.Add(Directory.GetCurrentDirectory());
            dirs.Add(AppDomain.CurrentDomain.BaseDirectory);

            foreach (string dir in dirs.Where(d => !string.IsNullOrEmpty(d)).Distinct(StringComparer.OrdinalIgnoreCase))
            {
                string found = BtlCore.Fb.FbsSchema.FindInDirectory(dir);
                if (found != null) return found;
            }

            using (var ofd = new OpenFileDialog
            {
                Title = "选择用于字段名的 .fbs（例如 battle.fbs）",
                Filter = "FlatBuffers Schema (*.fbs)|*.fbs|所有文件 (*.*)|*.*"
            })
            {
                if (ofd.ShowDialog(this) == DialogResult.OK)
                    return ofd.FileName;
            }

            throw new FileNotFoundException(
                "未找到 .fbs。请把 battle.fbs 放在关卡同目录，或在弹出对话框中选择。");
        }

        void ExportStageJson(string path, bool rawFieldIds)
        {
            if (_front == null)
                throw new InvalidOperationException("没有可导出的关卡数据");
            FrontNav.SyncGrid(Doc, mapCanvas.Cells);
            string fbsPath = ResolveFbsPath(path);
            var schema = BtlCore.Fb.FbsSchema.LoadFile(fbsPath);
            string json = rawFieldIds
                ? BtlCore.Fb.StageJson.ToFieldIds(Doc, schema)
                : BtlCore.Fb.StageJson.ToNamed(Doc, schema);
            File.WriteAllText(path, json, System.Text.Encoding.UTF8);
            statusLabel.Text = rawFieldIds
                ? $"已导出字段 ID JSON（fbs: {Path.GetFileName(fbsPath)}）"
                : $"已导出字段名 JSON（fbs: {Path.GetFileName(fbsPath)}）";
        }

        public static void SuspendDrawing(Control control)
        {
            if (control != null && !control.IsDisposed && control.IsHandleCreated)
            {
                SendMessage(control.Handle, WM_SETREDRAW, false, IntPtr.Zero);
            }
        }

        public static void ResumeDrawing(Control control)
        {
            if (control != null && !control.IsDisposed && control.IsHandleCreated)
            {
                SendMessage(control.Handle, WM_SETREDRAW, true, IntPtr.Zero);
                control.Refresh();
            }
        }

        private void RefreshJsonEditor()
        {
            if (!HasDoc || txtJsonEditor == null)
            {
                if (txtJsonEditor != null) txtJsonEditor.Text = "";
                return;
            }

            try
            {
                FrontNav.SyncGrid(Doc, mapCanvas.Cells);

                txtJsonEditor.Text = _front.ToJson();
            }
            catch
            {
                // JSON 展示页面已被隐藏，静默忽略所有格式化/渲染异常
            }
        }

        private void CellDoubleClickedClick(int cellIdx)
        {
            if (cellIdx < 0 || cellIdx >= mapCanvas.Cells.Count) return;

            var cell = mapCanvas.Cells[cellIdx];

            // 1. Determine current edit mode from tabControlRight
            string editMode = null;
            if (ActivePageId == "terrain")
            {
                editMode = "Terrain";
            }
            else if (ActivePageId == "unit")
            {
                if (cell.Unit == null) return; // 无部队时不跳转
                editMode = "Unit";
            }
            else if (ActivePageId == "site")
            {
                if (cell.TriggerBldg == null && cell.TriggerFort == null) return; // 无建筑/工事时不跳转
                editMode = "Building";
            }
            else
            {
                // 阵营编辑或全局配置等其他 Tab -> 不跳转
                return;
            }

            // 2. Switch to Registry tab on the left
            foreach (TabPage page in tabControlLeft.TabPages)
            {
                if (page.Text == "注册表模式")
                {
                    tabControlLeft.SelectedTab = page;
                    break;
                }
            }

            // 3. 跳到对应 BtlFront 路径（Tab 切入时已 Merge + LoadDocument）
            if (fbRegistryViewer != null && !string.IsNullOrEmpty(editMode))
                fbRegistryViewer.NavigateToCellData(editMode, cellIdx);
        }

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)

        {
            if (_openingFile) return base.ProcessCmdKey(ref msg, keyData);

            if (keyData == Keys.B)

            {

                if (IsEditingText())

                {

                    return base.ProcessCmdKey(ref msg, keyData);

                }

                chkBrushMode.Checked = !chkBrushMode.Checked;

                return true;

            }

            if (keyData == Keys.V)

            {

                if (IsEditingText())

                    return base.ProcessCmdKey(ref msg, keyData);

                if (ActivePageId == "terrain")

                {

                    PerformRandomVariantAction();

                    return true;

                }

            }

            if (keyData == Keys.O)

            {

                if (IsEditingText())

                    return base.ProcessCmdKey(ref msg, keyData);

                if (ActivePageId == "terrain")

                {

                    PerformRandomOffsetAction();

                    return true;

                }

            }

            if (keyData == (Keys.Control | Keys.C))

            {

                if (IsEditingText())

                {

                    return base.ProcessCmdKey(ref msg, keyData);

                }

                PerformCopyAction();

                return true;

            }

            if (keyData == (Keys.Control | Keys.V))

            {

                if (IsEditingText())

                {

                    return base.ProcessCmdKey(ref msg, keyData);

                }

                PerformPasteAction();

                return true;

            }

            if (keyData == (Keys.Control | Keys.Z))

            {

                if (IsEditingText())

                {

                    return base.ProcessCmdKey(ref msg, keyData);

                }

                PerformUndoAction();

                return true;

            }

            if (keyData == (Keys.Control | Keys.Y))

            {

                if (IsEditingText())

                {

                    return base.ProcessCmdKey(ref msg, keyData);

                }

                PerformRedoAction();

                return true;

            }

            return base.ProcessCmdKey(ref msg, keyData);

        }

        private bool IsEditingText()

        {

            Control c = this.ActiveControl;

            while (c != null)

            {

                if (c is TextBoxBase)

                    return true;

                if (c is ComboBox cb && cb.DropDownStyle != ComboBoxStyle.DropDownList)

                    return true;

                if (c is NumericUpDown)

                    return true;

                if (c.GetType().Name == "UpDownEdit")

                    return true;

                if (c is ContainerControl container)

                    c = container.ActiveControl;

                else

                    break;

            }

            return false;

        }

        private void CreateNewMap(ushort w, ushort h, ushort lm, ushort tm, ushort pw, ushort ph, ushort stageNum, ushort version, short tag)
        {
            _front = FrontSession.NewMap(w, h, lm, tm, pw, ph, stageNum, version);

            _loadedFilePath = ""; // Clean loaded path for new file!

            _isInitialLoading = true;

            try

            {

                OnDocumentLoaded();

                InitHistory();

            }

            finally

            {

                _isInitialLoading = false;

            }

            statusLabel.Text = "新建地图成功（内存中）";

            MessageBox.Show("新地图创建成功！您可以开始编辑它了。需要落盘时请用【文件】→【导出为…】。", "创建成功", MessageBoxButtons.OK, MessageBoxIcon.Information);

        }

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        static extern IntPtr SendMessage(IntPtr hWnd, int msg, bool wParam, IntPtr lParam);
        const int WM_SETREDRAW = 0x000B;

        void OnDocumentLoaded()
        {
            UpdateSaveButtonState();
            if (_isUndoRedoAction) mapCanvas.ApplyDocumentFromHistory(Doc);
            else if (_preparedCells != null) mapCanvas.LoadPreparedDocument(Doc, _preparedCells);
            else mapCanvas.Document = Doc;
            _selectedCellIdx = -1;
            RefreshEditorPages();
            if (!_isUndoRedoAction && !_isInitialLoading && !_suppressRegistryHistory) AddHistoryState();
            RefreshRegistryViewIfVisible();
        }

        void CellSelectedClick(int cellIdx)
        {
            if (cellIdx < 0 || cellIdx >= mapCanvas.Cells.Count) return;
            _selectedCellIdx = cellIdx;
            RefreshActiveEditorPage();
            RefreshSelectedTerrainUi();
            var cell = mapCanvas.Cells[cellIdx];
            statusLabel.Text = $"cellIdx: {cellIdx} (X: {cell.X}, Y: {cell.Y})";
        }

    }
}
