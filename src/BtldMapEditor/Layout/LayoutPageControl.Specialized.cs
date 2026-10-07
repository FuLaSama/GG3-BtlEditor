using System.Globalization;
using System.Text.Json.Nodes;
using BtlCore.Front;
using BtlCore.Scripting;
using BtldMapEditor.Front;

namespace BtldMapEditor
{
    // 专用画布控件也由 XML 声明创建；字段和动作仍使用同一套布局模型。
    public partial class LayoutPageControl
    {
        readonly List<TreeSection> _trees = new();
        readonly Dictionary<string, Label> _labels = new();
        readonly List<TerrainPalettePanel> _palettes = new();
        TabControl _paletteTabs;
        public PaletteTarget ActivePaletteTarget => _paletteTabs?.SelectedTab?.Tag is PaletteTarget target ? target : PaletteTarget.Main;
        public event Action<PaletteItem, PaletteTarget> PaletteSelected;
        public event Action<PaletteItem, PaletteTarget> PaletteActivated;
        public event Action<PaletteTarget> PaletteClearRequested;
        public event Action<PaletteTarget> PaletteTargetChanged;

        public void SetText(string id, string text)
        {
            if (_labels.TryGetValue(id, out var label)) label.Text = text;
        }

        public void ClearPaletteSelection()
        {
            foreach (var palette in _palettes) palette.SelectItem(null, false);
        }

        public void HighlightPalettes(MapCell cell)
        {
            foreach (var palette in _palettes)
            {
                if (cell == null) { palette.HighlightMatch(_ => false); continue; }
                if (palette.Target == PaletteTarget.Climate)
                    palette.HighlightMatch(it => it.Sea == true ? (cell.Terrain & 512) != 0
                        : it.T == (cell.Terrain & 7) && it.Variant == Math.Min((cell.Terrain >> 3) & 31, 11));
                else
                {
                    var attr = palette.Target switch
                    {
                        PaletteTarget.Main => cell.AttrA2,
                        PaletteTarget.Secondary => cell.AttrA3,
                        _ => cell.Attr
                    };
                    int id = attr?.V.Count > 0 ? Convert.ToInt32(attr.V[0]) : 0;
                    int variant = attr?.V.Count > 1 ? Convert.ToInt32(attr.V[1]) : 0;
                    palette.HighlightMatch(it => it.TerrainId == id && it.Variant == variant);
                }
            }
        }

        Control BuildLabel(LayoutSection section)
        {
            var label = new Label { Text = section.Text, AutoSize = true, MaximumSize = new Size(480, 0), Margin = new Padding(3, 6, 3, 6) };
            if (!string.IsNullOrEmpty(section.Id)) _labels[section.Id] = label;
            return label;
        }

        Control BuildPalettes(LayoutSection section)
        {
            var tabs = new TabControl { Height = 420, Dock = DockStyle.Top };
            _paletteTabs = tabs;
            foreach (var spec in section.Palettes)
            {
                var target = Enum.Parse<PaletteTarget>(spec.Target);
                var tab = new TabPage(spec.Title) { Tag = target };
                var palette = new TerrainPalettePanel(target,
                    target == PaletteTarget.Climate ? EditorPaletteCatalog.Climate : EditorPaletteCatalog.Terrain, 52, true);
                palette.ItemSelected += (item, layer) =>
                {
                    foreach (var other in _palettes) if (other != palette) other.SelectItem(null, false);
                    PaletteSelected?.Invoke(item, layer);
                };
                palette.ItemActivated += (item, layer) => PaletteActivated?.Invoke(item, layer);
                palette.ClearLayerRequested += () => PaletteClearRequested?.Invoke(target);
                _palettes.Add(palette);
                tab.Controls.Add(palette);
                tabs.TabPages.Add(tab);
            }
            tabs.SelectedIndexChanged += (_, _) => PaletteTargetChanged?.Invoke(ActivePaletteTarget);
            return tabs;
        }

        static object DefaultValue(LayoutField field)
        {
            if (string.IsNullOrEmpty(field.Default)) return null;
            if (field.Widget == "check") return bool.Parse(field.Default);
            if (field.Widget == "text" || field.Widget == "integers") return field.Default;
            return decimal.Parse(field.Default, CultureInfo.InvariantCulture);
        }

        void RefreshActionFields()
        {
            using var read = _host.BeginRead();
            bool previous = _loading;
            _loading = true;
            try
            {
                foreach (var form in _forms)
                {
                    if (string.IsNullOrEmpty(form.Field.Action) || !_host.CanGet(form.Field.Action)) continue;
                    object value = _host.CallGet(form.Field.Action, new ScriptArgs { CellIndex = _cell });
                    WriteEditor(form.Editor, value);
                    form.Shown = ReadEditor(form.Editor);
                }
            }
            finally { _loading = previous; }
        }

        Control BuildTree(LayoutSection section)
        {
            var holder = new TreeSection { Section = section, Editors = new() };
            var stack = NewStack();
            holder.List = new ListView { View = View.Details, FullRowSelect = true, MultiSelect = false, Height = 150, Dock = DockStyle.Top };
            foreach (var col in section.Columns) holder.List.Columns.Add(col.Header, 85);
            StackAdd(stack, holder.List);
            holder.Nodes = new TreeView { Height = 220, Dock = DockStyle.Top, HideSelection = false };
            StackAdd(stack, holder.Nodes);
            if (section.Groups.Count > 0)
            {
                foreach (var group in section.Groups)
                {
                    var grid = NewTriple();
                    foreach (var row in group.Rows) PlaceRow(grid, row, holder);
                    StackAdd(stack, Group(group.Title, grid));
                }
            }
            else
            {
                var grid = NewTriple();
                foreach (var field in section.Fields) PlaceRow(grid, new LayoutRow { Label = field.Label, Fields = { field } }, holder);
                StackAdd(stack, grid);
            }
            var buttons = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Top };
            foreach (var cmd in section.Commands)
            {
                var button = new Button { Text = cmd.Label, AutoSize = true, Enabled = cmd.Enabled };
                holder.Buttons.Add((cmd, button));
                button.Click += (_, _) => RunTreeCommand(holder, cmd);
                buttons.Controls.Add(button);
            }
            StackAdd(stack, buttons);
            holder.List.SelectedIndexChanged += (_, _) => { if (!_loading) FillNodes(holder); };
            holder.Nodes.AfterSelect += (_, _) => { if (!_loading) ShowTreeSelection(holder); };
            _trees.Add(holder);
            return Group(section.Title, stack);
        }

        BtlVector TreeVector(TreeSection tree) => _host.ReadNode(tree.Section.Bind) as BtlVector;
        JsonObject SelectedTree(TreeSection tree)
        {
            int i = tree.List.SelectedIndices.Count == 0 ? -1 : tree.List.SelectedIndices[0];
            var vec = TreeVector(tree);
            return vec != null && i >= 0 && i < vec.V.Count ? vec.V[i] as JsonObject : null;
        }

        void FillTreeList(TreeSection tree)
        {
            int keep = tree.List.SelectedIndices.Count == 0 ? -1 : tree.List.SelectedIndices[0];
            var nodePath = NodePath(tree.Nodes.SelectedNode);
            tree.List.Items.Clear();
            var vec = TreeVector(tree);
            if (vec != null)
            {
                foreach (var item in vec.V)
                {
                    var obj = item as JsonObject;
                    var row = new ListViewItem(obj?[tree.Section.Columns.FirstOrDefault()?.Bind ?? "id"]?.ToString() ?? "");
                    foreach (var col in tree.Section.Columns.Skip(1)) row.SubItems.Add(obj?[col.Bind]?.ToString() ?? "");
                    tree.List.Items.Add(row);
                }
            }
            if (keep >= 0 && tree.List.Items.Count > 0) tree.List.Items[Math.Min(keep, tree.List.Items.Count - 1)].Selected = true;
            FillNodes(tree);
            if (nodePath != null)
            {
                TreeNodeCollection nodes = tree.Nodes.Nodes;
                TreeNode selected = null;
                foreach (int i in nodePath)
                {
                    if (i < 0 || i >= nodes.Count) { selected = null; break; }
                    selected = nodes[i]; nodes = selected.Nodes;
                }
                tree.Nodes.SelectedNode = selected;
                selected?.EnsureVisible();
            }
            ShowTreeSelection(tree);
        }

        void FillNodes(TreeSection tree)
        {
            tree.Nodes.Nodes.Clear();
            if (SelectedTree(tree)?["node"] is JsonArray children)
                foreach (var child in children)
                    if (child is JsonObject obj) tree.Nodes.Nodes.Add(JsonTreeNode(obj));
            ShowTreeSelection(tree);
        }

        static TreeNode JsonTreeNode(JsonObject obj)
        {
            var node = new TreeNode(obj["class"]?.ToString() ?? obj["method"]?.ToString() ?? "(node)") { Tag = obj };
            if (obj["node"] is JsonArray children)
                foreach (var child in children)
                    if (child is JsonObject nested) node.Nodes.Add(JsonTreeNode(nested));
            return node;
        }

        static int[] NodePath(TreeNode node)
        {
            if (node == null) return null;
            var path = new List<int>();
            for (var current = node; current != null; current = current.Parent) path.Add(current.Index);
            path.Reverse(); return path.ToArray();
        }

        void ShowTreeSelection(TreeSection tree)
        {
            var root = SelectedTree(tree);
            var node = tree.Nodes.SelectedNode?.Tag as JsonObject;
            foreach (var field in tree.Section.Fields)
            {
                if (!tree.Editors.TryGetValue(field.Id, out var editor)) continue;
                JsonNode value = (field.Scope == "node" ? node : root)?[field.Bind];
                object shown = value is JsonArray arr ? arr.Select(v => (object)v?.ToString()).ToArray()
                    : value == null ? null : value.ToString();
                WriteEditor(editor, shown);
            }
            foreach (var (cmd, button) in tree.Buttons)
                button.Enabled = cmd.Enabled && (cmd.Id == "add" || (root != null
                    && ((cmd.Id != "add_child" && cmd.Id != "delete_node") || node != null)));
        }

        void RunTreeCommand(TreeSection tree, LayoutCommand cmd)
        {
            if (_loading || _host.Document == null || !cmd.Enabled) return;
            try
            {
                int? index = tree.List.SelectedIndices.Count == 0 ? null : tree.List.SelectedIndices[0];
                if (cmd.Id != "add" && index == null) return;
                var path = NodePath(tree.Nodes.SelectedNode);
                if ((cmd.Id == "add_child" || cmd.Id == "delete_node") && path == null) return;
                if (!string.IsNullOrEmpty(cmd.Confirm) && MessageBox.Show(cmd.Confirm, cmd.Label,
                    MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes) return;
                var input = ReadInputs(tree.Editors);
                if (path != null) input["path"] = path.Select(i => (object)i).ToArray();
                if (cmd.Id == "delete_node")
                {
                    input["index"] = path[^1];
                    input["path"] = path.SkipLast(1).Select(i => (object)i).ToArray();
                }
                _host.Run(cmd.Script, new ScriptArgs { Index = index, Input = input });
                if (cmd.Id == "add")
                {
                    bool previous = _loading;
                    _loading = true;
                    FillTreeList(tree);
                    if (tree.List.Items.Count > 0) tree.List.Items[^1].Selected = true;
                    _loading = previous;
                }
                _committed?.Invoke();
            }
            catch (Exception ex) { ShowError(ex); _committed?.Invoke(); Reload(_host.Document); }
        }

        sealed class TreeSection : BoundSection
        {
            public ListView List;
            public TreeView Nodes;
            public List<(LayoutCommand Command, Button Button)> Buttons { get; } = new();
        }
    }
}
