using System.Globalization;
using System.Text;
using BtlCore.Fb;

namespace BtldMapEditor.Front;

/// <summary>只读原文件视图。右侧虚拟列表按需取值，地址跳转只沿目标路径展开。</summary>
public sealed class BinaryRegistryView : UserControl
{
    const int TreePreviewLimit = 256;
    readonly TreeView _tree = new() { Dock = DockStyle.Fill, HideSelection = false, ShowNodeToolTips = true };
    readonly ListView _list = new() { Dock = DockStyle.Fill, View = View.Details, FullRowSelect = true,
        GridLines = true, VirtualMode = true, HideSelection = false };
    readonly TextBox _content = DetailBox();
    readonly TextBox _hex = DetailBox();
    readonly TextBox _diagnostics = DetailBox();
    readonly Label _sourceLabel = new() { Dock = DockStyle.Top, Height = 52, AutoEllipsis = true };
    readonly Dictionary<string, TreeNode> _paths = new(StringComparer.Ordinal);
    readonly HashSet<TreeNode> _built = new();
    BinaryInspectionNode _shown;
    FbsSchema _names;
    string _sourcePath;
    string _readIssues;
    ConvertIssue[] _issueSource = Array.Empty<ConvertIssue>();
    public BtlBinaryInspection Inspection { get; private set; }
    public string SelectedPath => _shown?.Path ?? "/";
    public event Action<string> PathChanged;

    public BinaryRegistryView()
    {
        Dock = DockStyle.Fill;
        var horizontal = new SplitContainer { Size = new Size(800, 560), Dock = DockStyle.Fill, Orientation = Orientation.Horizontal,
            FixedPanel = FixedPanel.Panel2, SplitterDistance = 380, Panel2MinSize = 70 };
        var vertical = new SplitContainer { Size = new Size(800, 360), Dock = DockStyle.Fill,
            FixedPanel = FixedPanel.Panel1, SplitterDistance = 240 };
        vertical.Panel1.Controls.Add(_tree); vertical.Panel2.Controls.Add(_list);
        horizontal.Panel1.Controls.Add(vertical);
        var tabs = new TabControl { Dock = DockStyle.Fill };
        foreach (var pair in new[] { ("完整内容", _content), ("原始字节", _hex), ("诊断与来源", _diagnostics) })
        {
            var page = new TabPage(pair.Item1); page.Controls.Add(pair.Item2); tabs.TabPages.Add(page);
        }
        horizontal.Panel2.Controls.Add(tabs);
        foreach (var pair in new[] { ("序号", 65), ("名称／说明", 210), ("类型解释", 150), ("内容", 210), ("状态", 110), ("文件位置", 180) })
            _list.Columns.Add(pair.Item1, pair.Item2);
        _list.RetrieveVirtualItem += (_, e) => e.Item = Row(_shown.GetChild(e.ItemIndex), e.ItemIndex);
        _list.SelectedIndexChanged += (_, _) =>
        {
            if (_list.SelectedIndices.Count > 0 && _shown != null)
                ShowDetails(_shown.GetChild(_list.SelectedIndices[0]));
        };
        _list.ItemActivate += (_, _) =>
        {
            if (_list.SelectedIndices.Count > 0 && _shown != null)
                NavigateToPath(_shown.GetChild(_list.SelectedIndices[0]).Path);
        };
        _tree.BeforeExpand += (_, e) => BuildChildren(e.Node);
        _tree.AfterSelect += (_, e) => { if (e.Node.Tag is BinaryInspectionNode node) Show(node); };
        Controls.Add(horizontal); Controls.Add(_sourceLabel);
    }

    static TextBox DetailBox() => new() { Dock = DockStyle.Fill, Multiline = true, ReadOnly = true,
        ScrollBars = ScrollBars.Both, WordWrap = false, Font = new Font("Consolas", 9f) };

    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        if (keyData == (Keys.Control | Keys.C))
        {
            if (_list.Focused && _list.SelectedIndices.Count > 0)
            {
                string text = Format(_shown.GetChild(_list.SelectedIndices[0]).Value);
                if (text.Length > 0) Clipboard.SetText(text);
                return true;
            }
            if (_tree.Focused) { Clipboard.SetText(SelectedPath); return true; }
        }
        return base.ProcessCmdKey(ref msg, keyData);
    }

    public void LoadInspection(BtlBinaryInspection inspection, string sourcePath, FbsSchema names = null, IEnumerable<ConvertIssue> issues = null)
    {
        Inspection = inspection ?? throw new ArgumentNullException(nameof(inspection));
        _sourcePath = sourcePath; _names = names;
        var diagnostics = (issues ?? Array.Empty<ConvertIssue>()).ToArray();
        _issueSource = diagnostics;
        _readIssues = string.Join("\r\n", diagnostics.Select(i => i.ToString()));
        _sourceLabel.Text = $"原始文件（只读）：{sourcePath ?? "内存字节"} · {inspection.ByteLength} 字节\n" +
            $"类型解释：{(inspection.Schema == null ? "未套用 schema" : Path.GetFileName(inspection.Schema.SourcePath))}" +
            (names == null ? "" : $" · 名称说明：{Path.GetFileName(names.SourcePath)}") +
            $" · 原读档警告／错误 {diagnostics.Count(i => i.Severity != IssueSeverity.Info)} 条";
        _tree.BeginUpdate();
        try
        {
            _list.VirtualListSize = 0; _paths.Clear(); _built.Clear(); _tree.Nodes.Clear();
            var root = MakeNode(inspection.Root); _tree.Nodes.Add(root); _tree.SelectedNode = root;
            root.Expand(); Show(inspection.Root);
        }
        finally { _tree.EndUpdate(); }
    }

    public void SetNames(FbsSchema schema)
    {
        string path = SelectedPath;
        LoadInspection(Inspection, _sourcePath, schema, _issueSource);
        NavigateToPath(path);
    }

    TreeNode MakeNode(BinaryInspectionNode value)
    {
        var tree = new TreeNode(Label(value)) { Tag = value, ToolTipText = value.Path + " · " + value.Type };
        if (value.ChildCount > 0) tree.Nodes.Add(new TreeNode("…"));
        _paths[value.Path] = tree;
        return tree;
    }

    void BuildChildren(TreeNode tree)
    {
        if (tree.Tag is not BinaryInspectionNode node || !_built.Add(tree)) return;
        tree.Nodes.Clear();
        int preview = Math.Min(node.ChildCount, TreePreviewLimit);
        for (int i = 0; i < preview; i++)
        {
            var child = node.GetChild(i);
            if (child.ChildCount > 0) tree.Nodes.Add(MakeNode(child));
        }
        if (node.Kind == "vector" && node.ChildCount > preview)
        {
            var last = node.GetChild(node.ChildCount - 1);
            if (last.Path.EndsWith("/decoded", StringComparison.Ordinal)) tree.Nodes.Add(MakeNode(last));
        }
        if (node.ChildCount > preview) tree.Nodes.Add(new TreeNode("更多项请在右侧列表或地址栏进入"));
    }

    public bool NavigateToPath(string path)
    {
        path = "/" + (path ?? "").Trim('/');
        var target = Inspection?.Find(path);
        if (target == null) return false;
        TreeNode parent = _tree.Nodes[0];
        string walk = "";
        foreach (string part in path.Trim('/').Split('/', StringSplitOptions.RemoveEmptyEntries))
        {
            walk += "/" + part;
            if (!_paths.TryGetValue(walk, out var child))
            {
                var value = Inspection.Find(walk);
                BuildChildren(parent);
                if (!_paths.TryGetValue(walk, out child)) { child = MakeNode(value); parent.Nodes.Add(child); }
            }
            parent.Expand(); parent = child;
        }
        _tree.SelectedNode = parent; parent.EnsureVisible(); Show(target);
        return true;
    }

    void Show(BinaryInspectionNode value)
    {
        _list.VirtualListSize = 0; _shown = value;
        _list.VirtualListSize = value.ChildCount; _list.Invalidate();
        ShowDetails(value); PathChanged?.Invoke(value.Path);
    }

    ListViewItem Row(BinaryInspectionNode value, int index)
    {
        string content = Format(value.Value).Replace("\r", " ").Replace("\n", " ");
        if (content.Length > 250) content = content[..250] + "…";
        string segment = value.Path[(value.Path.LastIndexOf('/') + 1)..];
        string id = segment.StartsWith('i') ? segment[1..] : segment == "decoded" ? "解码" : segment;
        var row = new ListViewItem(new[] { id, Label(value), value.Type,
            content, State(value.State), value.Offset is int offset
                ? value.PointerOffset is int pointer && pointer != offset ? $"0x{pointer:X} → 0x{offset:X}" : $"0x{offset:X}"
                : "无独立字节" });
        row.ForeColor = value.State switch
        {
            BinaryValueState.Invalid => Color.Firebrick,
            BinaryValueState.Unknown or BinaryValueState.ZeroPadding => Color.DarkOrange,
            BinaryValueState.Absent => Color.Gray,
            _ => SystemColors.WindowText
        };
        return row;
    }

    string Label(BinaryInspectionNode value)
    {
        if (_names == null || value.Kind == "json" || value.State == BinaryValueState.Decoded) return value.Name;
        string type = _names.RootType;
        FbsField field = null;
        foreach (string part in value.Path.Trim('/').Split('/', StringSplitOptions.RemoveEmptyEntries))
        {
            if (FbsSchema.IsVectorType(type)) type = _names.ResolveChildType(type);
            if (part.StartsWith('i')) { field = null; continue; }
            if (!int.TryParse(part, out int id) || !_names.TryGetField(type, id, out field)) return value.Name;
            type = field.Type;
        }
        if (field == null) return value.Name;
        string comment = FbsSchema.DisplayComment(field.Comment);
        return string.IsNullOrWhiteSpace(comment) ? field.Name : field.Name + " · " + comment;
    }

    void ShowDetails(BinaryInspectionNode value)
    {
        _content.Text = Format(value.Value);
        _hex.Text = value.Offset is int at ? Inspection.Hex(at, value.Length, 4096) : "此值来自 schema 缺省或解码解释，没有独立的原始字节范围。";
        if (value.VtableOffset is int vt)
            _hex.Text = "vtable：\r\n" + Inspection.Hex(vt, value.VtableSize, 4096) + "\r\n对象：\r\n" + _hex.Text;
        if (value.PointerOffset is int pointer && pointer != value.Offset)
            _hex.Text = "相对指针槽：\r\n" + Inspection.Hex(pointer, 4) + "\r\n指向的内容：\r\n" + _hex.Text;
        if (value.Path == "/") _hex.Text = "文件头 Root 偏移：\r\n" + Inspection.Hex(0, Math.Min(4, Inspection.ByteLength)) + "\r\n" + _hex.Text;
        _diagnostics.Text = SourceDetails(value);
    }

    string SourceDetails(BinaryInspectionNode value)
    {
        var text = new StringBuilder();
        text.AppendLine("原始文件：" + (_sourcePath ?? "内存字节"));
        text.AppendLine("SHA-256：" + Inspection.Sha256);
        text.AppendLine("解释 schema：" + (Inspection.Schema?.SourcePath ?? "未套用 schema"));
        text.AppendLine("路径：" + value.Path);
        text.AppendLine("状态：" + State(value.State));
        text.AppendLine($"位置：{(value.Offset is int at ? $"0x{at:X}" : "无独立字节")}；长度／间隔：{value.Length} 字节" +
            (value.ExactLength ? "" : "（预览／物理间隔，不代表已验证的字段宽度）"));
        if (value.VtableOffset is int vt) text.AppendLine($"vtable @0x{vt:X}，size={value.VtableSize}，objectSize={value.ObjectSize}，物理槽={value.PhysicalSlotCount}");
        text.AppendLine(value.Diagnostic);
        text.AppendLine("编辑器读入诊断（不随查看 schema 改变）：");
        text.AppendLine(_readIssues);
        return text.ToString();
    }

    static string Format(object value) => value is IFormattable f ? f.ToString(null, CultureInfo.InvariantCulture) : value?.ToString() ?? "";
    static string State(BinaryValueState state) => state switch
    {
        BinaryValueState.Present => "已写出",
        BinaryValueState.Absent => "未写出",
        BinaryValueState.Unknown => "类型未确定",
        BinaryValueState.Invalid => "解析失败",
        BinaryValueState.NullPointer => "已写出零偏移",
        BinaryValueState.ZeroPadding => "指向零填充",
        _ => "解码解释"
    };
}
