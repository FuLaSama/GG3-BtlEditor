using System.Globalization;
using BtlCore.Front;
using BtlCore.Scripting;

namespace BtldMapEditor
{
    /// <summary>按一份 page XML 生成输入框和列表，提交时调用 ScriptHost。</summary>
    public partial class LayoutPageControl : UserControl
    {
        readonly LayoutPage _page;
        readonly ScriptHost _host;
        readonly Action _committed;
        readonly Action<string, ScriptArgs> _nativeCommand;
        readonly List<FormRow> _forms = new List<FormRow>();
        readonly List<ListSection> _lists = new List<ListSection>();
        readonly List<BoundSection> _bound = new List<BoundSection>();
        int? _cell;
        bool _loading;
        public event Action<Exception> ErrorOccurred;

        public int? SelectedRow(string bind)
        {
            var list = _lists.FirstOrDefault(l => l.Section.Bind == bind);
            return SelectedListRow(list);
        }

        public LayoutPageControl(LayoutPage page, ScriptHost host, Action committed, Action<string, ScriptArgs> nativeCommand = null)
        {
            _page = page;
            _host = host;
            _committed = committed;
            _nativeCommand = nativeCommand;
            Dock = DockStyle.Top;
            AutoSize = true;
            AutoSizeMode = AutoSizeMode.GrowAndShrink;
            Build();
        }

        public void Reload(BtlFrontDocument doc)
        {
            if (doc == null) return;
            _host.Load(doc);
            using var read = _host.BeginRead();
            _loading = true;
            try
            {
                foreach (var form in _forms)
                {
                    object value = form.Field.Action != null && _host.CanGet(form.Field.Action)
                        ? _host.CallGet(form.Field.Action, new ScriptArgs { CellIndex = _cell })
                        : DefaultValue(form.Field);
                    form.Shown = value;
                    WriteEditor(form.Editor, value);
                }
                foreach (var list in _lists)
                    FillList(list);
                RefreshResolved();
                foreach (var tree in _trees) FillTreeList(tree);
            }
            finally
            {
                _loading = false;
            }
        }

        public void SetCell(int? cell, bool refresh = true)
        {
            int? next = cell >= 0 ? cell : null;
            if (refresh && _cell == next) return;
            _cell = next;
            if (refresh && _host.Document != null)
            {
                using var read = _host.BeginRead();
                RefreshResolved();
                RefreshActionFields();
            }
        }

        void Build()
        {
            var stack = NewStack();
            Controls.Add(stack);
            foreach (var section in _page.Sections)
                StackAdd(stack, section.Kind switch
                {
                    "list" => BuildList(section),
                    "tree" => BuildTree(section),
                    "palettes" => BuildPalettes(section),
                    "label" => BuildLabel(section),
                    _ => BuildForm(section)
                });
        }

        Control BuildForm(LayoutSection section)
        {
            var holder = new BoundSection { Section = section, Editors = new Dictionary<string, Control>() };
            Control body;
            if (section.Groups.Count > 0)
            {
                var stack = NewStack();
                for (int g = 0; g < section.Groups.Count; g++)
                {
                    var table = NewTriple();
                    foreach (var row in section.Groups[g].Rows)
                        PlaceRow(table, row, holder);
                    if (g == 0)
                    {
                        var bar = CommandBar(holder, () => _host.ResolveIndex(section.Resolve, _cell));
                        if (bar != null) AddSpan(table, bar);
                    }
                    StackAdd(stack, Group(section.Groups[g].Title, table));
                }
                body = stack;
            }
            else
            {
                var table = NewTriple();
                if (section.Rows.Count > 0)
                {
                    foreach (var row in section.Rows)
                        PlaceRow(table, row, holder);
                }
                else
                {
                    foreach (var field in section.Fields)
                        PlaceRow(table, new LayoutRow { Label = field.Label, Fields = { field } }, holder);
                }
                var bar = CommandBar(holder, () => _host.ResolveIndex(section.Resolve, _cell));
                if (bar != null) AddSpan(table, bar);
                body = Group(section.Title, table);
            }
            _bound.Add(holder);
            BindGates(holder);
            return body;
        }

        Control BuildList(LayoutSection section)
        {
            var list = new ListView
            {
                View = View.Details,
                FullRowSelect = true,
                MultiSelect = false,
                GridLines = true,
                Dock = DockStyle.Fill,
                HeaderStyle = ColumnHeaderStyle.Nonclickable,
                Margin = new Padding(0, 0, 0, 6)
            };
            foreach (var col in section.Columns)
                list.Columns.Add(col.Header, 90);
            var holder = new ListSection { Section = section, View = list, Editors = new Dictionary<string, Control>() };
            list.RetrieveVirtualItem += (_, e) => e.Item = ListItem(holder, e.ItemIndex);
            var table = NewTriple();
            AddSpan(table, list);
            table.RowStyles[table.RowCount - 1] = new RowStyle(SizeType.Absolute, 168f);
            if (section.Fields.Count > 0 && section.Fields.Count <= 6)
                AddSpan(table, InlineFields(section, holder));
            else
            {
                foreach (var field in section.Fields)
                    PlaceRow(table, new LayoutRow { Label = field.Label, Fields = { field } }, holder);
            }
            var bar = CommandBar(holder, () => SelectedListRow(holder));
            if (bar != null) AddSpan(table, bar);
            list.SelectedIndexChanged += (s, e) => ShowSelection(holder);
            _lists.Add(holder);
            BindGates(holder);
            return Group(section.Title, table);
        }

        Control InlineFields(LayoutSection section, BoundSection holder)
        {
            int n = section.Fields.Count;
            var grid = new TableLayoutPanel
            {
                Dock = DockStyle.Top,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                ColumnCount = n,
                RowCount = 2,
                Margin = new Padding(0, 0, 0, 4)
            };
            grid.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            grid.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            for (int i = 0; i < n; i++)
            {
                grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f / n));
                var field = section.Fields[i];
                grid.Controls.Add(new Label
                {
                    Text = field.Label,
                    AutoSize = true,
                    Anchor = AnchorStyles.Left,
                    Margin = new Padding(3, 2, 3, 2)
                }, i, 0);
                var editor = CreateEditor(field);
                editor.Margin = new Padding(3, 2, 3, 2);
                grid.Controls.Add(editor, i, 1);
                Register(holder, field, editor);
            }
            return grid;
        }

        void PlaceRow(TableLayoutPanel grid, LayoutRow row, BoundSection holder)
        {
            if (row.Fields.Count == 0) return;
            int r = grid.RowCount++;
            grid.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            var first = row.Fields[0];
            if (row.Fields.Count == 1 && first.Widget == "check")
            {
                var check = (CheckBox)CreateEditor(first);
                check.Text = string.IsNullOrEmpty(first.Label) ? row.Label : first.Label;
                check.Margin = new Padding(3, 6, 3, 4);
                grid.Controls.Add(check, 0, r);
                grid.SetColumnSpan(check, 3);
                Register(holder, first, check);
                return;
            }
            grid.Controls.Add(new Label
            {
                Text = row.Label,
                AutoSize = true,
                Anchor = AnchorStyles.Left,
                Margin = new Padding(0, 8, 8, 0)
            }, 0, r);
            int shown = Math.Min(2, row.Fields.Count);
            Control lead = null;
            for (int i = 0; i < shown; i++)
            {
                var editor = CreateEditor(row.Fields[i]);
                editor.Margin = new Padding(3, 4, 3, 4);
                grid.Controls.Add(editor, i + 1, r);
                Register(holder, row.Fields[i], editor);
                if (i == 0) lead = editor;
            }
            if (shown == 1 && lead != null && !string.IsNullOrEmpty(first.Lookup))
            {
                var name = new Label
                {
                    AutoSize = true,
                    AutoEllipsis = true,
                    Anchor = AnchorStyles.Left,
                    Margin = new Padding(8, 8, 0, 0),
                    Font = new Font(SystemFonts.DefaultFont, FontStyle.Bold),
                    ForeColor = first.Lookup == "unit" ? Color.DarkGreen : Color.DarkBlue,
                    Text = LookupText(first.Lookup, null)
                };
                grid.Controls.Add(name, 2, r);
                if (!string.IsNullOrEmpty(first.Id))
                    holder.Names[first.Id] = name;
                var captured = first;
                lead.TextChanged += (s, e) => RefreshName(holder, captured);
            }
            else if (shown == 1 && lead != null)
            {
                grid.SetColumnSpan(lead, 2);
            }
        }

        void Register(BoundSection holder, LayoutField field, Control editor)
        {
            editor.Name = field.Id ?? "";
            editor.Enabled = field.Enabled && !field.ReadOnly;
            Tip(editor, field.DisableReason);
            if (!string.IsNullOrEmpty(field.Id))
                holder.Editors[field.Id] = editor;
            var form = new FormRow { Field = field, Editor = editor, Holder = holder };
            _forms.Add(form);
            if (field.Enabled && !field.ReadOnly && !string.IsNullOrEmpty(field.Action))
                HookCommit(editor, () => CommitForm(form));
        }

        void CommitForm(FormRow form)
        {
            if (_loading || _host.Document == null) return;
            object value = ReadEditor(form.Editor);
            if (Same(value, form.Shown)) return;
            try
            {
                if (!string.IsNullOrEmpty(form.Holder.Section.Resolve) && _cell == null) return;
                _host.Run(form.Field.Action, new ScriptArgs { Value = value, CellIndex = _cell });
                form.Shown = value;
                _committed?.Invoke();
            }
            catch (Exception ex)
            {
                ShowError(ex);
                _committed?.Invoke();
                Reload(_host.Document);
            }
        }

        void RunCommand(LayoutSection section, Dictionary<string, Control> editors, LayoutCommand cmd, int? index)
        {
            if (_loading || _host.Document == null || !cmd.Enabled) return;
            if (NeedsSelection(cmd) && index == null)
            {
                string hint = string.IsNullOrEmpty(section.Resolve) ? "请先在列表中选中一行。" : "请先在地图上选中一格。";
                MessageBox.Show(hint, cmd.Label, MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            if (!string.IsNullOrEmpty(cmd.Confirm)
                && MessageBox.Show(cmd.Confirm, cmd.Label, MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes)
                return;
            try
            {
                var args = new ScriptArgs { Index = index, CellIndex = _cell, Input = ReadInputs(editors) };
                if (cmd.Id == "update" && index != null && !string.IsNullOrEmpty(section.Bind) && section.Bind[0] != '/')
                {
                    args.ObjectPath = section.Bind;
                    args.ObjectIndex = index;
                }
                if (!string.IsNullOrEmpty(cmd.Handler)) _nativeCommand?.Invoke(cmd.Handler, args);
                else
                {
                    _host.Run(cmd.Script, args);
                    var list = _lists.Find(l => l.Section == section);
                    if (list != null)
                        list.PendingIndex = cmd.Id switch
                        {
                            "add" => _host.Count(section.Bind) - 1,
                            "move_up" => Math.Max(0, (index ?? 0) - 1),
                            "move_down" => Math.Min(_host.Count(section.Bind) - 1, (index ?? 0) + 1),
                            "delete" => Math.Min(_host.Count(section.Bind) - 1, index ?? -1),
                            _ => index
                        };
                    _committed?.Invoke();
                }
            }
            catch (Exception ex)
            {
                ShowError(ex);
                _committed?.Invoke();
                Reload(_host.Document);
            }
        }

        void FillList(ListSection list)
        {
            list.View.BeginUpdate();
            try { FillListRows(list); }
            finally { list.View.EndUpdate(); }
        }

        void FillListRows(ListSection list)
        {
            int keep = list.PendingIndex ?? SelectedListRow(list) ?? -1;
            list.PendingIndex = null;
            if (list.View.VirtualMode) list.View.VirtualListSize = 0;
            else list.View.Items.Clear();
            list.Rows.Clear();
            list.CachedItems.Clear();
            int n = string.IsNullOrEmpty(list.Section.Bind) ? 0 : _host.Count(list.Section.Bind);
            int total = list.Section.Filter == "offmap"
                ? Convert.ToInt32(_host.ReadBind(null, "/1/0.0", null, null, "number", null, null))
                * Convert.ToInt32(_host.ReadBind(null, "/1/0.1", null, null, "number", null, null)) : 0;
            for (int i = 0; i < n; i++)
            {
                if (list.Section.Filter == "offmap")
                {
                    int cell = Convert.ToInt32(_host.ReadBind(list.Section.Bind, "/0.0", i, null, "number", null, null));
                    if (cell >= 0 && cell < total) continue;
                }
                list.Rows.Add(i);
            }
            list.View.VirtualMode = list.Rows.Count > 256;
            if (list.View.VirtualMode) list.View.VirtualListSize = list.Rows.Count;
            else for (int row = 0; row < list.Rows.Count; row++) list.View.Items.Add(ListItem(list, row));
            int selected = list.Rows.IndexOf(keep);
            if (selected >= 0)
                list.View.Items[selected].Selected = true;
            else
                ShowSelection(list);
            foreach (var button in list.NeedSelection)
                button.Enabled = list.View.SelectedIndices.Count > 0;
        }

        static int? SelectedListRow(ListSection list)
        {
            if (list == null || list.View.SelectedIndices.Count == 0) return null;
            int row = list.View.SelectedIndices[0];
            return row < list.Rows.Count ? list.Rows[row] : null;
        }

        ListViewItem ListItem(ListSection list, int row)
        {
            if (list.CachedItems.TryGetValue(row, out var cached)) return cached;
            if (row < 0 || row >= list.Rows.Count) return new ListViewItem("");
            int index = list.Rows[row];
            var item = new ListViewItem(CellText(list, index, 0)) { Tag = index };
            for (int c = 1; c < list.Section.Columns.Count; c++) item.SubItems.Add(CellText(list, index, c));
            // 长列表只缓存有限数量的行，不随滚动永久增长。
            if (list.CachedItems.Count >= 512) list.CachedItems.Clear();
            list.CachedItems[row] = item;
            return item;
        }

        void ShowSelection(ListSection list)
        {
            using var read = _host.BeginRead();
            bool selected = list.View.SelectedIndices.Count > 0;
            foreach (var button in list.NeedSelection)
                button.Enabled = selected;
            if (!selected)
            {
                foreach (var editor in list.Editors.Values)
                    WriteEditor(editor, null);
                ApplyGates(list);
                return;
            }
            int index = SelectedListRow(list) ?? -1;
            foreach (var field in list.Section.Fields)
            {
                if (string.IsNullOrEmpty(field.Id) || !list.Editors.TryGetValue(field.Id, out var editor)) continue;
                WriteEditor(editor, ReadField(list.Section, field, index));
                RefreshName(list, field);
            }
            ApplyGates(list);
        }

        object ReadField(LayoutSection section, LayoutField field, int? index)
        {
            if (!string.IsNullOrEmpty(field.Action) && _host.CanGet(field.Action))
                return _host.CallGet(field.Action, new ScriptArgs { Index = index, CellIndex = _cell });
            if (string.IsNullOrEmpty(field.Bind)) return DefaultValue(field);
            if (field.Widget == "integers")
            {
                string path = LayoutRead.Compose(section.Bind, field.Bind, index, _cell);
                if (path == null) return null;
                return Enumerable.Range(0, _host.Count(path))
                    .Select(i => _host.ReadBind(null, path + "[" + i + "]", null, _cell, "number", null, null)).ToArray();
            }
            return _host.ReadBind(section.Bind, field.Bind, index, _cell, field.Widget, field.Shift, field.Width);
        }

        void RefreshResolved()
        {
            using var read = _host.BeginRead();
            bool previous = _loading;
            _loading = true;
            try
            {
                foreach (var holder in _bound)
                {
                    int? index = _host.ResolveIndex(holder.Section.Resolve, _cell);
                    foreach (var field in holder.Section.Fields)
                    {
                        if (string.IsNullOrEmpty(field.Id) || !holder.Editors.TryGetValue(field.Id, out var editor)) continue;
                        object value = ReadField(holder.Section, field, index);
                        WriteEditor(editor, value);
                        var form = _forms.Find(f => f.Editor == editor);
                        if (form != null) form.Shown = ReadEditor(editor);
                        RefreshName(holder, field);
                    }
                    foreach (var button in holder.NeedSelection)
                        button.Enabled = index != null;
                    ApplyGates(holder);
                }
            }
            finally { _loading = previous; }
        }

        Control CommandBar(BoundSection holder, Func<int?> indexOf)
        {
            int n = holder.Section.Commands.Count;
            if (n == 0) return null;
            var bar = new TableLayoutPanel
            {
                ColumnCount = n,
                RowCount = 1,
                Dock = DockStyle.Top,
                AutoSize = false,
                Height = 36,
                MinimumSize = new Size(0, 36),
                Margin = new Padding(0, 6, 0, 2)
            };
            bar.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
            float share = 100f / n;
            for (int i = 0; i < n; i++)
            {
                var cmd = holder.Section.Commands[i];
                bar.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, share));
                var button = new Button
                {
                    Text = cmd.Label,
                    Dock = DockStyle.Fill,
                    Margin = new Padding(3, 0, 3, 0),
                    Enabled = cmd.Enabled
                };
                Tip(button, cmd.DisableReason);
                var captured = cmd;
                button.Click += (s, e) => RunCommand(holder.Section, holder.Editors, captured, indexOf());
                bar.Controls.Add(button, i, 0);
                if (cmd.Enabled && NeedsSelection(cmd))
                    holder.NeedSelection.Add(button);
            }
            return bar;
        }

        void BindGates(BoundSection holder)
        {
            foreach (var field in holder.Section.Fields)
            {
                if (field.Widget != "check") continue;
                if (string.IsNullOrEmpty(field.Id) || !holder.Editors.TryGetValue(field.Id, out var editor)) continue;
                if (editor is CheckBox check)
                    check.CheckedChanged += (s, e) => ApplyGates(holder);
            }
            ApplyGates(holder);
        }

        static bool NeedsSelection(LayoutCommand command) => command.Id == "update" || command.Id == "delete"
            || command.Id == "move_up" || command.Id == "move_down";

        static void ApplyGates(BoundSection holder)
        {
            foreach (var field in holder.Section.Fields)
            {
                if (string.IsNullOrEmpty(field.When)) continue;
                if (string.IsNullOrEmpty(field.Id) || !holder.Editors.TryGetValue(field.Id, out var editor)) continue;
                bool on = field.Enabled;
                if (on)
                {
                    foreach (var id in field.When.Split(','))
                    {
                        string key = id.Trim();
                        if (key.Length == 0) continue;
                        if (!holder.Editors.TryGetValue(key, out var gate) || gate is not CheckBox check || !check.Checked)
                        {
                            on = false;
                            break;
                        }
                    }
                }
                editor.Enabled = on;
                if (holder.Names.TryGetValue(field.Id, out var name))
                    name.Enabled = on;
            }
        }

        void RefreshName(BoundSection holder, LayoutField field)
        {
            if (string.IsNullOrEmpty(field.Lookup) || string.IsNullOrEmpty(field.Id)) return;
            if (!holder.Names.TryGetValue(field.Id, out var label)) return;
            if (!holder.Editors.TryGetValue(field.Id, out var editor)) return;
            decimal? value = null;
            if (editor is NullableNumericUpDown box)
                value = box.NullableValue;
            label.Text = LookupText(field.Lookup, value);
        }

        static string LookupText(string kind, decimal? value)
        {
            if (value == null)
            {
                switch (kind)
                {
                    case "building": return "名: 空";
                    case "fort": return "工事名称: 空";
                    case "general": return "将领姓名: 无";
                    case "ex": return "名称: 无";
                    default: return "兵种名称: 无";
                }
            }
            int id = (int)value.Value;
            switch (kind)
            {
                case "general": return "将领姓名: " + GameSettings.GetGeneralName(id);
                case "country": return "国家: " + GameSettings.GetCountryName(id);
                case "building": return "名: " + GameSettings.GetBuildingName(id);
                case "fort": return "工事名称: " + GameSettings.GetFortName(id);
                case "ex":
                    if (GameSettings.Units.TryGetValue(id, out var exName)) return "名称: " + exName;
                    return "名称: 未知特种";
                default: return "兵种名称: " + GameSettings.GetUnitName(id);
            }
        }

        string CellText(ListSection list, int index, int column)
        {
            if (column >= list.Section.Columns.Count) return "";
            string bind = list.Section.Columns[column].Bind;
            string path = LayoutRead.Compose(list.Section.Bind, bind, index, _cell);
            if (path != null && _host.ReadNode(path) is BtlVector vec)
                return string.Join(", ", vec.V);
            object value = _host.ReadBind(list.Section.Bind, list.Section.Columns[column].Bind, index, _cell, "number", null, null);
            if (value is bool b) return b ? "是" : "否";
            return value == null ? "" : Convert.ToString(value, CultureInfo.InvariantCulture);
        }

        Dictionary<string, object> ReadInputs(Dictionary<string, Control> editors)
        {
            var input = new Dictionary<string, object>();
            foreach (var pair in editors)
                input[pair.Key] = ReadEditor(pair.Value);
            return input;
        }

        static Control CreateEditor(LayoutField field)
        {
            if (field.Widget == "check")
                return new CheckBox { Text = "", AutoSize = true, Anchor = AnchorStyles.Left };
            if (field.Widget == "text" || field.Widget == "integers")
                return new TextBox { Anchor = AnchorStyles.Left | AnchorStyles.Right, Width = 160, Tag = field.Widget };
            var box = new NullableNumericUpDown { Anchor = AnchorStyles.Left | AnchorStyles.Right, Width = 120 };
            switch (field.Type)
            {
                case "u8": box.Minimum = 0; box.Maximum = 255; break;
                case "i8": box.Minimum = -128; box.Maximum = 127; break;
                case "i16": box.Minimum = -32768; box.Maximum = 32767; break;
                case "u16": box.Minimum = 0; box.Maximum = 65535; break;
                case "u32": box.Minimum = 0; box.Maximum = 4294967295; break;
                case "f32": box.DecimalPlaces = 2; box.Minimum = -1000000; box.Maximum = 1000000; break;
                case "i32": box.Minimum = int.MinValue; box.Maximum = int.MaxValue; break;
                default: box.Minimum = 0; box.Maximum = 65535; break;
            }
            if (field.Minimum != null) box.Minimum = field.Minimum.Value;
            if (field.Maximum != null) box.Maximum = field.Maximum.Value;
            return box;
        }

        static void HookCommit(Control editor, Action commit)
        {
            if (editor is CheckBox check)
                check.CheckedChanged += (s, e) => commit();
            if (editor is TextBox text && editor is not NullableNumericUpDown)
                text.Leave += (s, e) => commit();
            if (editor is NullableNumericUpDown box)
            {
                box.Leave += (s, e) => commit();
                box.KeyDown += (s, e) =>
                {
                    if (e.KeyCode == Keys.Enter)
                    {
                        e.SuppressKeyPress = true;
                        commit();
                    }
                };
            }
        }

        static object ReadEditor(Control editor)
        {
            if (editor is CheckBox check) return check.Checked;
            if (editor is TextBox text && editor is not NullableNumericUpDown)
                return Equals(text.Tag, "integers")
                    ? text.Text.Split(new[] { ',', '，', ';', ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries)
                        .Select(s => (object)int.Parse(s, CultureInfo.InvariantCulture)).ToArray()
                    : text.Text;
            if (editor is NullableNumericUpDown box)
            {
                if (box.NullableValue == null) return null;
                decimal n = box.NullableValue.Value;
                if (n < box.Minimum || n > box.Maximum) return null;
                return (double)n;
            }
            return null;
        }

        static void WriteEditor(Control editor, object value)
        {
            if (editor is TextBox text && editor is not NullableNumericUpDown)
            {
                text.Text = value is System.Collections.IEnumerable values && value is not string
                    ? string.Join(", ", values.Cast<object>()) : value?.ToString() ?? "";
                return;
            }
            if (editor is CheckBox check)
            {
                check.Checked = value is bool b && b;
                return;
            }
            if (editor is NullableNumericUpDown box)
            {
                if (value == null) box.NullableValue = null;
                else box.NullableValue = Convert.ToDecimal(value, CultureInfo.InvariantCulture);
            }
        }

        static bool Same(object a, object b)
        {
            if (a == null && b == null) return true;
            if (a == null || b == null) return false;
            if (a is double d) return Convert.ToDouble(b, CultureInfo.InvariantCulture) == d;
            return Equals(a, b);
        }

        static TableLayoutPanel NewStack()
        {
            var stack = new TableLayoutPanel
            {
                Dock = DockStyle.Top,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                ColumnCount = 1,
                Margin = new Padding(0),
                Padding = new Padding(2, 2, 2, 4)
            };
            stack.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
            return stack;
        }

        static void StackAdd(TableLayoutPanel stack, Control child)
        {
            int row = stack.RowCount++;
            stack.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            stack.Controls.Add(child, 0, row);
        }

        static TableLayoutPanel NewTriple()
        {
            var grid = new TableLayoutPanel
            {
                Dock = DockStyle.Top,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                ColumnCount = 3,
                Padding = new Padding(6, 16, 6, 6),
                Margin = new Padding(0)
            };
            grid.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50f));
            grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50f));
            return grid;
        }

        static void AddSpan(TableLayoutPanel grid, Control control)
        {
            int row = grid.RowCount++;
            grid.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            grid.Controls.Add(control, 0, row);
            grid.SetColumnSpan(control, 3);
        }

        static Control Group(string title, Control inner)
        {
            var box = new GroupBox
            {
                Text = title ?? "",
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                Dock = DockStyle.Top,
                Margin = new Padding(0, 0, 0, 8),
                Padding = new Padding(4)
            };
            inner.Dock = DockStyle.Top;
            box.Controls.Add(inner);
            return box;
        }

        static void Tip(Control control, string text)
        {
            if (string.IsNullOrEmpty(text)) return;
            var tip = new ToolTip();
            tip.SetToolTip(control, text);
        }

        void ShowError(Exception ex)
        {
            if (ErrorOccurred != null) { ErrorOccurred(ex); return; }
            string text = ex.InnerException == null ? ex.Message : ex.Message + "\n" + ex.InnerException.Message;
            MessageBox.Show(text, "布局脚本", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }

        sealed class FormRow
        {
            public LayoutField Field;
            public Control Editor;
            public object Shown;
            public BoundSection Holder;
        }

        class BoundSection
        {
            public LayoutSection Section;
            public Dictionary<string, Control> Editors;
            public Dictionary<string, Label> Names { get; } = new Dictionary<string, Label>();
            public List<Button> NeedSelection { get; } = new List<Button>();
        }

        sealed class ListSection : BoundSection
        {
            public ListView View;
            public int? PendingIndex;
            public readonly List<int> Rows = new();
            public readonly Dictionary<int, ListViewItem> CachedItems = new();
        }
    }
}
