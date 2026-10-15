using BtlCore.Fb;

namespace BtldMapEditor.Front;

public partial class FrontRegistryViewer
{
    BinaryRegistryView _binaryView;
    ComboBox _sourceChoice;
    CheckBox _editCurrent;
    Button _btnNamesOnly, _btnResetSchema, _btnRawSlots;
    FrontSession _session;
    BtlBinaryInspection _inspection;
    BtlBinaryInspection _boundOriginal;
    bool _selectingSource;
    public bool IsOriginalView => _sourceChoice?.SelectedIndex == 0 && _session?.Original != null;
    public bool CanEdit => !IsOriginalView && _editCurrent?.Checked == true && (_session?.CanExport ?? true);
    public BtlBinaryInspection Inspection => _inspection;

    void InitInspectionControls(Panel addressPanel)
    {
        _binaryView = new BinaryRegistryView { Visible = false };
        _binaryView.PathChanged += path => { if (IsOriginalView) _addressBar.Text = path; };
        var toolbar = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 34, WrapContents = false, Padding = new Padding(4, 2, 4, 2) };
        _sourceChoice = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 170 };
        _sourceChoice.Items.AddRange(new object[] { "原始文件（只读）", "当前文档" });
        _sourceChoice.SelectedIndex = 1;
        _editCurrent = new CheckBox { Text = "允许编辑当前文档", AutoSize = true, Padding = new Padding(3, 4, 3, 0) };
        _btnNamesOnly = new Button { Text = "仅导入名称与说明", AutoSize = true };
        _btnResetSchema = new Button { Text = "恢复内置 schema", AutoSize = true };
        _btnRawSlots = new Button { Text = "仅原始槽", AutoSize = true };
        toolbar.Controls.AddRange(new Control[] { _sourceChoice, _editCurrent, _btnNamesOnly, _btnRawSlots, _btnResetSchema });
        _sourceChoice.SelectedIndexChanged += (_, _) => { if (!_selectingSource) UpdateSourceView(); };
        _editCurrent.CheckedChanged += (_, _) => { if (!CanEdit) { _inlineEdit.Visible = false; _editingItem = null; } };
        _btnNamesOnly.Click += (_, _) => ChooseSchema(namesOnly: true);
        _btnRawSlots.Click += (_, _) =>
        {
            if (_session?.Original == null) return;
            _inspection = _session.Original.Reinterpret(null); _fbs = null;
            _sourceChoice.SelectedIndex = 0; UpdateSourceView(reloadOriginal: true);
        };
        _btnResetSchema.Click += (_, _) =>
        {
            _fbs = null; _inspection = _session?.Original;
            UpdateSourceView(reloadOriginal: true); RefreshTree();
        };
        // 工具栏使用独立布局行，两个 Fill 视图只能占内容行，避免相互覆盖。
        Controls.Remove(addressPanel); Controls.Remove(_split);
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3,
            Margin = Padding.Empty, Padding = Padding.Empty };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 38));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 38));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        toolbar.Dock = DockStyle.Fill; toolbar.Margin = Padding.Empty;
        addressPanel.Dock = DockStyle.Fill; addressPanel.Margin = Padding.Empty;
        var content = new Panel { Dock = DockStyle.Fill, Margin = Padding.Empty };
        content.Controls.Add(_split); content.Controls.Add(_binaryView);
        layout.Controls.Add(toolbar, 0, 0); layout.Controls.Add(addressPanel, 0, 1); layout.Controls.Add(content, 0, 2);
        Controls.Add(layout);
        _btnLoadFbs.Text = "用 .fbs 重新解释";
        _btnLoadFbs.Width = 185;
        UpdateSourceView();
    }

    public void LoadSession(FrontSession session, bool collapseAll = true)
    {
        bool newSource = !ReferenceEquals(_boundOriginal, session?.Original) || !ReferenceEquals(_session, session);
        _session = session;
        if (newSource)
        {
            _boundOriginal = session?.Original;
            _inspection = _boundOriginal; _fbs = null; _editCurrent.Checked = false;
            _selectingSource = true;
            _sourceChoice.SelectedIndex = _inspection == null ? 1 : 0;
            _selectingSource = false;
        }
        LoadDocument(session?.Document, collapseAll);
        UpdateSourceView(reloadOriginal: newSource);
    }

    /// <summary>重新解释只作用于原始字节视图；名称导入不改变数值、类型或节点结构。</summary>
    public void ApplySchema(FbsSchema schema, bool namesOnly = false)
    {
        if (schema == null || !schema.IsTable(schema.RootType)) throw new InvalidDataException("schema 没有有效的 root_type table");
        if (namesOnly)
        {
            _fbs = schema;
            if (_inspection != null) _binaryView.SetNames(schema);
            RefreshTree(); UpdateSourceView();
            return;
        }
        if (_session?.Original == null) throw new InvalidOperationException("当前文档没有原始二进制，不能重新解释文件。可以仅导入名称与说明。");
        // 先完成验证和构造，再替换当前有效视图。
        var interpreted = _session.Original.Reinterpret(schema);
        _inspection = interpreted; _fbs = schema;
        _sourceChoice.SelectedIndex = 0;
        UpdateSourceView(reloadOriginal: true);
    }

    void ChooseSchema(bool namesOnly)
    {
        using var dialog = new OpenFileDialog { Title = namesOnly ? "仅导入字段名称与说明" : "选择用于解释原始文件的 schema",
            Filter = "FlatBuffers Schema (*.fbs)|*.fbs|所有文件 (*.*)|*.*" };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        try { ApplySchema(FbsSchema.LoadFile(dialog.FileName), namesOnly); }
        catch (Exception ex) { MessageBox.Show("加载失败，保留上一份有效视图：\n" + ex.Message, "Schema", MessageBoxButtons.OK, MessageBoxIcon.Warning); }
    }

    void UpdateSourceView(bool reloadOriginal = false)
    {
        if (_binaryView == null) return;
        bool original = IsOriginalView;
        _split.Visible = !original; _binaryView.Visible = original;
        _editCurrent.Enabled = !original && (_session?.CanExport ?? true);
        _sourceChoice.Enabled = _session?.Original != null;
        _btnLoadFbs.Enabled = _session?.Original != null;
        _btnRawSlots.Enabled = _session?.Original != null;
        _lblFbsStatus.Text = original
            ? "只读 · 原始文件 · " + (_inspection?.Schema == null ? "未套用 schema" : Path.GetFileName(_inspection.Schema.SourcePath))
            : "当前文档 · 无物理字节来源" + (_fbs == null ? "" : " · 名称：" + Path.GetFileName(_fbs.SourcePath));
        if (original)
        {
            if (reloadOriginal || _binaryView.Inspection != _inspection)
                _binaryView.LoadInspection(_inspection, _session.SourcePath, _fbs ?? _inspection.Schema, _session.Issues);
            _addressBar.Text = _binaryView.SelectedPath;
        }
        else RefreshTree(collapseAll: false);
    }

    void ShowReadOnlyValue(ListViewItem item)
    {
        using var dialog = new Form { Text = "查看内容（只读）", Size = new Size(650, 420), StartPosition = FormStartPosition.CenterParent };
        dialog.Controls.Add(new TextBox { Dock = DockStyle.Fill, Multiline = true, ReadOnly = true,
            ScrollBars = ScrollBars.Both, WordWrap = false, Text = item.SubItems.Count > 3 ? item.SubItems[3].Text : "" });
        dialog.ShowDialog(this);
    }
}
