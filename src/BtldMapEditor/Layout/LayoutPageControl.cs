using System.Globalization;
using BtlCore.Front;
using BtlCore.Scripting;

namespace BtldMapEditor
{
    /// <summary>按一份 page XML 生成输入框和列表，提交时调用 ScriptHost。</summary>
    public class LayoutPageControl : UserControl
    {
        readonly LayoutPage _page;
        readonly ScriptHost _host;
        readonly Action _committed;
        readonly List<FormRow> _forms = new List<FormRow>();
        readonly List<ListSection> _lists = new List<ListSection>();
        bool _loading;

        public LayoutPageControl(LayoutPage page, ScriptHost host, Action committed)
        {
            _page = page;
            _host = host;
            _committed = committed;
            Dock = DockStyle.Top;
            AutoSize = true;
            AutoSizeMode = AutoSizeMode.GrowAndShrink;
            Build();
        }

        public void Reload(BtlFrontDocument doc)
        {
            if (doc == null) return;
            _host.Load(doc);
            _loading = true;
            try
            {
                foreach (var form in _forms)
                {
                    object value = form.Field.Action != null && _host.CanGet(form.Field.Action)
                        ? _host.CallGet(form.Field.Action)
                        : null;
                    form.Shown = value;
                    WriteEditor(form.Editor, value);
                }
                foreach (var list in _lists)
                    FillList(list);
            }
            finally
            {
                _loading = false;
            }
        }

        void Build()
        {
            var stack = new TableLayoutPanel
            {
                Dock = DockStyle.Top,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                ColumnCount = 1,
                Padding = new Padding(0, 4, 0, 4)
            };
            stack.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
            Controls.Add(stack);
            foreach (var section in _page.Sections)
            {
                stack.RowCount++;
                stack.RowStyles.Add(new RowStyle(SizeType.AutoSize));
                Control block = section.Kind == "list" ? BuildList(section) : BuildForm(section);
                stack.Controls.Add(block, 0, stack.RowCount - 1);
            }
        }

        Control BuildForm(LayoutSection section)
        {
            var grid = NewGrid(section.Title);
            int row = 0;
            foreach (var field in section.Fields)
            {
                grid.RowCount++;
                grid.RowStyles.Add(new RowStyle(SizeType.AutoSize));
                grid.Controls.Add(new Label { Text = field.Label, AutoSize = true, Anchor = AnchorStyles.Left }, 0, row);
                var editor = CreateEditor(field);
                editor.Enabled = field.Enabled;
                Tip(editor, field.DisableReason);
                grid.Controls.Add(editor, 1, row);
                var form = new FormRow { Field = field, Editor = editor };
                _forms.Add(form);
                if (field.Enabled && !string.IsNullOrEmpty(field.Action))
                    HookCommit(editor, () => CommitForm(form));
                row++;
            }
            return grid;
        }

        Control BuildList(LayoutSection section)
        {
            var grid = NewGrid(section.Title);
            var list = new ListView
            {
                View = View.Details,
                FullRowSelect = true,
                GridLines = true,
                Height = 140,
                Anchor = AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Top,
                HeaderStyle = ColumnHeaderStyle.Nonclickable
            };
            foreach (var col in section.Columns)
                list.Columns.Add(col.Header, 90);
            var holder = new ListSection { Section = section, View = list, Editors = new Dictionary<string, Control>() };
            int row = 0;
            grid.RowCount++;
            grid.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            grid.Controls.Add(list, 0, row);
            grid.SetColumnSpan(list, 2);
            row++;
            foreach (var field in section.Fields)
            {
                grid.RowCount++;
                grid.RowStyles.Add(new RowStyle(SizeType.AutoSize));
                grid.Controls.Add(new Label { Text = field.Label, AutoSize = true, Anchor = AnchorStyles.Left }, 0, row);
                var editor = CreateEditor(field);
                grid.Controls.Add(editor, 1, row);
                if (!string.IsNullOrEmpty(field.Id))
                    holder.Editors[field.Id] = editor;
                row++;
            }
            var buttons = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Top, WrapContents = false };
            foreach (var cmd in section.Commands)
            {
                var button = new Button { Text = cmd.Label, AutoSize = true, Enabled = cmd.Enabled };
                Tip(button, cmd.DisableReason);
                var captured = cmd;
                button.Click += (s, e) => RunCommand(holder, captured);
                buttons.Controls.Add(button);
                if (cmd.Id == "update" || cmd.Id == "delete")
                    holder.NeedSelection.Add(button);
            }
            grid.RowCount++;
            grid.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            grid.Controls.Add(buttons, 0, row);
            grid.SetColumnSpan(buttons, 2);
            list.SelectedIndexChanged += (s, e) => ShowSelection(holder);
            _lists.Add(holder);
            return grid;
        }

        void CommitForm(FormRow form)
        {
            if (_loading || _host.Document == null) return;
            object value = ReadEditor(form.Editor);
            if (Same(value, form.Shown)) return;
            try
            {
                _host.Run(form.Field.Action, new ScriptArgs { Value = value });
                form.Shown = value;
                _committed?.Invoke();
            }
            catch (Exception ex)
            {
                ShowError(ex);
                Reload(_host.Document);
            }
        }

        void RunCommand(ListSection list, LayoutCommand cmd)
        {
            if (_loading || _host.Document == null || !cmd.Enabled) return;
            int index = list.View.SelectedIndices.Count == 0 ? -1 : list.View.SelectedIndices[0];
            if ((cmd.Id == "update" || cmd.Id == "delete") && index < 0)
            {
                MessageBox.Show("请先在列表中选中一行。", cmd.Label, MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            if (!string.IsNullOrEmpty(cmd.Confirm)
                && MessageBox.Show(cmd.Confirm, cmd.Label, MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes)
                return;
            var args = new ScriptArgs
            {
                Index = index >= 0 ? index : null,
                Input = ReadInputs(list)
            };
            if (cmd.Id == "update")
            {
                args.ObjectPath = list.Section.Bind;
                args.ObjectIndex = index;
            }
            try
            {
                _host.Run(cmd.Script, args);
                _committed?.Invoke();
            }
            catch (Exception ex)
            {
                ShowError(ex);
                Reload(_host.Document);
            }
        }

        void FillList(ListSection list)
        {
            int keep = list.View.SelectedIndices.Count == 0 ? -1 : list.View.SelectedIndices[0];
            list.View.Items.Clear();
            int n = string.IsNullOrEmpty(list.Section.Bind) ? 0 : _host.Count(list.Section.Bind);
            for (int i = 0; i < n; i++)
            {
                var item = new ListViewItem(CellText(list, i, 0));
                for (int c = 1; c < list.Section.Columns.Count; c++)
                    item.SubItems.Add(CellText(list, i, c));
                list.View.Items.Add(item);
            }
            if (keep >= 0 && keep < list.View.Items.Count)
                list.View.Items[keep].Selected = true;
            else
                ShowSelection(list);
            foreach (var button in list.NeedSelection)
                button.Enabled = list.View.SelectedIndices.Count > 0;
        }

        void ShowSelection(ListSection list)
        {
            bool selected = list.View.SelectedIndices.Count > 0;
            foreach (var button in list.NeedSelection)
                button.Enabled = selected;
            if (!selected)
            {
                foreach (var editor in list.Editors.Values)
                    WriteEditor(editor, null);
                return;
            }
            int index = list.View.SelectedIndices[0];
            foreach (var field in list.Section.Fields)
            {
                if (string.IsNullOrEmpty(field.Id) || !list.Editors.TryGetValue(field.Id, out var editor)) continue;
                string bind = ColumnBind(list, field);
                object value = bind == null ? null : _host.GetAtField(list.Section.Bind, index, bind);
                WriteEditor(editor, value);
            }
        }

        static string ColumnBind(ListSection list, LayoutField field)
        {
            foreach (var col in list.Section.Columns)
            {
                if (field.Id == "type" && (col.Bind == "target_type" || col.Bind == "decal_type")) return col.Bind;
                if (field.Id == "value" && col.Bind == "target_value") return col.Bind;
                if (field.Id == "param1" && col.Bind == "param1") return col.Bind;
                if (field.Id == "param2" && col.Bind == "param2") return col.Bind;
                if (field.Id == "flag" && (col.Bind == "unk" || col.Bind == "val2")) return col.Bind;
                if (field.Id == "cell" && col.Bind == "cell_idx") return col.Bind;
                if (field.Id == "faction" && col.Bind == "faction_id") return col.Bind;
                if (field.Id == "is_key" && col.Bind == "val1") return col.Bind;
                if (field.Id == "start" && col.Bind == "x") return col.Bind;
                if (field.Id == "duration" && col.Bind == "y") return col.Bind;
            }
            return null;
        }

        string CellText(ListSection list, int index, int column)
        {
            if (column >= list.Section.Columns.Count) return "";
            object value = _host.GetAtField(list.Section.Bind, index, list.Section.Columns[column].Bind);
            if (value is bool b) return b ? "是" : "否";
            return value == null ? "" : Convert.ToString(value, CultureInfo.InvariantCulture);
        }

        Dictionary<string, object> ReadInputs(ListSection list)
        {
            var input = new Dictionary<string, object>();
            foreach (var pair in list.Editors)
                input[pair.Key] = ReadEditor(pair.Value);
            return input;
        }

        static Control CreateEditor(LayoutField field)
        {
            if (field.Widget == "check")
                return new CheckBox { Text = "", AutoSize = true, Anchor = AnchorStyles.Left };
            var box = new NullableNumericUpDown { Anchor = AnchorStyles.Left | AnchorStyles.Right, Width = 120 };
            switch (field.Type)
            {
                case "u8": box.Minimum = 0; box.Maximum = 255; break;
                case "i8": box.Minimum = -128; box.Maximum = 127; break;
                case "i16": box.Minimum = -32768; box.Maximum = 32767; break;
                case "u16": box.Minimum = 0; box.Maximum = 65535; break;
                case "i32": box.Minimum = int.MinValue; box.Maximum = int.MaxValue; break;
                default: box.Minimum = 0; box.Maximum = 65535; break;
            }
            return box;
        }

        static void HookCommit(Control editor, Action commit)
        {
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

        static TableLayoutPanel NewGrid(string title)
        {
            var grid = new TableLayoutPanel
            {
                Dock = DockStyle.Top,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                ColumnCount = 2,
                Margin = new Padding(0, 8, 0, 4)
            };
            grid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 110));
            grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
            if (!string.IsNullOrEmpty(title))
            {
                grid.RowCount = 1;
                grid.RowStyles.Add(new RowStyle(SizeType.AutoSize));
                var label = new Label
                {
                    Text = title,
                    AutoSize = true,
                    Font = new Font(SystemFonts.DefaultFont, FontStyle.Bold),
                    Margin = new Padding(0, 6, 0, 4)
                };
                grid.Controls.Add(label, 0, 0);
                grid.SetColumnSpan(label, 2);
            }
            return grid;
        }

        static void Tip(Control control, string text)
        {
            if (string.IsNullOrEmpty(text)) return;
            var tip = new ToolTip();
            tip.SetToolTip(control, text);
        }

        static void ShowError(Exception ex)
        {
            string text = ex.InnerException == null ? ex.Message : ex.Message + "\n" + ex.InnerException.Message;
            MessageBox.Show(text, "布局脚本", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }

        sealed class FormRow
        {
            public LayoutField Field;
            public Control Editor;
            public object Shown;
        }

        sealed class ListSection
        {
            public LayoutSection Section;
            public ListView View;
            public Dictionary<string, Control> Editors;
            public List<Button> NeedSelection { get; } = new List<Button>();
        }
    }
}
