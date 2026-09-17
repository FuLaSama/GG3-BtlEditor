/*
 * GuiForm.cs
 * 
 * 本文件是 BTL 格式转换工具的独立 GUI 界面（GuiForm）：
 * - 界面交互: 提供了转换模式下拉框、日志记录富文本框和一键转换按钮。
 * - 拖拽处理 (Drag-and-Drop): 支持将 BTL/BTLA/JSON 文件直接拖拽入窗口，程序会自动检测文件类型，匹配最合适的转换模式并异步执行 Toolchain 转换操作。
 */
using System;
using System.Drawing;
using System.IO;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace BtlToolchain
{
    public class GuiForm : Form
    {
        private string _srcPath;
        private string _destPath;

        private ComboBox cbMode;
        private Button btnExecute;
        private TextBox txtLog;
        private Label lblStatus;

        public GuiForm()
        {
            InitializeComponent();
            AppendLog("【欢迎使用 BTL 转换工具】");
            AppendLog("使用方法：选择下方所需的转换模式，然后将待处理的文件直接「拖拽」至本窗口任意位置即可完成转换。");
            AppendLog("--------------------------------------------------------------------------------");
        }

        private void InitializeComponent()
        {
            // Set Form Properties
            this.Text = "BTL转换工具";
            this.Size = new Size(680, 480);
            this.MinimumSize = new Size(600, 400);
            this.StartPosition = FormStartPosition.CenterScreen;
            this.Font = new Font("Microsoft YaHei", 9.5F, FontStyle.Regular, GraphicsUnit.Point);
            this.BackColor = Color.FromArgb(245, 246, 248);

            // Title Header Panel
            Panel headerPanel = new Panel
            {
                Dock = DockStyle.Top,
                Height = 65,
                BackColor = Color.FromArgb(41, 56, 85)
            };
            Label lblTitle = new Label
            {
                Text = "BTL转换工具 (支持拖拽)",
                ForeColor = Color.White,
                Font = new Font("Microsoft YaHei", 12.5F, FontStyle.Bold),
                Location = new Point(20, 20),
                AutoSize = true
            };
            headerPanel.Controls.Add(lblTitle);
            this.Controls.Add(headerPanel);

            // Content Panel (with padding)
            Panel contentPanel = new Panel
            {
                Dock = DockStyle.Fill,
                Padding = new Padding(20)
            };
            this.Controls.Add(contentPanel);

            // Group 1: Operation Modes
            GroupBox grpMode = new GroupBox
            {
                Text = "转换模式",
                Dock = DockStyle.Top,
                Height = 80,
                Padding = new Padding(15, 10, 15, 10)
            };
            contentPanel.Controls.Add(grpMode);

            TableLayoutPanel modeTable = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 2,
                RowCount = 1
            };
            modeTable.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            modeTable.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 120));
            grpMode.Controls.Add(modeTable);

            cbMode = new ComboBox
            {
                Dock = DockStyle.Fill,
                DropDownStyle = ComboBoxStyle.DropDownList,
                Margin = new Padding(0, 5, 0, 5)
            };
            cbMode.Items.AddRange(new object[] {
                "BTL ──> 逻辑 JSON",
                "逻辑 JSON ──> BTL",
                "BTL ──> BTLA",
                "BTLA ──> BTL",
                "BTLA ──> BTLD JSON",
                "BTLD JSON ──> BTLA",
                "BTLD JSON ──> 逻辑 JSON",
                "逻辑 JSON ──> BTLD JSON"
            });
            cbMode.SelectedIndex = 0;
            cbMode.SelectedIndexChanged += CbMode_SelectedIndexChanged;

            btnExecute = new Button
            {
                Text = "开始转换",
                Dock = DockStyle.Fill,
                BackColor = Color.FromArgb(70, 118, 196),
                ForeColor = Color.White,
                FlatStyle = FlatStyle.Flat,
                Font = new Font("Microsoft YaHei", 9.5F, FontStyle.Bold),
                Margin = new Padding(15, 3, 0, 3)
            };
            btnExecute.FlatAppearance.BorderSize = 0;
            btnExecute.Click += BtnExecute_Click;

            modeTable.Controls.Add(cbMode, 0, 0);
            modeTable.Controls.Add(btnExecute, 1, 0);

            // Spacer
            Panel spacer2 = new Panel { Dock = DockStyle.Top, Height = 10 };
            contentPanel.Controls.Add(spacer2);

            // Group 2: Log Console
            GroupBox grpLog = new GroupBox
            {
                Text = "运行日志与输出",
                Dock = DockStyle.Fill,
                Padding = new Padding(15, 10, 15, 15)
            };
            contentPanel.Controls.Add(grpLog);

            txtLog = new TextBox
            {
                Dock = DockStyle.Fill,
                Multiline = true,
                ScrollBars = ScrollBars.Vertical,
                ReadOnly = true,
                BackColor = Color.FromArgb(30, 30, 30),
                ForeColor = Color.FromArgb(220, 220, 220),
                Font = new Font("Consolas", 9F, FontStyle.Regular, GraphicsUnit.Point),
                Margin = new Padding(0, 0, 0, 10)
            };
            grpLog.Controls.Add(txtLog);

            // Status Panel
            Panel statusPanel = new Panel
            {
                Dock = DockStyle.Bottom,
                Height = 25,
                Padding = new Padding(0, 5, 0, 0)
            };
            lblStatus = new Label
            {
                Text = "准备就绪。请将文件拖入窗口开始。",
                ForeColor = Color.FromArgb(100, 100, 100),
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleLeft,
                Font = new Font("Microsoft YaHei", 9F, FontStyle.Regular)
            };
            statusPanel.Controls.Add(lblStatus);
            grpLog.Controls.Add(statusPanel);

            // Rearrange controls z-order to support Fill docking properly
            headerPanel.BringToFront();
            grpMode.BringToFront();
            spacer2.BringToFront();
            grpLog.BringToFront();

            // Enable drag-and-drop on all major controls
            this.AllowDrop = true;
            contentPanel.AllowDrop = true;
            grpMode.AllowDrop = true;
            grpLog.AllowDrop = true;
            txtLog.AllowDrop = true;

            Control[] dragControls = new Control[] { this, contentPanel, grpMode, grpLog, txtLog };
            foreach (var ctrl in dragControls)
            {
                ctrl.DragEnter += GuiForm_DragEnter;
                ctrl.DragDrop += GuiForm_DragDrop;
            }
        }

        private void CbMode_SelectedIndexChanged(object sender, EventArgs e)
        {
            // Auto update extensions when mode changes
            if (!string.IsNullOrEmpty(_srcPath))
            {
                string ext = GetExtensionForCurrentMode(isSource: false);
                _destPath = Path.ChangeExtension(_srcPath, ext);
            }
        }

        private async void BtnExecute_Click(object sender, EventArgs e)
        {
            string src = _srcPath;
            string dest = _destPath;

            if (string.IsNullOrEmpty(src))
            {
                MessageBox.Show("请先将需要转换的文件拖放到窗口内！", "提示", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            if (!File.Exists(src))
            {
                MessageBox.Show($"源文件不存在：\n{src}", "文件错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            btnExecute.Enabled = false;
            lblStatus.Text = "正在进行数据转换，请稍候...";
            lblStatus.ForeColor = Color.FromArgb(190, 110, 0);
            AppendLog($"[{DateTime.Now:HH:mm:ss}] 开始执行转换...");
            AppendLog($"源文件: {src}");
            AppendLog($"目标文件: {dest}");
            AppendLog($"转换模式: {cbMode.SelectedItem}");

            int mode = cbMode.SelectedIndex;

            try
            {
                // Run heavy operations asynchronously
                await Task.Run(() =>
                {
                    switch (mode)
                    {
                        case 0: // BTL -> Logical JSON
                            Toolchain.BtlToLogicalJson(src, dest);
                            break;
                        case 1: // Logical JSON -> BTL
                            Toolchain.LogicalJsonToBtl(src, dest);
                            break;
                        case 2: // BTL -> BTLA
                            Toolchain.Disassemble(src, dest);
                            break;
                        case 3: // BTLA -> BTL
                            Toolchain.Assemble(src, dest);
                            break;
                        case 4: // BTLA -> BTLD JSON
                            Toolchain.Decompile(src, dest);
                            break;
                        case 5: // BTLD JSON -> BTLA
                            Toolchain.Compile(src, dest);
                            break;
                        case 6: // BTLD JSON -> Logical JSON
                            Toolchain.BtldToLogicalJsonFile(src, dest);
                            break;
                        case 7: // Logical JSON -> BTLD JSON
                            Toolchain.LogicalToBtldJsonFile(src, dest);
                            break;
                    }
                });

                lblStatus.Text = "转换完成！操作成功。";
                lblStatus.ForeColor = Color.FromArgb(0, 120, 0);
                AppendLog($"[{DateTime.Now:HH:mm:ss}] 转换成功！生成目标文件：{dest}");
            }
            catch (Exception ex)
            {
                lblStatus.Text = "发生错误！详细信息见日志栏。";
                lblStatus.ForeColor = Color.FromArgb(200, 0, 0);
                AppendLog($"\n[ERROR] 转换失败！异常详细信息：\n{ex.ToString()}");
                MessageBox.Show($"操作失败，错误细节已记录到日志框中。\n\n{ex.Message}", "转换出错", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            finally
            {
                btnExecute.Enabled = true;
            }
        }

        private void AppendLog(string message)
        {
            if (txtLog.InvokeRequired)
            {
                txtLog.Invoke(new Action<string>(AppendLog), message);
                return;
            }
            txtLog.AppendText(message + Environment.NewLine);
        }

        private string GetExtensionForCurrentMode(bool isSource)
        {
            int mode = cbMode.SelectedIndex;
            bool targetJson = mode == 0 || mode == 4 || mode == 6;
            bool targetBtla = mode == 2 || mode == 5;
            bool targetBtl = mode == 1 || mode == 3;
            bool targetBtld = mode == 7;

            if (targetJson) return ".json";
            if (targetBtla) return ".btla";
            if (targetBtl) return ".btl";
            if (targetBtld) return ".btld.json";
            return ".json";
        }

        private void GuiForm_DragEnter(object sender, DragEventArgs e)
        {
            if (e.Data.GetDataPresent(DataFormats.FileDrop))
            {
                e.Effect = DragDropEffects.Copy;
            }
            else
            {
                e.Effect = DragDropEffects.None;
            }
        }

        private void GuiForm_DragDrop(object sender, DragEventArgs e)
        {
            string[] files = (string[])e.Data.GetData(DataFormats.FileDrop);
            if (files != null && files.Length > 0)
            {
                string file = files[0];
                if (Directory.Exists(file))
                {
                    AppendLog("拖入的是目录，请拖入单个文件 (.btl, .json, .btla, .btld.json)！");
                    MessageBox.Show("不支持拖入文件夹，请拖入单个文件！", "拖拽提示", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }

                string ext = Path.GetExtension(file).ToLower();
                int mode = cbMode.SelectedIndex;
                
                bool isBtl = ext == ".btl";
                bool isBtla = ext == ".btla";
                bool isJson = ext == ".json" || file.EndsWith(".btld.json", StringComparison.OrdinalIgnoreCase);

                bool isMatch = false;
                string expectedDesc = "";

                switch (mode)
                {
                    case 0: // BTL ──> 逻辑 JSON
                    case 2: // BTL ──> BTLA
                        isMatch = isBtl;
                        expectedDesc = ".btl 二进制文件";
                        break;
                    case 1: // 逻辑 JSON ──> BTL
                    case 7: // 逻辑 JSON ──> BTLD JSON
                        isMatch = isJson;
                        expectedDesc = ".json 逻辑文件";
                        break;
                    case 3: // BTLA ──> BTL
                    case 4: // BTLA ──> BTLD JSON
                        isMatch = isBtla;
                        expectedDesc = ".btla 汇编文本";
                        break;
                    case 5: // BTLD JSON ──> BTLA
                    case 6: // BTLD JSON ──> 逻辑 JSON
                        isMatch = isJson;
                        expectedDesc = ".btld.json 或 .json 物理键名文件";
                        break;
                }

                if (isMatch)
                {
                    _srcPath = file;
                    _destPath = Path.ChangeExtension(file, GetExtensionForCurrentMode(isSource: false));
                    AppendLog($"[拖拽一键触发] 拖入文件 {Path.GetFileName(file)}，匹配当前选中模式，开始转换...");
                    BtnExecute_Click(null, null);
                }
                else
                {
                    string modeName = cbMode.SelectedItem.ToString();
                    MessageBox.Show($"当前选中的转换模式是：\n【{modeName}】\n\n但这与拖入的文件格式不匹配！\n期望的文件格式为：{expectedDesc}", 
                        "文件格式不匹配", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    AppendLog($"[拖拽警告] 拖入的文件格式不匹配当前选中的模式【{modeName}】，已取消转换。");
                }
            }
        }
    }
}
