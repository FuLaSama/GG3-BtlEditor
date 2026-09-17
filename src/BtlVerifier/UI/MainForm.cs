/*
 * MainForm.cs
 * 
 * 本文件是 BTL Verifier 的交互分析器主窗体（MainForm）：
 * - 多维度分析视图:
 *   - Tab 1 (IDA 视图): 基于 RichTextBox 呈现高度仿真 IDA 的等宽伪代码注解，在左侧绘制逻辑控制流引线、跳转关联，右键支持克隆/追加元素、修改物理十六进制值与设置/取消断点。
 *   - Tab 2 (六角图 layer + 物理文件地址高亮): 结合 HexMapCanvas 做可视化展示，高亮高精度追踪所选格子的 Tiles、Attributes 物理二进制地址范围。
 *   - Tab 3 (战役全局面板): 分类树形解析阵营、势力限制、目标、增员等。
 * - 多项调试辅助: 整合了空白区域开辟工具、二进制 hex 编辑工具、一键崩溃模拟扫描器（调用 BtlSimLoader），并提供可视化虚拟单步调试环境（F7/F8/F9/F2）。
 */
using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Text;
using System.Windows.Forms;

namespace BtlVerifier
{
    // 验证器主界面窗体 (IDA等宽伪代码视图与六角地图Tab整合版)
    public class MainForm : Form
    {
        private byte[] _fileData;
        private StageModel _stage;
        private int _selectedCellIndex = -1;
        private List<DisasmItem> _disasmItems;
        private string[] _disasmLines;
        private bool _isSyncingSelection = false;
        private int _currentOffset = -1;
        private Stack<int> _navHistory = new Stack<int>();
        private static Dictionary<int, string> _countryNames;

        // UI 控件定义
        private MenuStrip menuStrip;
        private ToolStripMenuItem fileMenuItem;
        private ToolStripMenuItem openMenuItem;
        private ToolStripMenuItem dumpMenuItem;
        private ToolStripMenuItem exitMenuItem;
        private ToolStripMenuItem schemaMenuItem;
        private ToolStripMenuItem hardcodedSchemaMenuItem;
        private ToolStripMenuItem dynamicSchemaMenuItem;

        // 右键上下文菜单项
        private ToolStripMenuItem ctxEditValue;
        private ToolStripMenuItem ctxAppendElement;
        private ToolStripMenuItem ctxCopyLine;
        private ToolStripMenuItem ctxJumpTarget;
        private ToolStripMenuItem ctxToggleBreakpoint;

        private StatusStrip statusStrip;
        private ToolStripStatusLabel statusLabel;

        // Tab 控制器
        private TabControl tabControl;
        private TabPage tabPageDisasm;
        private TabPage tabPageMap;
        private TabPage tabPageBattle;
        private SplitContainer splitBattle;
        private ListView lvBattleCategories;
        private RichTextBox rtbBattleDetails;

        // Tab 1: 反汇编伪代码视图控件
        private Panel pnlSegmentBar;
        private Bitmap _segmentBarBmp;
        private RichTextBox rtbDisasm;
        private DoubleBufferedPanel pnlArrowGutter;
        private List<Tuple<int, int>> _allLinks = new List<Tuple<int, int>>();
        private int _cachedLineHeight = 17;

        // Tab 2: 原始六角地图与十六进制视图控件 (原有控件)
        private SplitContainer splitMain;
        private SplitContainer splitRight;
        private HexMapCanvas mapCanvas;
        private ListView lvDirectory;
        private Label lblDirectoryHeader;
        private Panel pnlDetails;
        private Label lblTitle;
        private TextBox txtDetails;

        public MainForm()
        {
            InitializeComponent();
            LoadCountryNames();
            Text = "BTL Struct Explorer - GG3 关卡与地图结构分析器";
            Size = new Size(1280, 800);
            StartPosition = FormStartPosition.CenterScreen;
            BackColor = Color.FromArgb(241, 245, 249); // 浅灰底色
            ForeColor = Color.Black;
        }

        private static void LoadCountryNames()
        {
            _countryNames = new Dictionary<int, string>();
            try
            {
                string path = Path.Combine(BtlSchema.WorkspacePath, "一些游戏内的定义文件", "CountrySettings.json");
                
                if (File.Exists(path))
                {
                    string json = File.ReadAllText(path);
                    using (var doc = System.Text.Json.JsonDocument.Parse(json))
                    {
                        foreach (var element in doc.RootElement.EnumerateArray())
                        {
                            int id = element.GetProperty("Id").GetInt32();
                            string name = element.GetProperty("Name").GetString();
                            _countryNames[id] = name;
                        }
                    }
                }
            }
            catch
            {
                // Ignore and keep _countryNames empty
            }
        }

        private void InitializeComponent()
        {
            // 1. 初始化菜单栏
            menuStrip = new MenuStrip();
            menuStrip.BackColor = Color.FromArgb(226, 232, 240); // 浅灰
            menuStrip.ForeColor = Color.Black;

            fileMenuItem = new ToolStripMenuItem("文件");
            fileMenuItem.ForeColor = Color.Black;

            openMenuItem = new ToolStripMenuItem("打开 BTL...", null, OpenBtlClick);
            openMenuItem.ForeColor = Color.Black;

            dumpMenuItem = new ToolStripMenuItem("导出结构布局文本...", null, DumpDisasmClick);
            dumpMenuItem.ForeColor = Color.Black;
            dumpMenuItem.Enabled = false;

            exitMenuItem = new ToolStripMenuItem("退出", null, (s, e) => Close());
            exitMenuItem.ForeColor = Color.Black;

            fileMenuItem.DropDownItems.Add(openMenuItem);
            fileMenuItem.DropDownItems.Add(dumpMenuItem);
            fileMenuItem.DropDownItems.Add(new ToolStripSeparator());
            fileMenuItem.DropDownItems.Add(exitMenuItem);
            menuStrip.Items.Add(fileMenuItem);

            schemaMenuItem = new ToolStripMenuItem("模式");
            schemaMenuItem.ForeColor = Color.Black;

            // 启动时自动加载默认 FBS Schema
            BtlSchema.AutoLoadDefaultFbs();

            hardcodedSchemaMenuItem = new ToolStripMenuItem("重载默认 FBS Schema", null, HardcodedSchemaClick);
            hardcodedSchemaMenuItem.ForeColor = Color.Black;
            hardcodedSchemaMenuItem.Checked = BtlSchema.UseDynamic;

            dynamicSchemaMenuItem = new ToolStripMenuItem("载入其他 FBS Schema...", null, DynamicSchemaClick);
            dynamicSchemaMenuItem.ForeColor = Color.Black;

            schemaMenuItem.DropDownItems.Add(hardcodedSchemaMenuItem);
            schemaMenuItem.DropDownItems.Add(dynamicSchemaMenuItem);
            menuStrip.Items.Add(schemaMenuItem);

            // 工具菜单
            var toolsMenuItem = new ToolStripMenuItem("工具");
            toolsMenuItem.ForeColor = Color.Black;

            var insertSpaceMenuItem = new ToolStripMenuItem("开辟空白区域...", null, InsertSpaceClick);
            insertSpaceMenuItem.ForeColor = Color.Black;

            var editHexMenuItem = new ToolStripMenuItem("修改选中项十六进制...", null, EditSelectedHexClick);
            editHexMenuItem.ForeColor = Color.Black;

            var runSimLoaderMenuItem = new ToolStripMenuItem("运行模拟加载崩溃检测...", null, RunSimLoaderClick);
            runSimLoaderMenuItem.ForeColor = Color.Black;

            var saveFileMenuItem = new ToolStripMenuItem("保存修改到文件", null, SaveModifiedFileClick);
            saveFileMenuItem.ForeColor = Color.Black;

            toolsMenuItem.DropDownItems.Add(insertSpaceMenuItem);
            toolsMenuItem.DropDownItems.Add(editHexMenuItem);
            toolsMenuItem.DropDownItems.Add(runSimLoaderMenuItem);
            toolsMenuItem.DropDownItems.Add(new ToolStripSeparator());
            toolsMenuItem.DropDownItems.Add(saveFileMenuItem);
            menuStrip.Items.Add(toolsMenuItem);

            // 调试菜单
            var debugMenuItem = new ToolStripMenuItem("调试");
            debugMenuItem.ForeColor = Color.Black;

            var startDebugMenuItem = new ToolStripMenuItem("开始/重置调试", null, (s, e) => StartResetDebug());
            startDebugMenuItem.ForeColor = Color.Black;

            var stepIntoMenuItem = new ToolStripMenuItem("单步步进 (F7)", null, (s, e) => StepInto());
            stepIntoMenuItem.ForeColor = Color.Black;

            var stepOverMenuItem = new ToolStripMenuItem("单步步过 (F8)", null, (s, e) => StepOver());
            stepOverMenuItem.ForeColor = Color.Black;

            var stepOutMenuItem = new ToolStripMenuItem("单步步出 (Ctrl+F7)", null, (s, e) => StepOut());
            stepOutMenuItem.ForeColor = Color.Black;

            var runDebugMenuItem = new ToolStripMenuItem("运行 (F9)", null, (s, e) => RunDebug());
            runDebugMenuItem.ForeColor = Color.Black;

            var clearBpsMenuItem = new ToolStripMenuItem("清除所有断点", null, (s, e) => {
                _breakpoints.Clear();
                pnlArrowGutter.Invalidate();
                rtbDisasm.Invalidate();
                statusLabel.Text = "所有断点已清除";
            });
            clearBpsMenuItem.ForeColor = Color.Black;

            debugMenuItem.DropDownItems.Add(startDebugMenuItem);
            debugMenuItem.DropDownItems.Add(new ToolStripSeparator());
            debugMenuItem.DropDownItems.Add(stepIntoMenuItem);
            debugMenuItem.DropDownItems.Add(stepOverMenuItem);
            debugMenuItem.DropDownItems.Add(stepOutMenuItem);
            debugMenuItem.DropDownItems.Add(runDebugMenuItem);
            debugMenuItem.DropDownItems.Add(new ToolStripSeparator());
            debugMenuItem.DropDownItems.Add(clearBpsMenuItem);
            menuStrip.Items.Add(debugMenuItem);

            Controls.Add(menuStrip);

            // 2. 初始化状态栏
            statusStrip = new StatusStrip();
            statusStrip.BackColor = Color.FromArgb(226, 232, 240);
            statusStrip.ForeColor = Color.Black;
            statusLabel = new ToolStripStatusLabel("未加载文件");
            statusLabel.ForeColor = Color.Black;
            statusStrip.Items.Add(statusLabel);
            Controls.Add(statusStrip);

            // 3. 初始化 Tab 控制器
            tabControl = new TabControl();
            tabControl.Dock = DockStyle.Fill;
            tabControl.BackColor = Color.FromArgb(241, 245, 249);
            Controls.Add(tabControl);

            // 3.1 创建 Tab 1 (IDA 属性反汇编视图)
            tabPageDisasm = new TabPage("结构布局与注解 (IDA 视图)");
            tabPageDisasm.BackColor = Color.FromArgb(241, 245, 249);
            tabPageDisasm.ForeColor = Color.Black;
            tabControl.TabPages.Add(tabPageDisasm);

            // 初始化 IDA 分段条 Panel
            pnlSegmentBar = new Panel();
            pnlSegmentBar.Dock = DockStyle.Top;
            pnlSegmentBar.Height = 25;
            pnlSegmentBar.BackColor = Color.FromArgb(226, 232, 240);
            pnlSegmentBar.Cursor = Cursors.Hand;
            pnlSegmentBar.Paint += SegmentBarPaint;
            pnlSegmentBar.MouseDown += SegmentBarMouseDown;
            pnlSegmentBar.MouseMove += SegmentBarMouseMove;
            tabPageDisasm.Controls.Add(pnlSegmentBar);

            pnlArrowGutter = new DoubleBufferedPanel();
            pnlArrowGutter.Dock = DockStyle.Left;
            pnlArrowGutter.Width = 60;
            pnlArrowGutter.BackColor = Color.White;
            pnlArrowGutter.Paint += ArrowGutterPaint;
            pnlArrowGutter.MouseDown += ArrowGutterMouseDown;
            tabPageDisasm.Controls.Add(pnlArrowGutter);

            rtbDisasm = new RichTextBox();
            rtbDisasm.Dock = DockStyle.Fill;
            rtbDisasm.ReadOnly = true;
            rtbDisasm.HideSelection = false;
            rtbDisasm.Font = new Font("Consolas", 10f, FontStyle.Regular);
            rtbDisasm.BackColor = Color.White;
            rtbDisasm.ForeColor = Color.Black;
            rtbDisasm.BorderStyle = BorderStyle.None;
            rtbDisasm.WordWrap = false;
            rtbDisasm.SelectionChanged += DisasmSelectionChanged;
            rtbDisasm.DoubleClick += DisasmDoubleClick;
            rtbDisasm.KeyDown += DisasmKeyDown;
            rtbDisasm.VScroll += DisasmVScroll;
            rtbDisasm.MouseWheel += DisasmMouseWheel;
            // IDA 视图右键上下文菜单
            var ctxDisasm = new ContextMenuStrip();
            ctxEditValue = new ToolStripMenuItem("修改属性值...", null, EditSelectedHexClick);
            ctxEditValue.ForeColor = Color.Black;
            ctxAppendElement = new ToolStripMenuItem("克隆并追加新元素", null, AppendElementClick);
            ctxAppendElement.ForeColor = Color.Black;
            ctxCopyLine = new ToolStripMenuItem("复制该行代码", null, CopyDisasmLineClick);
            ctxCopyLine.ForeColor = Color.Black;
            ctxJumpTarget = new ToolStripMenuItem("跳转到目标地址", null, JumpToTargetClick);
            ctxJumpTarget.ForeColor = Color.Black;

            ctxToggleBreakpoint = new ToolStripMenuItem("设置/取消断点 (F2)", null, ToggleBreakpointClick);
            ctxToggleBreakpoint.ForeColor = Color.Black;

            ctxDisasm.Items.Add(ctxEditValue);
            ctxDisasm.Items.Add(ctxAppendElement);
            ctxDisasm.Items.Add(new ToolStripSeparator());
            ctxDisasm.Items.Add(ctxCopyLine);
            ctxDisasm.Items.Add(ctxJumpTarget);
            ctxDisasm.Items.Add(new ToolStripSeparator());
            ctxDisasm.Items.Add(ctxToggleBreakpoint);
            ctxDisasm.Opening += CtxDisasm_Opening;
            rtbDisasm.ContextMenuStrip = ctxDisasm;

            tabPageDisasm.Controls.Add(rtbDisasm);

            // 调整 Z 顺序以确保布局排列正确 (DockStyle.Fill 的控件置于前端以防被遮挡)
            pnlSegmentBar.SendToBack();
            pnlArrowGutter.SendToBack();
            rtbDisasm.BringToFront();

            // 3.2 创建 Tab 2 (六角格地图视图)
            tabPageMap = new TabPage("六角格地图视图");
            tabPageMap.BackColor = Color.FromArgb(241, 245, 249);
            tabPageMap.ForeColor = Color.Black;
            tabControl.TabPages.Add(tabPageMap);

            // 4. 初始化地图渲染模式工具栏
            ToolStrip mapToolStrip = new ToolStrip();
            mapToolStrip.Dock = DockStyle.Top;
            mapToolStrip.BackColor = Color.FromArgb(226, 232, 240);
            mapToolStrip.GripStyle = ToolStripGripStyle.Hidden;

            ToolStripLabel lblMode = new ToolStripLabel("图层显示：");
            lblMode.ForeColor = Color.Black;
            mapToolStrip.Items.Add(lblMode);

            ToolStripButton btnAll = new ToolStripButton("显示全部");
            btnAll.Checked = true;
            btnAll.ForeColor = Color.Black;

            ToolStripButton btnTerrain = new ToolStripButton("仅地形");
            btnTerrain.ForeColor = Color.Black;

            ToolStripButton btnUnits = new ToolStripButton("仅单位");
            btnUnits.ForeColor = Color.Black;

            ToolStripButton btnBuildings = new ToolStripButton("仅建筑");
            btnBuildings.ForeColor = Color.Black;

            ToolStripButton btnForts = new ToolStripButton("仅工事");
            btnForts.ForeColor = Color.Black;

            ToolStripButton btnDecs = new ToolStripButton("仅装饰");
            btnDecs.ForeColor = Color.Black;

            ToolStripButton btnReinforcements = new ToolStripButton("仅增员");
            btnReinforcements.ForeColor = Color.Black;

            // 绑定切换逻辑
            btnAll.Click += (s, e) => {
                btnAll.Checked = true;
                btnTerrain.Checked = false;
                btnUnits.Checked = false;
                btnBuildings.Checked = false;
                btnForts.Checked = false;
                btnDecs.Checked = false;
                btnReinforcements.Checked = false;
                mapCanvas.RenderMode = MapRenderMode.All;
                PopulateDirectory();
            };
            btnTerrain.Click += (s, e) => {
                btnAll.Checked = false;
                btnTerrain.Checked = true;
                btnUnits.Checked = false;
                btnBuildings.Checked = false;
                btnForts.Checked = false;
                btnDecs.Checked = false;
                btnReinforcements.Checked = false;
                mapCanvas.RenderMode = MapRenderMode.TerrainOnly;
                PopulateDirectory();
            };
            btnUnits.Click += (s, e) => {
                btnAll.Checked = false;
                btnTerrain.Checked = false;
                btnUnits.Checked = true;
                btnBuildings.Checked = false;
                btnForts.Checked = false;
                btnDecs.Checked = false;
                btnReinforcements.Checked = false;
                mapCanvas.RenderMode = MapRenderMode.UnitsOnly;
                PopulateDirectory();
            };
            btnBuildings.Click += (s, e) => {
                btnAll.Checked = false;
                btnTerrain.Checked = false;
                btnUnits.Checked = false;
                btnBuildings.Checked = true;
                btnForts.Checked = false;
                btnDecs.Checked = false;
                btnReinforcements.Checked = false;
                mapCanvas.RenderMode = MapRenderMode.BuildingsOnly;
                PopulateDirectory();
            };
            btnForts.Click += (s, e) => {
                btnAll.Checked = false;
                btnTerrain.Checked = false;
                btnUnits.Checked = false;
                btnBuildings.Checked = false;
                btnForts.Checked = true;
                btnDecs.Checked = false;
                btnReinforcements.Checked = false;
                mapCanvas.RenderMode = MapRenderMode.FortificationsOnly;
                PopulateDirectory();
            };
            btnDecs.Click += (s, e) => {
                btnAll.Checked = false;
                btnTerrain.Checked = false;
                btnUnits.Checked = false;
                btnBuildings.Checked = false;
                btnForts.Checked = false;
                btnDecs.Checked = true;
                btnReinforcements.Checked = false;
                mapCanvas.RenderMode = MapRenderMode.DecorationsOnly;
                PopulateDirectory();
            };
            btnReinforcements.Click += (s, e) => {
                btnAll.Checked = false;
                btnTerrain.Checked = false;
                btnUnits.Checked = false;
                btnBuildings.Checked = false;
                btnForts.Checked = false;
                btnDecs.Checked = false;
                btnReinforcements.Checked = true;
                mapCanvas.RenderMode = MapRenderMode.Reinforcements;
                PopulateDirectory();
            };

            mapToolStrip.Items.Add(btnAll);
            mapToolStrip.Items.Add(btnTerrain);
            mapToolStrip.Items.Add(btnUnits);
            mapToolStrip.Items.Add(btnBuildings);
            mapToolStrip.Items.Add(btnForts);
            mapToolStrip.Items.Add(btnDecs);
            mapToolStrip.Items.Add(btnReinforcements);
            tabPageMap.Controls.Add(mapToolStrip);

            // 5. 初始化主分割器并放入 Tab 2
            splitMain = new SplitContainer();
            splitMain.Dock = DockStyle.Fill;
            splitMain.SplitterDistance = 650;
            splitMain.SplitterWidth = 5;
            splitMain.BackColor = Color.FromArgb(203, 213, 225); // 边框
            tabPageMap.Controls.Add(splitMain);

            // 确保布局排列正确 (Z顺序)
            splitMain.BringToFront();
            mapToolStrip.BringToFront();

            // 6. 初始化地图画布 (放入左栏)
            mapCanvas = new HexMapCanvas();
            mapCanvas.Dock = DockStyle.Fill;
            mapCanvas.CellSelected += CellSelectedHandler;
            mapCanvas.CellDoubleClicked += MapCanvasCellDoubleClicked;
            splitMain.Panel1.Controls.Add(mapCanvas);

            // 6. 初始化右侧分割器 (上下分栏)
            splitRight = new SplitContainer();
            splitRight.Dock = DockStyle.Fill;
            splitRight.Orientation = Orientation.Horizontal;
            splitRight.SplitterDistance = 380;
            splitRight.SplitterWidth = 5;
            splitRight.BackColor = Color.FromArgb(203, 213, 225);
            splitMain.Panel2.Controls.Add(splitRight);

            // 7. 初始化对象目录列表视图 (放入右上)
            var pnlDirHeader = new Panel();
            pnlDirHeader.Dock = DockStyle.Top;
            pnlDirHeader.Height = 28;
            pnlDirHeader.BackColor = Color.FromArgb(241, 245, 249); // 浅灰背景

            lblDirectoryHeader = new Label();
            lblDirectoryHeader.Dock = DockStyle.Fill;
            lblDirectoryHeader.Text = "【数据目录 - 请加载文件】";
            lblDirectoryHeader.Font = new Font("Segoe UI", 9.5f, FontStyle.Bold);
            lblDirectoryHeader.ForeColor = Color.FromArgb(71, 85, 105); // 深灰色
            lblDirectoryHeader.TextAlign = ContentAlignment.MiddleLeft;
            lblDirectoryHeader.Padding = new Padding(8, 0, 0, 0);
            pnlDirHeader.Controls.Add(lblDirectoryHeader);

            lvDirectory = new ListView();
            lvDirectory.Dock = DockStyle.Fill;
            lvDirectory.View = View.Details;
            lvDirectory.FullRowSelect = true;
            lvDirectory.GridLines = true;
            lvDirectory.MultiSelect = false;
            lvDirectory.Font = new Font("Segoe UI", 9.25f, FontStyle.Regular);
            lvDirectory.BackColor = Color.White;
            lvDirectory.ForeColor = Color.FromArgb(15, 23, 42);
            lvDirectory.BorderStyle = BorderStyle.None;
            lvDirectory.SelectedIndexChanged += DirectorySelectedIndexChanged;
            
            splitRight.Panel1.Controls.Add(lvDirectory);
            splitRight.Panel1.Controls.Add(pnlDirHeader); // 先加lvDirectory再加pnlDirHeader以防遮挡，因为都是Dock的

            // 8. 初始化详情面板 (放入右下)
            pnlDetails = new Panel();
            pnlDetails.Dock = DockStyle.Fill;
            pnlDetails.BackColor = Color.White;
            pnlDetails.Padding = new Padding(15);
            splitRight.Panel2.Controls.Add(pnlDetails);

            lblTitle = new Label();
            lblTitle.Text = "请选择一个格子查看物理字节关系";
            lblTitle.Font = new Font("Segoe UI", 11.5f, FontStyle.Bold);
            lblTitle.ForeColor = Color.FromArgb(20, 184, 166); // 青绿色
            lblTitle.Dock = DockStyle.Top;
            lblTitle.Height = 35;
            pnlDetails.Controls.Add(lblTitle);

            txtDetails = new TextBox();
            txtDetails.Multiline = true;
            txtDetails.ScrollBars = ScrollBars.Vertical;
            txtDetails.Dock = DockStyle.Fill;
            txtDetails.ReadOnly = true;
            txtDetails.BackColor = Color.FromArgb(241, 245, 249);
            txtDetails.ForeColor = Color.FromArgb(15, 23, 42);
            txtDetails.BorderStyle = BorderStyle.None;
            txtDetails.Font = new Font("Consolas", 10.5f, FontStyle.Regular);
            pnlDetails.Controls.Add(txtDetails);

            // 3.3 创建 Tab 3 (战役全局属性)
            tabPageBattle = new TabPage("战役全局属性");
            tabPageBattle.BackColor = Color.FromArgb(241, 245, 249);
            tabPageBattle.ForeColor = Color.Black;
            tabControl.TabPages.Add(tabPageBattle);

            splitBattle = new SplitContainer();
            splitBattle.Dock = DockStyle.Fill;
            splitBattle.SplitterDistance = 220;
            splitBattle.SplitterWidth = 5;
            splitBattle.BackColor = Color.FromArgb(203, 213, 225);
            tabPageBattle.Controls.Add(splitBattle);

            lvBattleCategories = new ListView();
            lvBattleCategories.Dock = DockStyle.Fill;
            lvBattleCategories.View = View.Details;
            lvBattleCategories.FullRowSelect = true;
            lvBattleCategories.MultiSelect = false;
            lvBattleCategories.HeaderStyle = ColumnHeaderStyle.None;
            lvBattleCategories.Columns.Add("Category", 200);
            lvBattleCategories.Font = new Font("Segoe UI", 10f, FontStyle.Bold);
            lvBattleCategories.BackColor = Color.FromArgb(248, 250, 252);
            lvBattleCategories.ForeColor = Color.FromArgb(15, 23, 42);
            lvBattleCategories.BorderStyle = BorderStyle.None;
            lvBattleCategories.SelectedIndexChanged += BattleCategoryChanged;

            // 添加分类列表选项
            lvBattleCategories.Items.Add(new ListViewItem(" 基本属性 (Basic Info)"));
            lvBattleCategories.Items.Add(new ListViewItem(" 阵营势力 (Factions)"));
            lvBattleCategories.Items.Add(new ListViewItem(" 阵营限制与卡牌 (Limits & Cards)"));
            lvBattleCategories.Items.Add(new ListViewItem(" 战役目标 (Targets)"));
            lvBattleCategories.Items.Add(new ListViewItem(" 增员部署点 (Reinforcements)"));
            lvBattleCategories.Items.Add(new ListViewItem(" 原始/其他全局 JSON 视图"));

            splitBattle.Panel1.Controls.Add(lvBattleCategories);

            rtbBattleDetails = new RichTextBox();
            rtbBattleDetails.Dock = DockStyle.Fill;
            rtbBattleDetails.ReadOnly = true;
            rtbBattleDetails.Font = new Font("Consolas", 10.5f, FontStyle.Regular);
            rtbBattleDetails.BackColor = Color.White;
            rtbBattleDetails.ForeColor = Color.Black;
            rtbBattleDetails.BorderStyle = BorderStyle.None;
            splitBattle.Panel2.Controls.Add(rtbBattleDetails);

            // 确保菜单栏与状态栏始终在最上层
            menuStrip.SendToBack();
            statusStrip.SendToBack();
        }

        private void OpenBtlClick(object sender, EventArgs e)
        {
            using (var ofd = new OpenFileDialog())
            {
                ofd.Filter = "GG3 关卡文件 (*.btl)|*.btl|所有文件 (*.*)|*.*";
                ofd.InitialDirectory = Path.Combine(BtlSchema.WorkspacePath, "游戏包体", "resources", "assets", "stage");

                if (ofd.ShowDialog() == DialogResult.OK)
                {
                    try
                    {
                        Cursor = Cursors.WaitCursor;

                        // 1. 加载原始二进制数据
                        _fileData = File.ReadAllBytes(ofd.FileName);

                        // 2. 加载结构模型与偏移
                        _stage = BtlBridge.LoadBtl(ofd.FileName);

                        // 3. 填充 hex 查看器 (Tab 2)
                        LoadHexView(_fileData);

                        // 4. 填充反汇编伪代码查看器 (Tab 1)
                        LoadDisasmView(_fileData, _stage);
                        dumpMenuItem.Enabled = true;

                        // 5. 渲染画布 (Tab 2)
                        mapCanvas.Stage = _stage;
                        statusLabel.Text = $"当前加载: {Path.GetFileName(ofd.FileName)} | 大小: {_fileData.Length} 字节";

                        // 6. 填充对象目录列表视图
                        PopulateDirectory();

                        // 7. 更新战役全局属性视图
                        UpdateBattleAttributesView();

                        lblTitle.Text = "文件加载成功，点击地图格子查看物理偏移";
                        txtDetails.Text = "";
                        _selectedCellIndex = -1;
                    }
                    catch (Exception ex)
                    {
                        HandleLoadException(ofd.FileName, ex, "打开关卡失败");
                    }
                    finally
                    {
                        Cursor = Cursors.Default;
                    }
                }
            }
        }

        private void HardcodedSchemaClick(object sender, EventArgs e)
        {
            try
            {
                Cursor = Cursors.WaitCursor;
                BtlSchema.AutoLoadDefaultFbs();
                hardcodedSchemaMenuItem.Checked = BtlSchema.UseDynamic;
                dynamicSchemaMenuItem.Checked = false;
                statusLabel.Text = BtlSchema.UseDynamic
                    ? $"已重载默认 FBS Schema: {Path.GetFileName(BtlSchema.DefaultFbsPath)}"
                    : "默认 FBS 文件未找到，请手动选择";
                ReloadCurrentFile();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"重载默认 FBS 失败：\n{ex.Message}", "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            finally
            {
                Cursor = Cursors.Default;
            }
        }

        private void DynamicSchemaClick(object sender, EventArgs e)
        {
            using (var ofd = new OpenFileDialog())
            {
                ofd.Filter = "FlatBuffers Schema 文件 (*.fbs)|*.fbs|所有文件 (*.*)|*.*";
                ofd.Title = "请选择外部 FlatBuffers Schema 文件以动态解析";
                ofd.InitialDirectory = Path.Combine(BtlSchema.WorkspacePath, "schema");

                if (ofd.ShowDialog() == DialogResult.OK)
                {
                    try
                    {
                        Cursor = Cursors.WaitCursor;
                        BtlSchema.LoadFromFbs(ofd.FileName);
                        
                        hardcodedSchemaMenuItem.Checked = false;
                        dynamicSchemaMenuItem.Checked = true;
                        statusLabel.Text = $"已动态加载 FBS Schema: {Path.GetFileName(ofd.FileName)}";
                        ReloadCurrentFile();
                    }
                    catch (Exception ex)
                    {
                        MessageBox.Show($"解析 FBS 模式文件失败:\n{ex.Message}", "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    }
                    finally
                    {
                        Cursor = Cursors.Default;
                    }
                }
            }
        }

        private void ReloadCurrentFile()
        {
            if (_stage != null && !string.IsNullOrEmpty(_stage.File) && File.Exists(_stage.File))
            {
                try
                {
                    Cursor = Cursors.WaitCursor;
                    string filePath = _stage.File;
                    
                    _fileData = File.ReadAllBytes(filePath);
                    _stage = BtlBridge.LoadBtl(filePath);

                    LoadHexView(_fileData);
                    LoadDisasmView(_fileData, _stage);

                    mapCanvas.Stage = _stage;
                    mapCanvas.Invalidate();

                    PopulateDirectory();

                    // 更新战役全局属性视图
                    UpdateBattleAttributesView();

                    lblTitle.Text = "文件重载成功，点击地图格子查看物理偏移";
                    txtDetails.Text = "";
                    _selectedCellIndex = -1;
                }
                catch (Exception ex)
                {
                    HandleLoadException(_stage.File, ex, "重新加载关卡失败");
                }
                finally
                {
                    Cursor = Cursors.Default;
                }
            }
        }

        private void HandleLoadException(string filePath, Exception ex, string title)
        {
            string errMessage = $"{title}:\n{ex.Message}\n\n是否保存详细错误日志以定位问题？";
            var result = MessageBox.Show(errMessage, "载入失败", MessageBoxButtons.YesNo, MessageBoxIcon.Error);
            if (result == DialogResult.Yes)
            {
                using (var sfd = new SaveFileDialog())
                {
                    sfd.Filter = "文本文件 (*.txt)|*.txt|所有文件 (*.*)|*.*";
                    sfd.FileName = $"{Path.GetFileNameWithoutExtension(filePath)}_load_error.txt";
                    sfd.Title = "保存崩溃错误日志";
                    if (sfd.ShowDialog() == DialogResult.OK)
                    {
                        try
                        {
                            var sb = new StringBuilder();
                            sb.AppendLine("================ BTL 载入失败错误日志 ================");
                            sb.AppendLine($"文件: {Path.GetFileName(filePath)}");
                            sb.AppendLine($"时间: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
                            sb.AppendLine($"类型: {title}");
                            sb.AppendLine($"异常信息: {ex.Message}");
                            sb.AppendLine("======================================================");
                            sb.AppendLine();
                            
                            string pathTrace = null;
                            if (ex is BtlDecompileException bde)
                            {
                                pathTrace = bde.BtlPathTrace;
                            }
                            else if (ex.InnerException is BtlDecompileException bdeInner)
                            {
                                pathTrace = bdeInner.BtlPathTrace;
                            }

                            if (!string.IsNullOrEmpty(pathTrace))
                            {
                                sb.AppendLine("【BTL 内部解析路径 (BTL Parsing Path Trace)】");
                                sb.AppendLine(pathTrace);
                            }
                            else
                            {
                                sb.AppendLine("【BTL 内部解析路径 (BTL Parsing Path Trace)】");
                                sb.AppendLine("（无可用路径追踪）");
                            }
                            
                            File.WriteAllText(sfd.FileName, sb.ToString(), Encoding.UTF8);
                            
                            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                            {
                                FileName = sfd.FileName,
                                UseShellExecute = true
                            });
                        }
                        catch (Exception saveEx)
                        {
                            MessageBox.Show($"保存日志失败: {saveEx.Message}", "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
                        }
                    }
                }
            }
        }

        private void DumpDisasmClick(object sender, EventArgs e)
        {
            if (_disasmItems == null || rtbDisasm == null) return;
            using (var sfd = new SaveFileDialog())
            {
                sfd.Filter = "文本文件 (*.txt)|*.txt|所有文件 (*.*)|*.*";
                sfd.FileName = _stage != null ? $"{Path.GetFileNameWithoutExtension(_stage.File)}_struct.txt" : "stage_struct.txt";
                if (sfd.ShowDialog() == DialogResult.OK)
                {
                    try
                    {
                        Cursor = Cursors.WaitCursor;
                        File.WriteAllText(sfd.FileName, rtbDisasm.Text);
                        MessageBox.Show("导出结构布局文本成功！", "成功", MessageBoxButtons.OK, MessageBoxIcon.Information);
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
        }

        private void LoadHexView(byte[] data)
        {
            // 十六进制查看器已被对象目录列表视图取代
        }

        private void DisasmSelectionChanged(object sender, EventArgs e)
        {
            if (_isSyncingSelection || _stage == null || _disasmLines == null) return;

            try
            {
                int charIndex = rtbDisasm.SelectionStart;
                int lineIndex = rtbDisasm.GetLineFromCharIndex(charIndex);
                if (lineIndex < 0 || lineIndex >= _disasmLines.Length) return;

                string lineText = _disasmLines[lineIndex];
                int colonIdx = lineText.IndexOf(':');
                if (lineText.StartsWith(".") && colonIdx > 0 && lineText.Length >= colonIdx + 9)
                {
                    string offsetStr = lineText.Substring(colonIdx + 1, 8);
                    int offset = Convert.ToInt32(offsetStr, 16);

                    _currentOffset = offset;
                    pnlSegmentBar.Invalidate();
                    pnlArrowGutter.Invalidate();

                    // 检索匹配物理地址的网格单元
                    CellModel matchedCell = null;
                    foreach (var cell in _stage.Cells)
                    {
                        if (cell.TileFileOffset == offset ||
                            cell.AttrA1FileOffset == offset ||
                            cell.AttrA2FileOffset == offset ||
                            cell.AttrA3FileOffset == offset ||
                            cell.UnitTableFileOffset == offset)
                        {
                            matchedCell = cell;
                            break;
                        }
                    }

                    if (matchedCell != null)
                    {
                        _isSyncingSelection = true;

                        // 联动同步更新地图画布与右侧面板，不反向回滚滚动条
                        _selectedCellIndex = matchedCell.Index;
                        mapCanvas.SelectedCellIndex = matchedCell.Index;
                        mapCanvas.Invalidate();

                        CellSelectedHandler(matchedCell.Index, syncDisasm: false);

                        _isSyncingSelection = false;
                    }
                }
            }
            catch
            {
                _isSyncingSelection = false;
            }
        }

        private void MapCanvasCellDoubleClicked(int index)
        {
            if (_stage == null || index < 0 || index >= _stage.Cells.Count) return;

            // 检查当前是不是仅部队/仅单位图层
            if (mapCanvas.RenderMode == MapRenderMode.UnitsOnly)
            {
                var cell = _stage.Cells[index];
                if (cell.Unit != null && cell.UnitTableFileOffset != 0)
                {
                    // 1. 切换到 IDA 视图 Tab
                    tabControl.SelectedTab = tabPageDisasm;

                    // 2. 跳转到该部队对应的偏移量，并选中高亮
                    JumpToOffset(cell.UnitTableFileOffset);
                }
            }
            else if (mapCanvas.RenderMode == MapRenderMode.TerrainOnly)
            {
                var cell = _stage.Cells[index];
                if (cell.TileFileOffset != 0)
                {
                    // 1. 切换到 IDA 视图 Tab
                    tabControl.SelectedTab = tabPageDisasm;

                    // 2. 跳转到该地块地形数据对应的偏移量，并选中高亮
                    JumpToOffset(cell.TileFileOffset);
                }
            }
            else if (mapCanvas.RenderMode == MapRenderMode.BuildingsOnly)
            {
                var cell = _stage.Cells[index];
                if (cell.TriggerBuilding != null && cell.TriggerBuilding.FileOffset != 0)
                {
                    // 1. 切换到 IDA 视图 Tab
                    tabControl.SelectedTab = tabPageDisasm;

                    // 2. 跳转到该建筑触发器事件对应的偏移量，并选中高亮
                    JumpToOffset(cell.TriggerBuilding.FileOffset);
                }
            }
            else if (mapCanvas.RenderMode == MapRenderMode.FortificationsOnly)
            {
                var cell = _stage.Cells[index];
                if (cell.TriggerFort != null && cell.TriggerFort.FileOffset != 0)
                {
                    // 1. 切换到 IDA 视图 Tab
                    tabControl.SelectedTab = tabPageDisasm;

                    // 2. 跳转到该工事触发器事件对应的偏移量，并选中高亮
                    JumpToOffset(cell.TriggerFort.FileOffset);
                }
            }
            else if (mapCanvas.RenderMode == MapRenderMode.DecorationsOnly)
            {
                var cell = _stage.Cells[index];
                if (cell.AttrA1FileOffset != 0)
                {
                    // 1. 切换到 IDA 视图 Tab
                    tabControl.SelectedTab = tabPageDisasm;

                    // 2. 跳转到该地块主要属性的物理文件偏移位置，并选中高亮
                    JumpToOffset(cell.AttrA1FileOffset);
                }
            }
        }

        private void CellSelectedHandler(int index)
        {
            CellSelectedHandler(index, true);
        }

        private void CellSelectedHandler(int index, bool syncDisasm)
        {
            if (_stage == null || index < 0 || index >= _stage.Cells.Count) return;

            _selectedCellIndex = index;
            var cell = _stage.Cells[index];

            _currentOffset = cell.TileFileOffset;
            pnlSegmentBar.Invalidate();

            try
            {
                // 1. 更新下方信息面板
                var sb = new StringBuilder();
                sb.AppendLine($"一维网格索引 (Index) : {cell.Index}");
                sb.AppendLine($"二维坐标系位置 (X, Y) : ({cell.X}, {cell.Y})");
                sb.AppendLine("--------------------------------------------------------------------------------");

                // 瓦片数据段展示
                sb.AppendLine($"【瓦片数据】 (Tiles Vector - 绿色高亮)");
                sb.AppendLine($"  - 物理文件偏移 (Offset) : 0x{cell.TileFileOffset:X}");
                sb.AppendLine($"  - 原始地块数值 (Terrain): 0x{cell.Terrain:X4} ({cell.Terrain})");
                sb.AppendLine($"  - 高位属性标志 (v6 Flag): 0x{cell.Terrain >> 8:X2}");
                sb.AppendLine($"    * 包含 A1 属性标志位 (v6 & 4)   : {((cell.Terrain >> 8 & 4) != 0 ? "是 (1)" : "否 (0)")}");
                sb.AppendLine($"    * 包含 A2 属性标志位 (v6 & 8)   : {((cell.Terrain >> 8 & 8) != 0 ? "是 (1)" : "否 (0)")}");
                sb.AppendLine($"    * 包含 A3 属性标志位 (v6 & 0x10): {((cell.Terrain >> 8 & 0x10) != 0 ? "是 (1)" : "否 (0)")}");
                sb.AppendLine("--------------------------------------------------------------------------------");

                // 附加属性段展示
                sb.AppendLine($"【动态附加属性】 (Attributes Vector - 黄色/紫色高亮)");
                if (cell.AttrA1FileOffset != 0)
                {
                    sb.AppendLine($"  - [A1 属性] Offset: 0x{cell.AttrA1FileOffset:X} | Bytes: {FormatBytes(cell.Attr)}");
                    sb.AppendLine($"    * Doodad 装饰 ID  (Byte 0) : {cell.Attr[0]}");
                    sb.AppendLine($"    * 装饰贴图索引/阵营 (Byte 1) : {cell.Attr[1]}");
                    sb.AppendLine($"    * 水平偏移 dx     (Byte 2) : {(sbyte)cell.Attr[2]}");
                    sb.AppendLine($"    * 垂直偏移 dy     (Byte 3) : {(sbyte)cell.Attr[3]}");
                }
                else
                {
                    sb.AppendLine("  - [A1 属性] 未配置 (默认填充 Byte 1=-1, 其余=0)");
                }

                if (cell.AttrA2FileOffset != 0)
                {
                    sb.AppendLine($"  - [A2 属性] Offset: 0x{cell.AttrA2FileOffset:X} | Bytes: {FormatBytes(cell.AttrA2)}");
                    sb.AppendLine($"    * Doodad 装饰 ID  (Byte 0) : {cell.AttrA2[0]}");
                    sb.AppendLine($"    * 装饰贴图索引/阵营 (Byte 1) : {cell.AttrA2[1]}");
                    sb.AppendLine($"    * 水平偏移 dx     (Byte 2) : {(sbyte)cell.AttrA2[2]}");
                    sb.AppendLine($"    * 垂直偏移 dy     (Byte 3) : {(sbyte)cell.AttrA2[3]}");
                }
                else
                {
                    sb.AppendLine("  - [A2 属性] 未配置 (默认填充 Byte 1=-1, 其余=0)");
                }
                if (cell.AttrA3FileOffset != 0)
                {
                    sb.AppendLine($"  - [A3 属性] Offset: 0x{cell.AttrA3FileOffset:X} | Bytes: {FormatBytes(cell.AttrA3)}");
                    sb.AppendLine($"    * Doodad 装饰 ID  (Byte 0) : {cell.AttrA3[0]}");
                    sb.AppendLine($"    * 装饰贴图索引/阵营 (Byte 1) : {cell.AttrA3[1]}");
                    sb.AppendLine($"    * 水平偏移 dx     (Byte 2) : {(sbyte)cell.AttrA3[2]}");
                    sb.AppendLine($"    * 垂直偏移 dy     (Byte 3) : {(sbyte)cell.AttrA3[3]}");
                }
                else
                {
                    sb.AppendLine("  - [A3 属性] 未配置 (默认填充 Byte 1=-1, 其余=0)");
                }
                sb.AppendLine("--------------------------------------------------------------------------------");

                // 初始部署部队段展示
                sb.AppendLine($"【部署部队配置】 (AIAgent Table - 红色高亮)");
                if (cell.Unit != null)
                {
                    sb.AppendLine($"  - AIAgent 表物理偏移 (Offset): 0x{cell.UnitTableFileOffset:X}");
                    sb.AppendLine($"  - 部署部队 ID     (Unit ID): {cell.Unit.UnitId}");
                    sb.AppendLine($"  - 所属势力 Faction (Faction): {cell.Unit.FactionId}");
                    sb.AppendLine($"  - 将领 ID        (General) : {(cell.Unit.GeneralId == 65535 ? "无将领" : cell.Unit.GeneralId.ToString())}");
                    sb.AppendLine($"  - 编队数量       (Stacks)  : {cell.Unit.StackCount}");
                    sb.AppendLine($"  - 生命值状况     (HP)      : {cell.Unit.HP} / {cell.Unit.MaxHP}");
                }
                else
                {
                    sb.AppendLine("  - 此格子没有部署初始部队");
                }
                sb.AppendLine("--------------------------------------------------------------------------------");

                // 增员部署点段展示
                sb.AppendLine($"【增员部队部署点】 (ReinforcePoint Table - 橙色高亮)");
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
                    sb.AppendLine($"  - 部署点绝对物理偏移 (Offset): 0x{cellRP.Offset:X}");
                    sb.AppendLine($"  - 允许增员的阵营 ID (Faction) : {cellRP.FactionId}");
                    sb.AppendLine($"  - 关键增员部署点    (Is Key)  : {(cellRP.IsKeyUnit ? "是 ★ (红圈/绿圈限制目标)" : "否")}");
                    sb.AppendLine($"  - 部署点附加标志    (Flag)    : {cellRP.Flag}");
                }
                else
                {
                    sb.AppendLine("  - 此格子不是增员部队部署点");
                }

                lblTitle.Text = $"当前选中格子: {cell.X}, {cell.Y}";
                txtDetails.Text = sb.ToString();

                // 2. 联动选中目录列表中的对应项
                SelectDirectoryItem(index);
            }
            catch {}

            // 3. 联动跳转并高亮 Tab 1 中的 IDA 文本行
            if (syncDisasm && !_isSyncingSelection)
            {
                SyncDisasmSelection(cell.TileFileOffset);
            }
        }

        private void SyncDisasmSelection(int offset)
        {
            if (rtbDisasm == null || _stage == null || _disasmItems == null || _disasmLines == null) return;

            try
            {
                _isSyncingSelection = true;

                // 寻找该 offset 的具体段前缀
                string section = ".terrain";
                foreach (var item in _disasmItems)
                {
                    if (item.Offset == offset)
                    {
                        section = item.Section;
                        break;
                    }
                }

                string prefix = $"{section,-9}:{offset:X8}";
                for (int i = 0; i < _disasmLines.Length; i++)
                {
                    if (_disasmLines[i].StartsWith(prefix))
                    {
                        int start = rtbDisasm.GetFirstCharIndexFromLine(i);
                        int len = _disasmLines[i].Length;
                        if (rtbDisasm.SelectionStart != start || rtbDisasm.SelectionLength != len)
                        {
                            SendMessage(rtbDisasm.Handle, WM_SETREDRAW, IntPtr.Zero, IntPtr.Zero);
                            try
                            {
                                int scrollIndex = Math.Max(0, i - 3);
                                int scrollStart = rtbDisasm.GetFirstCharIndexFromLine(scrollIndex);
                                rtbDisasm.Select(scrollStart, 0);
                                rtbDisasm.ScrollToCaret();

                                rtbDisasm.Select(start, len);
                            }
                            finally
                            {
                                SendMessage(rtbDisasm.Handle, WM_SETREDRAW, new IntPtr(1), IntPtr.Zero);
                                rtbDisasm.Refresh();
                            }
                        }
                        break;
                    }
                }
                _isSyncingSelection = false;
            }
            catch
            {
                _isSyncingSelection = false;
            }
        }

        private void ResetHexHighlights()
        {
        }

        private void HighlightHexRange(int offset, int length, Color backColor, Color foreColor)
        {
        }

        private void ScrollToHexOffset(int offset)
        {
        }

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wp, IntPtr lp);
        private const int WM_SETREDRAW = 0x0B;
        private const int EM_GETFIRSTVISIBLELINE = 0x00CE;
        private const int EM_LINESCROLL = 0x00B6;

        private void LoadDisasmView(byte[] data, StageModel stage)
        {
            _disasmItems = BtlBridge.GenerateDisasm(data, stage);

            // 1. 构造并缓存类似 IDA 的分段概览条 Bitmap (1000 像素采样宽度)
            if (data != null && data.Length > 0)
            {
                _segmentBarBmp = new Bitmap(1000, 20);
                using (var g = Graphics.FromImage(_segmentBarBmp))
                {
                    for (int x = 0; x < 1000; x++)
                    {
                        int offset = (int)((long)x * data.Length / 1000);
                        Color col = Color.FromArgb(203, 213, 225); // 默认数据灰 (.data)

                        foreach (var item in _disasmItems)
                        {
                            if (offset >= item.Offset && offset < item.Offset + item.Length)
                            {
                                col = GetSectionColor(item.Section);
                                break;
                            }
                        }

                        using (var pen = new Pen(col))
                        {
                            g.DrawLine(pen, x, 0, x, 20);
                        }
                    }
                }
                pnlSegmentBar.Invalidate(); // 刷新绘制分段条
            }

            // 2. 利用原生 RTF 拼接机制，进行单次赋值加载，从几秒提速到 5 毫秒（瞬间渲染完毕）
            var rtf = new StringBuilder();
            rtf.Append(@"{\rtf1\ansi\deff0{\fonttbl{\f0\fnil\fcharset0 Consolas;}{\f1\fnil\fcharset134 NSimSun;}}");
            // 颜色定义表:
            // 0: default, 
            // 1: 地址灰色 (100, 116, 139)
            // 2: 字节青绿 (13, 148, 136)
            // 3: 默认黑/深色 (15, 23, 42)
            // 4: 注释灰 (148, 163, 184)
            // 5: 属性紫 (109, 40, 217)
            // 6: 网格蓝 (3, 105, 161)
            // 7: 部队红 (185, 28, 28)
            // 8: XREF 粉色 (236, 72, 153)
            // 9: 省略字段暗琥珀色 (180, 130, 50)
            rtf.Append(@"{\colortbl ;\red100\green116\blue139;\red13\green148\blue136;\red15\green23\blue42;\red148\green163\blue184;\red109\green40\blue217;\red3\green105\blue161;\red185\green28\blue28;\red236\green72\blue153;\red180\green130\blue50;}");
            rtf.Append(@"\f0\fs20 ");

             foreach (var item in _disasmItems)
             {
                 // 强制设置固定行高 (250 twips = 12.5pt) 彻底防止中英文混排导致的高度抖动
                 rtf.Append(@"\sl-250\slmult0 ");

                 // === [DEFAULT] 省略字段：整行用暗琥珀色渲染，无 hex 列 ===
                 if (item.Description.StartsWith("[DEFAULT]"))
                 {
                     // 地址列: 仍显示表偏移，但标记为虚拟
                     rtf.Append(@"\f0\cf9 " + EscapeRtf($"{item.Section,-9}:{item.Offset:X8}    "));
                     // Hex 列: 空白占位
                     rtf.Append(@"\f0\cf9 " + EscapeRtf($"{"(omitted)",-35}    "));
                     // 描述列: 整行暗琥珀色
                     string defDesc = item.Description;
                     int defSemi = defDesc.IndexOf(';');
                     if (defSemi >= 0)
                     {
                         string defEng = defDesc.Substring(0, defSemi);
                         string defChi = defDesc.Substring(defSemi);
                         string defCommentFont = HasChinese(defChi) ? "\\f1" : "\\f0";
                         rtf.Append(@"\f0\cf9 " + EscapeRtf(defEng));
                         rtf.Append($"{defCommentFont}\\cf9 " + EscapeRtf(defChi));
                     }
                     else
                     {
                         rtf.Append(@"\f0\cf9 " + EscapeRtf(defDesc));
                     }
                     rtf.Append(@"\par ");
                     continue;
                 }

                 // 地址部分 (cf1)
                 rtf.Append(@"\f0\cf1 " + EscapeRtf($"{item.Section,-9}:{item.Offset:X8}    "));
 
                 // Hex 部分 (cf2)
                 rtf.Append(@"\f0\cf2 " + EscapeRtf($"{item.HexBytes,-35}    "));
 
                 // 描述主体着色
                 int cIdx = 3; // 默认 cf3
                 if (item.Description.StartsWith("align") || item.Description.StartsWith("db") || item.Description.StartsWith(";"))
                 {
                     cIdx = 4; // cf4
                 }
                 else if (item.Description.StartsWith("loc_"))
                 {
                     cIdx = 6; // cf6 (网格蓝)
                 }
                 else if (item.Description.Contains(".attr_a"))
                 {
                     cIdx = 5; // cf5
                 }
                 else if (item.Description.StartsWith("cell["))
                 {
                     cIdx = 6; // cf6
                 }
                 else if (item.Description.StartsWith("unit["))
                 {
                     cIdx = 7; // cf7
                 }
 
                 string descStr = item.Description;
                 int semiIdx = descStr.IndexOf(';');
                 if (semiIdx >= 0)
                 {
                     string engPart = descStr.Substring(0, semiIdx);
                     string chiPart = descStr.Substring(semiIdx);

                     if (!string.IsNullOrEmpty(item.XrefText))
                     {
                         string combinedEng = engPart;
                         if (combinedEng.Length < 60)
                         {
                             combinedEng = combinedEng.PadRight(60);
                         }
                         rtf.Append($"\\f0\\cf{cIdx} " + EscapeRtf(combinedEng));
                         rtf.Append(@" \cf8 " + EscapeRtf($" {item.XrefText}"));
                     }
                     else
                     {
                         rtf.Append($"\\f0\\cf{cIdx} " + EscapeRtf(engPart));
                     }
                     string commentFont = HasChinese(chiPart) ? "\\f1" : "\\f0";
                     rtf.Append($"{commentFont}\\cf4 " + EscapeRtf(chiPart));
                 }
                 else
                 {
                     string lineFont = HasChinese(descStr) ? "\\f1" : "\\f0";
                     if (!string.IsNullOrEmpty(item.XrefText))
                     {
                         if (descStr.Length < 60)
                         {
                             descStr = descStr.PadRight(60);
                         }
                         rtf.Append($"{lineFont}\\cf{cIdx} " + EscapeRtf(descStr));
                         rtf.Append(@" \f0\cf8 " + EscapeRtf($" {item.XrefText}"));
                     }
                     else
                     {
                          rtf.Append($"{lineFont}\\cf{cIdx} " + EscapeRtf(descStr));
                     }
                 }
 
                 rtf.Append(@"\par ");
             }

            rtf.Append("}");
            rtbDisasm.Rtf = rtf.ToString();
            _disasmLines = rtbDisasm.Lines;
            
            // 预先建立 O(1) 指针连线缓存，消除 Paint 中耗时的 O(N^2) 搜索
            _allLinks.Clear();
            if (_disasmItems != null)
            {
                var offsetToLine = new Dictionary<int, int>();
                for (int i = 0; i < _disasmItems.Count; i++)
                {
                    var item = _disasmItems[i];
                    if (item.Description.StartsWith("loc_"))
                    {
                        offsetToLine[item.Offset] = i;
                    }
                    else if (!offsetToLine.ContainsKey(item.Offset))
                    {
                        offsetToLine[item.Offset] = i;
                    }
                }

                for (int i = 0; i < _disasmItems.Count; i++)
                {
                    var item = _disasmItems[i];
                    if (item.TargetOffset.HasValue)
                    {
                        // 过滤掉无意义的 FlatBuffers 内置 vtable_offset 连线 (减少视觉噪音)
                        if (item.Description.Contains("vtable_offset") || item.Description.Contains(".vtable"))
                        {
                            continue;
                        }

                        if (offsetToLine.TryGetValue(item.TargetOffset.Value, out int dstLine))
                        {
                            _allLinks.Add(Tuple.Create(i, dstLine));
                        }
                    }
                }
            }

            // 预先测量并缓存单行高度，避免在滚动重绘时高频调用 Win32 接口
            MeasureLineHeight();

            pnlArrowGutter.Invalidate();
        }

        private void MeasureLineHeight()
        {
            if (rtbDisasm == null || _disasmLines == null || _disasmLines.Length < 2) return;
            try
            {
                int char1 = rtbDisasm.GetFirstCharIndexFromLine(0);
                int char2 = rtbDisasm.GetFirstCharIndexFromLine(1);
                if (char1 >= 0 && char2 >= 0)
                {
                    Point pt1 = rtbDisasm.GetPositionFromCharIndex(char1);
                    Point pt2 = rtbDisasm.GetPositionFromCharIndex(char2);
                    if (pt2.Y > pt1.Y)
                    {
                        _cachedLineHeight = pt2.Y - pt1.Y;
                        return;
                    }
                }
            }
            catch {}
            _cachedLineHeight = (int)Math.Round(rtbDisasm.Font.Height * 1.15);
            if (_cachedLineHeight <= 0) _cachedLineHeight = 17;
        }

        private Color GetSectionColor(string sec)
        {
            if (string.IsNullOrEmpty(sec))
                return Color.FromArgb(203, 213, 225); // 默认数据灰

            switch (sec.ToLower())
            {
                case ".header": return Color.FromArgb(148, 163, 184); // 灰色
                case ".terrain": return Color.FromArgb(74, 222, 128); // 绿色
                case ".battle": return Color.FromArgb(248, 113, 113); // 红色
                case ".metadata": return Color.FromArgb(250, 204, 21); // 黄色
                case ".trigger": return Color.FromArgb(96, 165, 250); // 蓝色
                case ".ai": return Color.FromArgb(192, 132, 252); // 紫色
                case ".region": return Color.FromArgb(45, 212, 191); // 青色
                case ".weather":
                case ".decal": return Color.FromArgb(251, 146, 60); // 橙色
                case ".rle": return Color.FromArgb(244, 114, 182); // 粉色
                case ".faction": return Color.FromArgb(236, 72, 153); // 深粉色/玫瑰红
            }

            int hash = 0;
            foreach (char c in sec)
            {
                hash = c + (hash << 6) + (hash << 16) - hash;
            }

            double hue = Math.Abs(hash % 360);
            return ColorFromHsv(hue, 0.65, 0.70);
        }

        private static Color ColorFromHsv(double hue, double saturation, double value)
        {
            int hi = Convert.ToInt32(Math.Floor(hue / 60)) % 6;
            double f = hue / 60 - Math.Floor(hue / 60);

            value = value * 255;
            int v = Convert.ToInt32(value);
            int p = Convert.ToInt32(value * (1 - saturation));
            int q = Convert.ToInt32(value * (1 - f * saturation));
            int t = Convert.ToInt32(value * (1 - (1 - f) * saturation));

            if (hi == 0)
                return Color.FromArgb(v, t, p);
            else if (hi == 1)
                return Color.FromArgb(q, v, p);
            else if (hi == 2)
                return Color.FromArgb(p, v, t);
            else if (hi == 3)
                return Color.FromArgb(p, q, v);
            else if (hi == 4)
                return Color.FromArgb(t, p, v);
            else
                return Color.FromArgb(v, p, q);
        }

        private string EscapeRtf(string text)
        {
            if (string.IsNullOrEmpty(text)) return "";
            
            var sb = new StringBuilder();
            foreach (char c in text)
            {
                if (c == '\\')
                {
                    sb.Append("\\\\");
                }
                else if (c == '{')
                {
                    sb.Append("\\{");
                }
                else if (c == '}')
                {
                    sb.Append("\\}");
                }
                else if (c > 127)
                {
                    // 使用 RTF 的 Unicode 逸出序列 \uN? (N 为带符号 16 位整数)
                    sb.Append("\\u" + ((short)c).ToString() + "?");
                }
                else
                {
                    sb.Append(c);
                }
            }
            return sb.ToString();
        }

        private bool HasChinese(string text)
        {
            if (string.IsNullOrEmpty(text)) return false;
            for (int i = 0; i < text.Length; i++)
            {
                if (text[i] >= 0x4E00 && text[i] <= 0x9FBB)
                {
                    return true;
                }
            }
            return false;
        }

        private void SegmentBarPaint(object sender, PaintEventArgs e)
        {
            // 1. 绘制分段背景
            if (_segmentBarBmp != null)
            {
                e.Graphics.DrawImage(_segmentBarBmp, 0, 0, pnlSegmentBar.Width, pnlSegmentBar.Height);
            }
            else
            {
                e.Graphics.Clear(Color.FromArgb(226, 232, 240));
            }

            // 2. 在当前所指的物理字节位置绘制发光黄/黑浮标指示器
            if (_currentOffset >= 0 && _fileData != null && _fileData.Length > 0)
            {
                float pct = (float)_currentOffset / _fileData.Length;
                int x = (int)(pct * pnlSegmentBar.Width);

                using (var shadowPen = new Pen(Color.FromArgb(15, 23, 42), 3)) // 深色投影 (3px 宽)
                using (var corePen = new Pen(Color.Yellow, 1))                // 黄色浮标 (1px 宽)
                {
                    e.Graphics.DrawLine(shadowPen, x, 0, x, pnlSegmentBar.Height);
                    e.Graphics.DrawLine(corePen, x, 0, x, pnlSegmentBar.Height);
                }
            }
        }

        private void SegmentBarMouseDown(object sender, MouseEventArgs e)
        {
            if (_fileData == null || _fileData.Length == 0 || _disasmItems == null || rtbDisasm == null) return;
            try
            {
                float pct = (float)e.X / pnlSegmentBar.Width;
                int targetOffset = (int)(pct * _fileData.Length);
                JumpToOffset(targetOffset);
            }
            catch {}
        }

        private void SegmentBarMouseMove(object sender, MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left)
            {
                if (_fileData == null || _fileData.Length == 0 || _disasmItems == null || rtbDisasm == null) return;
                try
                {
                    int x = Math.Max(0, Math.Min(e.X, pnlSegmentBar.Width));
                    float pct = (float)x / pnlSegmentBar.Width;
                    int targetOffset = (int)(pct * _fileData.Length);
                    JumpToOffset(targetOffset);
                }
                catch {}
            }
        }

        private void DisasmDoubleClick(object sender, EventArgs e)
        {
            if (_disasmItems == null || _fileData == null || rtbDisasm == null) return;
            try
            {
                int charIndex = rtbDisasm.SelectionStart;
                int lineIndex = rtbDisasm.GetLineFromCharIndex(charIndex);
                if (lineIndex < 0 || lineIndex >= _disasmItems.Count) return;

                var item = _disasmItems[lineIndex];
                if (item.TargetOffset.HasValue)
                {
                    _navHistory.Push(item.Offset);
                    JumpToOffset(item.TargetOffset.Value);
                }
            }
            catch {}
        }

        private void DisasmKeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Escape || (e.KeyCode == Keys.Left && e.Alt))
            {
                if (_navHistory.Count > 0)
                {
                    int prevOffset = _navHistory.Pop();
                    JumpToOffset(prevOffset);
                    e.Handled = true;
                }
            }
            else if (e.KeyCode == Keys.F7 && e.Control)
            {
                StepOut();
                e.Handled = true;
            }
            else if (e.KeyCode == Keys.F7)
            {
                StepInto();
                e.Handled = true;
            }
            else if (e.KeyCode == Keys.F8)
            {
                StepOver();
                e.Handled = true;
            }
            else if (e.KeyCode == Keys.F9)
            {
                RunDebug();
                e.Handled = true;
            }
            else if (e.KeyCode == Keys.F2)
            {
                ToggleBreakpointClick(this, EventArgs.Empty);
                e.Handled = true;
            }
        }

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            if (_stage != null && _disasmItems != null && _disasmItems.Count > 0)
            {
                if (keyData == Keys.F7)
                {
                    StepInto();
                    return true;
                }
                if (keyData == (Keys.F7 | Keys.Control))
                {
                    StepOut();
                    return true;
                }
                if (keyData == Keys.F8)
                {
                    StepOver();
                    return true;
                }
                if (keyData == Keys.F9)
                {
                    RunDebug();
                    return true;
                }
                if (keyData == Keys.F2)
                {
                    ToggleBreakpointClick(this, EventArgs.Empty);
                    return true;
                }
                if (keyData == Keys.Escape)
                {
                    if (_navHistory.Count > 0)
                    {
                        int prevOffset = _navHistory.Pop();
                        JumpToOffset(prevOffset);
                        return true;
                    }
                }
            }

            return base.ProcessCmdKey(ref msg, keyData);
        }

        private void JumpToOffset(int targetOffset)
        {
            if (_disasmItems == null || rtbDisasm == null) return;

            int bestIndex = -1;
            int minDist = int.MaxValue;

            for (int i = 0; i < _disasmItems.Count; i++)
            {
                int dist = Math.Abs(_disasmItems[i].Offset - targetOffset);
                if (dist < minDist)
                {
                    minDist = dist;
                    bestIndex = i;
                }
            }

            if (bestIndex != -1)
            {
                int start = rtbDisasm.GetFirstCharIndexFromLine(bestIndex);
                int len = 0;
                if (_disasmLines != null && bestIndex >= 0 && bestIndex < _disasmLines.Length)
                {
                    len = _disasmLines[bestIndex].Length;
                }

                if (rtbDisasm.SelectionStart != start || rtbDisasm.SelectionLength != len)
                {
                    _isSyncingSelection = true;
                    SendMessage(rtbDisasm.Handle, WM_SETREDRAW, IntPtr.Zero, IntPtr.Zero);
                    try
                    {
                        int scrollIndex = Math.Max(0, bestIndex - 3);
                        int scrollStart = rtbDisasm.GetFirstCharIndexFromLine(scrollIndex);
                        rtbDisasm.Select(scrollStart, 0);
                        rtbDisasm.ScrollToCaret();

                        rtbDisasm.Select(start, len);
                    }
                    finally
                    {
                        SendMessage(rtbDisasm.Handle, WM_SETREDRAW, new IntPtr(1), IntPtr.Zero);
                        rtbDisasm.Refresh();
                    }
                    _isSyncingSelection = false;
                }

                // 更新当前浮标位置
                int finalOffset = _disasmItems[bestIndex].Offset;
                _currentOffset = finalOffset;
                pnlSegmentBar.Invalidate();

                // 联动同步地图网格与 16 进制面板
                CellModel matchedCell = null;
                foreach (var cell in _stage.Cells)
                {
                    if (cell.TileFileOffset == finalOffset ||
                        cell.AttrA1FileOffset == finalOffset ||
                        cell.AttrA2FileOffset == finalOffset ||
                        cell.AttrA3FileOffset == finalOffset ||
                        cell.UnitTableFileOffset == finalOffset)
                    {
                        matchedCell = cell;
                        break;
                    }
                }

                if (matchedCell != null)
                {
                    _selectedCellIndex = matchedCell.Index;
                    mapCanvas.SelectedCellIndex = matchedCell.Index;
                    mapCanvas.Invalidate();
                    CellSelectedHandler(matchedCell.Index, syncDisasm: false);
                }
            }
        }
        private void DisasmVScroll(object sender, EventArgs e)
        {
            if (_stage == null || _disasmItems == null || _fileData == null || _fileData.Length == 0 || _isSyncingSelection) return;
            try
            {
                int firstLine = SendMessage(rtbDisasm.Handle, EM_GETFIRSTVISIBLELINE, IntPtr.Zero, IntPtr.Zero).ToInt32();
                if (firstLine >= 0 && firstLine < _disasmItems.Count)
                {
                    int newOffset = _disasmItems[firstLine].Offset;
                    if (_currentOffset != newOffset)
                    {
                        _currentOffset = newOffset;
                        pnlSegmentBar.Invalidate();
                    }
                }
                pnlArrowGutter.Invalidate();
            }
            catch {}
        }

        private void DisasmMouseWheel(object sender, MouseEventArgs e)
        {
            var hme = e as HandledMouseEventArgs;
            if (hme != null)
            {
                hme.Handled = true;
            }

            int scrollLines = SystemInformation.MouseWheelScrollLines;
            int lines;
            if (scrollLines == -1)
            {
                lines = (e.Delta > 0) ? -10 : 10;
            }
            else
            {
                lines = -(e.Delta * scrollLines) / 120;
            }

            if (lines != 0)
            {
                SendMessage(rtbDisasm.Handle, EM_LINESCROLL, IntPtr.Zero, new IntPtr(lines));
            }
        }

        private string FormatBytes(List<byte> list)
        {
            if (list == null) return "[]";
            var sb = new StringBuilder("[");
            for (int i = 0; i < list.Count; i++)
            {
                sb.Append($"0x{list[i]:X2}");
                if (i < list.Count - 1) sb.Append(", ");
            }
            sb.Append("]");
            return sb.ToString();
        }




        private string GetFortFeatureName(int feature)
        {
            switch (feature)
            {
                case 0: return "无 (None)";
                case 14: return "碉堡 (Bunker)";
                case 15: return "要塞 (Fortress)";
                case 16: return "雷达 (Radar)";
                case 56: return "雷区 (Mines)";
                case 57: return "战壕 (Trench)";
                default: return $"未定义工事 {feature}";
            }
        }

        private void ArrowGutterPaint(object sender, PaintEventArgs e)
        {
            if (_stage == null || _disasmItems == null || rtbDisasm == null || _disasmLines == null || _disasmLines.Length == 0) return;
            
            Graphics g = e.Graphics;
            g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            
            // 绘制右边缘的微弱分割竖线
            using (Pen borderPen = new Pen(Color.FromArgb(226, 232, 240), 1))
            {
                g.DrawLine(borderPen, pnlArrowGutter.Width - 1, 0, pnlArrowGutter.Width - 1, pnlArrowGutter.Height);
            }

            // 获取首个可见行的索引与相对位置
            int firstVisibleLine = SendMessage(rtbDisasm.Handle, EM_GETFIRSTVISIBLELINE, IntPtr.Zero, IntPtr.Zero).ToInt32();
            if (firstVisibleLine < 0 || firstVisibleLine >= _disasmLines.Length) return;

            // 计算 Gutter 面板与 RichTextBox 之间的 Y 轴坐标系偏移
            // (因为 pnlArrowGutter Dock=Left 占满 Tab 全高, 而 rtbDisasm 在 pnlSegmentBar 下方起始,
            //  两者的 Y=0 原点不同, 必须补偿这个差值)
            int yOffset = rtbDisasm.PointToScreen(Point.Empty).Y - pnlArrowGutter.PointToScreen(Point.Empty).Y;

            // 只需测量一次可见首行偏移 (1 次 Win32 API 交互)
            int char1 = rtbDisasm.GetFirstCharIndexFromLine(firstVisibleLine);
            int topY = 0;
            if (char1 >= 0)
            {
                Point pt1 = rtbDisasm.GetPositionFromCharIndex(char1);
                topY = pt1.Y;
            }

            // 估计当前视口可见的行数范围，裁剪并过滤出可见区域的连线
            int visibleLineCount = pnlArrowGutter.Height / _cachedLineHeight + 2;
            int vMin = firstVisibleLine - 5;
            int vMax = firstVisibleLine + visibleLineCount + 5;

            var visibleLinks = new List<ArrowLink>();
            foreach (var link in _allLinks)
            {
                int srcLine = link.Item1;
                int dstLine = link.Item2;

                // 判断箭头的竖直跨度是否与当前可见范围相交
                int minYLine = Math.Min(srcLine, dstLine);
                int maxYLine = Math.Max(srcLine, dstLine);
                if (maxYLine < vMin || minYLine > vMax)
                {
                    continue; // 跨度完全不可见，略过
                }

                // 采用 O(1) 线性插值直接计算物理像素 Y 坐标, 加上 yOffset 补偿坐标系差异
                int srcY = yOffset + topY + (srcLine - firstVisibleLine) * _cachedLineHeight + _cachedLineHeight / 2;
                int dstY = yOffset + topY + (dstLine - firstVisibleLine) * _cachedLineHeight + _cachedLineHeight / 2;

                visibleLinks.Add(new ArrowLink
                {
                    SrcLine = srcLine,
                    DstLine = dstLine,
                    SrcY = srcY,
                    DstY = dstY,
                    Lane = 0
                });
            }

            if (visibleLinks.Count > 0)
            {
                // 按照跨度从大到小排序，让长距离跨度线走到外侧通道以防交叉折叠
                visibleLinks.Sort((a, b) => Math.Abs(b.DstY - b.SrcY).CompareTo(Math.Abs(a.DstY - a.SrcY)));

                // 动态通道分配（Graph Coloring 思想的动态贪心布局）
                var lanes = new List<List<Tuple<int, int>>>();
                foreach (var link in visibleLinks)
                {
                    int minY = Math.Min(link.SrcY, link.DstY);
                    int maxY = Math.Max(link.SrcY, link.DstY);
                    
                    int allocatedLane = -1;
                    for (int l = 0; l < lanes.Count; l++)
                    {
                        bool overlap = false;
                        foreach (var interval in lanes[l])
                        {
                            if (maxY >= interval.Item1 - 2 && minY <= interval.Item2 + 2)
                            {
                                overlap = true;
                                break;
                            }
                        }
                        if (!overlap)
                        {
                            allocatedLane = l;
                            break;
                        }
                    }
                    
                    if (allocatedLane == -1)
                    {
                        allocatedLane = lanes.Count;
                        lanes.Add(new List<Tuple<int, int>>());
                    }
                    
                    lanes[allocatedLane].Add(Tuple.Create(minY, maxY));
                    link.Lane = allocatedLane;
                }

                // 获取当前选中的行索引 (光标所在位置)
                int selectedLine = -1;
                if (rtbDisasm.SelectionLength >= 0)
                {
                    selectedLine = rtbDisasm.GetLineFromCharIndex(rtbDisasm.SelectionStart);
                }

                // 判断当前可见链接中是否有与选中行相连的
                bool hasSelectedLink = visibleLinks.Any(link => link.SrcLine == selectedLine || link.DstLine == selectedLine);

                // 绘制所有指向箭头线
                foreach (var link in visibleLinks)
                {
                    bool isSelected = link.SrcLine == selectedLine || link.DstLine == selectedLine;
                    
                    Color arrowColor;
                    float penWidth;
                    
                    if (hasSelectedLink)
                    {
                        if (isSelected)
                        {
                            // 选中的关联线：高亮显示，线宽加粗
                            arrowColor = (link.DstY < link.SrcY) ? Color.FromArgb(239, 68, 68) : Color.FromArgb(59, 130, 246);
                            penWidth = 2.25f;
                        }
                        else
                        {
                            // 非选中的关联线：半透明虚化，移入背景
                            arrowColor = Color.FromArgb(40, 148, 163, 184); // 极低饱和度的淡灰色
                            penWidth = 1.0f;
                        }
                    }
                    else
                    {
                        // 没有关联连线被选中时：正常亮度显示，线宽标准
                        arrowColor = (link.DstY < link.SrcY) ? Color.FromArgb(239, 68, 68) : Color.FromArgb(59, 130, 246);
                        penWidth = 1.25f;
                    }
                    
                    using (Pen pen = new Pen(arrowColor, penWidth))
                    using (Brush brush = new SolidBrush(arrowColor))
                    {
                        int xStart = pnlArrowGutter.Width - 10;
                        int laneX = Math.Max(5, xStart - link.Lane * 8);

                        // 1. 从引用点画水平横线到通道
                        g.DrawLine(pen, xStart, link.SrcY, laneX, link.SrcY);
                        
                        // 2. 沿着通道画垂直竖线
                        g.DrawLine(pen, laneX, link.SrcY, laneX, link.DstY);
                        
                        // 3. 从通道画水平横线回到目标点
                        g.DrawLine(pen, laneX, link.DstY, xStart, link.DstY);

                        // 4. 在引用源头画小圆点
                        g.FillEllipse(brush, xStart - 2, link.SrcY - 2, 4, 4);

                        // 5. 在被引用目标头画右指向三角箭头
                        Point[] arrowPoints = {
                            new Point(xStart + 3, link.DstY),
                            new Point(xStart - 3, link.DstY - 3),
                            new Point(xStart - 3, link.DstY + 3)
                        };
                        g.FillPolygon(brush, arrowPoints);
                    }
                }
            }

            // 绘制所有设置的断点
            for (int lineIndex = firstVisibleLine; lineIndex < _disasmLines.Length; lineIndex++)
            {
                int lineY = yOffset + topY + (lineIndex - firstVisibleLine) * _cachedLineHeight + _cachedLineHeight / 2;
                if (lineY > pnlArrowGutter.Height) break;

                int offset = _disasmItems[lineIndex].Offset;
                if (_breakpoints.Contains(offset))
                {
                    using (var brush = new SolidBrush(Color.FromArgb(239, 68, 68))) // 红色
                    {
                        g.FillEllipse(brush, 4, lineY - 6, 12, 12);
                    }
                }
            }

            // 绘制当前调试指针 (PC) 黄色小箭头
            if (_isDebugActive && _debugPC >= 0 && _debugPC < _disasmItems.Count)
            {
                if (_debugPC >= vMin && _debugPC <= vMax)
                {
                    int lineY = yOffset + topY + (_debugPC - firstVisibleLine) * _cachedLineHeight + _cachedLineHeight / 2;
                    using (var brush = new SolidBrush(Color.FromArgb(254, 240, 138))) // 淡黄色
                    using (var borderPen = new Pen(Color.FromArgb(234, 179, 8), 1.5f)) // 深黄色边框
                    {
                        Point[] pointerPoints = {
                            new Point(4, lineY - 6),
                            new Point(14, lineY),
                            new Point(4, lineY + 6)
                        };
                        g.FillPolygon(brush, pointerPoints);
                        g.DrawPolygon(borderPen, pointerPoints);
                    }
                }
            }
        }

        private int FindLineIndexForOffset(int offset, bool preferLabel)
        {
            if (_disasmItems == null) return -1;
            int bestIndex = -1;
            for (int i = 0; i < _disasmItems.Count; i++)
            {
                if (_disasmItems[i].Offset == offset)
                {
                    if (bestIndex == -1) bestIndex = i;
                    if (preferLabel && _disasmItems[i].Description.StartsWith("loc_"))
                    {
                        return i;
                    }
                }
            }
            return bestIndex;
        }

        private bool _isSyncingDirectorySelection = false;

        private void DirectorySelectedIndexChanged(object sender, EventArgs e)
        {
            if (_isSyncingDirectorySelection || lvDirectory.SelectedItems.Count == 0 || _stage == null) return;
            var selectedItem = lvDirectory.SelectedItems[0];
            if (selectedItem.Tag is int cellIndex)
            {
                _selectedCellIndex = cellIndex;
                mapCanvas.SelectedCellIndex = cellIndex;
                mapCanvas.Invalidate();

                CellSelectedHandler(cellIndex, syncDisasm: true);
            }
        }

        private void SelectDirectoryItem(int cellIndex)
        {
            if (lvDirectory == null || _isSyncingDirectorySelection) return;
            _isSyncingDirectorySelection = true;
            try
            {
                foreach (ListViewItem item in lvDirectory.Items)
                {
                    if (item.Tag is int tagIndex && tagIndex == cellIndex)
                    {
                        if (!item.Selected)
                        {
                            lvDirectory.SelectedItems.Clear();
                            item.Selected = true;
                            item.EnsureVisible();
                        }
                        break;
                    }
                }
            }
            catch {}
            finally
            {
                _isSyncingDirectorySelection = false;
            }
        }

        private void PopulateDirectory()
        {
            if (_stage == null || lvDirectory == null) return;

            lvDirectory.BeginUpdate();
            lvDirectory.Items.Clear();
            lvDirectory.Columns.Clear();

            var mode = mapCanvas.RenderMode;
            
            if (mode == MapRenderMode.UnitsOnly)
            {
                lvDirectory.Columns.Add("No.", 40);
                lvDirectory.Columns.Add("坐标", 70);
                lvDirectory.Columns.Add("单位 ID", 60);
                lvDirectory.Columns.Add("部队种类", 95);
                lvDirectory.Columns.Add("兵力状况 (HP)", 90);
                lvDirectory.Columns.Add("阵营势力", 60);
                lvDirectory.Columns.Add("将领 ID", 70);

                int count = 0;
                foreach (var cell in _stage.Cells)
                {
                    if (cell.Unit != null)
                    {
                        var lvi = new ListViewItem(count.ToString());
                        lvi.SubItems.Add($"({cell.X}, {cell.Y})");
                        lvi.SubItems.Add(cell.Unit.UnitId.ToString());
                        lvi.SubItems.Add(GetUnitTypeName(cell.Unit.UnitId));
                        lvi.SubItems.Add($"{cell.Unit.HP}/{cell.Unit.MaxHP}");
                        lvi.SubItems.Add(cell.Unit.FactionId.ToString());
                        lvi.SubItems.Add(cell.Unit.GeneralId == 65535 ? "无将领" : cell.Unit.GeneralId.ToString());
                        lvi.Tag = cell.Index;
                        
                        if (cell.Index == _selectedCellIndex)
                        {
                            lvi.Selected = true;
                        }

                        lvDirectory.Items.Add(lvi);
                        count++;
                    }
                }
                lblDirectoryHeader.Text = $"【部队目录 - 共 {count} 个部队】";
            }
            else if (mode == MapRenderMode.BuildingsOnly)
            {
                lvDirectory.Columns.Add("No.", 40);
                lvDirectory.Columns.Add("坐标", 70);
                lvDirectory.Columns.Add("建筑 ID", 60);
                lvDirectory.Columns.Add("建筑种类 (Type)", 95);
                lvDirectory.Columns.Add("建筑名称", 110);
                lvDirectory.Columns.Add("所属阵营 (Owner)", 115);

                int count = 0;
                foreach (var cell in _stage.Cells)
                {
                    if (cell.TriggerBuilding != null)
                    {
                        var bSetting = GameSettings.GetBuilding(cell.TriggerBuilding.BuildingId);
                        string bName = bSetting != null ? bSetting.Name : "未知建筑";
                        string bTypeStr = bSetting != null ? GetBuildingTypeName(bSetting.Type) : "未知";

                        var lvi = new ListViewItem(count.ToString());
                        lvi.SubItems.Add($"({cell.X}, {cell.Y})");
                        lvi.SubItems.Add(cell.TriggerBuilding.BuildingId.ToString());
                        lvi.SubItems.Add(bTypeStr);
                        lvi.SubItems.Add(bName);
                        lvi.SubItems.Add(cell.TriggerBuilding.Owner.ToString());
                        lvi.Tag = cell.Index;

                        if (cell.Index == _selectedCellIndex)
                        {
                            lvi.Selected = true;
                        }

                        lvDirectory.Items.Add(lvi);
                        count++;
                    }
                }
                lblDirectoryHeader.Text = $"【建筑目录 - 共 {count} 个建筑】";
            }
            else if (mode == MapRenderMode.FortificationsOnly)
            {
                lvDirectory.Columns.Add("No.", 40);
                lvDirectory.Columns.Add("坐标", 80);
                lvDirectory.Columns.Add("工事 ID", 60);
                lvDirectory.Columns.Add("工事种类/名称", 140);

                int count = 0;
                foreach (var cell in _stage.Cells)
                {
                    if (cell.TriggerFort != null)
                    {
                        var fSetting = GameSettings.GetFortification(cell.TriggerFort.FortId);
                        string fName = fSetting != null ? fSetting.Name : "未知工事";

                        var lvi = new ListViewItem(count.ToString());
                        lvi.SubItems.Add($"({cell.X}, {cell.Y})");
                        lvi.SubItems.Add(cell.TriggerFort.FortId.ToString());
                        lvi.SubItems.Add(fName);
                        lvi.Tag = cell.Index;

                        if (cell.Index == _selectedCellIndex)
                        {
                            lvi.Selected = true;
                        }

                        lvDirectory.Items.Add(lvi);
                        count++;
                    }
                }
                lblDirectoryHeader.Text = $"【工事目录 - 共 {count} 个工事】";
            }
            else if (mode == MapRenderMode.DecorationsOnly)
            {
                lvDirectory.Columns.Add("No.", 40);
                lvDirectory.Columns.Add("坐标", 80);
                lvDirectory.Columns.Add("主要装饰类别", 140);
                lvDirectory.Columns.Add("位移 (Offset)", 90);

                int count = 0;
                foreach (var cell in _stage.Cells)
                {
                    var slots = new List<List<byte>> { cell.Attr, cell.AttrA2, cell.AttrA3 };
                    string decStr = "";
                    string offStr = "";
                    bool hasDec = false;

                    foreach (var slot in slots)
                    {
                        if (slot != null && slot.Count >= 4 && slot[0] >= 50 && slot[0] < 100)
                        {
                            hasDec = true;
                            string decName = GetDecorationName(slot[0]);
                            decStr += $"{decName}{slot[1]} ";
                            sbyte dx = (sbyte)slot[2];
                            sbyte dy = (sbyte)slot[3];
                            if (dx != 0 || dy != 0)
                            {
                                offStr += $"{dx},{dy} ";
                            }
                        }
                    }

                    if (hasDec)
                    {
                        var lvi = new ListViewItem(count.ToString());
                        lvi.SubItems.Add($"({cell.X}, {cell.Y})");
                        lvi.SubItems.Add(decStr.TrimEnd());
                        lvi.SubItems.Add(string.IsNullOrEmpty(offStr) ? "0,0" : offStr.TrimEnd());
                        lvi.Tag = cell.Index;

                        if (cell.Index == _selectedCellIndex)
                        {
                            lvi.Selected = true;
                        }

                        lvDirectory.Items.Add(lvi);
                        count++;
                    }
                }
                lblDirectoryHeader.Text = $"【地表装饰目录 - 共 {count} 个装饰】";
            }
            else if (mode == MapRenderMode.Reinforcements)
            {
                lvDirectory.Columns.Add("No.", 40);
                lvDirectory.Columns.Add("坐标", 70);
                lvDirectory.Columns.Add("格子索引", 80);
                lvDirectory.Columns.Add("增员阵营", 80);
                lvDirectory.Columns.Add("关键增兵", 80);
                lvDirectory.Columns.Add("附加标志", 80);
                lvDirectory.Columns.Add("物理偏移", 95);

                int count = 0;
                if (_stage.BattleInfo?.ReinforcePoints != null)
                {
                    foreach (var rp in _stage.BattleInfo.ReinforcePoints)
                    {
                        var lvi = new ListViewItem(count.ToString());
                        int x = 0, y = 0;
                        if (rp.CellIdx < _stage.Cells.Count)
                        {
                            var cell = _stage.Cells[rp.CellIdx];
                            x = cell.X;
                            y = cell.Y;
                        }
                        lvi.SubItems.Add($"({x}, {y})");
                        lvi.SubItems.Add(rp.CellIdx.ToString());
                        lvi.SubItems.Add(rp.FactionId.ToString());
                        lvi.SubItems.Add(rp.IsKeyUnit ? "是 ★" : "否");
                        lvi.SubItems.Add(rp.Flag.ToString());
                        lvi.SubItems.Add(rp.Offset != 0 ? $"0x{rp.Offset:X}" : "0x0");
                        lvi.Tag = (int)rp.CellIdx;

                        if (rp.CellIdx == _selectedCellIndex)
                        {
                            lvi.Selected = true;
                        }

                        lvDirectory.Items.Add(lvi);
                        count++;
                    }
                }
                lblDirectoryHeader.Text = $"【增员部署点目录 - 共 {count} 个】";
            }
            else if (mode == MapRenderMode.TerrainOnly)
            {
                lvDirectory.Columns.Add("No.", 45);
                lvDirectory.Columns.Add("坐标", 80);
                lvDirectory.Columns.Add("原始地形值", 80);
                lvDirectory.Columns.Add("地形种类 (Type)", 100);
                lvDirectory.Columns.Add("物理偏移 (Offset)", 95);

                int count = 0;
                foreach (var cell in _stage.Cells)
                {
                    var lvi = new ListViewItem(cell.Index.ToString());
                    lvi.SubItems.Add($"({cell.X}, {cell.Y})");
                    lvi.SubItems.Add($"0x{cell.Terrain:X4}");
                    lvi.SubItems.Add(GetTerrainTypeName(cell.Terrain & 0xFF));
                    lvi.SubItems.Add($"0x{cell.TileFileOffset:X}");
                    lvi.Tag = cell.Index;

                    if (cell.Index == _selectedCellIndex)
                    {
                        lvi.Selected = true;
                    }

                    lvDirectory.Items.Add(lvi);
                    count++;
                }
                lblDirectoryHeader.Text = $"【地形底色目录 - 共 {count} 个地块】";
            }
            else // MapRenderMode.All
            {
                lvDirectory.Columns.Add("类型 (Class)", 80);
                lvDirectory.Columns.Add("坐标", 75);
                lvDirectory.Columns.Add("名称 (Name)", 120);
                lvDirectory.Columns.Add("数值/所属国家", 150);

                int count = 0;
                foreach (var cell in _stage.Cells)
                {
                    if (cell.Unit != null)
                    {
                        var lvi = new ListViewItem("部队");
                        lvi.SubItems.Add($"({cell.X}, {cell.Y})");
                        lvi.SubItems.Add(GetUnitTypeName(cell.Unit.UnitId));
                        lvi.SubItems.Add($"HP: {cell.Unit.HP}/{cell.Unit.MaxHP} [阵营 {cell.Unit.FactionId}]");
                        lvi.Tag = cell.Index;
                        if (cell.Index == _selectedCellIndex) lvi.Selected = true;
                        lvDirectory.Items.Add(lvi);
                        count++;
                    }
                    if (cell.TriggerBuilding != null)
                    {
                        var bSetting = GameSettings.GetBuilding(cell.TriggerBuilding.BuildingId);
                        string bName = bSetting != null ? bSetting.Name : "未知建筑";
                        var lvi = new ListViewItem("建筑");
                        lvi.SubItems.Add($"({cell.X}, {cell.Y})");
                        lvi.SubItems.Add(bName);
                        lvi.SubItems.Add($"所属阵营: {cell.TriggerBuilding.Owner}");
                        lvi.Tag = cell.Index;
                        if (cell.Index == _selectedCellIndex) lvi.Selected = true;
                        lvDirectory.Items.Add(lvi);
                        count++;
                    }
                    if (cell.TriggerFort != null)
                    {
                        var fSetting = GameSettings.GetFortification(cell.TriggerFort.FortId);
                        string fName = fSetting != null ? fSetting.Name : "未知工事";
                        var lvi = new ListViewItem("工事");
                        lvi.SubItems.Add($"({cell.X}, {cell.Y})");
                        lvi.SubItems.Add(fName);
                        lvi.SubItems.Add("-");
                        lvi.Tag = cell.Index;
                        if (cell.Index == _selectedCellIndex) lvi.Selected = true;
                        lvDirectory.Items.Add(lvi);
                        count++;
                    }
                }
                lblDirectoryHeader.Text = $"【全图实体对象目录 - 共 {count} 个对象】";
            }

            AutoResizeListViewColumns(lvDirectory);
            lvDirectory.EndUpdate();
        }

        private void AutoResizeListViewColumns(ListView lv)
        {
            if (lv.Columns.Count == 0) return;

            if (lv.Items.Count == 0)
            {
                lv.AutoResizeColumns(ColumnHeaderAutoResizeStyle.HeaderSize);
                for (int i = 0; i < lv.Columns.Count; i++)
                {
                    lv.Columns[i].Width += 16; // Add DPI-friendly padding
                }
                return;
            }

            lv.AutoResizeColumns(ColumnHeaderAutoResizeStyle.HeaderSize);
            int[] headerWidths = new int[lv.Columns.Count];
            for (int i = 0; i < lv.Columns.Count; i++)
            {
                headerWidths[i] = lv.Columns[i].Width;
            }

            lv.AutoResizeColumns(ColumnHeaderAutoResizeStyle.ColumnContent);
            for (int i = 0; i < lv.Columns.Count; i++)
            {
                // Auto-adjust column to fit the max of header or items text, plus a padding
                lv.Columns[i].Width = Math.Max(headerWidths[i], lv.Columns[i].Width) + 16;
            }
        }

        private string GetBuildingTypeName(int type)
        {
            switch (type)
            {
                case 1: return "城市 (City)";
                case 2: return "工业 (Factory)";
                case 3: return "机场 (Airport)";
                case 4: return "港口 (Port)";
                case 5: return "资源 (Resource)";
                default: return "其他 (Other)";
            }
        }

        private string GetDecorationName(int id)
        {
            switch (id)
            {
                case 51: return "农田";
                case 52: return "沙丘";
                case 53: return "土地";
                case 54: return "草地";
                case 55: return "雪地";
                case 56: return "沙漠";
                default: return $"D{id}";
            }
        }

        private string GetTerrainTypeName(int type)
        {
            switch (type)
            {
                case 0: return "海洋 (Sea)";
                case 1: return "海岸 (Coast)";
                case 2: return "陆地 (Land)";
                case 3: return "森林 (Forest)";
                case 4: return "丘陵 (Hills)";
                case 5: return "高山 (Mountain)";
                case 6: return "荒地 (Wasteland)";
                case 7: return "沙漠 (Desert)";
                case 10: return "河流 (River)";
                default: return $"地形 {type}";
            }
        }

        private string GetUnitTypeName(int id)
        {
            switch (id)
            {
                case 1: return "轻步兵";
                case 2: return "突击步兵";
                case 3: return "重装步兵";
                case 4: return "装甲车";
                case 5: return "轻型坦克";
                case 6: return "中型坦克";
                case 7: return "重型坦克";
                case 8: return "超重型坦克";
                case 9: return "野战炮";
                case 10: return "榴弹炮";
                case 11: return "火箭炮";
                case 12: return "反坦克炮";
                case 13: return "装甲列车";
                case 21: return "驱逐舰";
                case 22: return "潜艇";
                case 23: return "巡洋舰";
                case 24: return "战列舰";
                case 25: return "航母";
                default: return $"部队 {id}";
            }
        }

        private void BattleCategoryChanged(object sender, EventArgs e)
        {
            UpdateBattleDetailsText();
        }

        private void UpdateBattleAttributesView()
        {
            if (lvBattleCategories.SelectedIndices.Count == 0 && lvBattleCategories.Items.Count > 0)
            {
                lvBattleCategories.Items[0].Selected = true;
            }
            UpdateBattleDetailsText();
        }

        private void UpdateBattleDetailsText()
        {
            if (_stage == null)
            {
                rtbBattleDetails.Text = "请加载 BTL 关卡文件。";
                return;
            }

            if (lvBattleCategories.SelectedIndices.Count == 0)
            {
                rtbBattleDetails.Text = "请选择左侧的一个分类。";
                return;
            }

            int index = lvBattleCategories.SelectedIndices[0];
            var sb = new StringBuilder();

            System.Text.Json.JsonElement rootEl;
            if (_stage.RawRoot.HasValue)
            {
                rootEl = _stage.RawRoot.Value;
            }
            else
            {
                string ser = System.Text.Json.JsonSerializer.Serialize(_stage);
                rootEl = System.Text.Json.JsonDocument.Parse(ser).RootElement;
            }

            switch (index)
            {
                case 0: // 基本属性
                    sb.AppendLine("================================================================================");
                    sb.AppendLine("                           战 役 基 本 属 性 (Dynamic)");
                    sb.AppendLine("================================================================================");
                    
                    sb.AppendLine("[Root Fields]");
                    var rootSchema = BtlSchema.GetSchema("Root");
                    foreach (var prop in rootEl.EnumerateObject())
                    {
                        if (prop.Value.ValueKind != System.Text.Json.JsonValueKind.Object && prop.Value.ValueKind != System.Text.Json.JsonValueKind.Array)
                        {
                            BtlSchema.FieldInfo fi = default;
                            bool found = false;
                            foreach (var val in rootSchema.Values)
                            {
                                if (val.Name == prop.Name) { fi = val; found = true; break; }
                            }
                            string typeInfo = "";
                            if (found)
                            {
                                string hex = GetHexRepresentation(prop.Value, fi.Type);
                                typeInfo = hex != null ? $"({fi.Type}, hex: {hex})" : $"({fi.Type})";
                            }
                            sb.AppendLine($"- {prop.Name,-30}: {prop.Value,-15} {typeInfo}");
                        }
                    }
                    sb.AppendLine();

                    if (rootEl.TryGetProperty("map_terrain", out var mapTerrain))
                    {
                        sb.AppendLine("[map_terrain]");
                        var mtSchema = BtlSchema.GetSchema("MapTerrain");
                        foreach (var prop in mapTerrain.EnumerateObject())
                        {
                            BtlSchema.FieldInfo fi = default;
                            bool found = false;
                            foreach (var val in mtSchema.Values)
                            {
                                if (val.Name == prop.Name) { fi = val; found = true; break; }
                            }

                            if (prop.Name == "size")
                            {
                                sb.AppendLine("  - size:");
                                var sizeSchema = BtlSchema.GetSchema("Size");
                                foreach (var sizeProp in prop.Value.EnumerateObject())
                                {
                                    BtlSchema.FieldInfo sfi = default;
                                    bool sfound = false;
                                    foreach (var val in sizeSchema.Values)
                                    {
                                        if (val.Name == sizeProp.Name) { sfi = val; sfound = true; break; }
                                    }
                                    string stypeInfo = "";
                                    if (sfound)
                                    {
                                        string hex = GetHexRepresentation(sizeProp.Value, sfi.Type);
                                        stypeInfo = hex != null ? $"({sfi.Type}, hex: {hex})" : $"({sfi.Type})";
                                    }
                                    sb.AppendLine($"      {sizeProp.Name,-26}: {sizeProp.Value,-15} {stypeInfo}");
                                }
                            }
                            else if (prop.Value.ValueKind != System.Text.Json.JsonValueKind.Object && prop.Value.ValueKind != System.Text.Json.JsonValueKind.Array)
                            {
                                string typeInfo = "";
                                if (found)
                                {
                                    string hex = GetHexRepresentation(prop.Value, fi.Type);
                                    typeInfo = hex != null ? $"({fi.Type}, hex: {hex})" : $"({fi.Type})";
                                }
                                sb.AppendLine($"  - {prop.Name,-28}: {prop.Value,-15} {typeInfo}");
                            }
                        }
                        sb.AppendLine();
                    }

                    if (rootEl.TryGetProperty("stage_metadata", out var stageMetadata))
                    {
                        sb.AppendLine("[stage_metadata]");
                        var smSchema = BtlSchema.GetSchema("StageMetadata");
                        foreach (var prop in stageMetadata.EnumerateObject())
                        {
                            if (prop.Value.ValueKind != System.Text.Json.JsonValueKind.Object && prop.Value.ValueKind != System.Text.Json.JsonValueKind.Array)
                            {
                                BtlSchema.FieldInfo fi = default;
                                bool found = false;
                                foreach (var val in smSchema.Values)
                                {
                                    if (val.Name == prop.Name) { fi = val; found = true; break; }
                                }
                                string typeInfo = "";
                                if (found)
                                {
                                    string hex = GetHexRepresentation(prop.Value, fi.Type);
                                    typeInfo = hex != null ? $"({fi.Type}, hex: {hex})" : $"({fi.Type})";
                                }
                                sb.AppendLine($"  - {prop.Name,-28}: {prop.Value,-15} {typeInfo}");
                            }
                        }
                        sb.AppendLine();
                    }

                    if (rootEl.TryGetProperty("faction_info", out var factionInfo))
                    {
                        sb.AppendLine("[faction_info]");
                        var fiSchema = BtlSchema.GetSchema("FactionInfo");
                        foreach (var prop in factionInfo.EnumerateObject())
                        {
                            if (prop.Value.ValueKind != System.Text.Json.JsonValueKind.Object && prop.Value.ValueKind != System.Text.Json.JsonValueKind.Array)
                            {
                                BtlSchema.FieldInfo fi = default;
                                bool found = false;
                                foreach (var val in fiSchema.Values)
                                {
                                    if (val.Name == prop.Name) { fi = val; found = true; break; }
                                }
                                string typeInfo = "";
                                if (found)
                                {
                                    string hex = GetHexRepresentation(prop.Value, fi.Type);
                                    typeInfo = hex != null ? $"({fi.Type}, hex: {hex})" : $"({fi.Type})";
                                }
                                sb.AppendLine($"  - {prop.Name,-28}: {prop.Value,-15} {typeInfo}");
                            }
                        }
                    }
                    sb.AppendLine("================================================================================");
                    break;

                case 1: // 阵营势力
                    sb.AppendLine("================================================================================");
                    sb.AppendLine("                           阵 营 势 力 定 义 (Factions)");
                    sb.AppendLine("================================================================================");
                    if (rootEl.TryGetProperty("faction_info", out var fiObj) && fiObj.TryGetProperty("factions", out var factionsArr) && factionsArr.ValueKind == System.Text.Json.JsonValueKind.Array)
                    {
                        int fCount = factionsArr.GetArrayLength();
                        sb.AppendLine($"总共定义了 {fCount} 个阵营势力：\n");
                        for (int i = 0; i < fCount; i++)
                        {
                            var f = factionsArr[i];
                            sb.AppendLine($"[阵营索引 #{i}]");
                            sb.Append(FormatJsonElement(f, "Faction", "  "));
                            sb.AppendLine("  ----------------------------------------------------------------------------");
                        }
                    }
                    else
                    {
                        sb.AppendLine("无阵营势力定义数据。");
                    }
                    break;

                case 2: // 阵营限制与卡牌
                    sb.AppendLine("================================================================================");
                    sb.AppendLine("                         阵 营 卡 牌 与 兵 力 限 制 关 联");
                    sb.AppendLine("================================================================================");
                    
                    if (rootEl.TryGetProperty("faction_info", out var factionInfoCards))
                    {
                        if (factionInfoCards.TryGetProperty("faction_cards", out var factionCards) && factionCards.ValueKind == System.Text.Json.JsonValueKind.Array)
                        {
                            sb.AppendLine("【阵营卡牌关联表 (faction_cards)】:");
                            sb.Append(FormatJsonElement(factionCards, "FactionCardsRelation", ""));
                        }
                        else
                        {
                            sb.AppendLine("无阵营卡牌关联数据。");
                        }

                        sb.AppendLine();
                        if (factionInfoCards.TryGetProperty("faction_limits", out var factionLimits) && factionLimits.ValueKind == System.Text.Json.JsonValueKind.Array)
                        {
                            sb.AppendLine("【阵营兵力限制关联表 (faction_limits)】:");
                            sb.Append(FormatJsonElement(factionLimits, "FactionLimitsRelation", ""));
                        }
                        else
                        {
                            sb.AppendLine("无阵营限制关联数据。");
                        }
                    }
                    else
                    {
                        sb.AppendLine("无 faction_info 数据。");
                    }
                    break;

                case 3: // 战役目标
                    sb.AppendLine("================================================================================");
                    sb.AppendLine("                           战 役 目 标 (Stage Targets)");
                    sb.AppendLine("================================================================================");
                    if (rootEl.TryGetProperty("stage_metadata", out var metaObj) && metaObj.TryGetProperty("targets", out var targetsArr) && targetsArr.ValueKind == System.Text.Json.JsonValueKind.Array)
                    {
                        int tCount = targetsArr.GetArrayLength();
                        sb.AppendLine($"总共定义了 {tCount} 个战役目标：\n");
                        for (int i = 0; i < tCount; i++)
                        {
                            var t = targetsArr[i];
                            sb.AppendLine($"[目标索引 #{i}]");
                            sb.Append(FormatJsonElement(t, "StageTarget", "  "));
                            sb.AppendLine("  ----------------------------------------------------------------------------");
                        }
                    }
                    else
                    {
                        sb.AppendLine("无战役目标定义。");
                    }
                    break;

                case 4: // 增员部署点
                    sb.AppendLine("================================================================================");
                    sb.AppendLine("                      增 员 部 队 部 署 点 (battle_info.reinforce_points)");
                    sb.AppendLine("================================================================================");
                    if (rootEl.TryGetProperty("battle_info", out var biObj) && biObj.TryGetProperty("reinforce_points", out var rpArr) && rpArr.ValueKind == System.Text.Json.JsonValueKind.Array)
                    {
                        int rpCount = rpArr.GetArrayLength();
                        sb.AppendLine($"总共定义了 {rpCount} 个增员部署点：\n");
                        for (int i = 0; i < rpCount; i++)
                        {
                            var rp = rpArr[i];
                            sb.AppendLine($"[部署点索引 #{i}]");
                            sb.Append(FormatJsonElement(rp, "ReinforcePoint", "  "));
                            sb.AppendLine("  ----------------------------------------------------------------------------");
                        }
                    }
                    else
                    {
                        sb.AppendLine("无增员部署点数据。");
                    }
                    break;

                case 5: // 其他未解析/原始数据
                    sb.AppendLine("================================================================================");
                    sb.AppendLine("                           其 他 关 键 原 始 配 置 (Raw Json)");
                    sb.AppendLine("================================================================================");
                    
                    if (rootEl.TryGetProperty("stage_config", out var scObj))
                    {
                        sb.AppendLine("【关卡全局配置 (stage_config) - JSON 预览】:");
                        sb.AppendLine(System.Text.Json.JsonSerializer.Serialize(scObj, new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
                    }
                    else
                    {
                        sb.AppendLine("无 stage_config 数据。");
                    }

                    sb.AppendLine("\n【区域/触发器相关原始 JSON】:");
                    if (rootEl.TryGetProperty("trigger_info", out var tiObj))
                    {
                        if (tiObj.TryGetProperty("events", out var eventsArr))
                        {
                            sb.AppendLine($"- 触发器事件数量: {eventsArr.GetArrayLength()}");
                        }
                        sb.AppendLine("\n【触发器配置 (trigger_info) - JSON 预览】:");
                        sb.AppendLine(System.Text.Json.JsonSerializer.Serialize(tiObj, new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
                    }
                    if (rootEl.TryGetProperty("region_info", out var riObj))
                    {
                        sb.AppendLine("\n【区域配置 (region_info) - JSON 预览】:");
                        sb.AppendLine(System.Text.Json.JsonSerializer.Serialize(riObj, new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
                    }
                    if (rootEl.TryGetProperty("decal_info", out var diObj))
                    {
                        sb.AppendLine("\n【装饰图层配置 (decal_info) - JSON 预览】:");
                        sb.AppendLine(System.Text.Json.JsonSerializer.Serialize(diObj, new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
                    }
                    break;
            }

            rtbBattleDetails.Text = sb.ToString();
        }

        private static string FormatJsonElement(System.Text.Json.JsonElement elem, string typeName, string indent = "")
        {
            var sb = new StringBuilder();
            var schema = BtlSchema.GetSchema(typeName);

            if (elem.ValueKind == System.Text.Json.JsonValueKind.Object)
            {
                foreach (var prop in elem.EnumerateObject())
                {
                    BtlSchema.FieldInfo fi = default;
                    bool found = false;
                    foreach (var val in schema.Values)
                    {
                        if (val.Name == prop.Name) { fi = val; found = true; break; }
                    }

                    if (prop.Value.ValueKind == System.Text.Json.JsonValueKind.Object)
                    {
                        sb.AppendLine($"{indent}- {prop.Name}:");
                        sb.Append(FormatJsonElement(prop.Value, found ? fi.Type : "unknown", indent + "  "));
                    }
                    else if (prop.Value.ValueKind == System.Text.Json.JsonValueKind.Array)
                    {
                        string elemType = "unknown";
                        if (found)
                        {
                            if (fi.Type.StartsWith("[") && fi.Type.EndsWith("]"))
                                elemType = fi.Type.Substring(1, fi.Type.Length - 2);
                            else if (fi.Type.StartsWith("vector_"))
                                elemType = fi.Type.Substring(7);
                        }
                        sb.AppendLine($"{indent}- {prop.Name}:");
                        sb.Append(FormatJsonElement(prop.Value, elemType, indent + "  "));
                    }
                    else
                    {
                        string valStr = prop.Value.ToString();
                        string typeInfo = "";
                        if (found)
                        {
                            string hex = GetHexRepresentation(prop.Value, fi.Type);
                            typeInfo = hex != null ? $"({fi.Type}, hex: {hex})" : $"({fi.Type})";
                        }
                        sb.AppendLine($"{indent}- {prop.Name,-25}: {valStr,-15} {typeInfo}");
                    }
                }
            }
            else if (elem.ValueKind == System.Text.Json.JsonValueKind.Array)
            {
                int index = 0;
                foreach (var item in elem.EnumerateArray())
                {
                    if (item.ValueKind == System.Text.Json.JsonValueKind.Object || item.ValueKind == System.Text.Json.JsonValueKind.Array)
                    {
                        sb.AppendLine($"{indent}[Index #{index}]");
                        sb.Append(FormatJsonElement(item, typeName, indent + "  "));
                    }
                    else
                    {
                        string hex = GetHexRepresentation(item, typeName);
                        string typeInfo = hex != null ? $"({typeName}, hex: {hex})" : $"({typeName})";
                        sb.AppendLine($"{indent}- {item,-15} {typeInfo}");
                    }
                    index++;
                }
            }
            else
            {
                string hex = GetHexRepresentation(elem, typeName);
                string typeInfo = hex != null ? $"({typeName}, hex: {hex})" : $"({typeName})";
                sb.AppendLine($"{indent}{elem,-15} {typeInfo}");
            }
            return sb.ToString();
        }

        private static string GetHexRepresentation(System.Text.Json.JsonElement val, string type)
        {
            try
            {
                if (type == "uint16" || type == "ushort")
                {
                    ushort u16 = val.GetUInt16();
                    byte[] bytes = BitConverter.GetBytes(u16);
                    return string.Join(" ", Array.ConvertAll(bytes, b => b.ToString("X2")));
                }
                if (type == "int16" || type == "short")
                {
                    short s16 = val.GetInt16();
                    byte[] bytes = BitConverter.GetBytes(s16);
                    return string.Join(" ", Array.ConvertAll(bytes, b => b.ToString("X2")));
                }
                if (type == "uint32" || type == "uint")
                {
                    uint u32 = val.GetUInt32();
                    byte[] bytes = BitConverter.GetBytes(u32);
                    return string.Join(" ", Array.ConvertAll(bytes, b => b.ToString("X2")));
                }
                if (type == "int32" || type == "int")
                {
                    int s32 = val.GetInt32();
                    byte[] bytes = BitConverter.GetBytes(s32);
                    return string.Join(" ", Array.ConvertAll(bytes, b => b.ToString("X2")));
                }
                if (type == "uint8" || type == "ubyte" || type == "byte")
                {
                    byte u8 = val.GetByte();
                    return u8.ToString("X2");
                }
                if (type == "int8" || type == "sbyte")
                {
                    sbyte s8 = val.GetSByte();
                    return ((byte)s8).ToString("X2");
                }
                if (type == "bool")
                {
                    bool b = val.GetBoolean();
                    return b ? "01" : "00";
                }
                if (type == "float")
                {
                    float f = val.GetSingle();
                    byte[] bytes = BitConverter.GetBytes(f);
                    return string.Join(" ", Array.ConvertAll(bytes, b => b.ToString("X2")));
                }
                if (type == "double")
                {
                    double d = val.GetDouble();
                    byte[] bytes = BitConverter.GetBytes(d);
                    return string.Join(" ", Array.ConvertAll(bytes, b => b.ToString("X2")));
                }
                if (type == "string")
                {
                    string s = val.GetString();
                    if (s != null)
                    {
                        byte[] bytes = Encoding.UTF8.GetBytes(s);
                        return string.Join(" ", Array.ConvertAll(bytes, b => b.ToString("X2")));
                    }
                }
            }
            catch
            {
                // Fallback for parsing/range errors
            }
            return null;
        }

        private void InsertSpaceClick(object sender, EventArgs e)
        {
            if (_fileData == null || _stage == null)
            {
                MessageBox.Show("请先打开一个 BTL 文件", "提示", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            using (Form f = new Form())
            {
                f.Text = "开辟空白区域";
                f.Size = new Size(350, 240);
                f.FormBorderStyle = FormBorderStyle.FixedDialog;
                f.MaximizeBox = false;
                f.MinimizeBox = false;
                f.StartPosition = FormStartPosition.CenterParent;

                Label lblOffset = new Label() { Text = "插入绝对位置(Hex/Dec):", Left = 20, Top = 20, Width = 150 };
                TextBox txtOffset = new TextBox() { Left = 170, Top = 20, Width = 140, Text = $"0x{_fileData.Length:X}" };

                Label lblLen = new Label() { Text = "插入字节长度:", Left = 20, Top = 50, Width = 150 };
                TextBox txtLen = new TextBox() { Left = 170, Top = 50, Width = 140, Text = "64" };

                CheckBox chkAlign = new CheckBox() { Text = "强制4字节对齐", Checked = true, Left = 20, Top = 80, Width = 200 };

                Label lblFill = new Label() { Text = "填充字节(Hex):", Left = 20, Top = 110, Width = 150 };
                TextBox txtFill = new TextBox() { Left = 170, Top = 110, Width = 140, Text = "00" };

                Button btnOk = new Button() { Text = "确定", DialogResult = DialogResult.OK, Left = 80, Top = 150, Width = 80 };
                Button btnCancel = new Button() { Text = "取消", DialogResult = DialogResult.Cancel, Left = 180, Top = 150, Width = 80 };

                f.Controls.AddRange(new Control[] { lblOffset, txtOffset, lblLen, txtLen, chkAlign, lblFill, txtFill, btnOk, btnCancel });
                f.AcceptButton = btnOk;
                f.CancelButton = btnCancel;

                if (f.ShowDialog(this) == DialogResult.OK)
                {
                    try
                    {
                        string offsetStr = txtOffset.Text.Trim();
                        int offset = offsetStr.StartsWith("0x", StringComparison.OrdinalIgnoreCase)
                            ? Convert.ToInt32(offsetStr.Substring(2), 16)
                            : Convert.ToInt32(offsetStr, 10);

                        int len = Convert.ToInt32(txtLen.Text.Trim(), 10);

                        if (chkAlign.Checked)
                        {
                            if (offset % 4 != 0)
                            {
                                offset = ((offset + 3) / 4) * 4;
                            }
                            if (len % 4 != 0)
                            {
                                len = ((len + 3) / 4) * 4;
                            }
                        }

                        byte fillByte = Convert.ToByte(txtFill.Text.Trim(), 16);

                        if (offset < 0 || offset > _fileData.Length)
                        {
                            MessageBox.Show("插入位置超出文件大小范围", "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
                            return;
                        }

                        // 备份原文件
                        string filePath = _stage.File;
                        string backupPath = filePath + ".original";
                        if (!File.Exists(backupPath))
                        {
                            File.Copy(filePath, backupPath);
                        }

                        // 执行插入和偏移重算
                        byte[] newData = BtlBridge.RecalculateOffsetsAndInsert(_fileData, offset, len, fillByte);

                        // 写回文件
                        File.WriteAllBytes(filePath, newData);

                        MessageBox.Show($"成功开辟空白区域！\n插入位置: 0x{offset:X}\n实际插入长度: {len} 字节\n原文件已备份为: {Path.GetFileName(backupPath)}", "成功", MessageBoxButtons.OK, MessageBoxIcon.Information);

                        // 重新加载
                        ReloadCurrentFile();
                    }
                    catch (Exception ex)
                    {
                        MessageBox.Show($"操作失败:\n{ex.Message}", "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    }
                }
            }
        }

        private void SaveModifiedFileClick(object sender, EventArgs e)
        {
            if (_fileData == null || _stage == null) return;
            try
            {
                using (var sfd = new SaveFileDialog())
                {
                    sfd.Filter = "GG3 关卡文件 (*.btl)|*.btl|所有文件 (*.*)|*.*";
                    sfd.FileName = Path.GetFileName(_stage.File);
                    if (sfd.ShowDialog() == DialogResult.OK)
                    {
                        File.WriteAllBytes(sfd.FileName, _fileData);
                        MessageBox.Show("保存修改成功！", "成功", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    }
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"另存为失败:\n{ex.Message}", "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void CopyDisasmLineClick(object sender, EventArgs e)
        {
            if (rtbDisasm == null) return;
            if (string.IsNullOrEmpty(rtbDisasm.SelectedText))
            {
                int charIndex = rtbDisasm.SelectionStart;
                int lineIndex = rtbDisasm.GetLineFromCharIndex(charIndex);
                if (lineIndex >= 0 && _disasmLines != null && lineIndex < _disasmLines.Length)
                {
                    Clipboard.SetText(_disasmLines[lineIndex]);
                }
            }
            else
            {
                Clipboard.SetText(rtbDisasm.SelectedText);
            }
        }

        private void JumpToTargetClick(object sender, EventArgs e)
        {
            DisasmDoubleClick(sender, e);
        }

        private void CtxDisasm_Opening(object sender, System.ComponentModel.CancelEventArgs e)
        {
            if (_disasmItems == null || rtbDisasm == null)
            {
                e.Cancel = true;
                return;
            }

            int charIndex = rtbDisasm.SelectionStart;
            int lineIndex = rtbDisasm.GetLineFromCharIndex(charIndex);
            if (lineIndex < 0 || lineIndex >= _disasmItems.Count)
            {
                e.Cancel = true;
                return;
            }

            var item = _disasmItems[lineIndex];

            // 1. 判断是否可以跳转
            ctxJumpTarget.Enabled = item.TargetOffset.HasValue;

            // 2. 解析是否为字段，以及是否关联向量
            string desc = item.Description;
            bool isField = desc.Contains(":") && !desc.StartsWith(";") && !desc.StartsWith("loc_") && !desc.StartsWith("align") && !desc.StartsWith("db") && !item.Description.StartsWith("[DEFAULT]");
            
            ctxEditValue.Enabled = isField && item.Length > 0;

            string vectorPath = GetVectorPathAndIndex(desc, out _);
            ctxAppendElement.Enabled = (vectorPath != null);
            if (vectorPath != null)
            {
                ctxAppendElement.Text = $"克隆并追加元素 到 {vectorPath.Substring(vectorPath.LastIndexOf('.') + 1)}";
            }
            else
            {
                ctxAppendElement.Text = "克隆并追加新元素";
            }
        }

        private string GetVectorPathAndIndex(string desc, out int elementIndex)
        {
            elementIndex = 0;
            // 匹配格式: Root.xxx[Index] 或 Root.xxx[Index].yyy
            var match = System.Text.RegularExpressions.Regex.Match(desc, @"(Root\.[a-zA-Z0-9_\.]+?)\[(\d+)\]");
            if (match.Success)
            {
                elementIndex = int.Parse(match.Groups[2].Value);
                return match.Groups[1].Value;
            }
            
            // 匹配格式: Root.xxx.length 或 Root.xxx_offset
            var matchVec = System.Text.RegularExpressions.Regex.Match(desc, @"(Root\.[a-zA-Z0-9_\.]+?)(?:\.length|_offset):");
            if (matchVec.Success)
            {
                return matchVec.Groups[1].Value;
            }
            
            return null;
        }

        private int FindVectorFileOffset(string vectorPath, out string elemType)
        {
            elemType = "unknown";
            BtlBridge.ResolveFieldTypeAndComment(vectorPath, out string fieldType, out _, out _);
            if (fieldType.StartsWith("vector_"))
            {
                elemType = fieldType.Substring(7);
            }
            else if (fieldType.StartsWith("["))
            {
                elemType = fieldType.Trim('[', ']');
            }

            string offsetDescPattern = vectorPath + "_offset";
            foreach (var item in _disasmItems)
            {
                if (item.Description.StartsWith(offsetDescPattern, StringComparison.OrdinalIgnoreCase) && item.TargetOffset.HasValue)
                {
                    return item.TargetOffset.Value;
                }
            }
            return -1;
        }

        private void EditSelectedHexClick(object sender, EventArgs e)
        {
            if (_disasmItems == null || _fileData == null || rtbDisasm == null) return;

            int charIndex = rtbDisasm.SelectionStart;
            int lineIndex = rtbDisasm.GetLineFromCharIndex(charIndex);
            if (lineIndex < 0 || lineIndex >= _disasmItems.Count)
            {
                MessageBox.Show("请在列表中选择一行有效的结构数据", "提示", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            var item = _disasmItems[lineIndex];
            if (item.Length == 0 || item.Description.StartsWith("[DEFAULT]"))
            {
                MessageBox.Show("该行为虚拟说明或分隔线，无物理数据字节", "提示", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            string desc = item.Description;
            int colonIdx = desc.IndexOf(':');
            if (colonIdx <= 0 || desc.StartsWith(";"))
            {
                MessageBox.Show("选中的行不是可编辑的属性字段", "提示", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            string pathAndField = desc.Substring(0, colonIdx).Trim();
            string valueAndComment = desc.Substring(colonIdx + 1).Trim();

            string fieldName = pathAndField.Substring(pathAndField.LastIndexOf('.') + 1);
            string currentVal = valueAndComment;
            string comment = "";
            int semiIdx = valueAndComment.IndexOf(';');
            if (semiIdx >= 0)
            {
                currentVal = valueAndComment.Substring(0, semiIdx).Trim();
                comment = valueAndComment.Substring(semiIdx + 1).Trim();
            }

            // 解析字段类型和枚举
            BtlBridge.ResolveFieldTypeAndComment(pathAndField, out string fieldType, out string schemaComment, out string enumName);
            if (string.IsNullOrEmpty(comment))
            {
                comment = schemaComment;
            }

            // 弹窗修改值
            using (Form f = new Form())
            {
                f.Text = $"修改属性值 - {fieldName}";
                f.Size = new Size(420, 260);
                f.FormBorderStyle = FormBorderStyle.FixedDialog;
                f.MaximizeBox = false;
                f.MinimizeBox = false;
                f.StartPosition = FormStartPosition.CenterParent;

                Label lblField = new Label() { Text = $"字段路径: {pathAndField}", Left = 20, Top = 15, Width = 380 };
                Label lblType = new Label() { Text = $"字段类型: {fieldType} (物理大小: {item.Length} 字节)", Left = 20, Top = 40, Width = 380 };
                Label lblComment = new Label() { 
                    Text = $"注释描述: {(string.IsNullOrEmpty(comment) ? "无" : comment)}", 
                    Left = 20, Top = 65, Width = 380, 
                    ForeColor = Color.FromArgb(71, 85, 105),
                    Height = 40
                };

                Label lblInput = new Label() { Text = "新属性值:", Left = 20, Top = 115, Width = 100 };

                Control inputControl = null;
                ComboBox cmbEnum = null;
                CheckBox chkBool = null;
                TextBox txtVal = null;

                bool isEnum = !string.IsNullOrEmpty(enumName) && BtlSchema.GetSchema(enumName).Count > 0;

                if (fieldType == "bool")
                {
                    chkBool = new CheckBox() { Left = 130, Top = 115, Checked = (currentVal.ToLower() == "true" || currentVal == "1"), Text = "启用/激活" };
                    inputControl = chkBool;
                }
                else if (isEnum)
                {
                    cmbEnum = new ComboBox() { Left = 130, Top = 115, Width = 230, DropDownStyle = ComboBoxStyle.DropDownList };
                    var enumSchema = BtlSchema.GetSchema(enumName);
                    
                    int currentIntValue = 0;
                    try { currentIntValue = Convert.ToInt32(currentVal.Split(' ')[0]); } catch { }

                    int selectedIdx = 0;
                    int idx = 0;
                    foreach (var kvp in enumSchema)
                    {
                        string name = kvp.Value.Name;
                        if (name.StartsWith("type_"))
                        {
                            int enumVal = int.Parse(name.Substring(5));
                            string descText = $"{enumVal} - {kvp.Value.Comment}";
                            cmbEnum.Items.Add(new KeyValuePair<int, string>(enumVal, descText));
                            if (enumVal == currentIntValue)
                            {
                                selectedIdx = idx;
                            }
                            idx++;
                        }
                    }
                    cmbEnum.DisplayMember = "Value";
                    cmbEnum.SelectedIndex = selectedIdx;
                    inputControl = cmbEnum;
                }
                else
                {
                    txtVal = new TextBox() { Left = 130, Top = 115, Width = 230, Text = currentVal };
                    inputControl = txtVal;
                }

                Button btnOk = new Button() { Text = "保存并重载", DialogResult = DialogResult.OK, Left = 100, Top = 170, Width = 100 };
                Button btnCancel = new Button() { Text = "取消", DialogResult = DialogResult.Cancel, Left = 220, Top = 170, Width = 100 };

                f.Controls.AddRange(new Control[] { lblField, lblType, lblComment, lblInput, inputControl, btnOk, btnCancel });
                f.AcceptButton = btnOk;
                f.CancelButton = btnCancel;

                if (f.ShowDialog(this) == DialogResult.OK)
                {
                    try
                    {
                        byte[] newBytes = null;

                        if (fieldType == "bool")
                        {
                            newBytes = new byte[] { (byte)(chkBool.Checked ? 1 : 0) };
                        }
                        else if (isEnum)
                        {
                            var kvp = (KeyValuePair<int, string>)cmbEnum.SelectedItem;
                            int enumVal = kvp.Key;
                            newBytes = SerializeValue(enumVal, fieldType, item.Length);
                        }
                        else
                        {
                            string inputStr = txtVal.Text.Trim();
                            newBytes = SerializeValueStr(inputStr, fieldType, item.Length);
                        }

                        if (newBytes == null || newBytes.Length != item.Length)
                        {
                            MessageBox.Show("转换字节失败或长度不匹配", "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
                            return;
                        }

                        // 备份原文件
                        string filePath = _stage.File;
                        string backupPath = filePath + ".original";
                        if (!File.Exists(backupPath))
                        {
                            File.Copy(filePath, backupPath);
                        }

                        BtlBridge.UpdateHexValue(_fileData, item.Offset, newBytes);

                        // 写回文件
                        File.WriteAllBytes(filePath, _fileData);

                        MessageBox.Show("属性修改成功！", "成功", MessageBoxButtons.OK, MessageBoxIcon.Information);

                        ReloadCurrentFile();
                    }
                    catch (Exception ex)
                    {
                        MessageBox.Show($"修改失败:\n{ex.Message}", "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    }
                }
            }
        }

        private byte[] SerializeValue(int val, string type, int length)
        {
            if (length == 1) return new byte[] { (byte)val };
            if (length == 2) return BitConverter.GetBytes((ushort)val);
            if (length == 4) return BitConverter.GetBytes(val);
            return null;
        }

        private byte[] SerializeValueStr(string valStr, string type, int length)
        {
            if (type == "float")
            {
                float f = float.Parse(valStr);
                return BitConverter.GetBytes(f);
            }
            if (type == "double")
            {
                double d = double.Parse(valStr);
                return BitConverter.GetBytes(d);
            }

            long val = 0;
            if (valStr.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
            {
                val = Convert.ToInt64(valStr.Substring(2), 16);
            }
            else
            {
                val = Convert.ToInt64(valStr, 10);
            }

            if (length == 1) return new byte[] { (byte)val };
            if (length == 2) return BitConverter.GetBytes((short)val);
            if (length == 4) return BitConverter.GetBytes((int)val);
            if (length == 8) return BitConverter.GetBytes(val);
            return null;
        }

        private void AppendElementClick(object sender, EventArgs e)
        {
            if (_disasmItems == null || _fileData == null || rtbDisasm == null) return;

            int charIndex = rtbDisasm.SelectionStart;
            int lineIndex = rtbDisasm.GetLineFromCharIndex(charIndex);
            if (lineIndex < 0 || lineIndex >= _disasmItems.Count) return;

            var item = _disasmItems[lineIndex];
            string vectorPath = GetVectorPathAndIndex(item.Description, out int templateIndex);

            if (vectorPath == null)
            {
                MessageBox.Show("选中行无法关联到任何向量，请在向量列表或其子元素行右击追加", "提示", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            int vectorOffset = FindVectorFileOffset(vectorPath, out string elemType);
            if (vectorOffset <= 0)
            {
                MessageBox.Show("无法定位该向量在文件中的物理起始地址", "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            var confirmResult = MessageBox.Show(
                $"确认要克隆索引为 {templateIndex} 的元素并追加到向量 {vectorPath.Substring(vectorPath.LastIndexOf('.') + 1)} 的末尾吗？\n该操作将自动更新相关的所有偏移指针并重算文件结构。", 
                "确认克隆与追加", 
                MessageBoxButtons.YesNo, 
                MessageBoxIcon.Question);

            if (confirmResult == DialogResult.Yes)
            {
                try
                {
                    string filePath = _stage.File;
                    string backupPath = filePath + ".original";
                    if (!File.Exists(backupPath))
                    {
                        File.Copy(filePath, backupPath);
                    }

                    byte[] newData = BtlBridge.CloneAndAppendVectorElement(_fileData, vectorOffset, templateIndex, elemType);

                    File.WriteAllBytes(filePath, newData);

                    MessageBox.Show("元素克隆并追加成功，全文件偏移已自动重算并完成重新载入！", "成功", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    ReloadCurrentFile();
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"操作失败:\n{ex.Message}", "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            }
        }

        private void RunSimLoaderClick(object sender, EventArgs e)
        {
            if (_stage == null)
            {
                MessageBox.Show("请先打开一个 BTL 关卡文件！", "提示", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            var logs = BtlSimLoader.Simulate(_stage, out bool hasCrash);
            var sb = new StringBuilder();
            sb.AppendLine("================ BTL 模拟加载与崩溃检测报告 ================");
            sb.AppendLine($"文件: {Path.GetFileName(_stage.File)}");
            sb.AppendLine($"时间: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
            sb.AppendLine($"状态: {(hasCrash ? "❌ 存在崩溃/闪退风险" : "✅ 验证通过，未见崩溃危险")}");
            sb.AppendLine("==========================================================");
            sb.AppendLine();

            foreach (var log in logs)
            {
                sb.AppendLine($"[{log.Type,-5}] [{log.Component,-13}] {log.Message}");
            }

            using (var form = new Form())
            {
                form.Text = "BTL 模拟加载崩溃检测";
                form.Size = new Size(800, 550);
                form.StartPosition = FormStartPosition.CenterParent;
                form.MinimizeBox = false;
                form.MaximizeBox = false;

                var txt = new TextBox();
                txt.Multiline = true;
                txt.ScrollBars = ScrollBars.Vertical;
                txt.ReadOnly = true;
                txt.Dock = DockStyle.Fill;
                txt.Font = new Font("Consolas", 10f);
                txt.Text = sb.ToString();

                form.Controls.Add(txt);
                form.ShowDialog(this);
            }
        }

        // 调试状态成员变量
        private bool _isDebugActive = false;
        private int _debugPC = -1; // _disasmItems 中的行索引
        private Stack<int> _debugCallStack = new Stack<int>();
        private HashSet<int> _breakpoints = new HashSet<int>(); // 存储断点的绝对文件物理偏移 (Offset)

        private void ArrowGutterMouseDown(object sender, MouseEventArgs e)
        {
            if (_stage == null || _disasmItems == null || rtbDisasm == null || _disasmLines == null || _disasmLines.Length == 0) return;

            try
            {
                int firstVisibleLine = SendMessage(rtbDisasm.Handle, EM_GETFIRSTVISIBLELINE, IntPtr.Zero, IntPtr.Zero).ToInt32();
                int yOffset = rtbDisasm.PointToScreen(Point.Empty).Y - pnlArrowGutter.PointToScreen(Point.Empty).Y;
                int char1 = rtbDisasm.GetFirstCharIndexFromLine(firstVisibleLine);
                int topY = 0;
                if (char1 >= 0)
                {
                    Point pt1 = rtbDisasm.GetPositionFromCharIndex(char1);
                    topY = pt1.Y;
                }

                // 根据 Y 坐标估计哪一行被点击
                int clickedLine = firstVisibleLine + (e.Y - yOffset - topY) / _cachedLineHeight;
                if (clickedLine >= 0 && clickedLine < _disasmItems.Count)
                {
                    int offset = _disasmItems[clickedLine].Offset;
                    if (_breakpoints.Contains(offset))
                    {
                        _breakpoints.Remove(offset);
                    }
                    else
                    {
                        _breakpoints.Add(offset);
                    }

                    pnlArrowGutter.Invalidate();
                    rtbDisasm.Invalidate();
                }
            }
            catch {}
        }

        private void StartResetDebug()
        {
            if (_stage == null || _disasmItems == null || _disasmItems.Count == 0)
            {
                MessageBox.Show("请先打开一个 BTL 关卡文件！", "提示", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            // 在启动调试时调用验证模拟器，拦截存在崩溃闪退风险的文件
            var logs = BtlSimLoader.Simulate(_stage, out bool hasCrash);
            if (hasCrash)
            {
                var sb = new StringBuilder();
                sb.AppendLine("❌ 警告：检测到该 BTL 文件在游戏加载时有 100% 闪退/崩溃风险！");
                sb.AppendLine("详细检测错误如下：");
                foreach (var log in logs)
                {
                    if (log.Type == "FATAL" || log.Type == "ERROR")
                    {
                        sb.AppendLine($"[{log.Type}] [{log.Component}] {log.Message}");
                    }
                }
                MessageBox.Show(sb.ToString(), "崩溃检测异常 - 拒绝启动调试", MessageBoxButtons.OK, MessageBoxIcon.Error);
                _isDebugActive = false;
                _debugPC = -1;
                return;
            }

            _isDebugActive = true;
            _debugPC = 0;
            _debugCallStack.Clear();
            statusLabel.Text = "调试已启动 | PC 指针位于文件头部 (Offset: 0x00000000)";
            SyncDebugSelection();
        }

        private void StepInto()
        {
            if (!_isDebugActive)
            {
                StartResetDebug();
                return;
            }

            if (_debugPC < 0 || _debugPC >= _disasmItems.Count)
            {
                EndDebug("已经执行到文件末尾！");
                return;
            }

            var item = _disasmItems[_debugPC];
            // Step Into: 如果当前行是一个跳转偏移量，且为正向引用字段（包含 _offset 或 entry_point），则跟进去
            if (item.TargetOffset.HasValue && 
                (item.Description.Contains("_offset") || item.Description.Contains("entry_point") || item.Description.Contains("_ptr")))
            {
                int target = item.TargetOffset.Value;
                int targetIndex = FindDisasmItemIndex(target);
                if (targetIndex >= 0)
                {
                    _debugCallStack.Push(_debugPC + 1); // 压入返回地址
                    _debugPC = targetIndex;
                    statusLabel.Text = $"步入子表 -> 目标偏移: 0x{target:X8}";
                    SyncDebugSelection();
                    return;
                }
            }

            // 否则正常单步移向下一行
            _debugPC++;
            if (_debugPC >= _disasmItems.Count)
            {
                EndDebug("调试顺利结束，未见加载崩溃异常！");
            }
            else
            {
                statusLabel.Text = $"步进 -> 偏移: 0x{_disasmItems[_debugPC].Offset:X8}";
                SyncDebugSelection();
            }
        }

        private void StepOver()
        {
            if (!_isDebugActive)
            {
                StartResetDebug();
                return;
            }

            if (_debugPC < 0 || _debugPC >= _disasmItems.Count)
            {
                EndDebug("已经执行到文件末尾！");
                return;
            }

            // Step Over: 不进入 TargetOffset，直接单步移向下一行
            _debugPC++;
            if (_debugPC >= _disasmItems.Count)
            {
                EndDebug("调试顺利结束，未见加载崩溃异常！");
            }
            else
            {
                statusLabel.Text = $"步过 -> 偏移: 0x{_disasmItems[_debugPC].Offset:X8}";
                SyncDebugSelection();
            }
        }

        private void StepOut()
        {
            if (!_isDebugActive) return;

            if (_debugCallStack.Count > 0)
            {
                // 弹出调用栈返回
                _debugPC = _debugCallStack.Pop();
                if (_debugPC >= _disasmItems.Count)
                {
                    EndDebug("调试顺利结束！");
                }
                else
                {
                    statusLabel.Text = $"步出至父级 -> 偏移: 0x{_disasmItems[_debugPC].Offset:X8}";
                    SyncDebugSelection();
                }
            }
            else
            {
                // 跳出当前段
                string curSec = _disasmItems[_debugPC].Section;
                int nextIndex = _debugPC;
                while (nextIndex < _disasmItems.Count && _disasmItems[nextIndex].Section == curSec)
                {
                    nextIndex++;
                }

                if (nextIndex >= _disasmItems.Count)
                {
                    EndDebug("调试顺利结束！");
                }
                else
                {
                    _debugPC = nextIndex;
                    statusLabel.Text = $"步出当前段 -> 进入 {curSec} 段下一部分";
                    SyncDebugSelection();
                }
            }
        }

        private void RunDebug()
        {
            if (!_isDebugActive)
            {
                StartResetDebug();
                if (!_isDebugActive) return;
            }

            statusLabel.Text = "正在运行调试...";
            Application.DoEvents();

            bool hitBreakpoint = false;
            // 顺次向下执行，因为所有表行均在 Flat 列表中展开，无需自动跟随指针跳转（防止反向引用导致死循环）
            while (_debugPC < _disasmItems.Count)
            {
                var item = _disasmItems[_debugPC];

                // 触发断点
                if (_breakpoints.Contains(item.Offset))
                {
                    hitBreakpoint = true;
                    break;
                }

                _debugPC++;
            }

            if (hitBreakpoint)
            {
                statusLabel.Text = $"触发断点停下 -> 偏移: 0x{_disasmItems[_debugPC].Offset:X8}";
                SyncDebugSelection();
                MessageBox.Show($"已在断点偏移 0x{_disasmItems[_debugPC].Offset:X8} 处暂停执行！", "断点暂停", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            else
            {
                EndDebug("调试运行结束，未见加载崩溃异常！");
            }
        }

        private int FindDisasmItemIndex(int offset)
        {
            if (_disasmItems == null) return -1;
            for (int i = 0; i < _disasmItems.Count; i++)
            {
                if (_disasmItems[i].Offset == offset) return i;
            }
            return -1;
        }

        private void EndDebug(string msg)
        {
            _isDebugActive = false;
            _debugPC = -1;
            _debugCallStack.Clear();
            pnlArrowGutter.Invalidate();
            rtbDisasm.Invalidate();
            statusLabel.Text = "调试已结束";
            MessageBox.Show(msg, "调试器提示", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        private void ToggleBreakpointClick(object sender, EventArgs e)
        {
            if (_disasmItems == null || rtbDisasm == null) return;

            try
            {
                int charIndex = rtbDisasm.SelectionStart;
                int lineIndex = rtbDisasm.GetLineFromCharIndex(charIndex);
                if (lineIndex >= 0 && lineIndex < _disasmItems.Count)
                {
                    int offset = _disasmItems[lineIndex].Offset;
                    if (_breakpoints.Contains(offset))
                    {
                        _breakpoints.Remove(offset);
                        statusLabel.Text = $"已清除断点: 0x{offset:X8}";
                    }
                    else
                    {
                        _breakpoints.Add(offset);
                        statusLabel.Text = $"已设置断点: 0x{offset:X8}";
                    }

                    pnlArrowGutter.Invalidate();
                    rtbDisasm.Invalidate();
                }
            }
            catch {}
        }

        private void SyncDebugSelection()
        {
            if (rtbDisasm == null || _disasmItems == null || _debugPC < 0 || _debugPC >= _disasmItems.Count) return;

            try
            {
                _isSyncingSelection = true;
                int start = rtbDisasm.GetFirstCharIndexFromLine(_debugPC);
                int len = _disasmLines[_debugPC].Length;

                SendMessage(rtbDisasm.Handle, WM_SETREDRAW, IntPtr.Zero, IntPtr.Zero);
                try
                {
                    int firstVisibleLine = SendMessage(rtbDisasm.Handle, EM_GETFIRSTVISIBLELINE, IntPtr.Zero, IntPtr.Zero).ToInt32();
                    int visibleLineCount = pnlArrowGutter.Height / _cachedLineHeight;

                    if (_debugPC < firstVisibleLine || _debugPC > firstVisibleLine + visibleLineCount - 2)
                    {
                        int scrollIndex = Math.Max(0, _debugPC - visibleLineCount / 2);
                        int scrollStart = rtbDisasm.GetFirstCharIndexFromLine(scrollIndex);
                        rtbDisasm.Select(scrollStart, 0);
                        rtbDisasm.ScrollToCaret();
                    }

                    rtbDisasm.Select(start, len);
                }
                finally
                {
                    SendMessage(rtbDisasm.Handle, WM_SETREDRAW, new IntPtr(1), IntPtr.Zero);
                    rtbDisasm.Refresh();
                }

                pnlArrowGutter.Invalidate();
                _isSyncingSelection = false;
            }
            catch
            {
                _isSyncingSelection = false;
            }
        }

        private class ArrowLink
        {
            public int SrcLine;
            public int DstLine;
            public int SrcY;
            public int DstY;
            public int Lane;
        }
    }

    public class DoubleBufferedPanel : Panel
    {
        public DoubleBufferedPanel()
        {
            DoubleBuffered = true;
            SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint, true);
        }
    }
}
