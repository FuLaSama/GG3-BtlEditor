using System.Globalization;
using BtlCore.Scripting;

namespace BtldMapEditor;

/// <summary>控件完全由 Lua 参数声明创建；没有工具专属的硬编码表单。</summary>
public sealed class ScriptToolDialog : Form
{
    readonly ScriptTool _tool;
    readonly Dictionary<string, Control> _inputs = new();
    public event Action<Exception> ErrorOccurred;

    public ScriptToolDialog(ScriptTool tool, Func<ToolParameter, IReadOnlyList<ToolChoice>> choices,
        Action<Dictionary<string, object>> execute)
    {
        _tool = tool;
        Text = tool.Title;
        ClientSize = new Size(520, Math.Min(650, 130 + tool.Parameters.Count * 42));
        StartPosition = FormStartPosition.CenterParent;
        MinimizeBox = false; MaximizeBox = false;
        var scroll = new Panel { Dock = DockStyle.Fill, AutoScroll = true, Padding = new Padding(12) };
        var table = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, ColumnCount = 2 };
        table.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 150));
        table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        var info = new Label { Text = "作用范围：" + ScopeLabel(tool.Target) + (string.IsNullOrEmpty(tool.Description) ? "" : "\n" + tool.Description),
            AutoSize = true, MaximumSize = new Size(470, 0), Margin = new Padding(3, 0, 3, 12) };
        table.Controls.Add(info, 0, 0); table.SetColumnSpan(info, 2);
        int row = 1;
        foreach (var parameter in tool.Parameters)
        {
            table.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            table.Controls.Add(new Label { Text = parameter.Label, AutoSize = true, MaximumSize = new Size(145, 0), Anchor = AnchorStyles.Left }, 0, row);
            Control editor;
            if (parameter.Kind == "boolean") editor = new CheckBox { Checked = parameter.Default is true, AutoSize = true };
            else if (parameter.Kind is "choice" or "faction" or "unit_type" or "general")
            {
                var values = choices(parameter);
                // 名称资源缺失时仍允许输入数字 ID，普通 choice 始终使用选项。
                if (values.Count == 0 && parameter.Kind != "choice")
                    editor = new TextBox { Text = Convert.ToString(parameter.Default, CultureInfo.InvariantCulture) ?? "" };
                else
                {
                    var combo = new ComboBox { DropDownStyle = parameter.Kind == "choice" ? ComboBoxStyle.DropDownList : ComboBoxStyle.DropDown,
                        DisplayMember = nameof(ToolChoice.Label) };
                    foreach (var choice in values) combo.Items.Add(choice);
                    int selected = values.ToList().FindIndex(v => SameValue(v.Value, parameter.Default));
                    combo.SelectedIndex = selected >= 0 ? selected : parameter.Default == null && values.Count > 0 ? 0 : -1;
                    if (combo.SelectedIndex < 0 && parameter.Default != null) combo.Text = Convert.ToString(parameter.Default, CultureInfo.InvariantCulture);
                    editor = combo;
                }
            }
            else editor = new TextBox { Text = Convert.ToString(parameter.Default, CultureInfo.InvariantCulture) ?? "" };
            editor.Name = parameter.Id; editor.Dock = DockStyle.Fill; editor.Margin = new Padding(3, 6, 3, 6);
            _inputs.Add(parameter.Id, editor);
            table.Controls.Add(editor, 1, row++);
        }
        scroll.Controls.Add(table); Controls.Add(scroll);
        var buttons = new FlowLayoutPanel { Dock = DockStyle.Bottom, AutoSize = true, FlowDirection = FlowDirection.RightToLeft, Padding = new Padding(8) };
        var cancel = new Button { Text = "取消", DialogResult = DialogResult.Cancel, AutoSize = true };
        var run = new Button { Name = "run_tool", Text = "执行工具", AutoSize = true };
        run.Click += (_, _) =>
        {
            try { execute(ReadInput()); DialogResult = DialogResult.OK; Close(); }
            catch (Exception error)
            {
                if (ErrorOccurred != null) ErrorOccurred(error);
                else MessageBox.Show(this, error.Message, Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        };
        buttons.Controls.Add(cancel); buttons.Controls.Add(run); Controls.Add(buttons);
        AcceptButton = run; CancelButton = cancel;
    }

    public Dictionary<string, object> ReadInput()
    {
        var result = new Dictionary<string, object>();
        foreach (var parameter in _tool.Parameters)
        {
            var control = _inputs[parameter.Id];
            if (control is CheckBox check) result[parameter.Id] = check.Checked;
            else if (control is ComboBox combo)
            {
                if (combo.SelectedItem is ToolChoice choice) result[parameter.Id] = choice.Value;
                else if (string.IsNullOrWhiteSpace(combo.Text)) result[parameter.Id] = null;
                else if (parameter.Kind != "choice" && double.TryParse(combo.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out double id)) result[parameter.Id] = id;
                else throw new InvalidDataException("「" + parameter.Label + "」请从列表选择，或输入数字 ID");
            }
            else if (parameter.Kind == "text") result[parameter.Id] = control.Text;
            else if (string.IsNullOrWhiteSpace(control.Text)) result[parameter.Id] = null;
            else if (double.TryParse(control.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out double number)) result[parameter.Id] = number;
            else throw new InvalidDataException("「" + parameter.Label + "」需要数字");
        }
        return result;
    }

    static bool SameValue(object left, object right) => left != null && right != null
        && Convert.ToString(left, CultureInfo.InvariantCulture) == Convert.ToString(right, CultureInfo.InvariantCulture);

    public static string ScopeLabel(string target) => target switch
    {
        "selected_unit" => "选中部队", "all_units" => "全部部队（含增援）", "faction_units" => "指定势力的部队",
        "selected_faction" => "选中势力或选中部队的所属势力", "all_factions" => "全部势力",
        "selected_cell" => "选中格子", "all_cells" => "全部格子", "stage" => "当前关卡", "map" => "当前地图",
        _ => target
    };
}
