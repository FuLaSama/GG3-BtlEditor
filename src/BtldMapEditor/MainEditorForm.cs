/*
 * MainEditorForm.cs  （BtldMapEditor）
 *
 * 战役地图编辑器主窗体。菜单、选项卡、画布、撤销都在这里搭起来。
 *
 * 数据怎么走：
 *   打开 .btl → FrontSession.FromBtl（BtlToFront，类型来自 battle.fbs）
 *   打开 .json → FrontSession.FromJsonFile（fbs 字段名或字段 ID）
 *   右侧数字框 / 列表 → 已加载的 Lua 动作写 Document
 *   画布 MapCell[] 是投影：脚本写完先 RebuildCells，撤销前 SyncGrid 再收格子
 *   撤销栈是一整份 BtlFront JSON 快照（不是命令队列）
 *   导出 .btl / 字段名 JSON / 字段 ID JSON
 *
 * 本文件偏 UI 布局（选项卡、菜单、新建地图对话框）。
 * 面板读写在 MainEditorForm.FrontOps.cs，国家行为树在 MainEditorForm.BtTab.cs。
 *
 * GameSettings 只提供兵种名、贴图目录；关卡结构不走 StageModel。
 */
using System;

using System.Collections.Generic;

using System.Drawing;

using System.IO;

using System.Linq;

using System.Text.Json;

using System.Windows.Forms;

using BtldMapEditor.Front;
using BtlCore.Fb;
using BtlCore.Front;
using BtlCore.Scripting;



namespace BtldMapEditor

{

    public partial class MainEditorForm : Form

    {

        BtlFrontDocument Doc => _front?.Document;
        bool HasDoc => Doc?.Root != null;
        private FrontSession _front;

        private string _loadedFilePath;

        private int _selectedCellIdx = -1;

        private BtlTable _selectedOffMapUnit;
        // 每项是一份完整 BtlFront JSON 快照
        private readonly List<string> _history = new List<string>();
        private int _historyIndex = -1;
        private bool _isUndoRedoAction = false;
        private bool _isInitialLoading = false;



        // UI 控件

        private MenuStrip menuStrip;

        private ToolStripMenuItem fileMenu;

        private ToolStripMenuItem openItem;

        private ToolStripMenuItem newMapItem;

        private ToolStripMenuItem exportItem;

        private ToolStripMenuItem exitItem;



        private StatusStrip statusStrip;

        private ToolStripStatusLabel statusLabel;



        private SplitContainer splitMain;

        private EditorMapCanvas mapCanvas;



        private TabControl tabControlRight;

        private TabPage tabTerrainEdit;

        private TabPage tabUnitEdit;

        private TabPage tabBuildingEdit;

        private TabPage tabFactions;

        private TabPage tabCountryBt;

        private TabPage tabStageInfo;



        private JsonViewer txtJsonEditor;
        private FrontRegistryViewer fbRegistryViewer;
        private bool _registryEdited = false;
        private bool _suppressRegistryHistory = false;

        private TabControl tabControlLeft;



        // 视图渲染模式选择

        private ComboBox cbRenderMode;
        private ComboBox cbVisualStyle;

        private CheckBox chkBrushMode;



        // Cell Edit Controls

        private Label lblCellCoords;

        private Label lblCellCoordsUnit;

        private Label lblCellCoordsBuilding;



        // Terrain Edit Controls
        private CheckBox chkPlayableFlag;
        private CheckBox chkSeaFlag;
        private Label lblTerrainBrush;
        private Label lblTerrainLayers;
        private TabControl _terrainLayerTabs;
        private TrackBar trkTerrainDx;
        private TrackBar trkTerrainDy;
        private Label lblTerrainOffsetTitle;
        private Label lblTerrainDxVal;
        private Label lblTerrainDyVal;
        private TerrainPalettePanel _paletteClimate;
        private TerrainPalettePanel _paletteMain;
        private TerrainPalettePanel _paletteSecondary;
        private TerrainPalettePanel _paletteDecor;
        private PaletteDrag _terrainBrush;



        // Unit Group

        private GroupBox gbUnit;

        private NullableNumericUpDown nudUnitType;

        private Label lblUnitTypeName;

        private NullableNumericUpDown nudUnitFaction;

        private NullableNumericUpDown nudUnitAgentId;

        private NullableNumericUpDown nudUnitHp;

        private NullableNumericUpDown nudUnitMaxHp;

        private NullableNumericUpDown nudUnitStack;

        private NullableNumericUpDown nudUnitLevel;

        private NullableNumericUpDown nudUnitMobility;

        private NullableNumericUpDown nudUnitDirection;

        private NullableNumericUpDown nudUnitVal8;

        private NullableNumericUpDown nudUnitVal9;

        private Button btnAddDeleteUnit;



        // Additional Unit Controls

        private NullableNumericUpDown nudUnitPlayMode;

        private NullableNumericUpDown nudUnitAiTarget;

        private NullableNumericUpDown nudUnitFactionExtra;



        // Unit Behavior (Strategy) Group

        private GroupBox gbUnitBehavior;

        private CheckBox chkUnitBehavior;

        private NullableNumericUpDown nudUnitBehaviorField0;

        private NullableNumericUpDown nudUnitBehaviorId;

        private NullableNumericUpDown nudUnitBehaviorField2;

        private NullableNumericUpDown nudUnitBehaviorRadius;

        private NullableNumericUpDown nudUnitBehaviorField4;

        private NullableNumericUpDown nudUnitBehaviorCenter;

        private NullableNumericUpDown nudUnitBehaviorField6;



        // Unit General (AIAgentTable11) Group

        private GroupBox gbUnitGeneral;

        private CheckBox chkUnitGeneral;

        private NullableNumericUpDown nudUnitGeneralId;

        private Label lblUnitGeneralName;

        private CheckBox chkUnitGeneralActive;

        private NullableNumericUpDown nudUnitGeneralParam2;



        // Unit ExArmy (AIAgentTable10) Group

        private GroupBox gbUnitExArmy;

        private CheckBox chkUnitExArmy;

        private NullableNumericUpDown nudUnitExArmyId;

        private Label lblUnitExArmyName;

        private NullableNumericUpDown nudUnitExArmyHp;

        private NullableNumericUpDown nudUnitExArmyMaxHp;

        private NullableNumericUpDown nudUnitExArmyField1;

        private NullableNumericUpDown nudUnitExArmyField4;

        private NullableNumericUpDown nudUnitExArmyField5;

        // AI Behaviors (ai_info.ai_behaviors) Group
        private GroupBox gbFactionAiBehaviors;
        private ListView lvFactionAiBehaviors;
        private NullableNumericUpDown nudAiBehaviorActionFlag;
        private TextBox txtAiBehaviorTargetCells;
        private Button btnAddBehaviorRule;
        private Button btnSaveBehaviorRule;
        private Button btnDeleteBehaviorRule;
        private bool _isUpdatingBehaviorUi = false;



        // Building Group

        private GroupBox gbBuilding;

        private NullableNumericUpDown nudBldgType;
        private Label lblBldgTypeName;

        private NullableNumericUpDown nudBldgOwner;

        private NullableNumericUpDown nudBldgFlag;

        private NullableNumericUpDown nudBldgExtraFlag;

        private NullableNumericUpDown nudBldgDx;

        private NullableNumericUpDown nudBldgDy;

        private NullableNumericUpDown nudBldgField6;

        private Button btnAddDeleteBldg;



        // Fort Group

        private GroupBox gbFort;

        private NullableNumericUpDown nudFortType;
        private Label lblFortTypeName;

        private Button btnAddDeleteFort;

        private NullableNumericUpDown nudFortField1;

        private NullableNumericUpDown nudFortField3;

        private bool _isUpdatingFortUi = false;



        // Faction Edit Controls

        private ListBox lbFactions;

        private NullableNumericUpDown nudFactionId;

        private NullableNumericUpDown nudFactionCamp;

        private NullableNumericUpDown nudFactionCountry;

        private Label lblFactionCountryName;

        private NullableNumericUpDown nudFactionIsAI;

        private NullableNumericUpDown nudFactionVal5;

        private NullableNumericUpDown nudFactionAlign1;

        private NullableNumericUpDown nudFactionGold;

        private NullableNumericUpDown nudFactionTech;

        private NullableNumericUpDown nudFactionIncomeMod;

        private NullableNumericUpDown nudFactionDamageMod;

        private NullableNumericUpDown nudFactionHpMod;

        private NullableNumericUpDown nudFactionColorR;

        private NullableNumericUpDown nudFactionColorG;

        private NullableNumericUpDown nudFactionColorB;

        private NullableNumericUpDown nudFactionColorA;

        private NullableNumericUpDown nudFactionAlign2;

        private NullableNumericUpDown nudFactionConfigId;

        private NullableNumericUpDown nudFactionGeneralFlag;

        private NullableNumericUpDown nudFactionConfigRef;



        // Stage Config / Metadata Controls

        private CheckBox chkFog;

        private ListView lvTargets;

        private ListView lvReinforces;

        private ListView lvWeathers;

        private ListView lvReinforceUnits;

        // Country Behavior Tree Controls

        private ListView lvBtTrees;

        private TreeView tvBtNodes;

        private NullableNumericUpDown nudBtTid;

        private NullableNumericUpDown nudBtId;

        private TextBox txtBtName;

        private TextBox txtBtAgent;

        private TextBox txtBtClass;

        private NullableNumericUpDown nudBtNodeId;

        private TextBox txtBtNodeClass;

        private TextBox txtBtMethod;

        private TextBox txtBtParams;

        private TextBox txtBtRounds;

        private NullableNumericUpDown nudBtResult;

        private NullableNumericUpDown nudBtCount;

        // Stage Target & Reinforce Editing Controls

        private NullableNumericUpDown nudTargetType;

        private NullableNumericUpDown nudTargetValue;

        private NullableNumericUpDown nudTargetParam1;

        private NullableNumericUpDown nudTargetParam2;

        private NullableNumericUpDown nudTargetFlag;



        private NullableNumericUpDown nudRpCellIdx;

        private NullableNumericUpDown nudRpFactionId;

        private CheckBox chkRpIsKey;

        private NullableNumericUpDown nudRpFlag;

        // Weather Editing Controls

        private NullableNumericUpDown nudWeatherType;

        private NullableNumericUpDown nudWeatherStart;

        private NullableNumericUpDown nudWeatherDuration;

        private Point _factionDragStartPoint;



        // Map Size / Playable Area Controls

        private NullableNumericUpDown nudMapW;

        private NullableNumericUpDown nudMapH;

        private NullableNumericUpDown nudExpandLeft;

        private NullableNumericUpDown nudExpandRight;

        private NullableNumericUpDown nudExpandUp;

        private NullableNumericUpDown nudExpandDown;

        private NullableNumericUpDown nudLeftMargin;

        private NullableNumericUpDown nudTopMargin;

        private NullableNumericUpDown nudPlayWidth;

        private NullableNumericUpDown nudPlayHeight;

        private Button btnResizeMap;



        // Clipboard storage for copy-paste functionality

        private ushort? _copiedTerrain;

        private BtlStruct _copiedAttr;

        private BtlStruct _copiedAttrA2;

        private BtlStruct _copiedAttrA3;

        public MainEditorForm()

        {

            // 地形名等仍从 GameSettings 加载（兵种/贴图显示用）；字段类型来自 battle.fbs
            GameSettings.LoadAllSettings();

            InitializeComponent();

            Text = "BTLD 关卡与地图逻辑编辑器 - GG3 Mod Tools";

            Size = new Size(1280, 800);

            StartPosition = FormStartPosition.CenterScreen;



            PopulateDropdowns();

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

                WrapContents = false,

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



            // 右侧 Tab 容器

            tabControlRight = new TabControl();

            tabControlRight.Dock = DockStyle.Fill;

            splitMain.Panel2.Controls.Add(tabControlRight);



            // Tab 1: 地形修改

            tabTerrainEdit = new TabPage("地形修改");

            InitializeTerrainEditTab();

            tabControlRight.TabPages.Add(tabTerrainEdit);



            // Tab 2: 部队修改

            tabUnitEdit = new TabPage("部队修改");

            InitializeUnitEditTab();

            tabControlRight.TabPages.Add(tabUnitEdit);



            // Tab 3: 建筑修改

            tabBuildingEdit = new TabPage("建筑修改");

            InitializeBuildingEditTab();

            tabControlRight.TabPages.Add(tabBuildingEdit);



            // Tab 4: 势力编辑

            tabFactions = new TabPage("阵营/势力");

            InitializeFactionsTab();

            tabControlRight.TabPages.Add(tabFactions);

            // Tab 5: 国家行为树

            tabCountryBt = new TabPage("国家行为树");

            InitializeCountryBtTab();

            tabControlRight.TabPages.Add(tabCountryBt);



            // Tab 6: 关卡设置

            tabStageInfo = new TabPage("全局配置");

            InitializeStageInfoTab();

            tabControlRight.TabPages.Add(tabStageInfo);

        }



        private void InitializeTerrainEditTab()
        {
            tabTerrainEdit.Padding = new Padding(8);

            var root = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                RowCount = 7,
                Padding = new Padding(0)
            };
            root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
            tabTerrainEdit.Controls.Add(root);

            lblCellCoords = new Label
            {
                Text = "未选中地块",
                Font = new Font("Segoe UI", 10f, FontStyle.Bold),
                AutoSize = true,
                Margin = new Padding(0, 0, 0, 4)
            };
            root.Controls.Add(lblCellCoords, 0, 0);

            var flagsRow = new FlowLayoutPanel
            {
                AutoSize = true,
                WrapContents = true,
                Dock = DockStyle.Top,
                Margin = new Padding(0, 0, 0, 2)
            };
            chkPlayableFlag = new CheckBox
            {
                Text = "是否为可玩地块",
                AutoSize = true,
                Margin = new Padding(0, 2, 16, 2)
            };
            chkPlayableFlag.CheckedChanged += PlayableFlagChanged;
            chkSeaFlag = new CheckBox
            {
                Text = "海洋",
                AutoSize = true,
                Margin = new Padding(0, 2, 16, 2)
            };
            chkSeaFlag.CheckedChanged += SeaFlagChanged;
            flagsRow.Controls.Add(chkPlayableFlag);
            flagsRow.Controls.Add(chkSeaFlag);
            root.Controls.Add(flagsRow, 0, 1);

            lblTerrainBrush = new Label
            {
                Text = "画笔: 未选择（点选贴图后拖到地图，或复制粘贴）  |  V 随机变种  O 随机偏移（±32）",
                AutoSize = true,
                MaximumSize = new Size(480, 0),
                ForeColor = Color.FromArgb(71, 85, 105),
                Margin = new Padding(0, 0, 0, 4),
                Cursor = Cursors.Hand
            };
            lblTerrainBrush.Click += (s, e) => ClearTerrainBrush();
            root.Controls.Add(lblTerrainBrush, 0, 2);

            var globalRandomRow = new FlowLayoutPanel
            {
                AutoSize = true,
                WrapContents = true,
                Margin = new Padding(0, 0, 0, 4)
            };
            var btnGlobalRandomVariant = new Button
            {
                Text = "全局随机变种",
                AutoSize = true,
                Margin = new Padding(0, 0, 8, 0)
            };
            var btnGlobalRandomOffset = new Button
            {
                Text = "全局随机偏移",
                AutoSize = true,
                Margin = new Padding(0, 0, 0, 0)
            };
            btnGlobalRandomVariant.Click += (_, __) => PerformGlobalRandomVariantAction();
            btnGlobalRandomOffset.Click += (_, __) => PerformGlobalRandomOffsetAction();
            globalRandomRow.Controls.Add(btnGlobalRandomVariant);
            globalRandomRow.Controls.Add(btnGlobalRandomOffset);
            root.Controls.Add(globalRandomRow, 0, 3);

            lblTerrainLayers = new Label
            {
                Text = "地质: —\r\n主地形: —    次地形: —    装饰: —",
                AutoSize = true,
                MaximumSize = new Size(480, 0),
                ForeColor = Color.FromArgb(51, 65, 85),
                Margin = new Padding(0, 0, 0, 4)
            };
            root.Controls.Add(lblTerrainLayers, 0, 4);

            var offsetPanel = new TableLayoutPanel
            {
                ColumnCount = 3,
                RowCount = 4,
                AutoSize = true,
                Dock = DockStyle.Top,
                Margin = new Padding(0, 0, 0, 6)
            };
            offsetPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 28f));
            offsetPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
            offsetPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 36f));
            lblTerrainOffsetTitle = new Label
            {
                Text = "贴图偏移（主地形）",
                AutoSize = true,
                ForeColor = Color.FromArgb(51, 65, 85),
                Margin = new Padding(0, 0, 0, 2)
            };
            offsetPanel.Controls.Add(lblTerrainOffsetTitle, 0, 0);
            offsetPanel.SetColumnSpan(lblTerrainOffsetTitle, 3);
            trkTerrainDx = new TrackBar
            {
                Minimum = -128,
                Maximum = 127,
                TickFrequency = 32,
                SmallChange = 1,
                LargeChange = 8,
                AutoSize = false,
                Height = 36,
                Dock = DockStyle.Fill,
                Margin = new Padding(0, 0, 4, 0)
            };
            trkTerrainDy = new TrackBar
            {
                Minimum = -128,
                Maximum = 127,
                TickFrequency = 32,
                SmallChange = 1,
                LargeChange = 8,
                AutoSize = false,
                Height = 36,
                Dock = DockStyle.Fill,
                Margin = new Padding(0, 0, 4, 0)
            };
            trkTerrainDx.ValueChanged += TerrainOffsetChanged;
            trkTerrainDy.ValueChanged += TerrainOffsetChanged;
            trkTerrainDx.MouseUp += (_, __) => { if (!_terrainOffsetDragging) AddHistoryState(); };
            trkTerrainDy.MouseUp += (_, __) => { if (!_terrainOffsetDragging) AddHistoryState(); };
            lblTerrainDxVal = new Label { Text = "0", AutoSize = true, TextAlign = ContentAlignment.MiddleRight, Margin = new Padding(0, 8, 0, 0) };
            lblTerrainDyVal = new Label { Text = "0", AutoSize = true, TextAlign = ContentAlignment.MiddleRight, Margin = new Padding(0, 8, 0, 0) };
            offsetPanel.Controls.Add(new Label { Text = "X", AutoSize = true, Margin = new Padding(0, 10, 0, 0) }, 0, 1);
            offsetPanel.Controls.Add(trkTerrainDx, 1, 1);
            offsetPanel.Controls.Add(lblTerrainDxVal, 2, 1);
            offsetPanel.Controls.Add(new Label { Text = "Y", AutoSize = true, Margin = new Padding(0, 10, 0, 0) }, 0, 2);
            offsetPanel.Controls.Add(trkTerrainDy, 1, 2);
            offsetPanel.Controls.Add(lblTerrainDyVal, 2, 2);
            var offsetHint = new Label
            {
                Text = "Shift + 左键拖动地图可微调偏移（有符号 -128～127）",
                AutoSize = true,
                ForeColor = Color.FromArgb(100, 116, 139),
                Margin = new Padding(0, 2, 0, 0),
                Font = new Font("Segoe UI", 7.5f)
            };
            offsetPanel.Controls.Add(offsetHint, 0, 3);
            offsetPanel.SetColumnSpan(offsetHint, 3);
            root.Controls.Add(offsetPanel, 0, 5);

            _terrainLayerTabs = new TabControl { Dock = DockStyle.Fill };
            var tabClimate = new TabPage("地质");
            var tabMain = new TabPage("主地形");
            var tabSecondary = new TabPage("次地形");
            var tabDecor = new TabPage("装饰");
            _terrainLayerTabs.TabPages.Add(tabClimate);
            _terrainLayerTabs.TabPages.Add(tabMain);
            _terrainLayerTabs.TabPages.Add(tabSecondary);
            _terrainLayerTabs.TabPages.Add(tabDecor);
            root.Controls.Add(_terrainLayerTabs, 0, 6);

            _paletteClimate = new TerrainPalettePanel(PaletteTarget.Climate, EditorPaletteCatalog.Climate, 52, true);
            _paletteMain = new TerrainPalettePanel(PaletteTarget.Main, EditorPaletteCatalog.Terrain, 52, true);
            _paletteSecondary = _paletteMain;
            _paletteDecor = _paletteMain;
            tabClimate.Controls.Add(_paletteClimate);
            tabMain.Controls.Add(_paletteMain);
            _terrainLayerTabs.SelectedIndexChanged += (s, e) =>
            {
                if (_terrainLayerTabs.SelectedTab == tabMain)
                {
                    _paletteMain.Target = PaletteTarget.Main;
                    tabMain.Controls.Add(_paletteMain);
                }
                else if (_terrainLayerTabs.SelectedTab == tabSecondary)
                {
                    _paletteMain.Target = PaletteTarget.Secondary;
                    tabSecondary.Controls.Add(_paletteMain);
                }
                else if (_terrainLayerTabs.SelectedTab == tabDecor)
                {
                    _paletteMain.Target = PaletteTarget.Decor;
                    tabDecor.Controls.Add(_paletteMain);
                }
                if (_selectedCellIdx >= 0 && _selectedCellIdx < mapCanvas.Cells.Count)
                {
                    RefreshTerrainPaletteSelection(mapCanvas.Cells[_selectedCellIdx]);
                    RefreshTerrainOffsetUi(mapCanvas.Cells[_selectedCellIdx]);
                }
                if (_terrainBrush != null && _terrainBrush.Target != PaletteTarget.Climate && _terrainLayerTabs.SelectedTab != tabClimate)
                {
                    _terrainBrush = new PaletteDrag { Item = _terrainBrush.Item, Target = _paletteMain.Target };
                    lblTerrainBrush.Text = "画笔: " + _terrainBrush.Item.Label + "  →  " + PaletteTargetName(_terrainBrush.Target) + "（再点这里取消）";
                }
            };

            HookPalette(_paletteClimate);
            HookPalette(_paletteMain);
        }

        private void HookPalette(TerrainPalettePanel panel)
        {
            panel.ItemSelected += (item, target) =>
            {
                if (panel != _paletteClimate) _paletteClimate.SelectItem(null, false);
                if (panel != _paletteMain) _paletteMain.SelectItem(null, false);
                target = panel.Target;
                _terrainBrush = new PaletteDrag { Item = item, Target = target };
                lblTerrainBrush.Text = "画笔: " + item.Label + "  →  " + PaletteTargetName(target) + "（再点这里取消）";
            };
            panel.ItemActivated += (item, target) =>
            {
                target = panel.Target;
                _terrainBrush = new PaletteDrag { Item = item, Target = target };
                lblTerrainBrush.Text = "画笔: " + item.Label + "  →  " + PaletteTargetName(target);
                if (_selectedCellIdx >= 0)
                    ApplyPaletteToCell(_selectedCellIdx, item, target, recordHistory: true);
            };
            panel.ClearLayerRequested += () =>
            {
                if (_selectedCellIdx < 0) return;
                ClearPaletteLayer(_selectedCellIdx, panel.Target, recordHistory: true);
            };
        }

        void ClearTerrainBrush()
        {
            _terrainBrush = null;
            _paletteClimate?.SelectItem(null, false);
            _paletteMain?.SelectItem(null, false);
            _paletteSecondary?.SelectItem(null, false);
            _paletteDecor?.SelectItem(null, false);
            if (lblTerrainBrush != null)
                lblTerrainBrush.Text = "画笔: 未选择（点选贴图后拖到地图，或复制粘贴）  |  V 随机变种  O 随机偏移（±32）";
        }

        static string PaletteTargetName(PaletteTarget target)
        {
            switch (target)
            {
                case PaletteTarget.Climate: return "地质";
                case PaletteTarget.Main: return "主地形";
                case PaletteTarget.Secondary: return "次地形";
                case PaletteTarget.Decor: return "装饰";
                default: return target.ToString();
            }
        }

        private void InitializeUnitEditTab()

        {

            tabUnitEdit.AutoScroll = true;



            TableLayoutPanel tlpMain = new TableLayoutPanel

            {

                Dock = DockStyle.Fill,

                AutoScroll = true,

                ColumnCount = 1,

                Padding = new Padding(10)

            };

            tlpMain.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));

            tabUnitEdit.Controls.Add(tlpMain);



            lblCellCoordsUnit = new Label

            {

                Text = "未选中地块",

                Font = new Font("Segoe UI", 10f, FontStyle.Bold),

                AutoSize = true,

                Anchor = AnchorStyles.Left | AnchorStyles.Right,

                Margin = new Padding(0, 0, 0, 10)

            };

            tlpMain.Controls.Add(lblCellCoordsUnit);

            // 0. 援军列表

            GroupBox gbReinforceUnits = new GroupBox

            {

                Text = "援军列表 (不在地图上的部队)",

                Anchor = AnchorStyles.Left | AnchorStyles.Right,

                AutoSize = true,

                AutoSizeMode = AutoSizeMode.GrowAndShrink,

                Margin = new Padding(0, 0, 0, 10)

            };

            tlpMain.Controls.Add(gbReinforceUnits);



            lvReinforceUnits = new ListView { Anchor = AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Top, Height = 120, View = View.Details, FullRowSelect = true, GridLines = true, Margin = new Padding(5) };

            lvReinforceUnits.Columns.Add("部队 ID", 150);

            gbReinforceUnits.Controls.Add(lvReinforceUnits);

            lvReinforceUnits.SelectedIndexChanged += LvReinforceUnitsSelectedIndexChanged;



            // 1. 基础部队数据 GroupBox

            gbUnit = new GroupBox

            {

                Text = "基础部队信息",

                Anchor = AnchorStyles.Left | AnchorStyles.Right,

                AutoSize = true,

                AutoSizeMode = AutoSizeMode.GrowAndShrink,

                Margin = new Padding(0, 0, 0, 10)

            };

            tlpMain.Controls.Add(gbUnit);



            TableLayoutPanel tlpUnit = new TableLayoutPanel

            {

                Dock = DockStyle.Top,

                AutoSize = true,

                Padding = new Padding(5, 18, 5, 5),

                ColumnCount = 3,

                RowCount = 9

            };

            tlpUnit.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

            tlpUnit.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50f));

            tlpUnit.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50f));

            gbUnit.Controls.Add(tlpUnit);



            Label lblUnitType = new Label { Text = "兵种类型 ID:", Anchor = AnchorStyles.Left, AutoSize = true };

            nudUnitType = new NullableNumericUpDown { Anchor = AnchorStyles.Left | AnchorStyles.Right, Minimum = -1, Maximum = 65535 };

            lblUnitTypeName = new Label { Text = "兵种名称: 无", Anchor = AnchorStyles.Left, AutoSize = true, ForeColor = Color.DarkGreen, Font = new Font("Segoe UI", 9f, FontStyle.Bold) };

            btnAddDeleteUnit = new Button { Text = "添加部队", Anchor = AnchorStyles.Left | AnchorStyles.Right, Height = 32 };

            btnAddDeleteUnit.Click += AddDeleteUnitClick;



            Label lblUnitFaction = new Label { Text = "归属势力:", Anchor = AnchorStyles.Left, AutoSize = true };

            nudUnitFaction = new NullableNumericUpDown { Anchor = AnchorStyles.Left | AnchorStyles.Right, Minimum = -1, Maximum = 65535 };

            nudUnitAgentId = new NullableNumericUpDown { Anchor = AnchorStyles.Left | AnchorStyles.Right, Minimum = -1, Maximum = 65535 };



            Label lblUnitHp = new Label { Text = "当前/最大生命值:", Anchor = AnchorStyles.Left, AutoSize = true };

            nudUnitHp = new NullableNumericUpDown { Anchor = AnchorStyles.Left | AnchorStyles.Right, Minimum = -1, Maximum = 65535 };

            nudUnitMaxHp = new NullableNumericUpDown { Anchor = AnchorStyles.Left | AnchorStyles.Right, Minimum = -1, Maximum = 65535 };



            Label lblUnitStack = new Label { Text = "编数/部队等级:", Anchor = AnchorStyles.Left, AutoSize = true };

            nudUnitStack = new NullableNumericUpDown { Anchor = AnchorStyles.Left | AnchorStyles.Right, Minimum = -1, Maximum = 255 };

            nudUnitLevel = new NullableNumericUpDown { Anchor = AnchorStyles.Left | AnchorStyles.Right, Minimum = -1, Maximum = 255 };



            Label lblUnitPlayMode = new Label { Text = "关键目标:", Anchor = AnchorStyles.Left, AutoSize = true };

            nudUnitPlayMode = new NullableNumericUpDown { Anchor = AnchorStyles.Left | AnchorStyles.Right, Minimum = -1, Maximum = 255 };



            Label lblUnitAiTarget = new Label { Text = "初始士气值:", Anchor = AnchorStyles.Left, AutoSize = true };

            nudUnitAiTarget = new NullableNumericUpDown { Anchor = AnchorStyles.Left | AnchorStyles.Right, Minimum = -1, Maximum = 255 };



            Label lblUnitFactionExtra = new Label { Text = "修饰势力标志:", Anchor = AnchorStyles.Left, AutoSize = true };

            nudUnitFactionExtra = new NullableNumericUpDown { Anchor = AnchorStyles.Left | AnchorStyles.Right, Minimum = -1, Maximum = 255 };



            nudUnitMobility = new NullableNumericUpDown { Anchor = AnchorStyles.Left | AnchorStyles.Right, Minimum = -1, Maximum = 255 };

            nudUnitDirection = new NullableNumericUpDown { Anchor = AnchorStyles.Left | AnchorStyles.Right, Minimum = -1, Maximum = 255 };

            nudUnitVal8 = new NullableNumericUpDown { Anchor = AnchorStyles.Left | AnchorStyles.Right, Minimum = -1, Maximum = 65535 };

            nudUnitVal9 = new NullableNumericUpDown { Anchor = AnchorStyles.Left | AnchorStyles.Right, Minimum = -1, Maximum = 65535 };



            tlpUnit.Controls.Add(lblUnitType, 0, 0);

            tlpUnit.Controls.Add(nudUnitType, 1, 0);

            tlpUnit.Controls.Add(btnAddDeleteUnit, 2, 0);



            Label lblUnitNameTitle = new Label { Text = "兵种名称:", Anchor = AnchorStyles.Left, AutoSize = true };

            tlpUnit.Controls.Add(lblUnitNameTitle, 0, 1);

            tlpUnit.Controls.Add(lblUnitTypeName, 1, 1);

            tlpUnit.SetColumnSpan(lblUnitTypeName, 2);



            tlpUnit.Controls.Add(new Label { Text = "归属势力 / 部队ID:", Anchor = AnchorStyles.Left, AutoSize = true }, 0, 2);

            tlpUnit.Controls.Add(nudUnitFaction, 1, 2);

            tlpUnit.Controls.Add(nudUnitAgentId, 2, 2);



            tlpUnit.Controls.Add(new Label { Text = "当前/最大生命值:", Anchor = AnchorStyles.Left, AutoSize = true }, 0, 3);

            tlpUnit.Controls.Add(nudUnitHp, 1, 3);

            tlpUnit.Controls.Add(nudUnitMaxHp, 2, 3);



            tlpUnit.Controls.Add(new Label { Text = "堆叠编制 / 经验等级:", Anchor = AnchorStyles.Left, AutoSize = true }, 0, 4);

            tlpUnit.Controls.Add(nudUnitStack, 1, 4);

            tlpUnit.Controls.Add(nudUnitLevel, 2, 4);



            tlpUnit.Controls.Add(new Label { Text = "关键目标 / 初始士气:", Anchor = AnchorStyles.Left, AutoSize = true }, 0, 5);

            tlpUnit.Controls.Add(nudUnitPlayMode, 1, 5);

            tlpUnit.Controls.Add(nudUnitAiTarget, 2, 5);



            tlpUnit.Controls.Add(new Label { Text = "修饰势力 / 部队移动力:", Anchor = AnchorStyles.Left, AutoSize = true }, 0, 6);

            tlpUnit.Controls.Add(nudUnitFactionExtra, 1, 6);

            tlpUnit.Controls.Add(nudUnitMobility, 2, 6);



            tlpUnit.Controls.Add(new Label { Text = "部队朝向 / 参数 8:", Anchor = AnchorStyles.Left, AutoSize = true }, 0, 7);

            tlpUnit.Controls.Add(nudUnitDirection, 1, 7);

            tlpUnit.Controls.Add(nudUnitVal8, 2, 7);



            tlpUnit.Controls.Add(new Label { Text = "参数 9:", Anchor = AnchorStyles.Left, AutoSize = true }, 0, 8);

            tlpUnit.Controls.Add(nudUnitVal9, 1, 8);



            // 2. AI 行为树 GroupBox

            gbUnitBehavior = new GroupBox

            {

                Text = "部队AI行为",

                Anchor = AnchorStyles.Left | AnchorStyles.Right,

                AutoSize = true,

                AutoSizeMode = AutoSizeMode.GrowAndShrink,

                Margin = new Padding(0, 0, 0, 10)

            };

            tlpMain.Controls.Add(gbUnitBehavior);



            TableLayoutPanel tlpBehavior = new TableLayoutPanel

            {

                Dock = DockStyle.Top,

                AutoSize = true,

                Padding = new Padding(5, 18, 5, 5),

                ColumnCount = 2,

                RowCount = 8

            };

            tlpBehavior.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

            tlpBehavior.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));

            gbUnitBehavior.Controls.Add(tlpBehavior);



            chkUnitBehavior = new CheckBox { Text = "启用部队行为", Anchor = AnchorStyles.Left, AutoSize = true };



            nudUnitBehaviorField0 = new NullableNumericUpDown { Anchor = AnchorStyles.Left | AnchorStyles.Right, Minimum = -1, Maximum = 255 };

            nudUnitBehaviorId = new NullableNumericUpDown { Anchor = AnchorStyles.Left | AnchorStyles.Right, Minimum = -1, Maximum = 65535 };



            nudUnitBehaviorField2 = new NullableNumericUpDown { Anchor = AnchorStyles.Left | AnchorStyles.Right, Minimum = -32769, Maximum = 32767 };

            nudUnitBehaviorRadius = new NullableNumericUpDown { Anchor = AnchorStyles.Left | AnchorStyles.Right, Minimum = -1, Maximum = 255 };



            nudUnitBehaviorField4 = new NullableNumericUpDown { Anchor = AnchorStyles.Left | AnchorStyles.Right, Minimum = -1, Maximum = 255 };

            nudUnitBehaviorCenter = new NullableNumericUpDown { Anchor = AnchorStyles.Left | AnchorStyles.Right, Minimum = -1, Maximum = 65535 };



            nudUnitBehaviorField6 = new NullableNumericUpDown { Anchor = AnchorStyles.Left | AnchorStyles.Right, Minimum = -32769, Maximum = 32767 };



            tlpBehavior.Controls.Add(chkUnitBehavior, 0, 0);

            tlpBehavior.SetColumnSpan(chkUnitBehavior, 2);



            tlpBehavior.Controls.Add(new Label { Text = "参数 0:", Anchor = AnchorStyles.Left, AutoSize = true }, 0, 1);

            tlpBehavior.Controls.Add(nudUnitBehaviorField0, 1, 1);



            tlpBehavior.Controls.Add(new Label { Text = "参数 1:", Anchor = AnchorStyles.Left, AutoSize = true }, 0, 2);

            tlpBehavior.Controls.Add(nudUnitBehaviorId, 1, 2);



            tlpBehavior.Controls.Add(new Label { Text = "参数 2:", Anchor = AnchorStyles.Left, AutoSize = true }, 0, 3);

            tlpBehavior.Controls.Add(nudUnitBehaviorField2, 1, 3);



            tlpBehavior.Controls.Add(new Label { Text = "参数 3:", Anchor = AnchorStyles.Left, AutoSize = true }, 0, 4);

            tlpBehavior.Controls.Add(nudUnitBehaviorRadius, 1, 4);



            tlpBehavior.Controls.Add(new Label { Text = "参数 4:", Anchor = AnchorStyles.Left, AutoSize = true }, 0, 5);

            tlpBehavior.Controls.Add(nudUnitBehaviorField4, 1, 5);



            tlpBehavior.Controls.Add(new Label { Text = "参数 5:", Anchor = AnchorStyles.Left, AutoSize = true }, 0, 6);

            tlpBehavior.Controls.Add(nudUnitBehaviorCenter, 1, 6);



            tlpBehavior.Controls.Add(new Label { Text = "参数 6:", Anchor = AnchorStyles.Left, AutoSize = true }, 0, 7);

            tlpBehavior.Controls.Add(nudUnitBehaviorField6, 1, 7);



            // 3. 将领附加配置 GroupBox

            gbUnitGeneral = new GroupBox

            {

                Text = "将领配置",

                Anchor = AnchorStyles.Left | AnchorStyles.Right,

                AutoSize = true,

                AutoSizeMode = AutoSizeMode.GrowAndShrink,

                Margin = new Padding(0, 0, 0, 10)

            };

            tlpMain.Controls.Add(gbUnitGeneral);



            TableLayoutPanel tlpGeneral = new TableLayoutPanel

            {

                Dock = DockStyle.Top,

                AutoSize = true,

                Padding = new Padding(5, 18, 5, 5),

                ColumnCount = 3,

                RowCount = 4

            };

            tlpGeneral.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

            tlpGeneral.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50f));

            tlpGeneral.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50f));

            for (int r = 0; r < 4; r++) tlpGeneral.RowStyles.Add(new RowStyle(SizeType.Absolute, 32f));

            gbUnitGeneral.Controls.Add(tlpGeneral);



            chkUnitGeneral = new CheckBox { Text = "启用将领配置", Anchor = AnchorStyles.Left, AutoSize = true };

            Label lblGenId = new Label { Text = "附加将领 ID:", Anchor = AnchorStyles.Left, AutoSize = true };

            nudUnitGeneralId = new NullableNumericUpDown { Anchor = AnchorStyles.Left | AnchorStyles.Right, Minimum = -1, Maximum = 65535 };

            lblUnitGeneralName = new Label { Text = "将领姓名: 无", Anchor = AnchorStyles.Left, AutoSize = true, AutoEllipsis = true, ForeColor = Color.DarkBlue };



            chkUnitGeneralActive = new CheckBox { Text = "启用附加参数", Anchor = AnchorStyles.Left, AutoSize = true };



            Label lblGenP2 = new Label { Text = "附加参数:", Anchor = AnchorStyles.Left, AutoSize = true };

            nudUnitGeneralParam2 = new NullableNumericUpDown { Anchor = AnchorStyles.Left | AnchorStyles.Right, Minimum = -1, Maximum = 255 };



            tlpGeneral.Controls.Add(chkUnitGeneral, 0, 0);

            tlpGeneral.SetColumnSpan(chkUnitGeneral, 3);



            tlpGeneral.Controls.Add(lblGenId, 0, 1);

            tlpGeneral.Controls.Add(nudUnitGeneralId, 1, 1);

            tlpGeneral.Controls.Add(lblUnitGeneralName, 2, 1);



            tlpGeneral.Controls.Add(chkUnitGeneralActive, 0, 2);

            tlpGeneral.SetColumnSpan(chkUnitGeneralActive, 3);



            tlpGeneral.Controls.Add(lblGenP2, 0, 3);

            tlpGeneral.Controls.Add(nudUnitGeneralParam2, 1, 3);



            // 4. 特种部队附加配置 GroupBox

            gbUnitExArmy = new GroupBox

            {

                Text = "特种部队配置",

                Anchor = AnchorStyles.Left | AnchorStyles.Right,

                AutoSize = true,

                AutoSizeMode = AutoSizeMode.GrowAndShrink,

                Margin = new Padding(0, 0, 0, 10)

            };

            tlpMain.Controls.Add(gbUnitExArmy);



            TableLayoutPanel tlpExArmy = new TableLayoutPanel

            {

                Dock = DockStyle.Top,

                AutoSize = true,

                Padding = new Padding(5, 18, 5, 5),

                ColumnCount = 3,

                RowCount = 5

            };

            tlpExArmy.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

            tlpExArmy.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50f));

            tlpExArmy.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50f));

            for (int r = 0; r < 5; r++) tlpExArmy.RowStyles.Add(new RowStyle(SizeType.Absolute, 32f));

            gbUnitExArmy.Controls.Add(tlpExArmy);



            chkUnitExArmy = new CheckBox { Text = "启用特种部队配置", Anchor = AnchorStyles.Left, AutoSize = true };

            Label lblExArmyId = new Label { Text = "特种部队 ID:", Anchor = AnchorStyles.Left, AutoSize = true };

            nudUnitExArmyId = new NullableNumericUpDown { Anchor = AnchorStyles.Left | AnchorStyles.Right, Minimum = -1, Maximum = 65535 };

            lblUnitExArmyName = new Label { Text = "名称: 无", Anchor = AnchorStyles.Left, AutoSize = true, AutoEllipsis = true, ForeColor = Color.DarkBlue };



            Label lblExArmyHp = new Label { Text = "当前/最大血量:", Anchor = AnchorStyles.Left, AutoSize = true };

            nudUnitExArmyHp = new NullableNumericUpDown { Anchor = AnchorStyles.Left | AnchorStyles.Right, Minimum = -1, Maximum = 65535 };

            nudUnitExArmyMaxHp = new NullableNumericUpDown { Anchor = AnchorStyles.Left | AnchorStyles.Right, Minimum = -1, Maximum = 65535 };



            Label lblExArmyF1F4 = new Label { Text = "参数 1 / 4:", Anchor = AnchorStyles.Left, AutoSize = true };

            nudUnitExArmyField1 = new NullableNumericUpDown { Anchor = AnchorStyles.Left | AnchorStyles.Right, Minimum = -1, Maximum = 255 };

            nudUnitExArmyField4 = new NullableNumericUpDown { Anchor = AnchorStyles.Left | AnchorStyles.Right, Minimum = -1, Maximum = 65535 };



            Label lblExArmyF5 = new Label { Text = "参数 5:", Anchor = AnchorStyles.Left, AutoSize = true };

            nudUnitExArmyField5 = new NullableNumericUpDown { Anchor = AnchorStyles.Left | AnchorStyles.Right, Minimum = -1, Maximum = 65535 };



            tlpExArmy.Controls.Add(chkUnitExArmy, 0, 0);

            tlpExArmy.SetColumnSpan(chkUnitExArmy, 3);



            tlpExArmy.Controls.Add(lblExArmyId, 0, 1);

            tlpExArmy.Controls.Add(nudUnitExArmyId, 1, 1);

            tlpExArmy.Controls.Add(lblUnitExArmyName, 2, 1);



            tlpExArmy.Controls.Add(lblExArmyHp, 0, 2);

            tlpExArmy.Controls.Add(nudUnitExArmyHp, 1, 2);

            tlpExArmy.Controls.Add(nudUnitExArmyMaxHp, 2, 2);



            tlpExArmy.Controls.Add(lblExArmyF1F4, 0, 3);

            tlpExArmy.Controls.Add(nudUnitExArmyField1, 1, 3);

            tlpExArmy.Controls.Add(nudUnitExArmyField4, 2, 3);



            tlpExArmy.Controls.Add(lblExArmyF5, 0, 4);

            tlpExArmy.Controls.Add(nudUnitExArmyField5, 1, 4);

            // 5. 阵营与部队 AI 战术指示规则 GroupBox
            // 5. 阵营与部队 AI 战术指示规则 GroupBox
            gbFactionAiBehaviors = new GroupBox
            {
                Text = "部队组",
                Anchor = AnchorStyles.Left | AnchorStyles.Right,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                Margin = new Padding(0, 0, 0, 10)
            };
            tlpMain.Controls.Add(gbFactionAiBehaviors);

            TableLayoutPanel tlpAiBehaviors = new TableLayoutPanel
            {
                Dock = DockStyle.Top,
                AutoSize = true,
                Padding = new Padding(5, 18, 5, 5),
                ColumnCount = 3,
                RowCount = 4
            };
            tlpAiBehaviors.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33.33f));
            tlpAiBehaviors.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33.33f));
            tlpAiBehaviors.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33.34f));
            gbFactionAiBehaviors.Controls.Add(tlpAiBehaviors);

            // Row 0: 表格展示区 (ListView)
            lvFactionAiBehaviors = new ListView
            {
                Anchor = AnchorStyles.Left | AnchorStyles.Right,
                Height = 125,
                View = View.Details,
                FullRowSelect = true,
                GridLines = true,
                MultiSelect = false
            };
            lvFactionAiBehaviors.Columns.Add("参数 1", 120);
            lvFactionAiBehaviors.Columns.Add("参数 2", 260);

            tlpAiBehaviors.Controls.Add(lvFactionAiBehaviors, 0, 0);
            tlpAiBehaviors.SetColumnSpan(lvFactionAiBehaviors, 3);

            // Row 1: 参数 1 编辑栏
            TableLayoutPanel tlpFlagRow = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                AutoSize = true,
                ColumnCount = 2,
                Margin = new Padding(0, 4, 0, 2)
            };
            tlpFlagRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            tlpFlagRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));

            Label lblActionFlag = new Label { Text = "参数 1:", Anchor = AnchorStyles.Left, AutoSize = true };
            nudAiBehaviorActionFlag = new NullableNumericUpDown
            {
                Anchor = AnchorStyles.Left | AnchorStyles.Right,
                Minimum = 0,
                Maximum = 65535
            };
            tlpFlagRow.Controls.Add(lblActionFlag, 0, 0);
            tlpFlagRow.Controls.Add(nudAiBehaviorActionFlag, 1, 0);

            tlpAiBehaviors.Controls.Add(tlpFlagRow, 0, 1);
            tlpAiBehaviors.SetColumnSpan(tlpFlagRow, 3);

            // Row 2: 参数 2 编辑栏
            TableLayoutPanel tlpCellsRow = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                AutoSize = true,
                ColumnCount = 2,
                Margin = new Padding(0, 2, 0, 4)
            };
            tlpCellsRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            tlpCellsRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));

            Label lblTargetCells = new Label { Text = "参数 2:", Anchor = AnchorStyles.Left, AutoSize = true };
            txtAiBehaviorTargetCells = new TextBox { Anchor = AnchorStyles.Left | AnchorStyles.Right };

            tlpCellsRow.Controls.Add(lblTargetCells, 0, 0);
            tlpCellsRow.Controls.Add(txtAiBehaviorTargetCells, 1, 0);

            tlpAiBehaviors.Controls.Add(tlpCellsRow, 0, 2);
            tlpAiBehaviors.SetColumnSpan(tlpCellsRow, 3);

            // Row 3: 底部三大操作按钮 (添加 / 保存 / 删除)
            btnAddBehaviorRule = new Button { Text = "添加部队组", AutoSize = true, Anchor = AnchorStyles.Left | AnchorStyles.Right };
            btnSaveBehaviorRule = new Button { Text = "保存选中修改", AutoSize = true, Anchor = AnchorStyles.Left | AnchorStyles.Right };
            btnDeleteBehaviorRule = new Button { Text = "删除选中部队组", AutoSize = true, Anchor = AnchorStyles.Left | AnchorStyles.Right };

            tlpAiBehaviors.Controls.Add(btnAddBehaviorRule, 0, 3);
            tlpAiBehaviors.Controls.Add(btnSaveBehaviorRule, 1, 3);
            tlpAiBehaviors.Controls.Add(btnDeleteBehaviorRule, 2, 3);

            lvFactionAiBehaviors.SelectedIndexChanged += BehaviorSelectedChanged;
            btnAddBehaviorRule.Click += AddBehaviorRule;
            btnSaveBehaviorRule.Click += SaveBehaviorRule;
            btnDeleteBehaviorRule.Click += DeleteBehaviorRule;

            // 属性更改绑定
            nudUnitType.ValueChanged += UnitPropertyChanged;

            nudUnitFaction.ValueChanged += UnitPropertyChanged;

            nudUnitAgentId.ValueChanged += UnitPropertyChanged;

            nudUnitHp.ValueChanged += UnitPropertyChanged;

            nudUnitMaxHp.ValueChanged += UnitPropertyChanged;

            nudUnitStack.ValueChanged += UnitPropertyChanged;

            nudUnitLevel.ValueChanged += UnitPropertyChanged;

            nudUnitMobility.ValueChanged += UnitPropertyChanged;

            nudUnitDirection.ValueChanged += UnitPropertyChanged;

            nudUnitVal8.ValueChanged += UnitPropertyChanged;

            nudUnitVal9.ValueChanged += UnitPropertyChanged;



            nudUnitPlayMode.ValueChanged += UnitPropertyChanged;

            nudUnitAiTarget.ValueChanged += UnitPropertyChanged;

            nudUnitFactionExtra.ValueChanged += UnitPropertyChanged;



            chkUnitBehavior.CheckedChanged += UnitPropertyChanged;
            chkUnitBehavior.CheckedChanged += (s, e) => UpdateOptionalGroupEnabledStates();

            nudUnitBehaviorField0.ValueChanged += UnitPropertyChanged;

            nudUnitBehaviorId.ValueChanged += UnitPropertyChanged;

            nudUnitBehaviorField2.ValueChanged += UnitPropertyChanged;

            nudUnitBehaviorRadius.ValueChanged += UnitPropertyChanged;

            nudUnitBehaviorField4.ValueChanged += UnitPropertyChanged;

            nudUnitBehaviorCenter.ValueChanged += UnitPropertyChanged;

            nudUnitBehaviorField6.ValueChanged += UnitPropertyChanged;



            chkUnitGeneral.CheckedChanged += UnitPropertyChanged;
            chkUnitGeneral.CheckedChanged += (s, e) => UpdateOptionalGroupEnabledStates();

            nudUnitGeneralId.ValueChanged += UnitPropertyChanged;

            chkUnitGeneralActive.CheckedChanged += UnitPropertyChanged;
            chkUnitGeneralActive.CheckedChanged += (s, e) => UpdateOptionalGroupEnabledStates();

            nudUnitGeneralParam2.ValueChanged += UnitPropertyChanged;



            chkUnitExArmy.CheckedChanged += UnitPropertyChanged;
            chkUnitExArmy.CheckedChanged += (s, e) => UpdateOptionalGroupEnabledStates();

            nudUnitExArmyId.ValueChanged += UnitPropertyChanged;

            nudUnitExArmyHp.ValueChanged += UnitPropertyChanged;

            nudUnitExArmyMaxHp.ValueChanged += UnitPropertyChanged;

            nudUnitExArmyField1.ValueChanged += UnitPropertyChanged;

            nudUnitExArmyField4.ValueChanged += UnitPropertyChanged;

            nudUnitExArmyField5.ValueChanged += UnitPropertyChanged;

        }



        private void InitializeBuildingEditTab()

        {

            tabBuildingEdit.AutoScroll = true;



            TableLayoutPanel tlpMain = new TableLayoutPanel

            {

                Dock = DockStyle.Fill,

                AutoScroll = true,

                ColumnCount = 1,

                Padding = new Padding(10)

            };

            tlpMain.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));

            tabBuildingEdit.Controls.Add(tlpMain);



            lblCellCoordsBuilding = new Label

            {

                Text = "未选中地块",

                Font = new Font("Segoe UI", 10f, FontStyle.Bold),

                AutoSize = true,

                Anchor = AnchorStyles.Left | AnchorStyles.Right,

                Margin = new Padding(0, 0, 0, 10)

            };

            tlpMain.Controls.Add(lblCellCoordsBuilding);



            // 2. 建筑 GroupBox

            gbBuilding = new GroupBox

            {

                Text = "建筑配置",

                Anchor = AnchorStyles.Left | AnchorStyles.Right,

                AutoSize = true,

                AutoSizeMode = AutoSizeMode.GrowAndShrink,

                Margin = new Padding(0, 0, 0, 10)

            };

            tlpMain.Controls.Add(gbBuilding);



            TableLayoutPanel tlpBldg = new TableLayoutPanel

            {

                Dock = DockStyle.Top,

                AutoSize = true,

                Padding = new Padding(5, 18, 5, 5),

                ColumnCount = 3,

                RowCount = 7

            };

            tlpBldg.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

            tlpBldg.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50f));

            tlpBldg.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50f));

            gbBuilding.Controls.Add(tlpBldg);



            Label lblBldgType = new Label { Text = "建筑配置 ID:", Anchor = AnchorStyles.Left, AutoSize = true };

            nudBldgType = new NullableNumericUpDown { Anchor = AnchorStyles.Left | AnchorStyles.Right, Minimum = -1, Maximum = 65535 };
            lblBldgTypeName = new Label { Text = "建筑名称: 空", Anchor = AnchorStyles.Left, AutoSize = true, ForeColor = Color.DarkBlue };

            btnAddDeleteBldg = new Button { Text = "添加建筑", Anchor = AnchorStyles.Left | AnchorStyles.Right, Height = 32 };

            btnAddDeleteBldg.Click += AddDeleteBldgClick;



            Label lblBldgOwner = new Label { Text = "参数 3:", Anchor = AnchorStyles.Left, AutoSize = true };

            nudBldgOwner = new NullableNumericUpDown { Anchor = AnchorStyles.Left | AnchorStyles.Right, Minimum = -1, Maximum = 65535 };



            Label lblBldgFlag = new Label { Text = "所属势力 ID:", Anchor = AnchorStyles.Left, AutoSize = true };

            nudBldgFlag = new NullableNumericUpDown { Anchor = AnchorStyles.Left | AnchorStyles.Right, Minimum = -1, Maximum = 255 };



            Label lblBldgExtraFlag = new Label { Text = "参数 2:", Anchor = AnchorStyles.Left, AutoSize = true };

            nudBldgExtraFlag = new NullableNumericUpDown { Anchor = AnchorStyles.Left | AnchorStyles.Right, Minimum = -1, Maximum = 255 };



            Label lblBldgOffsets = new Label { Text = "贴图偏移 dx / dy:", Anchor = AnchorStyles.Left, AutoSize = true };

            nudBldgDx = new NullableNumericUpDown { Anchor = AnchorStyles.Left | AnchorStyles.Right, Minimum = -129, Maximum = 127 };

            nudBldgDy = new NullableNumericUpDown { Anchor = AnchorStyles.Left | AnchorStyles.Right, Minimum = -129, Maximum = 127 };



            Label lblBldgField6 = new Label { Text = "参数 6:", Anchor = AnchorStyles.Left, AutoSize = true };

            nudBldgField6 = new NullableNumericUpDown { Anchor = AnchorStyles.Left | AnchorStyles.Right, Minimum = -1, Maximum = 255 };



            // Row 0: 参数 0
            tlpBldg.Controls.Add(lblBldgFlag, 0, 0);

            tlpBldg.Controls.Add(nudBldgFlag, 1, 0);


            // Row 1: 建筑配置 ID
            tlpBldg.Controls.Add(lblBldgType, 0, 1);

            tlpBldg.Controls.Add(nudBldgType, 1, 1);

            tlpBldg.Controls.Add(lblBldgTypeName, 2, 1);


            // Row 2: 参数 2
            tlpBldg.Controls.Add(lblBldgExtraFlag, 0, 2);

            tlpBldg.Controls.Add(nudBldgExtraFlag, 1, 2);


            // Row 3: 所属势力 ID
            tlpBldg.Controls.Add(lblBldgOwner, 0, 3);

            tlpBldg.Controls.Add(nudBldgOwner, 1, 3);


            // Row 4: 微调偏移 dx / dy
            tlpBldg.Controls.Add(lblBldgOffsets, 0, 4);

            tlpBldg.Controls.Add(nudBldgDx, 1, 4);

            tlpBldg.Controls.Add(nudBldgDy, 2, 4);


            // Row 5: 参数 6
            tlpBldg.Controls.Add(lblBldgField6, 0, 5);

            tlpBldg.Controls.Add(nudBldgField6, 1, 5);


            // Row 6: 添加/删除建筑 按钮
            tlpBldg.Controls.Add(btnAddDeleteBldg, 0, 6);
            tlpBldg.SetColumnSpan(btnAddDeleteBldg, 3);



            nudBldgType.ValueChanged += (s, ev) => {
                var bldgId = (ushort?)nudBldgType.NullableValue;
                lblBldgTypeName.Text = "名: " + (bldgId.HasValue ? GameSettings.GetBuildingName(bldgId.Value) : "空");
            };

            nudBldgType.ValueChanged += BuildingPropertyChanged;

            nudBldgOwner.ValueChanged += BuildingPropertyChanged;

            nudBldgFlag.ValueChanged += BuildingPropertyChanged;

            nudBldgExtraFlag.ValueChanged += BuildingPropertyChanged;

            nudBldgDx.ValueChanged += BuildingPropertyChanged;

            nudBldgDy.ValueChanged += BuildingPropertyChanged;

            nudBldgField6.ValueChanged += BuildingPropertyChanged;



            // 3. 工事 GroupBox

            gbFort = new GroupBox

            {

                Text = "军事工事配置",

                Anchor = AnchorStyles.Left | AnchorStyles.Right,

                AutoSize = true,

                AutoSizeMode = AutoSizeMode.GrowAndShrink,

                Margin = new Padding(0, 0, 0, 10)

            };

            tlpMain.Controls.Add(gbFort);



            TableLayoutPanel tlpFort = new TableLayoutPanel

            {

                Dock = DockStyle.Top,

                AutoSize = true,

                Padding = new Padding(5, 18, 5, 5),

                ColumnCount = 3,

                RowCount = 4

            };

            tlpFort.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

            tlpFort.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50f));

            tlpFort.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50f));

            gbFort.Controls.Add(tlpFort);



            Label lblFortType = new Label { Text = "工事配置 ID:", Anchor = AnchorStyles.Left, AutoSize = true };

            nudFortType = new NullableNumericUpDown { Anchor = AnchorStyles.Left | AnchorStyles.Right, Minimum = -1, Maximum = 255 };
            lblFortTypeName = new Label { Text = "工事名称: 空", Anchor = AnchorStyles.Left, AutoSize = true, ForeColor = Color.DarkBlue };

            btnAddDeleteFort = new Button { Text = "添加工事", Anchor = AnchorStyles.Left | AnchorStyles.Right, Height = 32 };

            btnAddDeleteFort.Click += AddDeleteFortClick;



            Label lblFortField1 = new Label { Text = "参数 2:", Anchor = AnchorStyles.Left, AutoSize = true };

            nudFortField1 = new NullableNumericUpDown { Anchor = AnchorStyles.Left | AnchorStyles.Right, Minimum = -1, Maximum = 65535 };



            Label lblFortField3 = new Label { Text = "参数 3:", Anchor = AnchorStyles.Left, AutoSize = true };

            nudFortField3 = new NullableNumericUpDown { Anchor = AnchorStyles.Left | AnchorStyles.Right, Minimum = -1, Maximum = 65535 };



            // Row 0: 工事配置 ID
            tlpFort.Controls.Add(lblFortType, 0, 0);

            tlpFort.Controls.Add(nudFortType, 1, 0);

            tlpFort.Controls.Add(lblFortTypeName, 2, 0);


            // Row 1: 事件参数
            tlpFort.Controls.Add(lblFortField1, 0, 1);

            tlpFort.Controls.Add(nudFortField1, 1, 1);


            // Row 2: 反向偏移
            tlpFort.Controls.Add(lblFortField3, 0, 2);

            tlpFort.Controls.Add(nudFortField3, 1, 2);


            // Row 3: 添加/删除工事 按钮
            tlpFort.Controls.Add(btnAddDeleteFort, 0, 3);
            tlpFort.SetColumnSpan(btnAddDeleteFort, 3);



            nudFortType.ValueChanged += (s, ev) => {
                var fortId = (int?)nudFortType.NullableValue;
                lblFortTypeName.Text = "工事名称: " + (fortId.HasValue ? GameSettings.GetFortName(fortId.Value) : "空");
            };

            nudFortType.ValueChanged += FortPropertyChanged;

            nudFortField1.ValueChanged += FortPropertyChanged;

            nudFortField3.ValueChanged += FortPropertyChanged;

        }



        private void InitializeFactionsTab()

        {

            TableLayoutPanel tlpFactionsMain = new TableLayoutPanel

            {

                Dock = DockStyle.Fill,

                ColumnCount = 2,

                RowCount = 1,

                Padding = new Padding(10)

            };

            tlpFactionsMain.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 180));

            tlpFactionsMain.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));

            tabFactions.Controls.Add(tlpFactionsMain);



            TableLayoutPanel tlpLeftFactions = new TableLayoutPanel

            {

                Dock = DockStyle.Fill,

                ColumnCount = 1,

                RowCount = 4,

                Margin = new Padding(0)

            };

            tlpLeftFactions.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));

            tlpLeftFactions.RowStyles.Add(new RowStyle(SizeType.Absolute, 30));

            tlpLeftFactions.RowStyles.Add(new RowStyle(SizeType.Absolute, 30));



            lbFactions = new ListBox { Dock = DockStyle.Fill, Margin = new Padding(0, 0, 0, 5), AllowDrop = true };

            lbFactions.SelectedIndexChanged += SelectedFactionIndexChanged;

            lbFactions.MouseDown += LbFactionsMouseDown;

            lbFactions.MouseMove += LbFactionsMouseMove;

            lbFactions.DragOver += LbFactionsDragOver;

            lbFactions.DragDrop += LbFactionsDragDrop;

            tlpLeftFactions.Controls.Add(lbFactions, 0, 0);



            Button btnAddFaction = new Button { Text = "添加阵营", Dock = DockStyle.Fill, Margin = new Padding(0, 0, 0, 5) };

            btnAddFaction.Click += AddFactionClick;

            tlpLeftFactions.Controls.Add(btnAddFaction, 0, 1);



            Button btnDeleteFaction = new Button { Text = "删除阵营", Dock = DockStyle.Fill, Margin = new Padding(0, 0, 0, 0) };

            btnDeleteFaction.Click += DeleteFactionClick;

            tlpLeftFactions.Controls.Add(btnDeleteFaction, 0, 2);



            tlpFactionsMain.Controls.Add(tlpLeftFactions, 0, 0);



            Panel pnlFactionDetail = new Panel { Dock = DockStyle.Fill, AutoScroll = true, Margin = new Padding(0) };

            tlpFactionsMain.Controls.Add(pnlFactionDetail, 1, 0);



            TableLayoutPanel tlpFaction = new TableLayoutPanel

            {

                Dock = DockStyle.Top,

                AutoSize = true,

                ColumnCount = 2,

                RowCount = 16

            };

            tlpFaction.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

            tlpFaction.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));

            for (int i = 0; i < 16; i++)

            {

                tlpFaction.RowStyles.Add(new RowStyle(SizeType.AutoSize));

            }

            pnlFactionDetail.Controls.Add(tlpFaction);



            Label lblId = new Label { Text = "战役内势力 ID:", Anchor = AnchorStyles.Left, AutoSize = true };

            nudFactionId = new NullableNumericUpDown { Anchor = AnchorStyles.Left | AnchorStyles.Right, Minimum = -1, Maximum = 65535 };



            Label lblCamp = new Label { Text = "所属阵营 ID:", Anchor = AnchorStyles.Left, AutoSize = true };

            nudFactionCamp = new NullableNumericUpDown { Anchor = AnchorStyles.Left | AnchorStyles.Right, Minimum = -1, Maximum = 9 };



            Label lblCountry = new Label { Text = "国家 ID:", Anchor = AnchorStyles.Left, AutoSize = true };

            nudFactionCountry = new NullableNumericUpDown { Anchor = AnchorStyles.Left | AnchorStyles.Right, Minimum = -1, Maximum = 65535 };
            lblFactionCountryName = new Label { Text = "(未知国家)", Anchor = AnchorStyles.Left, AutoSize = true, ForeColor = Color.FromArgb(71, 85, 105) };

            TableLayoutPanel tlpCountry = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1, AutoSize = true, Margin = new Padding(0) };
            tlpCountry.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50f));
            tlpCountry.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50f));
            tlpCountry.Controls.Add(nudFactionCountry, 0, 0);
            tlpCountry.Controls.Add(lblFactionCountryName, 1, 0);



            Label lblIsAI = new Label { Text = "控制权类型:", Anchor = AnchorStyles.Left, AutoSize = true };

            nudFactionIsAI = new NullableNumericUpDown { Anchor = AnchorStyles.Left | AnchorStyles.Right, Minimum = -1, Maximum = 255 };



            Label lblVal5 = new Label { Text = "参数 4:", Anchor = AnchorStyles.Left, AutoSize = true };

            nudFactionVal5 = new NullableNumericUpDown { Anchor = AnchorStyles.Left | AnchorStyles.Right, Minimum = -1, Maximum = 255 };



            Label lblAlign1 = new Label { Text = "占位对齐 1:", Anchor = AnchorStyles.Left, AutoSize = true };

            nudFactionAlign1 = new NullableNumericUpDown { Anchor = AnchorStyles.Left | AnchorStyles.Right, Minimum = -1, Maximum = 255 };



            Label lblGold = new Label { Text = "初始金币资源:", Anchor = AnchorStyles.Left, AutoSize = true };

            nudFactionGold = new NullableNumericUpDown { Anchor = AnchorStyles.Left | AnchorStyles.Right, Minimum = -1, Maximum = 9999999 };



            Label lblTech = new Label { Text = "初始科技资源:", Anchor = AnchorStyles.Left, AutoSize = true };

            nudFactionTech = new NullableNumericUpDown { Anchor = AnchorStyles.Left | AnchorStyles.Right, Minimum = -1, Maximum = 9999999 };



            Label lblIncome = new Label { Text = "阵营修正 1:", Anchor = AnchorStyles.Left, AutoSize = true };

            nudFactionIncomeMod = new NullableNumericUpDown { Anchor = AnchorStyles.Left | AnchorStyles.Right, DecimalPlaces = 2, Increment = 0.05M, Minimum = -1, Maximum = 10 };



            Label lblDamage = new Label { Text = "阵营修正 2:", Anchor = AnchorStyles.Left, AutoSize = true };

            nudFactionDamageMod = new NullableNumericUpDown { Anchor = AnchorStyles.Left | AnchorStyles.Right, DecimalPlaces = 2, Increment = 0.05M, Minimum = -1, Maximum = 10 };



            Label lblHpMod = new Label { Text = "部队生命值倍率修正:", Anchor = AnchorStyles.Left, AutoSize = true };

            nudFactionHpMod = new NullableNumericUpDown { Anchor = AnchorStyles.Left | AnchorStyles.Right, DecimalPlaces = 2, Increment = 0.05M, Minimum = -1, Maximum = 10 };



            Label lblColor = new Label { Text = "代表颜色 (RGBA):", Anchor = AnchorStyles.Left, AutoSize = true };

            TableLayoutPanel tlpColor = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 8, RowCount = 1, AutoSize = true, Margin = new Padding(0) };

            tlpColor.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

            tlpColor.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 25f));

            tlpColor.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

            tlpColor.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 25f));

            tlpColor.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

            tlpColor.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 25f));

            tlpColor.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

            tlpColor.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 25f));



            Label lblR = new Label { Text = "R:", AutoSize = true, Anchor = AnchorStyles.Left };

            nudFactionColorR = new NullableNumericUpDown { Minimum = -1, Maximum = 255, Value = -1, Anchor = AnchorStyles.Left | AnchorStyles.Right };

            Label lblG = new Label { Text = "G:", AutoSize = true, Anchor = AnchorStyles.Left };

            nudFactionColorG = new NullableNumericUpDown { Minimum = -1, Maximum = 255, Value = -1, Anchor = AnchorStyles.Left | AnchorStyles.Right };

            Label lblB = new Label { Text = "B:", AutoSize = true, Anchor = AnchorStyles.Left };

            nudFactionColorB = new NullableNumericUpDown { Minimum = -1, Maximum = 255, Value = -1, Anchor = AnchorStyles.Left | AnchorStyles.Right };

            Label lblA = new Label { Text = "A:", AutoSize = true, Anchor = AnchorStyles.Left };

            nudFactionColorA = new NullableNumericUpDown { Minimum = -1, Maximum = 255, Value = -1, Anchor = AnchorStyles.Left | AnchorStyles.Right };



            tlpColor.Controls.Add(lblR, 0, 0);

            tlpColor.Controls.Add(nudFactionColorR, 1, 0);

            tlpColor.Controls.Add(lblG, 2, 0);

            tlpColor.Controls.Add(nudFactionColorG, 3, 0);

            tlpColor.Controls.Add(lblB, 4, 0);

            tlpColor.Controls.Add(nudFactionColorB, 5, 0);

            tlpColor.Controls.Add(lblA, 6, 0);

            tlpColor.Controls.Add(nudFactionColorA, 7, 0);



            Label lblAlign2 = new Label { Text = "占位对齐 2:", Anchor = AnchorStyles.Left, AutoSize = true };

            nudFactionAlign2 = new NullableNumericUpDown { Anchor = AnchorStyles.Left | AnchorStyles.Right, Minimum = -1, Maximum = 65535 };



            Label lblConfigId = new Label { Text = "参数 13:", Anchor = AnchorStyles.Left, AutoSize = true };

            nudFactionConfigId = new NullableNumericUpDown { Anchor = AnchorStyles.Left | AnchorStyles.Right, Minimum = -1, Maximum = 65535 };



            Label lblGenFlag = new Label { Text = "参数 14:", Anchor = AnchorStyles.Left, AutoSize = true };

            nudFactionGeneralFlag = new NullableNumericUpDown { Anchor = AnchorStyles.Left | AnchorStyles.Right, Minimum = -1, Maximum = 255 };



            Label lblConfigRef = new Label { Text = "参数 15:", Anchor = AnchorStyles.Left, AutoSize = true };

            nudFactionConfigRef = new NullableNumericUpDown { Anchor = AnchorStyles.Left | AnchorStyles.Right, Minimum = -1, Maximum = 65535 };



            tlpFaction.Controls.Add(lblId, 0, 0);

            tlpFaction.Controls.Add(nudFactionId, 1, 0);



            tlpFaction.Controls.Add(lblCamp, 0, 1);

            tlpFaction.Controls.Add(nudFactionCamp, 1, 1);



            tlpFaction.Controls.Add(lblCountry, 0, 2);

            tlpFaction.Controls.Add(tlpCountry, 1, 2);



            tlpFaction.Controls.Add(lblIsAI, 0, 3);

            tlpFaction.Controls.Add(nudFactionIsAI, 1, 3);



            tlpFaction.Controls.Add(lblVal5, 0, 4);

            tlpFaction.Controls.Add(nudFactionVal5, 1, 4);



            tlpFaction.Controls.Add(lblAlign1, 0, 5);

            tlpFaction.Controls.Add(nudFactionAlign1, 1, 5);



            tlpFaction.Controls.Add(lblGold, 0, 6);

            tlpFaction.Controls.Add(nudFactionGold, 1, 6);



            tlpFaction.Controls.Add(lblTech, 0, 7);

            tlpFaction.Controls.Add(nudFactionTech, 1, 7);



            tlpFaction.Controls.Add(lblIncome, 0, 8);

            tlpFaction.Controls.Add(nudFactionIncomeMod, 1, 8);



            tlpFaction.Controls.Add(lblDamage, 0, 9);

            tlpFaction.Controls.Add(nudFactionDamageMod, 1, 9);



            tlpFaction.Controls.Add(lblHpMod, 0, 10);

            tlpFaction.Controls.Add(nudFactionHpMod, 1, 10);



            tlpFaction.Controls.Add(lblColor, 0, 11);

            tlpFaction.Controls.Add(tlpColor, 1, 11);



            tlpFaction.Controls.Add(lblAlign2, 0, 12);

            tlpFaction.Controls.Add(nudFactionAlign2, 1, 12);



            tlpFaction.Controls.Add(lblConfigId, 0, 13);

            tlpFaction.Controls.Add(nudFactionConfigId, 1, 13);



            tlpFaction.Controls.Add(lblGenFlag, 0, 14);

            tlpFaction.Controls.Add(nudFactionGeneralFlag, 1, 14);



            tlpFaction.Controls.Add(lblConfigRef, 0, 15);

            tlpFaction.Controls.Add(nudFactionConfigRef, 1, 15);



            nudFactionId.ValueChanged += FactionPropertyChanged;

            nudFactionCamp.ValueChanged += FactionPropertyChanged;

            nudFactionCountry.ValueChanged += FactionPropertyChanged;

            nudFactionIsAI.ValueChanged += FactionPropertyChanged;

            nudFactionVal5.ValueChanged += FactionPropertyChanged;

            nudFactionAlign1.ValueChanged += FactionPropertyChanged;

            nudFactionGold.ValueChanged += FactionPropertyChanged;

            nudFactionTech.ValueChanged += FactionPropertyChanged;

            nudFactionIncomeMod.ValueChanged += FactionPropertyChanged;

            nudFactionDamageMod.ValueChanged += FactionPropertyChanged;

            nudFactionHpMod.ValueChanged += FactionPropertyChanged;

            nudFactionColorR.ValueChanged += FactionPropertyChanged;

            nudFactionColorG.ValueChanged += FactionPropertyChanged;

            nudFactionColorB.ValueChanged += FactionPropertyChanged;

            nudFactionColorA.ValueChanged += FactionPropertyChanged;

            nudFactionAlign2.ValueChanged += FactionPropertyChanged;

            nudFactionConfigId.ValueChanged += FactionPropertyChanged;

            nudFactionGeneralFlag.ValueChanged += FactionPropertyChanged;

            nudFactionConfigRef.ValueChanged += FactionPropertyChanged;

        }



        private void InitializeCountryBtTab()

        {

            tabCountryBt.AutoScroll = true;



            TableLayoutPanel tlpBtMain = new TableLayoutPanel

            {

                Dock = DockStyle.Top,

                AutoSize = true,

                AutoSizeMode = AutoSizeMode.GrowAndShrink,

                ColumnCount = 1,

                Padding = new Padding(10)

            };

            tlpBtMain.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));

            tabCountryBt.Controls.Add(tlpBtMain);



            Label lblBtTrees = new Label { Text = "行为树列表:", Font = new Font("Segoe UI", 9f, FontStyle.Bold), AutoSize = true, Margin = new Padding(0, 0, 0, 5) };

            lvBtTrees = new ListView { Anchor = AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Top, Height = 140, View = View.Details, FullRowSelect = true, GridLines = true, Margin = new Padding(0, 0, 0, 10) };

            lvBtTrees.Columns.Add("btid", 70);

            lvBtTrees.Columns.Add("name", 140);

            lvBtTrees.Columns.Add("agent", 170);

            lvBtTrees.Columns.Add("class", 100);

            lvBtTrees.Columns.Add("id", 70);

            tlpBtMain.Controls.Add(lblBtTrees);

            tlpBtMain.Controls.Add(lvBtTrees);

            lvBtTrees.SelectedIndexChanged += LvBtTreesSelectedIndexChanged;



            TableLayoutPanel tlpBtTreeButtons = new TableLayoutPanel

            {

                Anchor = AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Top,

                Height = 35,

                ColumnCount = 2,

                RowCount = 1,

                Margin = new Padding(0, 0, 0, 10)

            };

            tlpBtTreeButtons.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50f));

            tlpBtTreeButtons.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50f));



            Button btnAddBt = new Button { Text = "添加行为树", Dock = DockStyle.Fill };

            btnAddBt.Click += AddBtTreeClick;

            Button btnDeleteBt = new Button { Text = "删除行为树", Dock = DockStyle.Fill };

            btnDeleteBt.Click += DeleteBtTreeClick;



            tlpBtTreeButtons.Controls.Add(btnAddBt, 0, 0);

            tlpBtTreeButtons.Controls.Add(btnDeleteBt, 1, 0);

            tlpBtMain.Controls.Add(tlpBtTreeButtons);



            Label lblBtNodes = new Label { Text = "节点树:", Font = new Font("Segoe UI", 9f, FontStyle.Bold), AutoSize = true, Margin = new Padding(0, 10, 0, 5) };

            tvBtNodes = new TreeView { Anchor = AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Top, Height = 180, FullRowSelect = true, HideSelection = false, Margin = new Padding(0, 0, 0, 10) };

            tlpBtMain.Controls.Add(lblBtNodes);

            tlpBtMain.Controls.Add(tvBtNodes);

            tvBtNodes.AfterSelect += TvBtNodesAfterSelect;



            TableLayoutPanel tlpBtNodeButtons = new TableLayoutPanel

            {

                Anchor = AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Top,

                Height = 35,

                ColumnCount = 4,

                RowCount = 1,

                Margin = new Padding(0, 0, 0, 10)

            };

            tlpBtNodeButtons.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 25f));

            tlpBtNodeButtons.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 25f));

            tlpBtNodeButtons.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 25f));

            tlpBtNodeButtons.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 25f));



            Button btnAddNodeTree = new Button { Text = "添加节点树", Dock = DockStyle.Fill };

            btnAddNodeTree.Click += AddBtNodeTreeClick;

            Button btnDeleteNodeTree = new Button { Text = "删除节点树", Dock = DockStyle.Fill };

            btnDeleteNodeTree.Click += DeleteBtNodeTreeClick;

            Button btnAddNode = new Button { Text = "添加节点", Dock = DockStyle.Fill };

            btnAddNode.Click += AddBtNodeClick;

            Button btnDeleteNode = new Button { Text = "删除节点", Dock = DockStyle.Fill };

            btnDeleteNode.Click += DeleteBtNodeClick;



            tlpBtNodeButtons.Controls.Add(btnAddNodeTree, 0, 0);

            tlpBtNodeButtons.Controls.Add(btnDeleteNodeTree, 1, 0);

            tlpBtNodeButtons.Controls.Add(btnAddNode, 2, 0);

            tlpBtNodeButtons.Controls.Add(btnDeleteNode, 3, 0);

            tlpBtMain.Controls.Add(tlpBtNodeButtons);



            GroupBox gbBtTree = new GroupBox

            {

                Text = "行为树属性",

                Anchor = AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Top,

                AutoSize = true,

                AutoSizeMode = AutoSizeMode.GrowAndShrink,

                Margin = new Padding(0, 0, 0, 10)

            };

            tlpBtMain.Controls.Add(gbBtTree);



            TableLayoutPanel tlpBtTree = new TableLayoutPanel

            {

                Dock = DockStyle.Top,

                AutoSize = true,

                Padding = new Padding(5, 15, 5, 5),

                ColumnCount = 2,

                RowCount = 5

            };

            tlpBtTree.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 100));

            tlpBtTree.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));

            for (int i = 0; i < 5; i++)

            {

                tlpBtTree.RowStyles.Add(new RowStyle(SizeType.AutoSize));

            }

            gbBtTree.Controls.Add(tlpBtTree);



            nudBtTid = new NullableNumericUpDown { Anchor = AnchorStyles.Left | AnchorStyles.Right, Minimum = 0, Maximum = 65535 };

            nudBtId = new NullableNumericUpDown { Anchor = AnchorStyles.Left | AnchorStyles.Right, Minimum = 0, Maximum = 65535 };

            txtBtName = new TextBox { Anchor = AnchorStyles.Left | AnchorStyles.Right };

            txtBtAgent = new TextBox { Anchor = AnchorStyles.Left | AnchorStyles.Right };

            txtBtClass = new TextBox { Anchor = AnchorStyles.Left | AnchorStyles.Right };



            tlpBtTree.Controls.Add(new Label { Text = "btid:", Anchor = AnchorStyles.Left, AutoSize = true }, 0, 0);

            tlpBtTree.Controls.Add(nudBtTid, 1, 0);

            tlpBtTree.Controls.Add(new Label { Text = "id:", Anchor = AnchorStyles.Left, AutoSize = true }, 0, 1);

            tlpBtTree.Controls.Add(nudBtId, 1, 1);

            tlpBtTree.Controls.Add(new Label { Text = "name:", Anchor = AnchorStyles.Left, AutoSize = true }, 0, 2);

            tlpBtTree.Controls.Add(txtBtName, 1, 2);

            tlpBtTree.Controls.Add(new Label { Text = "agent:", Anchor = AnchorStyles.Left, AutoSize = true }, 0, 3);

            tlpBtTree.Controls.Add(txtBtAgent, 1, 3);

            tlpBtTree.Controls.Add(new Label { Text = "class:", Anchor = AnchorStyles.Left, AutoSize = true }, 0, 4);

            tlpBtTree.Controls.Add(txtBtClass, 1, 4);



            GroupBox gbBtNode = new GroupBox

            {

                Text = "节点属性",

                Anchor = AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Top,

                AutoSize = true,

                AutoSizeMode = AutoSizeMode.GrowAndShrink,

                Margin = new Padding(0, 0, 0, 10)

            };

            tlpBtMain.Controls.Add(gbBtNode);



            TableLayoutPanel tlpBtNode = new TableLayoutPanel

            {

                Dock = DockStyle.Top,

                AutoSize = true,

                Padding = new Padding(5, 15, 5, 5),

                ColumnCount = 2,

                RowCount = 7

            };

            tlpBtNode.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 100));

            tlpBtNode.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));

            for (int i = 0; i < 7; i++)

            {

                tlpBtNode.RowStyles.Add(new RowStyle(SizeType.AutoSize));

            }

            gbBtNode.Controls.Add(tlpBtNode);



            nudBtNodeId = new NullableNumericUpDown { Anchor = AnchorStyles.Left | AnchorStyles.Right, Minimum = 0, Maximum = 65535 };

            txtBtNodeClass = new TextBox { Anchor = AnchorStyles.Left | AnchorStyles.Right };

            txtBtMethod = new TextBox { Anchor = AnchorStyles.Left | AnchorStyles.Right };

            txtBtParams = new TextBox { Anchor = AnchorStyles.Left | AnchorStyles.Right };

            txtBtRounds = new TextBox { Anchor = AnchorStyles.Left | AnchorStyles.Right };

            nudBtResult = new NullableNumericUpDown { Anchor = AnchorStyles.Left | AnchorStyles.Right, Minimum = -32768, Maximum = 65535 };

            nudBtCount = new NullableNumericUpDown { Anchor = AnchorStyles.Left | AnchorStyles.Right, Minimum = 0, Maximum = 65535 };



            tlpBtNode.Controls.Add(new Label { Text = "节点 id:", Anchor = AnchorStyles.Left, AutoSize = true }, 0, 0);

            tlpBtNode.Controls.Add(nudBtNodeId, 1, 0);

            tlpBtNode.Controls.Add(new Label { Text = "class:", Anchor = AnchorStyles.Left, AutoSize = true }, 0, 1);

            tlpBtNode.Controls.Add(txtBtNodeClass, 1, 1);

            tlpBtNode.Controls.Add(new Label { Text = "method:", Anchor = AnchorStyles.Left, AutoSize = true }, 0, 2);

            tlpBtNode.Controls.Add(txtBtMethod, 1, 2);

            tlpBtNode.Controls.Add(new Label { Text = "params:", Anchor = AnchorStyles.Left, AutoSize = true }, 0, 3);

            tlpBtNode.Controls.Add(txtBtParams, 1, 3);

            tlpBtNode.Controls.Add(new Label { Text = "rounds:", Anchor = AnchorStyles.Left, AutoSize = true }, 0, 4);

            tlpBtNode.Controls.Add(txtBtRounds, 1, 4);

            tlpBtNode.Controls.Add(new Label { Text = "result:", Anchor = AnchorStyles.Left, AutoSize = true }, 0, 5);

            tlpBtNode.Controls.Add(nudBtResult, 1, 5);

            tlpBtNode.Controls.Add(new Label { Text = "count:", Anchor = AnchorStyles.Left, AutoSize = true }, 0, 6);

            tlpBtNode.Controls.Add(nudBtCount, 1, 6);



            TableLayoutPanel tlpBtBtns = new TableLayoutPanel

            {

                Anchor = AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Top,

                Height = 35,

                ColumnCount = 1,

                RowCount = 1,

                Margin = new Padding(0, 10, 0, 0)

            };

            tlpBtBtns.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));

            Button btnSaveBt = new Button { Text = "保存修改", Dock = DockStyle.Fill };

            btnSaveBt.Click += SaveBtTreeClick;

            tlpBtBtns.Controls.Add(btnSaveBt, 0, 0);

            tlpBtMain.Controls.Add(tlpBtBtns);

        }



        private void InitializeStageInfoTab()

        {

            tabStageInfo.AutoScroll = true;



            TableLayoutPanel tlpStageMain = new TableLayoutPanel

            {

                Dock = DockStyle.Top,

                AutoSize = true,

                AutoSizeMode = AutoSizeMode.GrowAndShrink,

                ColumnCount = 1,

                RowCount = 10,

                Padding = new Padding(10)

            };

            tlpStageMain.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));

            for (int i = 0; i < 10; i++)

            {

                tlpStageMain.RowStyles.Add(new RowStyle(SizeType.AutoSize));

            }

            tabStageInfo.Controls.Add(tlpStageMain);



            // 基础信息 (元数据)

            TableLayoutPanel tlpMeta = new TableLayoutPanel

            {

                Anchor = AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Top,

                AutoSize = true,

                AutoSizeMode = AutoSizeMode.GrowAndShrink,

                ColumnCount = 2,

                RowCount = 1,

                Margin = new Padding(0)

            };

            tlpMeta.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 120));

            tlpMeta.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));

            for (int i = 0; i < 1; i++)

            {

                tlpMeta.RowStyles.Add(new RowStyle(SizeType.AutoSize));

            }

            tlpStageMain.Controls.Add(tlpMeta);

            CreateStageSlot(tlpStageMain);



            chkFog = new CheckBox { Text = "开启战争迷雾", Anchor = AnchorStyles.Left, AutoSize = true, Margin = new Padding(0, 8, 0, 0) };

            chkFog.CheckedChanged += FogCheckChanged;

            tlpMeta.Controls.Add(chkFog, 0, 0);

            tlpMeta.SetColumnSpan(chkFog, 2);



            // 挑战目标列表

            Label lblTargets = new Label { Text = "关卡目标:", Font = new Font("Segoe UI", 9f, FontStyle.Bold), AutoSize = true, Margin = new Padding(0, 10, 0, 5) };

            lvTargets = new ListView { Anchor = AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Top, Height = 180, View = View.Details, FullRowSelect = true, GridLines = true, Margin = new Padding(0, 0, 0, 10) };

            lvTargets.Columns.Add("目标类别 ID", 100);

            lvTargets.Columns.Add("目标值", 100);

            lvTargets.Columns.Add("参数1", 90);

            lvTargets.Columns.Add("参数2", 90);

            _stageSlot.Controls.Add(lblTargets);

            _stageSlot.Controls.Add(lvTargets);



            // 挑战目标编辑区域 (嵌入式)

            TableLayoutPanel tlpTargetsEdit = new TableLayoutPanel

            {

                Anchor = AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Top,

                AutoSize = true,

                ColumnCount = 5,

                RowCount = 2,

                Margin = new Padding(0, 0, 0, 10)

            };

            for (int i = 0; i < 5; i++)

            {

                tlpTargetsEdit.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 20f));

            }

            tlpTargetsEdit.RowStyles.Add(new RowStyle(SizeType.AutoSize));

            tlpTargetsEdit.RowStyles.Add(new RowStyle(SizeType.AutoSize));



            nudTargetType = new NullableNumericUpDown { Anchor = AnchorStyles.Left | AnchorStyles.Right, Minimum = -1, Maximum = 65535 };

            nudTargetValue = new NullableNumericUpDown { Anchor = AnchorStyles.Left | AnchorStyles.Right, Minimum = -32769, Maximum = 32767 };

            nudTargetParam1 = new NullableNumericUpDown { Anchor = AnchorStyles.Left | AnchorStyles.Right, Minimum = -1, Maximum = 65535 };

            nudTargetParam2 = new NullableNumericUpDown { Anchor = AnchorStyles.Left | AnchorStyles.Right, Minimum = -1, Maximum = 65535 };

            nudTargetFlag = new NullableNumericUpDown { Anchor = AnchorStyles.Left | AnchorStyles.Right, Minimum = -1, Maximum = 255 };



            tlpTargetsEdit.Controls.Add(new Label { Text = "类别 ID:", AutoSize = true, Anchor = AnchorStyles.Left }, 0, 0);

            tlpTargetsEdit.Controls.Add(nudTargetType, 0, 1);

            tlpTargetsEdit.Controls.Add(new Label { Text = "目标值:", AutoSize = true, Anchor = AnchorStyles.Left }, 1, 0);

            tlpTargetsEdit.Controls.Add(nudTargetValue, 1, 1);

            tlpTargetsEdit.Controls.Add(new Label { Text = "参数 1:", AutoSize = true, Anchor = AnchorStyles.Left }, 2, 0);

            tlpTargetsEdit.Controls.Add(nudTargetParam1, 2, 1);

            tlpTargetsEdit.Controls.Add(new Label { Text = "参数 2:", AutoSize = true, Anchor = AnchorStyles.Left }, 3, 0);

            tlpTargetsEdit.Controls.Add(nudTargetParam2, 3, 1);

            tlpTargetsEdit.Controls.Add(new Label { Text = "标志/状态:", AutoSize = true, Anchor = AnchorStyles.Left }, 4, 0);

            tlpTargetsEdit.Controls.Add(nudTargetFlag, 4, 1);



            // 操作按钮

            TableLayoutPanel tlpTargetsBtns = new TableLayoutPanel

            {

                Anchor = AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Top,

                Height = 35,

                ColumnCount = 3,

                RowCount = 1,

                Margin = new Padding(0, 0, 0, 15)

            };

            tlpTargetsBtns.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33.33f));

            tlpTargetsBtns.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33.33f));

            tlpTargetsBtns.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33.33f));



            Button btnAddTarget = new Button { Text = "添加目标条件", Dock = DockStyle.Fill };

            btnAddTarget.Click += AddTargetClick;

            Button btnUpdateTarget = new Button { Text = "保存选中修改", Dock = DockStyle.Fill };

            btnUpdateTarget.Click += UpdateTargetClick;

            Button btnDeleteTarget = new Button { Text = "删除选中目标", Dock = DockStyle.Fill };

            btnDeleteTarget.Click += DeleteTargetClick;



            tlpTargetsBtns.Controls.Add(btnAddTarget, 0, 0);

            tlpTargetsBtns.Controls.Add(btnUpdateTarget, 1, 0);

            tlpTargetsBtns.Controls.Add(btnDeleteTarget, 2, 0);



            _stageSlot.Controls.Add(tlpTargetsEdit);

            _stageSlot.Controls.Add(tlpTargetsBtns);

            lvTargets.SelectedIndexChanged += LvTargetsSelectedIndexChanged;



            // 增员部署点列表

            Label lblReinforces = new Label { Text = "增兵部署点配置:", Font = new Font("Segoe UI", 9f, FontStyle.Bold), AutoSize = true, Margin = new Padding(0, 10, 0, 5) };

            lvReinforces = new ListView { Anchor = AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Top, Height = 180, View = View.Details, FullRowSelect = true, GridLines = true, Margin = new Padding(0, 0, 0, 10) };

            lvReinforces.Columns.Add("格子索引", 90);

            lvReinforces.Columns.Add("所属势力 ID", 100);

            lvReinforces.Columns.Add("参数1", 90);

            lvReinforces.Columns.Add("参数2", 90);

            _stageSlot.Controls.Add(lblReinforces);

            _stageSlot.Controls.Add(lvReinforces);



            // 部署点编辑区域 (嵌入式)

            TableLayoutPanel tlpReinforcesEdit = new TableLayoutPanel

            {

                Anchor = AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Top,

                AutoSize = true,

                ColumnCount = 4,

                RowCount = 2,

                Margin = new Padding(0, 0, 0, 10)

            };

            for (int i = 0; i < 4; i++)

            {

                tlpReinforcesEdit.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 25f));

            }

            tlpReinforcesEdit.RowStyles.Add(new RowStyle(SizeType.AutoSize));

            tlpReinforcesEdit.RowStyles.Add(new RowStyle(SizeType.AutoSize));



            nudRpCellIdx = new NullableNumericUpDown { Anchor = AnchorStyles.Left | AnchorStyles.Right, Minimum = -1, Maximum = 65535 };

            nudRpFactionId = new NullableNumericUpDown { Anchor = AnchorStyles.Left | AnchorStyles.Right, Minimum = -1, Maximum = 255 };

            chkRpIsKey = new CheckBox { Text = "参数1", Anchor = AnchorStyles.Left, AutoSize = true, Margin = new Padding(5, 5, 0, 0) };

            nudRpFlag = new NullableNumericUpDown { Anchor = AnchorStyles.Left | AnchorStyles.Right, Minimum = -1, Maximum = 255 };



            tlpReinforcesEdit.Controls.Add(new Label { Text = "格子索引:", AutoSize = true, Anchor = AnchorStyles.Left }, 0, 0);

            tlpReinforcesEdit.Controls.Add(nudRpCellIdx, 0, 1);

            tlpReinforcesEdit.Controls.Add(new Label { Text = "所属势力 ID:", AutoSize = true, Anchor = AnchorStyles.Left }, 1, 0);

            tlpReinforcesEdit.Controls.Add(nudRpFactionId, 1, 1);

            tlpReinforcesEdit.Controls.Add(new Label { Text = "参数1:", AutoSize = true, Anchor = AnchorStyles.Left }, 2, 0);

            tlpReinforcesEdit.Controls.Add(chkRpIsKey, 2, 1);

            tlpReinforcesEdit.Controls.Add(new Label { Text = "参数2:", AutoSize = true, Anchor = AnchorStyles.Left }, 3, 0);

            tlpReinforcesEdit.Controls.Add(nudRpFlag, 3, 1);



            // 部署点操作按钮

            TableLayoutPanel tlpReinforcesBtns = new TableLayoutPanel

            {

                Anchor = AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Top,

                Height = 35,

                ColumnCount = 3,

                RowCount = 1,

                Margin = new Padding(0, 0, 0, 15)

            };

            tlpReinforcesBtns.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33.33f));

            tlpReinforcesBtns.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33.33f));

            tlpReinforcesBtns.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33.33f));



            Button btnAddReinforce = new Button { Text = "添加部署点", Dock = DockStyle.Fill };

            btnAddReinforce.Click += AddReinforceClick;

            Button btnUpdateReinforce = new Button { Text = "保存选中修改", Dock = DockStyle.Fill };

            btnUpdateReinforce.Click += UpdateReinforceClick;

            Button btnDeleteReinforce = new Button { Text = "删除选中部署", Dock = DockStyle.Fill };

            btnDeleteReinforce.Click += DeleteReinforceClick;



            tlpReinforcesBtns.Controls.Add(btnAddReinforce, 0, 0);

            tlpReinforcesBtns.Controls.Add(btnUpdateReinforce, 1, 0);

            tlpReinforcesBtns.Controls.Add(btnDeleteReinforce, 2, 0);



            _stageSlot.Controls.Add(tlpReinforcesEdit);

            _stageSlot.Controls.Add(tlpReinforcesBtns);

            lvReinforces.SelectedIndexChanged += LvReinforcesSelectedIndexChanged;

            // 天气配置列表

            Label lblWeathers = new Label { Text = "天气配置:", Font = new Font("Segoe UI", 9f, FontStyle.Bold), AutoSize = true, Margin = new Padding(0, 10, 0, 5) };

            lvWeathers = new ListView { Anchor = AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Top, Height = 150, View = View.Details, FullRowSelect = true, GridLines = true, Margin = new Padding(0, 0, 0, 10) };

            lvWeathers.Columns.Add("天气类型", 100);

            lvWeathers.Columns.Add("起始回合", 100);

            lvWeathers.Columns.Add("持续回合数", 100);

            _stageSlot.Controls.Add(lblWeathers);

            _stageSlot.Controls.Add(lvWeathers);



            // 天气编辑区域

            TableLayoutPanel tlpWeathersEdit = new TableLayoutPanel

            {

                Anchor = AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Top,

                AutoSize = true,

                ColumnCount = 3,

                RowCount = 2,

                Margin = new Padding(0, 0, 0, 10)

            };

            for (int i = 0; i < 3; i++)

            {

                tlpWeathersEdit.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33.33f));

            }

            tlpWeathersEdit.RowStyles.Add(new RowStyle(SizeType.AutoSize));

            tlpWeathersEdit.RowStyles.Add(new RowStyle(SizeType.AutoSize));



            nudWeatherType = new NullableNumericUpDown { Anchor = AnchorStyles.Left | AnchorStyles.Right, Minimum = 0, Maximum = 255 };

            nudWeatherStart = new NullableNumericUpDown { Anchor = AnchorStyles.Left | AnchorStyles.Right, Minimum = 0, Maximum = 65535 };

            nudWeatherDuration = new NullableNumericUpDown { Anchor = AnchorStyles.Left | AnchorStyles.Right, Minimum = 0, Maximum = 65535 };



            tlpWeathersEdit.Controls.Add(new Label { Text = "天气类型:", AutoSize = true, Anchor = AnchorStyles.Left }, 0, 0);

            tlpWeathersEdit.Controls.Add(nudWeatherType, 0, 1);

            tlpWeathersEdit.Controls.Add(new Label { Text = "起始回合:", AutoSize = true, Anchor = AnchorStyles.Left }, 1, 0);

            tlpWeathersEdit.Controls.Add(nudWeatherStart, 1, 1);

            tlpWeathersEdit.Controls.Add(new Label { Text = "持续回合数:", AutoSize = true, Anchor = AnchorStyles.Left }, 2, 0);

            tlpWeathersEdit.Controls.Add(nudWeatherDuration, 2, 1);



            // 天气操作按钮

            TableLayoutPanel tlpWeathersBtns = new TableLayoutPanel

            {

                Anchor = AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Top,

                Height = 35,

                ColumnCount = 3,

                RowCount = 1,

                Margin = new Padding(0, 0, 0, 15)

            };

            tlpWeathersBtns.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33.33f));

            tlpWeathersBtns.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33.33f));

            tlpWeathersBtns.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33.33f));



            Button btnAddWeather = new Button { Text = "添加天气", Dock = DockStyle.Fill };

            btnAddWeather.Click += AddWeatherClick;

            Button btnUpdateWeather = new Button { Text = "保存选中修改", Dock = DockStyle.Fill };

            btnUpdateWeather.Click += UpdateWeatherClick;

            Button btnDeleteWeather = new Button { Text = "删除选中天气", Dock = DockStyle.Fill };

            btnDeleteWeather.Click += DeleteWeatherClick;



            tlpWeathersBtns.Controls.Add(btnAddWeather, 0, 0);

            tlpWeathersBtns.Controls.Add(btnUpdateWeather, 1, 0);

            tlpWeathersBtns.Controls.Add(btnDeleteWeather, 2, 0);



            _stageSlot.Controls.Add(tlpWeathersEdit);

            _stageSlot.Controls.Add(tlpWeathersBtns);

            lvWeathers.SelectedIndexChanged += LvWeathersSelectedIndexChanged;

            TryInstallStageLayout();



            // 4. 地图尺寸与游玩边界 GroupBox

            GroupBox gbMap = new GroupBox

            {

                Text = "地图尺寸与可游玩区域配置",

                Anchor = AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Top,

                AutoSize = true,

                AutoSizeMode = AutoSizeMode.GrowAndShrink,

                Margin = new Padding(0, 10, 0, 10)

            };

            tlpStageMain.Controls.Add(gbMap);



            TableLayoutPanel tlpMap = new TableLayoutPanel

            {

                Dock = DockStyle.Top,

                AutoSize = true,

                Padding = new Padding(5, 15, 5, 5),

                ColumnCount = 2,

                RowCount = 11

            };

            tlpMap.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

            tlpMap.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));

            for (int i = 0; i < 11; i++)

            {

                tlpMap.RowStyles.Add(new RowStyle(SizeType.AutoSize));

            }

            gbMap.Controls.Add(tlpMap);



            Label lblMapW = new Label { Text = "地图总宽度:", Anchor = AnchorStyles.Left, AutoSize = true };

            nudMapW = new NullableNumericUpDown { Anchor = AnchorStyles.Left | AnchorStyles.Right, Minimum = 1, Maximum = 500, Enabled = false };

            Label lblMapH = new Label { Text = "地图总高度:", Anchor = AnchorStyles.Left, AutoSize = true };

            nudMapH = new NullableNumericUpDown { Anchor = AnchorStyles.Left | AnchorStyles.Right, Minimum = 1, Maximum = 500, Enabled = false };



            Label lblExpandLeft = new Label { Text = "向左扩展格数:", Anchor = AnchorStyles.Left, AutoSize = true };

            nudExpandLeft = new NullableNumericUpDown { Anchor = AnchorStyles.Left | AnchorStyles.Right, Minimum = 0, Maximum = 500 };

            Label lblExpandRight = new Label { Text = "向右扩展格数:", Anchor = AnchorStyles.Left, AutoSize = true };

            nudExpandRight = new NullableNumericUpDown { Anchor = AnchorStyles.Left | AnchorStyles.Right, Minimum = 0, Maximum = 500 };

            Label lblExpandUp = new Label { Text = "向上扩展格数:", Anchor = AnchorStyles.Left, AutoSize = true };

            nudExpandUp = new NullableNumericUpDown { Anchor = AnchorStyles.Left | AnchorStyles.Right, Minimum = 0, Maximum = 500 };

            Label lblExpandDown = new Label { Text = "向下扩展格数:", Anchor = AnchorStyles.Left, AutoSize = true };

            nudExpandDown = new NullableNumericUpDown { Anchor = AnchorStyles.Left | AnchorStyles.Right, Minimum = 0, Maximum = 500 };

            btnResizeMap = new Button { Text = "应用并重构地图尺寸", Anchor = AnchorStyles.Left | AnchorStyles.Right, Height = 32 };

            btnResizeMap.Click += ResizeMapClick;



            Label lblLeftMargin = new Label { Text = "左侧不可玩留白:", Anchor = AnchorStyles.Left, AutoSize = true };

            nudLeftMargin = new NullableNumericUpDown { Anchor = AnchorStyles.Left | AnchorStyles.Right, Minimum = 0, Maximum = 500 };

            Label lblTopMargin = new Label { Text = "顶部不可玩留白:", Anchor = AnchorStyles.Left, AutoSize = true };

            nudTopMargin = new NullableNumericUpDown { Anchor = AnchorStyles.Left | AnchorStyles.Right, Minimum = 0, Maximum = 500 };

            Label lblPlayWidth = new Label { Text = "可游玩区域宽度:", Anchor = AnchorStyles.Left, AutoSize = true };

            nudPlayWidth = new NullableNumericUpDown { Anchor = AnchorStyles.Left | AnchorStyles.Right, Minimum = 1, Maximum = 500 };

            Label lblPlayHeight = new Label { Text = "可游玩区域高度:", Anchor = AnchorStyles.Left, AutoSize = true };

            nudPlayHeight = new NullableNumericUpDown { Anchor = AnchorStyles.Left | AnchorStyles.Right, Minimum = 1, Maximum = 500 };



            // 绑定数值微调关联事件

            nudLeftMargin.ValueChanged += (s, e) => { if (FrontNav.MapSize(Doc) != null) { FrontNav.SetMember(FrontNav.MapSize(Doc), 2, (ushort)nudLeftMargin.Value); AddHistoryState(); } };

            nudTopMargin.ValueChanged += (s, e) => { if (FrontNav.MapSize(Doc) != null) { FrontNav.SetMember(FrontNav.MapSize(Doc), 3, (ushort)nudTopMargin.Value); AddHistoryState(); } };

            nudPlayWidth.ValueChanged += (s, e) => { if (FrontNav.MapSize(Doc) != null) { FrontNav.SetMember(FrontNav.MapSize(Doc), 4, (ushort)nudPlayWidth.Value); AddHistoryState(); } };

            nudPlayHeight.ValueChanged += (s, e) => { if (FrontNav.MapSize(Doc) != null) { FrontNav.SetMember(FrontNav.MapSize(Doc), 5, (ushort)nudPlayHeight.Value); AddHistoryState(); } };



            tlpMap.Controls.Add(lblMapW, 0, 0);

            tlpMap.Controls.Add(nudMapW, 1, 0);

            tlpMap.Controls.Add(lblMapH, 0, 1);

            tlpMap.Controls.Add(nudMapH, 1, 1);



            tlpMap.Controls.Add(lblExpandLeft, 0, 2);

            tlpMap.Controls.Add(nudExpandLeft, 1, 2);

            tlpMap.Controls.Add(lblExpandRight, 0, 3);

            tlpMap.Controls.Add(nudExpandRight, 1, 3);

            tlpMap.Controls.Add(lblExpandUp, 0, 4);

            tlpMap.Controls.Add(nudExpandUp, 1, 4);

            tlpMap.Controls.Add(lblExpandDown, 0, 5);

            tlpMap.Controls.Add(nudExpandDown, 1, 5);

            tlpMap.Controls.Add(btnResizeMap, 1, 6);

            tlpMap.Controls.Add(lblLeftMargin, 0, 7);

            tlpMap.Controls.Add(nudLeftMargin, 1, 7);

            tlpMap.Controls.Add(lblTopMargin, 0, 8);

            tlpMap.Controls.Add(nudTopMargin, 1, 8);

            tlpMap.Controls.Add(lblPlayWidth, 0, 9);

            tlpMap.Controls.Add(nudPlayWidth, 1, 9);

            tlpMap.Controls.Add(lblPlayHeight, 0, 10);

            tlpMap.Controls.Add(nudPlayHeight, 1, 10);

        }



        private void PopulateDropdowns()

        {

            // 地形下拉框已弃用，地形编辑采用更细粒度的 BaseTerrain + Slot 模式



            // 部队兵种已采用手动输入 ID 配合名称显示的模式



            // 建筑已被替换为 NumericUpDown 自由输入框，无需填充下拉菜单项



            // 工事已被替换为 NumericUpDown 自由输入框，无需填充下拉菜单项



            // 国家 ID 已被替换为 NumericUpDown 自由输入框，无需填充下拉菜单项

        }



        private void OpenFileClick(object sender, EventArgs e)
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
                OpenPath(ofd.FileName);
            }
        }

        void OpenPath(string path)
        {
            try
            {
                Cursor = Cursors.WaitCursor;
                string kind;
                if (IsBtlFile(path))
                {
                    ApplyBtlFile(path);
                    kind = "BTL";
                }
                else if (IsJsonFile(path))
                {
                    ApplyJsonFile(path);
                    kind = _front?.OpenedKind ?? "JSON";
                }
                else
                {
                    throw new InvalidDataException(
                        "无法识别后缀。请使用 .btl 或 .json");
                }

                _loadedFilePath = path;
                statusLabel.Text = $"已打开 ({kind}): {Path.GetFileName(path)}";
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
                MessageBox.Show($"打开失败:\n{ex.Message}", "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            finally
            {
                Cursor = Cursors.Default;
            }
        }

        // ---- 关卡 IO：按后缀打开；内存真相源仍是 BtlFront ----

        static bool IsBtlFile(string path) =>
            !string.IsNullOrEmpty(path)
            && path.EndsWith(".btl", StringComparison.OrdinalIgnoreCase)
            && !path.EndsWith(".btlf", StringComparison.OrdinalIgnoreCase);

        static bool IsJsonFile(string path)
        {
            if (string.IsNullOrEmpty(path)) return false;
            return path.EndsWith(".json", StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>读 .btl：BtlToFront → Document。</summary>
        void ApplyBtlFile(string path)
        {
            _front = FrontSession.FromBtl(path);
        }

        /// <summary>加载关卡 JSON（fbs 字段名或原始字段 ID）。</summary>
        void ApplyJsonFile(string path)
        {
            _front = FrontSession.FromJsonFile(path);
        }

        /// <summary>从 BtlFront JSON 文本恢复会话（撤销/重做）。</summary>
        void ApplyFrontJson(string json)
        {
            _front = FrontSession.FromJson(json);
        }

        /// <summary>历史快照：整份 BtlFront JSON。</summary>
        string SnapshotFrontJson()
        {
            if (!HasDoc) return null;
            FrontNav.SyncGrid(Doc, mapCanvas.Cells);
            return _front.ToJson();
        }

        /// <summary>导出 .btl：先把格子收进 Document，再按我们的 FB 算法整文件重建。</summary>
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
        }


        /// <summary>打开或新建成功后：刷列表、重建画布格子、同步注册表。</summary>
        private void OnDocumentLoaded()
        {
            UpdateSaveButtonState();

            if (_isUndoRedoAction)
                mapCanvas.ApplyDocumentFromHistory(Doc);
            else
                mapCanvas.Document = Doc;

            _selectedCellIdx = -1;

            lblCellCoords.Text = "未选中地块";

            lblCellCoordsUnit.Text = "未选中地块";

            lblCellCoordsBuilding.Text = "未选中地块";



            // 全局配置 Tab

            bool fogOff = IsFogOff(FrontNav.ScalarV(FrontNav.Map(Doc), 4));

            chkFog.CheckedChanged -= FogCheckChanged;

            chkFog.Checked = !fogOff;

            chkFog.CheckedChanged += FogCheckChanged;



            // 地图尺寸与游玩边界

            if (FrontNav.MapSize(Doc) != null)
            {
                var sz = FrontNav.MapSize(Doc);
                nudMapW.Value = FrontNav.MemberU16(sz, 0);
                nudMapH.Value = FrontNav.MemberU16(sz, 1);
                nudExpandLeft.Value = 0;
                nudExpandRight.Value = 0;
                nudExpandUp.Value = 0;
                nudExpandDown.Value = 0;
                nudLeftMargin.Value = FrontNav.MemberU16(sz, 2);
                nudTopMargin.Value = FrontNav.MemberU16(sz, 3);
                nudPlayWidth.Value = FrontNav.MemberU16(sz, 4);
                nudPlayHeight.Value = FrontNav.MemberU16(sz, 5);
            }



            // 势力加载

            lbFactions.Items.Clear();
            var factions = FrontNav.TableItems(FrontNav.FactionList(Doc));
            for (int i = 0; i < factions.Count; i++)
            {
                var info = FrontNav.ChildStruct(factions[i], 0);
                int factionId = info != null ? (int)FrontNav.MemberU16(info, 0) : i;
                int countryId = info != null ? (int)FrontNav.MemberU16(info, 1) : 0;
                lbFactions.Items.Add($"势力 {factionId}: {GameSettings.GetCountryName(countryId)}");
            }
            if (lbFactions.Items.Count > 0)
                lbFactions.SelectedIndex = 0;



            ReloadStageLists();
            _stageLayout?.Reload(Doc);

            RefreshCountryBtTab();



            // 读取/加载后记历史；注册表内联改写走 DataChanged→AddHistoryState，此处跳过以免重复
            if (!_isUndoRedoAction && !_isInitialLoading && !_suppressRegistryHistory)
            {
                AddHistoryState();
            }

            RefreshRegistryViewIfVisible();
        }

        /// <summary>若当前在注册表 Tab，用最新 Document 刷新树（Undo/地图改完保留展开）。</summary>
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
            if (HasDoc)
            {
                FrontNav.SyncGrid(Doc, mapCanvas.Cells);
                string json = SnapshotFrontJson();
                _history.Add(json);
            }
            _historyIndex = 0;
        }

        /// <summary>
        /// 记一帧撤销。先 SyncGrid，再序列化整棵 Front。
        /// 与当前帧相同则跳过；一旦继续编辑会丢掉 redo 尾部。
        /// </summary>
        private void AddHistoryState()
        {
            if (!HasDoc) return;
            FrontNav.SyncGrid(Doc, mapCanvas.Cells);
            string json = SnapshotFrontJson();

            // Prevent duplicate adjacent states
            if (_historyIndex >= 0 && _historyIndex < _history.Count && _history[_historyIndex] == json)
            {
                return;
            }

            // Remove any redo states
            if (_historyIndex < _history.Count - 1)
            {
                _history.RemoveRange(_historyIndex + 1, _history.Count - (_historyIndex + 1));
            }

            _history.Add(json);
            _historyIndex = _history.Count - 1;

            // Limit history size to 100 steps
            if (_history.Count > 100)
            {
                _history.RemoveAt(0);
                _historyIndex--;
            }
        }

        private void LoadHistoryState(string json)
        {
            try
            {
                _isUndoRedoAction = true;
                _isUpdatingTerrainUi = true;
                _isUpdatingUnitUi = true;
                _isUpdatingBuildingUi = true;
                _isUpdatingFortUi = true;

                ApplyFrontJson(json);
                OnDocumentLoaded();
            }
            finally
            {
                _isUndoRedoAction = false;
                _isUpdatingTerrainUi = false;
                _isUpdatingUnitUi = false;
                _isUpdatingBuildingUi = false;
                _isUpdatingFortUi = false;
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



        private static bool IsRawFieldJson(string json)

        {

            return json.Contains("\"field_0$") ||

                   json.Contains("\"field_1$") ||

                   json.Contains("\"field_2$") ||

                   json.Contains("\"field_3$") ||

                   json.Contains("\"field_4$") ||

                   json.Contains("\"field_5$") ||

                   json.Contains("\"field_6$") ||

                   json.Contains("\"field_7$") ||

                   json.Contains("\"field_8$") ||

                   json.Contains("\"field_9$") ||

                   json.Contains("\"field_10$") ||

                   json.Contains("\"field_11$");

        }



        private static bool IsFogOff(object value)

        {

            if (value == null) return false;

            try

            {

                if (value is System.Text.Json.JsonElement je)

                {

                    return je.ValueKind == System.Text.Json.JsonValueKind.True ||

                           (je.ValueKind == System.Text.Json.JsonValueKind.Number && je.GetInt32() != 0);

                }

                return Convert.ToBoolean(value);

            }

            catch

            {

                return false;

            }

        }



        private void FogCheckChanged(object sender, EventArgs e)

        {

            if (FrontNav.Map(Doc) == null) return;
            FrontNav.SetScalar(FrontNav.Map(Doc), 4, "bool", chkFog.Checked ? false : true);
            AddHistoryState();

        }



        private List<ushort> ParseTargetCellsInput(string text)
        {
            var cells = new List<ushort>();
            if (!string.IsNullOrWhiteSpace(text))
            {
                var parts = text.Split(new[] { ',', ' ', ';', '\t' }, StringSplitOptions.RemoveEmptyEntries);
                foreach (var part in parts)
                {
                    if (ushort.TryParse(part.Trim(), out ushort val))
                    {
                        cells.Add(val);
                    }
                }
            }
            return cells;
        }

        
        private void SetControlEnabledSmooth(Control ctrl, bool enabled, bool nullOnDisable = false)
        {
            if (ctrl == null) return;
            if (ctrl is NullableNumericUpDown nud)
            {
                nud.Enabled = enabled;
                if (!enabled && nullOnDisable)
                {
                    nud.NullableValue = null;
                }
            }
            else if (ctrl is TextBox tb)
            {
                tb.ReadOnly = !enabled;
                tb.TabStop = enabled;
                tb.BackColor = enabled ? SystemColors.Window : SystemColors.Control;
                tb.ForeColor = enabled ? SystemColors.WindowText : SystemColors.GrayText;
                if (!enabled && nullOnDisable)
                {
                    tb.Text = "";
                }
            }
            else
            {
                ctrl.Enabled = enabled;
            }
        }

        private void UpdateOptionalGroupEnabledStates()
        {
            // 部队行为配置
            if (chkUnitBehavior != null)
            {
                bool ub = chkUnitBehavior.Checked;
                SetControlEnabledSmooth(nudUnitBehaviorField0, ub);
                SetControlEnabledSmooth(nudUnitBehaviorId, ub);
                SetControlEnabledSmooth(nudUnitBehaviorField2, ub);
                SetControlEnabledSmooth(nudUnitBehaviorRadius, ub);
                SetControlEnabledSmooth(nudUnitBehaviorField4, ub);
                SetControlEnabledSmooth(nudUnitBehaviorCenter, ub);
                SetControlEnabledSmooth(nudUnitBehaviorField6, ub);
            }

            // 将领配置
            if (chkUnitGeneral != null)
            {
                bool ug = chkUnitGeneral.Checked;
                SetControlEnabledSmooth(nudUnitGeneralId, ug);
                if (lblUnitGeneralName != null) lblUnitGeneralName.Enabled = ug;
                if (chkUnitGeneralActive != null) chkUnitGeneralActive.Enabled = ug;

                bool uga = ug && (chkUnitGeneralActive != null && chkUnitGeneralActive.Checked);
                SetControlEnabledSmooth(nudUnitGeneralParam2, uga);
            }

            // 特种部队配置
            if (chkUnitExArmy != null)
            {
                bool ue = chkUnitExArmy.Checked;
                SetControlEnabledSmooth(nudUnitExArmyId, ue);
                if (lblUnitExArmyName != null) lblUnitExArmyName.Enabled = ue;
                SetControlEnabledSmooth(nudUnitExArmyHp, ue);
                SetControlEnabledSmooth(nudUnitExArmyMaxHp, ue);
                SetControlEnabledSmooth(nudUnitExArmyField1, ue);
                SetControlEnabledSmooth(nudUnitExArmyField4, ue);
                SetControlEnabledSmooth(nudUnitExArmyField5, ue);
            }
        }

        private string GetAiBehaviorPresetName(ushort flag)
        {
            switch (flag)
            {
                case 1: return "集火强攻目标区域";
                case 2: return "建立防线/驻守地块";
                case 3: return "轰炸/空袭预警目标";
                case 4: return "预设巡逻/推进路线";
                case 5: return "渡海/跨海登陆集结";
                default: return $"地块ID/自定义Flag ({flag})";
            }
        }
        private bool _isUpdatingTerrainUi = false;
        private bool _isUpdatingOffsetUi = false;
        private bool _terrainOffsetDragging = false;



        private void PlayableFlagChanged(object sender, EventArgs e)
        {
            if (_selectedCellIdx < 0 || _isUpdatingTerrainUi) return;
            var cell = mapCanvas.Cells[_selectedCellIdx];
            ushort terrain = cell.Terrain;
            if (chkPlayableFlag.Checked)
                terrain = (ushort)(terrain & unchecked((ushort)~(1 << 8)));
            else
                terrain = (ushort)(terrain | (1 << 8));
            cell.Terrain = terrain;
            mapCanvas.Invalidate();
            AddHistoryState();
        }

        private void SeaFlagChanged(object sender, EventArgs e)
        {
            if (_selectedCellIdx < 0 || _isUpdatingTerrainUi) return;
            if (!RunEdit("set_sea", new ScriptArgs
            {
                CellIndex = _selectedCellIdx,
                Value = chkSeaFlag.Checked
            })) return;
            RefreshTerrainPaletteSelection(mapCanvas.Cells[_selectedCellIdx]);
            AddHistoryState();
        }

        void MapCanvasPaletteDragEnter(object sender, DragEventArgs e)
        {
            e.Effect = e.Data.GetDataPresent(typeof(PaletteDrag)) ? DragDropEffects.Copy : DragDropEffects.None;
        }

        void MapCanvasPaletteDragOver(object sender, DragEventArgs e)
        {
            if (!e.Data.GetDataPresent(typeof(PaletteDrag)))
            {
                e.Effect = DragDropEffects.None;
                mapCanvas.DropPreviewIndex = -1;
                return;
            }
            e.Effect = DragDropEffects.Copy;
            Point pt = mapCanvas.PointToClient(new Point(e.X, e.Y));
            mapCanvas.DropPreviewIndex = mapCanvas.HitTestCell(pt);
        }

        void MapCanvasPaletteDragDrop(object sender, DragEventArgs e)
        {
            mapCanvas.DropPreviewIndex = -1;
            if (!e.Data.GetDataPresent(typeof(PaletteDrag))) return;
            var drag = (PaletteDrag)e.Data.GetData(typeof(PaletteDrag));
            Point pt = mapCanvas.PointToClient(new Point(e.X, e.Y));
            int idx = mapCanvas.HitTestCell(pt);
            if (idx >= 0)
                ApplyPaletteToCell(idx, drag.Item, drag.Target, recordHistory: true);
        }

        bool ApplyPaletteToCell(int cellIdx, PaletteItem item, PaletteTarget target, bool recordHistory)
        {
            if (item == null || cellIdx < 0 || cellIdx >= mapCanvas.Cells.Count) return false;
            bool ok = target == PaletteTarget.Climate
                ? RunEdit("apply_climate", new ScriptArgs
                {
                    CellIndex = cellIdx,
                    Item = EditInput(
                        ("sea", item.Sea == true),
                        ("t", item.T),
                        ("variant", item.Variant))
                })
                : RunEdit("apply_layer", new ScriptArgs
                {
                    CellIndex = cellIdx,
                    Item = EditInput(
                        ("layer", TerrainLayerKey(target)),
                        ("terrain_id", item.TerrainId ?? 0),
                        ("variant", item.Variant),
                        ("dx", item.Dx),
                        ("dy", item.Dy))
                });
            if (!ok) return false;
            if (cellIdx == _selectedCellIdx)
            {
                var cell = mapCanvas.Cells[cellIdx];
                RefreshTerrainPaletteSelection(cell);
                RefreshTerrainOffsetUi(cell);
            }
            if (recordHistory)
                AddHistoryState();
            return true;
        }

        void ClearPaletteLayer(int cellIdx, PaletteTarget target, bool recordHistory)
        {
            if (cellIdx < 0 || cellIdx >= mapCanvas.Cells.Count) return;
            bool ok = target == PaletteTarget.Climate
                ? RunEdit("set_sea", new ScriptArgs { CellIndex = cellIdx, Value = false })
                : RunEdit("clear_layer", new ScriptArgs
                {
                    CellIndex = cellIdx,
                    Input = EditInput(("layer", TerrainLayerKey(target)))
                });
            if (!ok) return;
            if (cellIdx == _selectedCellIdx)
            {
                var cell = mapCanvas.Cells[cellIdx];
                RefreshTerrainPaletteSelection(cell);
                RefreshTerrainOffsetUi(cell);
            }
            if (recordHistory)
                AddHistoryState();
        }

        PaletteTarget GetActiveTerrainLayerTarget()
        {
            if (_terrainLayerTabs == null) return PaletteTarget.Main;
            if (_terrainLayerTabs.SelectedTab == null) return PaletteTarget.Main;
            string name = _terrainLayerTabs.SelectedTab.Text;
            if (name == "地质") return PaletteTarget.Climate;
            if (name == "次地形") return PaletteTarget.Secondary;
            if (name == "装饰") return PaletteTarget.Decor;
            return PaletteTarget.Main;
        }

        void MapCanvasMouseDown(object sender, MouseEventArgs e)
        {
            if (e.Button != MouseButtons.Left || (Control.ModifierKeys & Keys.Shift) == 0) return;
            if (tabControlRight.SelectedTab != tabTerrainEdit) return;
            if (_selectedCellIdx < 0) return;
            if (!LayerHasContent(mapCanvas.Cells[_selectedCellIdx], GetActiveTerrainLayerTarget())) return;
            float mapX = e.X - mapCanvas.AutoScrollPosition.X;
            float mapY = e.Y - mapCanvas.AutoScrollPosition.Y;
            if (mapCanvas.GetCellAtPoint(mapX, mapY) != _selectedCellIdx) return;
            _terrainOffsetDragging = true;
            mapCanvas.Capture = true;
            ApplyTerrainOffsetFromMouse(mapX, mapY, recordHistory: false);
        }

        void MapCanvasMouseMove(object sender, MouseEventArgs e)
        {
            if (!_terrainOffsetDragging) return;
            if ((Control.ModifierKeys & Keys.Shift) == 0 || e.Button != MouseButtons.Left) return;
            float mapX = e.X - mapCanvas.AutoScrollPosition.X;
            float mapY = e.Y - mapCanvas.AutoScrollPosition.Y;
            ApplyTerrainOffsetFromMouse(mapX, mapY, recordHistory: false);
        }

        void MapCanvasMouseUp(object sender, MouseEventArgs e)
        {
            if (!_terrainOffsetDragging || e.Button != MouseButtons.Left) return;
            _terrainOffsetDragging = false;
            mapCanvas.Capture = false;
            AddHistoryState();
        }


        const int RandomOffsetMin = -32;
        const int RandomOffsetMax = 32;

        /// <summary>河流、出海口、海滩接壤：变种/偏移固定，随机会破坏拼接。</summary>
        static readonly HashSet<int> ProtectedRandomTerrainIds = new HashSet<int> { 46, 47, 48, 49, 50 };

        static bool IsProtectedRandomTerrain(int terrainId)
        {
            if (terrainId <= 0) return false;
            if (ProtectedRandomTerrainIds.Contains(terrainId)) return true;
            return GameSettings.MapTerrains.TryGetValue(terrainId, out var setting) && setting.Type == 10;
        }

        void PerformRandomVariantAction()
        {
            if (!HasDoc || _selectedCellIdx < 0 || _selectedCellIdx >= mapCanvas.Cells.Count) return;
            if (tabControlRight.SelectedTab != tabTerrainEdit) return;

            var cell = mapCanvas.Cells[_selectedCellIdx];
            RandomizeCellVariants(cell, Random.Shared);

            mapCanvas.Invalidate();
            RefreshTerrainPaletteSelection(cell);
            RefreshTerrainOffsetUi(cell);
            AddHistoryState();
            statusLabel.Text = $"随机变种：地块 #{_selectedCellIdx}";
        }

        void PerformRandomOffsetAction()
        {
            if (!HasDoc || _selectedCellIdx < 0 || _selectedCellIdx >= mapCanvas.Cells.Count) return;
            if (tabControlRight.SelectedTab != tabTerrainEdit) return;

            var cell = mapCanvas.Cells[_selectedCellIdx];
            int changed = RandomizeCellOffsets(cell, Random.Shared);

            if (changed == 0)
            {
                statusLabel.Text = "随机偏移：当前地块无可偏移的贴图层";
                return;
            }

            mapCanvas.Invalidate();
            RefreshTerrainOffsetUi(cell);
            AddHistoryState();
            statusLabel.Text = $"随机偏移：地块 #{_selectedCellIdx}（{changed} 层）";
        }

        void PerformGlobalRandomVariantAction()
        {
            if (!HasDoc || mapCanvas.Cells.Count == 0) return;

            var rng = Random.Shared;
            foreach (var cell in mapCanvas.Cells)
                RandomizeCellVariants(cell, rng);

            mapCanvas.Invalidate();
            RefreshSelectedTerrainUi();
            AddHistoryState();
            statusLabel.Text = $"全局随机变种：{mapCanvas.Cells.Count} 格";
        }

        void PerformGlobalRandomOffsetAction()
        {
            if (!HasDoc || mapCanvas.Cells.Count == 0) return;

            var rng = Random.Shared;
            int layerCount = 0;
            foreach (var cell in mapCanvas.Cells)
                layerCount += RandomizeCellOffsets(cell, rng);

            mapCanvas.Invalidate();
            RefreshSelectedTerrainUi();
            AddHistoryState();
            statusLabel.Text = layerCount > 0
                ? $"全局随机偏移：{mapCanvas.Cells.Count} 格，{layerCount} 层"
                : "全局随机偏移：地图上没有可偏移的贴图层";
        }

        void RefreshSelectedTerrainUi()
        {
            if (_selectedCellIdx >= 0 && _selectedCellIdx < mapCanvas.Cells.Count)
            {
                var cell = mapCanvas.Cells[_selectedCellIdx];
                RefreshTerrainPaletteSelection(cell);
                RefreshTerrainOffsetUi(cell);
            }
        }

        string LayerName(int id) => id <= 0 ? "无" : GetDoodadName(id);

        private string GetBaseTerrainName(int baseT)
        {
            switch (baseT)
            {
                case 0: return "土地";
                case 1: return "草地";
                case 2: return "沙漠";
                case 3: return "雪地";
                case 4: return "第五套陆地";
                default: return $"未知地质({baseT})";
            }
        }

        private string GetDoodadName(int id)

        {

            if (id == 0) return "无";

            if (GameSettings.MapTerrains.TryGetValue(id, out var terrain))

                return terrain.Name;

            if (GameSettings.Buildings.TryGetValue(id, out var bldg))

                return bldg.Name;

            if (GameSettings.Fortifications.TryGetValue(id, out var fort))

                return fort.Name;

            if (GameSettings.TerrainTypes.TryGetValue(id, out var typeName))

                return typeName;

            return $"未知({id})";

        }



        private static object GetExtensionValue(Dictionary<string, object> ext, string key)

        {

            if (ext == null || !ext.TryGetValue(key, out var val)) return null;

            if (val is System.Text.Json.JsonElement je)

            {

                switch (je.ValueKind)

                {

                    case System.Text.Json.JsonValueKind.Number:

                        if (je.TryGetInt32(out int i)) return i;

                        if (je.TryGetDouble(out double d)) return d;

                        break;

                    case System.Text.Json.JsonValueKind.True: return true;

                    case System.Text.Json.JsonValueKind.False: return false;

                    case System.Text.Json.JsonValueKind.String: return je.GetString();

                }

            }

            return val;

        }



        private static int GetExtensionInt(Dictionary<string, object> ext, string key, int defaultVal = 0)

        {

            var val = GetExtensionValue(ext, key);

            if (val == null) return defaultVal;

            try

            {

                if (val is System.Text.Json.JsonElement je)

                {

                    if (je.ValueKind == System.Text.Json.JsonValueKind.Number && je.TryGetInt32(out int intVal)) return intVal;

                    if (je.ValueKind == System.Text.Json.JsonValueKind.String && int.TryParse(je.GetString(), out int strVal)) return strVal;

                    return defaultVal;

                }

                return Convert.ToInt32(val);

            }

            catch

            {

                return defaultVal;

            }

        }



        private static bool GetExtensionBool(Dictionary<string, object> ext, string key, bool defaultVal = false)

        {

            var val = GetExtensionValue(ext, key);

            if (val == null) return defaultVal;

            try

            {

                return Convert.ToBoolean(val);

            }

            catch

            {

                return defaultVal;

            }

        }



        private static Dictionary<string, object> GetExtensionDict(Dictionary<string, object> ext, string key)
        {
            if (ext == null || !ext.TryGetValue(key, out var val)) return null;

            if (val is BtlTable bt)
                return FlatTableToUiDict(bt);

            if (val is System.Text.Json.JsonElement je && je.ValueKind == System.Text.Json.JsonValueKind.Object)
            {
                // BtlFront node {t,f,...}
                if (je.TryGetProperty("f", out var fEl) && fEl.ValueKind == System.Text.Json.JsonValueKind.Object)
                {
                    var flat = new Dictionary<string, object>();
                    foreach (var prop in fEl.EnumerateObject())
                        flat["field_" + prop.Name] = UnwrapExtensionLeaf(prop.Value);
                    return flat;
                }
                var dict = new Dictionary<string, object>();
                foreach (var prop in je.EnumerateObject())
                    dict[prop.Name] = UnwrapExtensionLeaf(prop.Value);
                return dict;
            }

            if (val is Dictionary<string, object> d)
            {
                if (d.TryGetValue("f", out var nested) && nested is Dictionary<string, object> nf)
                {
                    var flat = new Dictionary<string, object>();
                    foreach (var kv in nf)
                        flat["field_" + kv.Key] = UnwrapExtensionLeaf(kv.Value);
                    return flat;
                }
                // FillExtras flat dict may contain BtlScalar; UI wants bare values
                var ui = new Dictionary<string, object>();
                foreach (var kv in d)
                    ui[kv.Key] = UnwrapExtensionLeaf(kv.Value);
                return ui;
            }

            return null;
        }

        static bool ExtDictHasAny(Dictionary<string, object> dict, params string[] keys)
        {
            if (dict == null) return false;
            foreach (var k in keys)
            {
                if (dict.ContainsKey(k)) return true;
            }
            return false;
        }

        static Dictionary<string, object> FlatTableToUiDict(BtlTable tbl)
        {
            var d = new Dictionary<string, object>();
            foreach (var kv in tbl.F)
            {
                string k = "field_" + kv.Key.ToString(System.Globalization.CultureInfo.InvariantCulture);
                if (kv.Value is BtlScalar sc)
                    d[k] = sc.V;
                else if (kv.Value is BtlTable nested)
                    d[k] = FlatTableToUiDict(nested);
                else
                    d[k] = kv.Value;
            }
            return d;
        }

        static object UnwrapExtensionLeaf(object val)
        {
            if (val is BtlScalar sc) return sc.V;
            if (val is System.Text.Json.JsonElement je)
            {
                if (je.ValueKind == System.Text.Json.JsonValueKind.Object)
                {
                    if (je.TryGetProperty("v", out var v))
                        return UnwrapExtensionLeaf(v);
                    if (je.TryGetProperty("f", out var f) && f.ValueKind == System.Text.Json.JsonValueKind.Object)
                    {
                        var d = new Dictionary<string, object>();
                        foreach (var p in f.EnumerateObject())
                            d["field_" + p.Name] = UnwrapExtensionLeaf(p.Value);
                        return d;
                    }
                    var obj = new Dictionary<string, object>();
                    foreach (var p in je.EnumerateObject())
                        obj[p.Name] = UnwrapExtensionLeaf(p.Value);
                    return obj;
                }
                if (je.ValueKind == System.Text.Json.JsonValueKind.True) return true;
                if (je.ValueKind == System.Text.Json.JsonValueKind.False) return false;
                if (je.ValueKind == System.Text.Json.JsonValueKind.String) return je.GetString();
                if (je.ValueKind == System.Text.Json.JsonValueKind.Number)
                {
                    if (je.TryGetInt64(out long l)) return l;
                    if (je.TryGetDouble(out double dbl)) return dbl;
                }
                return je;
            }
            if (val is Dictionary<string, object> dict && dict.TryGetValue("v", out var inner))
                return UnwrapExtensionLeaf(inner);
            return val;
        }



        private static int GetResilientInt(Dictionary<string, object> dict, string section, string uniqueKey, string rawFieldName, string fallbackName, int defaultVal)
        {
            if (dict == null) return defaultVal;

            // V2：不查 fbs/符号表；按 field_N / 显式别名试键
            if (dict.ContainsKey(rawFieldName)) return GetExtensionInt(dict, rawFieldName, defaultVal);
            if (dict.ContainsKey(uniqueKey)) return GetExtensionInt(dict, uniqueKey, defaultVal);
            if (!string.IsNullOrEmpty(fallbackName) && dict.ContainsKey(fallbackName)) return GetExtensionInt(dict, fallbackName, defaultVal);

            foreach (var key in dict.Keys)
            {
                if (key.Equals(rawFieldName, StringComparison.OrdinalIgnoreCase) ||
                    key.Equals(uniqueKey, StringComparison.OrdinalIgnoreCase) ||
                    (!string.IsNullOrEmpty(fallbackName) && key.Equals(fallbackName, StringComparison.OrdinalIgnoreCase)))
                {
                    return GetExtensionInt(dict, key, defaultVal);
                }
            }

            return defaultVal;
        }

        private static bool GetResilientBool(Dictionary<string, object> dict, string section, string uniqueKey, string rawFieldName, string fallbackName, bool defaultVal)
        {
            if (dict == null) return defaultVal;

            if (dict.ContainsKey(rawFieldName)) return GetExtensionBool(dict, rawFieldName, defaultVal);
            if (dict.ContainsKey(uniqueKey)) return GetExtensionBool(dict, uniqueKey, defaultVal);
            if (!string.IsNullOrEmpty(fallbackName) && dict.ContainsKey(fallbackName)) return GetExtensionBool(dict, fallbackName, defaultVal);

            foreach (var key in dict.Keys)
            {
                if (key.Equals(rawFieldName, StringComparison.OrdinalIgnoreCase) ||
                    key.Equals(uniqueKey, StringComparison.OrdinalIgnoreCase) ||
                    (!string.IsNullOrEmpty(fallbackName) && key.Equals(fallbackName, StringComparison.OrdinalIgnoreCase)))
                {
                    return GetExtensionBool(dict, key, defaultVal);
                }
            }

            return defaultVal;
        }



        private bool _isUpdatingUnitUi = false;



        private bool _isUpdatingBuildingUi = false;



        // Faction Edit Logic



        private void LbFactionsMouseDown(object sender, MouseEventArgs e)

        {

            _factionDragStartPoint = e.Location;

        }



        private void LbFactionsMouseMove(object sender, MouseEventArgs e)

        {

            if (e.Button == MouseButtons.Left && lbFactions.Items.Count > 0)

            {

                if (Math.Abs(e.X - _factionDragStartPoint.X) > SystemInformation.DragSize.Width ||

                    Math.Abs(e.Y - _factionDragStartPoint.Y) > SystemInformation.DragSize.Height)

                {

                    int index = lbFactions.IndexFromPoint(_factionDragStartPoint);

                    if (index >= 0 && index < lbFactions.Items.Count)

                    {

                        lbFactions.DoDragDrop(lbFactions.Items[index], DragDropEffects.Move);

                    }

                }

            }

        }



        private void LbFactionsDragOver(object sender, DragEventArgs e)

        {

            e.Effect = DragDropEffects.Move;

        }



        // RefreshAstContext / AST / fbs 已从 V2 移除：本工程只读写 BtlFront。







        /// <summary>
        /// 导出为…：BTL、带 fbs 字段名的 JSON、只含字段 ID 的 JSON。
        /// </summary>
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

        /// <summary>从打开文件目录 / 导出目录 / 当前目录找 .fbs；找不到则让用户选。</summary>
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

[System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern IntPtr SendMessage(IntPtr hWnd, int msg, bool wParam, IntPtr lParam);
        private const int WM_SETREDRAW = 0x000B;

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













        private void SelectComboBoxItem(ComboBox cb, int id)

        {

            for (int i = 0; i < cb.Items.Count; i++)

            {

                var item = cb.Items[i] as DropdownItem;

                if (item != null && item.Id == id)

                {

                    cb.SelectedIndex = i;

                    return;

                }

            }

            cb.SelectedIndex = -1;

        }



        private class DropdownItem

        {

            public int Id { get; }

            public string DisplayName { get; }

            public DropdownItem(int id, string name) { Id = id; DisplayName = name; }

            public override string ToString() => DisplayName;

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



        /// <summary>V2：只认 field_N（及显式 fallback），不查 fbs / SymbolManager。</summary>
        private static List<string> GetFieldKeys(string tableName, string fieldName)
        {
            return GetResilientSearchKeys(tableName, fieldName, null);
        }

        private static List<string> GetResilientSearchKeys(string tableName, string fieldName, string fallbackUniqueKey)
        {
            var keys = new List<string>();
            if (!string.IsNullOrEmpty(fieldName))
                keys.Add(fieldName);
            if (!string.IsNullOrEmpty(fallbackUniqueKey))
                keys.Add(fallbackUniqueKey);
            return keys.Distinct().ToList();
        }



        private void CellDoubleClickedClick(int cellIdx)
        {
            if (cellIdx < 0 || cellIdx >= mapCanvas.Cells.Count) return;

            var cell = mapCanvas.Cells[cellIdx];

            // 1. Determine current edit mode from tabControlRight
            string editMode = null;
            if (tabControlRight.SelectedTab == tabTerrainEdit)
            {
                editMode = "Terrain";
            }
            else if (tabControlRight.SelectedTab == tabUnitEdit)
            {
                if (cell.Unit == null) return; // 无部队时不跳转
                editMode = "Unit";
            }
            else if (tabControlRight.SelectedTab == tabBuildingEdit)
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



        private static int FindArrayElementPosition(string text, int arrayStartIdx, int elementIdx, out int elementLength)

        {

            elementLength = 0;

            int startBrace = text.IndexOf('[', arrayStartIdx);

            if (startBrace == -1) return -1;



            int currentIdx = startBrace + 1;

            int commaCount = 0;

            while (commaCount < elementIdx && currentIdx < text.Length)

            {

                if (text[currentIdx] == ']') return -1; // End of array before reaching index

                if (text[currentIdx] == ',')

                {

                    commaCount++;

                }

                currentIdx++;

            }



            // Now currentIdx is at the start of the elementIdx-th element (or whitespace before it)

            while (currentIdx < text.Length && (char.IsWhiteSpace(text[currentIdx]) || text[currentIdx] == '\r' || text[currentIdx] == '\n'))

            {

                currentIdx++;

            }



            if (currentIdx >= text.Length || text[currentIdx] == ']') return -1;



            // Find the length of the element (up to comma, whitespace, or close bracket)

            int endIdx = currentIdx;

            while (endIdx < text.Length && text[endIdx] != ',' && text[endIdx] != ']' && !char.IsWhiteSpace(text[endIdx]) && text[endIdx] != '\r' && text[endIdx] != '\n')

            {

                endIdx++;

            }



            elementLength = endIdx - currentIdx;

            return currentIdx;

        }



        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)

        {

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

                if (tabControlRight.SelectedTab == tabTerrainEdit)

                {

                    PerformRandomVariantAction();

                    return true;

                }

            }

            if (keyData == Keys.O)

            {

                if (IsEditingText())

                    return base.ProcessCmdKey(ref msg, keyData);

                if (tabControlRight.SelectedTab == tabTerrainEdit)

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



        private void NewMapClick(object sender, EventArgs e)

        {

            using (CreateMapDialog dlg = new CreateMapDialog())

            {

                if (dlg.ShowDialog() == DialogResult.OK)

                {

                    CreateNewMap(dlg.MapWidth, dlg.MapHeight, dlg.LeftMargin, dlg.TopMargin, dlg.PlayWidth, dlg.PlayHeight, dlg.StageNum, dlg.Version, dlg.Tag);

                }

            }

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

    }



    public class CreateMapDialog : Form

    {

        public ushort MapWidth { get; private set; }

        public ushort MapHeight { get; private set; }

        public ushort LeftMargin { get; private set; }

        public ushort TopMargin { get; private set; }

        public ushort PlayWidth { get; private set; }

        public ushort PlayHeight { get; private set; }

        public ushort StageNum { get; private set; }

        public ushort Version { get; private set; }

        public new short Tag { get; private set; }



        private NullableNumericUpDown nudW, nudH, nudLM, nudTM, nudPW, nudPH, nudSN, nudV, nudT;



        public CreateMapDialog()

        {

            Text = "从无到有创建新地图";

            Size = new Size(420, 500);

            FormBorderStyle = FormBorderStyle.FixedDialog;

            MaximizeBox = false;

            MinimizeBox = false;

            StartPosition = FormStartPosition.CenterParent;



            TableLayoutPanel tlp = new TableLayoutPanel

            {

                Dock = DockStyle.Fill,

                Padding = new Padding(15),

                ColumnCount = 2,

                RowCount = 10

            };

            tlp.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 180));

            tlp.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));

            for (int i = 0; i < 10; i++)

            {

                tlp.RowStyles.Add(new RowStyle(SizeType.Absolute, 35));

            }

            Controls.Add(tlp);



            tlp.Controls.Add(new Label { Text = "地图总宽度 (Width):", Anchor = AnchorStyles.Left, AutoSize = true }, 0, 0);

            nudW = new NullableNumericUpDown { Anchor = AnchorStyles.Left | AnchorStyles.Right, Minimum = 1, Maximum = 500, Value = 30 };

            tlp.Controls.Add(nudW, 1, 0);



            tlp.Controls.Add(new Label { Text = "地图总高度 (Height):", Anchor = AnchorStyles.Left, AutoSize = true }, 0, 1);

            nudH = new NullableNumericUpDown { Anchor = AnchorStyles.Left | AnchorStyles.Right, Minimum = 1, Maximum = 500, Value = 30 };

            tlp.Controls.Add(nudH, 1, 1);



            tlp.Controls.Add(new Label { Text = "左侧不可玩留白 (Left):", Anchor = AnchorStyles.Left, AutoSize = true }, 0, 2);

            nudLM = new NullableNumericUpDown { Anchor = AnchorStyles.Left | AnchorStyles.Right, Minimum = 0, Maximum = 500, Value = 0 };

            tlp.Controls.Add(nudLM, 1, 2);



            tlp.Controls.Add(new Label { Text = "顶部不可玩留白 (Top):", Anchor = AnchorStyles.Left, AutoSize = true }, 0, 3);

            nudTM = new NullableNumericUpDown { Anchor = AnchorStyles.Left | AnchorStyles.Right, Minimum = 0, Maximum = 500, Value = 0 };

            tlp.Controls.Add(nudTM, 1, 3);



            tlp.Controls.Add(new Label { Text = "可游玩区域宽度 (Play W):", Anchor = AnchorStyles.Left, AutoSize = true }, 0, 4);

            nudPW = new NullableNumericUpDown { Anchor = AnchorStyles.Left | AnchorStyles.Right, Minimum = 1, Maximum = 500, Value = 30 };

            tlp.Controls.Add(nudPW, 1, 4);



            tlp.Controls.Add(new Label { Text = "可游玩区域高度 (Play H):", Anchor = AnchorStyles.Left, AutoSize = true }, 0, 5);

            nudPH = new NullableNumericUpDown { Anchor = AnchorStyles.Left | AnchorStyles.Right, Minimum = 1, Maximum = 500, Value = 30 };

            tlp.Controls.Add(nudPH, 1, 5);



            tlp.Controls.Add(new Label { Text = "未知文件头1:", Anchor = AnchorStyles.Left, AutoSize = true }, 0, 6);

            nudSN = new NullableNumericUpDown { Anchor = AnchorStyles.Left | AnchorStyles.Right, Minimum = 0, Maximum = 9999999, Value = 0 };

            tlp.Controls.Add(nudSN, 1, 6);



            tlp.Controls.Add(new Label { Text = "未知文件头2:", Anchor = AnchorStyles.Left, AutoSize = true }, 0, 7);

            nudV = new NullableNumericUpDown { Anchor = AnchorStyles.Left | AnchorStyles.Right, Minimum = 0, Maximum = 65535, Value = 1 };

            tlp.Controls.Add(nudV, 1, 7);



            tlp.Controls.Add(new Label { Text = "未知文件头3:", Anchor = AnchorStyles.Left, AutoSize = true }, 0, 8);

            nudT = new NullableNumericUpDown { Anchor = AnchorStyles.Left | AnchorStyles.Right, Minimum = -32768, Maximum = 32767, Value = 0 };

            tlp.Controls.Add(nudT, 1, 8);



            // Co-ordinate values

            nudW.ValueChanged += (s, e) => { nudPW.Maximum = nudW.Value - nudLM.Value; nudPW.Value = Math.Min(nudPW.Value, nudW.Value - nudLM.Value); };

            nudLM.ValueChanged += (s, e) => { nudPW.Maximum = nudW.Value - nudLM.Value; nudPW.Value = Math.Min(nudPW.Value, nudW.Value - nudLM.Value); };

            nudH.ValueChanged += (s, e) => { nudPH.Maximum = nudH.Value - nudTM.Value; nudPH.Value = Math.Min(nudPH.Value, nudH.Value - nudTM.Value); };

            nudTM.ValueChanged += (s, e) => { nudPH.Maximum = nudH.Value - nudTM.Value; nudPH.Value = Math.Min(nudPH.Value, nudH.Value - nudTM.Value); };



            Button btnOk = new Button { Text = "确定创建", Anchor = AnchorStyles.None, Width = 100, Height = 30 };

            btnOk.Click += (s, e) =>

            {

                if (nudPW.Value + nudLM.Value > nudW.Value)

                {

                    MessageBox.Show("错误：可游玩区域宽度 + 左侧留白不能大于地图总宽度！", "输入错误", MessageBoxButtons.OK, MessageBoxIcon.Error);

                    return;

                }

                if (nudPH.Value + nudTM.Value > nudH.Value)

                {

                    MessageBox.Show("错误：可游玩区域高度 + 顶部留白不能大于地图总高度！", "输入错误", MessageBoxButtons.OK, MessageBoxIcon.Error);

                    return;

                }



                MapWidth = (ushort)nudW.Value;

                MapHeight = (ushort)nudH.Value;

                LeftMargin = (ushort)nudLM.Value;

                TopMargin = (ushort)nudTM.Value;

                PlayWidth = (ushort)nudPW.Value;

                PlayHeight = (ushort)nudPH.Value;

                StageNum = (ushort)nudSN.Value;

                Version = (ushort)nudV.Value;

                Tag = (short)nudT.Value;



                DialogResult = DialogResult.OK;

                Close();

            };

            tlp.Controls.Add(btnOk, 1, 9);

        }

    }

}

