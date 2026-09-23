/*
 * FrontRegistryViewer.cs
 *
 * 仿 Windows 注册表的 BtlFront 钻探视图：左边树、右边字段列表。
 * 直接绑 Document 的 BtlNode，不读二进制、不做反汇编。
 *
 * 节点种类来自 Front 自己的 t/elem（table / vector / struct / u16…），不靠 fbs 也能显示。
 * 字段名（map_terrain、Faction…）要等用户点「加载 .fbs」之后才套上，而且只显示行尾中文注释。
 * 读 BTL 用的 SoftSchema 仍会自己找 battle.fbs，和这边的显示开关无关。
 * 操作尽量跟 regedit 一样：左边单击只选中并刷新右侧，双击或 +/- 才展开；
 * 右边双击/回车：能进的项（table/vector/struct）就进入，标量则弹出修改；Backspace 回上一级。
 * 右键增删尾部字段/向量元素。改值都进 FrontEdit，触发 DataChanged 后主窗体应 RebuildCells。
 *
 * 缺省槽（vtable 未写出）和写出的 0 不是一回事：
 *   标量 / bool 缺省 → 类型仍是 u16/bool，内容显示 0/false（灰字 = 未写出，游戏读取为默认 0）
 *   表 / 向量 / 字符串缺省 → 才是真正的 NULL
 *   Front 里已经存在的 0 用正常颜色显示，不要再标成 null。
 */
using System.Globalization;
using BtlCore.Fb;
using BtlCore.Front;

namespace BtldMapEditor.Front
{
    public class FrontRegistryViewer : UserControl
    {
        TextBox _addressBar;
        Button _btnLoadFbs;
        Label _lblFbsStatus;
        SplitContainer _split;
        TreeView _tree;
        ListView _list;
        ImageList _icons;

        BtlFrontDocument _doc;
        FbsSchema _fbs;
        TreeNode _shown;
        bool _internalSelect;
        bool _loaded;
        bool _dirty;
        bool _treeBuilt;

        TextBox _inlineEdit;
        ListViewItem _editingItem;
        bool _committing;

        /// <summary>当前绑定的 BtlFront 文档（就地修改）。</summary>
        public BtlFrontDocument Document => _doc;

        public event EventHandler DataChanged;
        public event Action UndoRequested;
        public event Action RedoRequested;

        public FrontRegistryViewer()
        {
            InitializeComponent();
        }

        void InitializeComponent()
        {
            Dock = DockStyle.Fill;
            _icons = CreateRegistryImageList();

            var top = new Panel
            {
                Dock = DockStyle.Top,
                Height = 32,
                Padding = new Padding(6, 4, 6, 4),
                BackColor = Color.FromArgb(240, 240, 240)
            };

            _btnLoadFbs = new Button
            {
                Text = "加载 .fbs 文件",
                Dock = DockStyle.Right,
                Width = 115,
                Font = new Font("Segoe UI", 8.5f)
            };
            _btnLoadFbs.Click += BtnLoadFbs_Click;

            _lblFbsStatus = new Label
            {
                Text = "(未载入 fbs · 仅 id)",
                Dock = DockStyle.Right,
                AutoSize = true,
                TextAlign = ContentAlignment.MiddleCenter,
                ForeColor = Color.Gray,
                Font = new Font("Segoe UI", 8.5f),
                Padding = new Padding(4, 6, 8, 0)
            };

            _addressBar = new TextBox
            {
                Dock = DockStyle.Fill,
                Font = new Font("Segoe UI", 9.5f)
            };
            _addressBar.KeyDown += AddressBar_KeyDown;

            // Dock 顺序：先 Fill 再 Right，Right 会挤在右侧
            top.Controls.Add(_addressBar);
            top.Controls.Add(_lblFbsStatus);
            top.Controls.Add(_btnLoadFbs);

            _split = new SplitContainer
            {
                Dock = DockStyle.Fill,
                Orientation = Orientation.Vertical,
                SplitterDistance = 240
            };

            _tree = new TreeView
            {
                Dock = DockStyle.Fill,
                HideSelection = false,
                ShowLines = true,
                ShowPlusMinus = true,
                FullRowSelect = true,
                ImageList = _icons,
                Font = new Font("Segoe UI", 9f)
            };
            // 单击 = 选中并显示右侧（AfterSelect）。展开/折叠只走 +/- 或双击，不要在选中时自动展开。
            _tree.AfterSelect += Tree_AfterSelect;
            _tree.BeforeExpand += Tree_BeforeExpand;
            _tree.AfterExpand += (s, e) => { e.Node.ImageIndex = 1; e.Node.SelectedImageIndex = 1; };
            _tree.AfterCollapse += (s, e) => { e.Node.ImageIndex = 0; e.Node.SelectedImageIndex = 0; };
            _tree.KeyDown += Tree_KeyDown;
            _split.Panel1.Controls.Add(_tree);

            _list = new ListView
            {
                Dock = DockStyle.Fill,
                View = View.Details,
                FullRowSelect = true,
                GridLines = true,
                HideSelection = false,
                SmallImageList = _icons,
                Font = new Font("Segoe UI", 9f),
                Activation = ItemActivation.Standard,
                ShowItemToolTips = true
            };
            _list.Columns.Add("序号", 60);
            _list.Columns.Add("名称", 200);
            _list.Columns.Add("类型", 110);
            _list.Columns.Add("内容", 400);
            _list.ItemActivate += List_ItemActivate;
            _list.MouseUp += List_MouseUp;
            _split.Panel2.Controls.Add(_list);

            InitInlineEditor();
            TryDoubleBuffer(_list);
            TryDoubleBuffer(_tree);

            Controls.Add(_split);
            Controls.Add(top);
        }

        static void TryDoubleBuffer(Control c)
        {
            try
            {
                typeof(Control).GetProperty("DoubleBuffered",
                        System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
                    ?.SetValue(c, true, null);
            }
            catch { /* ignore */ }
        }

        protected override void OnLoad(EventArgs e)
        {
            base.OnLoad(e);
            if (_split != null && Width > 400)
                _split.SplitterDistance = 240;
        }

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            if (keyData == (Keys.Control | Keys.Z))
            {
                CommitInlineEdit();
                UndoRequested?.Invoke();
                return true;
            }
            if (keyData == (Keys.Control | Keys.Y) || keyData == (Keys.Control | Keys.Shift | Keys.Z))
            {
                CommitInlineEdit();
                RedoRequested?.Invoke();
                return true;
            }
            if (keyData == Keys.Back)
            {
                if (_inlineEdit != null && _inlineEdit.Visible) return false;
                if (_addressBar != null && _addressBar.Focused) return false;
                return GoParentKey();
            }
            if (keyData == Keys.Delete)
            {
                if (_inlineEdit != null && _inlineEdit.Visible) return false;
                if (_addressBar != null && _addressBar.Focused) return false;
                return TryDeleteSelectedListItem();
            }
            return base.ProcessCmdKey(ref msg, keyData);
        }

        #region Public API

        /// <summary>绑定一份 Document 并重建树。collapseAll=true 时收起全部（新文件）；false 尽量保留展开。</summary>
        public void LoadDocument(BtlFrontDocument doc, bool collapseAll = true)
        {
            if (!collapseAll && !_dirty && _treeBuilt && ReferenceEquals(_doc, doc))
                return;

            _doc = doc;
            _loaded = doc?.Root != null;
            RefreshTree(collapseAll);
            _treeBuilt = true;
            _dirty = false;
        }

        /// <summary>地图侧改过 Document 后调用；下次 Refresh / 切入时会重建。</summary>
        public void NotifyExternalDataChanged()
        {
            _dirty = true;
        }

        /// <summary>按当前 Document 重建树。尽量保住展开路径和选中项。</summary>
        public void RefreshTree(bool collapseAll = false)
        {
            var expanded = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            string selectedPath = null;

            if (!collapseAll)
            {
                SaveExpanded(_tree.Nodes, expanded);
                if (_tree.SelectedNode?.Tag is NodeTag sel)
                    selectedPath = sel.Path;
            }

            _tree.BeginUpdate();
            try
            {
                _tree.Nodes.Clear();
                _list.Items.Clear();
                _addressBar.Text = "/";
                _shown = null;

                if (_doc?.Root == null)
                    return;

                var root = MakeTreeNode(RootNodeTitle(), "/", _doc.Root, isFolder: true, RootSchemaType());
                _tree.Nodes.Add(root);
                BuildChildren(root, _doc.Root, "/", RootSchemaType());
                root.Expand();

                if (!collapseAll && expanded.Count > 0)
                    RestoreExpanded(_tree.Nodes, expanded);

                if (!string.IsNullOrEmpty(selectedPath))
                {
                    var n = FindByPath(_tree.Nodes, selectedPath);
                    if (n != null)
                    {
                        _internalSelect = true;
                        _tree.SelectedNode = n;
                        _internalSelect = false;
                        ShowNode(n);
                    }
                }
                else
                {
                    _tree.SelectedNode = root;
                    ShowNode(root);
                }
            }
            finally
            {
                _tree.EndUpdate();
            }
        }

        /// <summary>
        /// 双击格子跳转：Terrain→/1/2；Unit→/6/0 里 cellIdx 匹配的 agent；Building→/5/0。
        /// </summary>
        public bool NavigateToCellData(string editMode, int cellIdx)
        {
            if (_tree.Nodes.Count == 0 || _doc?.Root == null) return false;

            string path = null;
            if (string.Equals(editMode, "Terrain", StringComparison.OrdinalIgnoreCase))
            {
                path = "/1/2";
            }
            else if (string.Equals(editMode, "Unit", StringComparison.OrdinalIgnoreCase))
            {
                path = FindAgentPath(cellIdx);
            }
            else if (string.Equals(editMode, "Building", StringComparison.OrdinalIgnoreCase))
            {
                path = FindTriggerPath(cellIdx);
            }
            else if (string.Equals(editMode, "Reinforce", StringComparison.OrdinalIgnoreCase))
            {
                path = FindAgentPath(65535);
            }

            if (string.IsNullOrEmpty(path)) return false;
            return NavigateToPath(path);
        }

        public bool NavigateToPath(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) return false;
            path = NormalizePath(path);
            var node = FindByPath(_tree.Nodes, path);
            if (node == null)
            {
                // 路径对应节点可能尚未展开构建：从 Root 按段展开
                node = EnsurePathBuilt(path);
            }
            if (node == null) return false;

            _tree.SelectedNode = node;
            node.EnsureVisible();
            _tree.Focus();
            return true;
        }

        public void CommitInlineEdit()
        {
            if (_committing || _editingItem == null || !_inlineEdit.Visible) return;
            _committing = true;
            try
            {
                string newText = _inlineEdit.Text.Trim();
                string oldText = _editingItem.SubItems.Count > 3 ? _editingItem.SubItems[3].Text : "";
                if (newText != oldText && _editingItem.Tag is ValueTag vt)
                    ApplyScalarEdit(vt, newText, _editingItem);
            }
            finally
            {
                _inlineEdit.Visible = false;
                _editingItem = null;
                _committing = false;
            }
        }

        #endregion

        #region Tree build

        sealed class NodeTag
        {
            /// <summary>地址栏路径，例如 /1/2/i3。</summary>
            public string Path;
            /// <summary>BtlTable / BtlVector / BtlStruct，就是 Document 上的那个对象。</summary>
            public object Target;
            public string Kind;   // table / vector / struct
            /// <summary>加载 fbs 后才有：当前节点的 schema 类型名。未加载时为空，显示只靠 Front 的 t。</summary>
            public string SchemaType;
        }

        sealed class ValueTag
        {
            public string Path;
            public object Container; // BtlTable | BtlVector | BtlStruct
            public int Key;          // field id / index
            public string Type;
            public bool IsScalar;
            public bool IsAbsent;    // table 上缺省槽（vtable 未写出；标量显示默认 0，指针显示 NULL）
            public BtlScalar Scalar; // when value is BtlScalar node
            public string SchemaType; // 父表/结构类型，用于命名
        }

        TreeNode MakeTreeNode(string text, string path, object target, bool isFolder, string schemaType)
        {
            return new TreeNode(text)
            {
                Tag = new NodeTag
                {
                    Path = path,
                    Target = target,
                    Kind = KindOf(target),
                    SchemaType = schemaType ?? ""
                },
                ImageIndex = isFolder ? 0 : 3,
                SelectedImageIndex = isFolder ? 0 : 3
            };
        }

        static string KindOf(object o)
        {
            if (o is BtlTable) return "table";
            if (o is BtlVector) return "vector";
            if (o is BtlStruct) return "struct";
            return "unknown";
        }

        void BuildChildren(TreeNode parent, object target, string parentPath, string parentSchemaType)
        {
            parent.Nodes.Clear();
            if (target is BtlTable tbl)
            {
                foreach (var kv in tbl.F.OrderBy(x => x.Key))
                {
                    if (!IsNavigableNode(kv.Value)) continue;
                    string childPath = JoinPath(parentPath, IdLabel(kv.Key));
                    string childSchema = ResolveChildSchemaType(parentSchemaType, kv.Key, kv.Value);
                    string title = FieldDisplayName(parentSchemaType, kv.Key);
                    var child = MakeTreeNode(title, childPath, kv.Value, isFolder: true, childSchema);
                    if (HasNavigableChildren(kv.Value))
                        child.Nodes.Add(new TreeNode("…"));
                    parent.Nodes.Add(child);
                }
            }
            else if (target is BtlVector vec)
            {
                // parentSchemaType 对向量节点表示元素类型（如 AIAgent）
                string elemSchema = parentSchemaType ?? "";
                for (int i = 0; i < vec.V.Count; i++)
                {
                    object item = vec.V[i];
                    if (!IsNavigableLoose(item)) continue;
                    string childPath = JoinPath(parentPath, "i" + IdLabel(i));
                    string title = VectorItemTitle(i, elemSchema);
                    var child = MakeTreeNode(title, childPath, item, isFolder: true, elemSchema);
                    if (HasNavigableChildren(item))
                        child.Nodes.Add(new TreeNode("…"));
                    parent.Nodes.Add(child);
                }
            }
            else if (target is BtlStruct st)
            {
                // struct：子节点较少钻探；成员主要在右侧列表显示
                for (int i = 0; i < st.V.Count; i++)
                {
                    object m = st.V[i];
                    if (!IsNavigableLoose(m)) continue;
                    string childPath = JoinPath(parentPath, IdLabel(i));
                    string title = StructMemberDisplayName(parentSchemaType, i);
                    string childSchema = ResolveStructMemberChildType(parentSchemaType, i);
                    var child = MakeTreeNode(title, childPath, m, isFolder: true, childSchema);
                    if (HasNavigableChildren(m))
                        child.Nodes.Add(new TreeNode("…"));
                    parent.Nodes.Add(child);
                }
            }
        }

        /// <summary>显示名：有 fbs 时用字段名，否则数字 id。</summary>
        static string IdLabel(int id) => id.ToString(CultureInfo.InvariantCulture);

        static bool IsNavigableNode(BtlNode n) =>
            n is BtlTable || n is BtlVector || n is BtlStruct;

        static bool IsNavigableLoose(object o) =>
            o is BtlTable || o is BtlVector || o is BtlStruct;

        static bool HasNavigableChildren(object o)
        {
            if (o is BtlTable t) return t.F.Values.Any(IsNavigableNode);
            if (o is BtlVector v) return v.V.Any(IsNavigableLoose);
            return false;
        }

        void Tree_BeforeExpand(object sender, TreeViewCancelEventArgs e)
        {
            if (e.Node?.Tag is not NodeTag tag) return;
            if (e.Node.Nodes.Count == 1 && e.Node.Nodes[0].Tag == null && e.Node.Nodes[0].Text == "…")
                BuildChildren(e.Node, tag.Target, tag.Path, tag.SchemaType);
        }

        void Tree_AfterSelect(object sender, TreeViewEventArgs e)
        {
            if (_internalSelect) return;
            CommitInlineEdit();
            ShowNode(e.Node);
        }

        void ShowNode(TreeNode node)
        {
            _shown = node;
            _list.BeginUpdate();
            try
            {
                _list.Items.Clear();
                if (node?.Tag is not NodeTag tag)
                {
                    _addressBar.Text = "";
                    return;
                }

                _addressBar.Text = tag.Path;
                FillList(tag);
            }
            finally
            {
                _list.EndUpdate();
            }
        }

        void FillList(NodeTag tag)
        {
            string schemaType = tag.SchemaType ?? "";
            if (tag.Target is BtlTable tbl)
            {
                int maxId = VisibleTableMaxFieldId(tbl, schemaType);
                for (int id = 0; id <= maxId; id++)
                {
                    string path = JoinPath(tag.Path, IdLabel(id));
                    string name = FieldDisplayName(schemaType, id);
                    if (tbl.F.TryGetValue(id, out BtlNode node) && node != null)
                        AddFieldRow(id, name, node, path, tbl, id, schemaType);
                    else
                        AddAbsentFieldRow(id, name, path, tbl, schemaType);
                }
            }
            else if (tag.Target is BtlVector vec)
            {
                string elem = vec.Elem ?? "?";
                if (!string.IsNullOrEmpty(vec.Enc))
                    elem += " enc=" + vec.Enc;
                for (int i = 0; i < vec.V.Count; i++)
                {
                    object item = vec.V[i];
                    string path = JoinPath(tag.Path, "i" + IdLabel(i));
                    string name = VectorItemTitle(i, schemaType);
                    if (item is BtlNode bn)
                        AddFieldRow(i, name, bn, path, vec, i, schemaType);
                    else
                        AddLooseScalarRow(i, name, elem, item, path, vec, i, schemaType);
                }
            }
            else if (tag.Target is BtlStruct st)
            {
                for (int i = 0; i < st.V.Count; i++)
                {
                    object m = st.V[i];
                    string path = JoinPath(tag.Path, IdLabel(i));
                    string name = StructMemberDisplayName(schemaType, i);
                    if (m is BtlNode bn)
                        AddFieldRow(i, name, bn, path, st, i, schemaType);
                    else
                        AddLooseScalarRow(i, name, GuessLooseType(m), m, path, st, i, schemaType);
                }
            }
        }

        void AddFieldRow(int index, string name, BtlNode node, string path, object container, int key, string schemaType)
        {
            if (node == null)
            {
                AddAbsentFieldRow(key, name, path, container as BtlTable, schemaType);
                return;
            }

            if (node is BtlScalar sc)
            {
                object shown = sc.V;
                if (shown == null && !IsStringType(sc.T))
                    shown = SoftSchema.DefaultValue(sc.T ?? "u16");
                var item = new ListViewItem(new[]
                {
                    index.ToString(CultureInfo.InvariantCulture),
                    name,
                    sc.T ?? "scalar",
                    FormatValue(shown)
                })
                {
                    ImageIndex = IsStringType(sc.T) ? 2 : 3,
                    Tag = new ValueTag
                    {
                        Path = path,
                        Container = container,
                        Key = key,
                        Type = sc.T ?? "scalar",
                        IsScalar = true,
                        Scalar = sc,
                        SchemaType = schemaType
                    }
                };
                _list.Items.Add(item);
                return;
            }

            string ownSchema = OwnSchemaType(container, key, node, schemaType);
            string type = TypeLabel(node, ownSchema);
            string content = Summarize(node);
            var folder = new ListViewItem(new[]
            {
                index.ToString(CultureInfo.InvariantCulture),
                name,
                type,
                content
            })
            {
                ImageIndex = 0,
                Tag = new ValueTag
                {
                    Path = path,
                    Container = container,
                    Key = key,
                    Type = type,
                    IsScalar = false,
                    SchemaType = schemaType
                }
            };
            _list.Items.Add(folder);
        }

        void AddLooseScalarRow(int index, string name, string type, object value, string path, object container, int key, string schemaType)
        {
            var item = new ListViewItem(new[]
            {
                index.ToString(CultureInfo.InvariantCulture),
                name,
                type,
                FormatValue(value)
            })
            {
                ImageIndex = string.Equals(type, "string", StringComparison.OrdinalIgnoreCase) ? 2 : 3,
                Tag = new ValueTag
                {
                    Path = path,
                    Container = container,
                    Key = key,
                    Type = type,
                    IsScalar = true,
                    Scalar = null,
                    SchemaType = schemaType
                }
            };
            _list.Items.Add(item);
        }

        static string Summarize(BtlNode node)
        {
            if (node is BtlTable t) return "fields=" + t.F.Count;
            if (node is BtlVector v)
            {
                string s = "len=" + v.V.Count + " elem=" + (v.Elem ?? "?");
                if (!string.IsNullOrEmpty(v.Enc)) s += " enc=" + v.Enc;
                return s;
            }
            if (node is BtlStruct st) return "members=" + st.V.Count;
            return "";
        }

        static string FormatValue(object v)
        {
            if (v == null) return "";
            if (v is bool b) return b ? "true" : "false";
            if (v is IFormattable f) return f.ToString(null, CultureInfo.InvariantCulture);
            return Convert.ToString(v, CultureInfo.InvariantCulture) ?? "";
        }

        static string GuessLooseType(object v)
        {
            return v switch
            {
                null => "null",
                bool => "bool",
                string => "string",
                float or double => "f64",
                byte => "u8",
                sbyte => "i8",
                ushort => "u16",
                short => "i16",
                uint => "u32",
                int => "i32",
                ulong => "u64",
                long => "i64",
                _ => "scalar"
            };
        }

        static bool IsStringType(string t) =>
            string.Equals(t, "string", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(t, "str", StringComparison.OrdinalIgnoreCase);

        static bool IsNavigableTypeName(string type)
        {
            if (string.IsNullOrEmpty(type)) return false;
            string t = type.Trim();
            int cut = t.IndexOfAny(new[] { '<', '(' });
            if (cut > 0) t = t.Substring(0, cut).Trim();
            return t.Equals("table", StringComparison.OrdinalIgnoreCase)
                || t.Equals("vector", StringComparison.OrdinalIgnoreCase)
                || t.Equals("struct", StringComparison.OrdinalIgnoreCase);
        }

        static bool CanOpenValue(ValueTag vt) =>
            vt != null && !vt.IsAbsent && !vt.IsScalar && IsNavigableTypeName(vt.Type);

        #endregion

        #region Inline edit / context menu

        static readonly string[] FrontScalarTypes =
        {
            "u8", "i8", "u16", "i16", "u32", "i32", "u64", "i64",
            "f32", "f64", "bool", "string", "table", "vector", "struct"
        };

        void InitInlineEditor()
        {
            _inlineEdit = new TextBox
            {
                Visible = false,
                BorderStyle = BorderStyle.FixedSingle,
                Font = new Font("Segoe UI", 9f)
            };
            _inlineEdit.KeyDown += (s, e) =>
            {
                if (e.KeyCode == Keys.Enter)
                {
                    e.SuppressKeyPress = true;
                    CommitInlineEdit();
                }
                else if (e.KeyCode == Keys.Escape)
                {
                    e.SuppressKeyPress = true;
                    _inlineEdit.Visible = false;
                    _editingItem = null;
                }
            };
            _inlineEdit.LostFocus += (s, e) => CommitInlineEdit();
            _list.Controls.Add(_inlineEdit);
        }

        void List_ItemActivate(object sender, EventArgs e)
        {
            if (_list.SelectedItems.Count == 0) return;
            OpenOrEditListItem(_list.SelectedItems[0]);
        }

        /// <summary>右边：能进的项进入子键；标量弹出修改；空槽则激活。跟 regedit 双击/回车一致。</summary>
        void OpenOrEditListItem(ListViewItem item)
        {
            if (item?.Tag is not ValueTag vt || _shown == null) return;
            if (vt.IsAbsent)
            {
                if (vt.IsScalar)
                    StartItemEditing(item);
                else
                    ActivateAbsentField(item);
                return;
            }
            if (CanOpenValue(vt))
            {
                NavigateToPath(vt.Path);
                return;
            }
            if (vt.IsScalar)
                StartItemEditing(item);
        }

        void Tree_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeyCode != Keys.Enter || _tree.SelectedNode == null) return;
            e.Handled = true;
            e.SuppressKeyPress = true;
            if (_tree.SelectedNode.Nodes.Count > 0)
                _tree.SelectedNode.Toggle();
        }

        bool GoParentKey()
        {
            string path = null;
            if (_shown?.Tag is NodeTag tag)
                path = tag.Path;
            else if (_tree.SelectedNode?.Tag is NodeTag sel)
                path = sel.Path;
            path = NormalizePath(path);
            if (string.IsNullOrEmpty(path) || path == "/") return false;
            int slash = path.LastIndexOf('/');
            string parent = slash <= 0 ? "/" : path.Substring(0, slash);
            return NavigateToPath(parent);
        }

        bool TryDeleteSelectedListItem()
        {
            if (!_list.Focused || _list.SelectedItems.Count == 0) return false;
            if (_list.SelectedItems[0].Tag is not ValueTag vt) return false;
            if (vt.IsAbsent) return false;
            if (vt.Container is BtlTable)
            {
                int maxId = GetTableMaxFieldId(vt.Container as BtlTable);
                if (vt.Key == maxId)
                    PromptDeleteTableField(vt);
                else
                    ClearTableField(vt);
                return true;
            }
            if (vt.Container is BtlVector vec)
            {
                PromptDeleteVectorItem(vec, vt.Key);
                return true;
            }
            return false;
        }

        void List_MouseUp(object sender, MouseEventArgs e)
        {
            if (e.Button != MouseButtons.Right) return;
            if (_shown?.Tag is not NodeTag) return;

            var hit = _list.HitTest(e.Location);
            var cms = new ContextMenuStrip();

            if (hit.Item != null && hit.Item.Tag is ValueTag vt)
            {
                string fieldName = hit.Item.SubItems.Count > 1 ? hit.Item.SubItems[1].Text : IdLabel(vt.Key);
                bool isTable = vt.Container is BtlTable;
                bool isVector = vt.Container is BtlVector;
                bool isStruct = vt.Container is BtlStruct;

                if (isTable)
                {
                    var tbl = (BtlTable)vt.Container;
                    int maxId = GetTableMaxFieldId(tbl);
                    bool isMax = vt.Key == maxId && maxId >= 0;

                    if (vt.IsAbsent)
                    {
                        var act = new ToolStripMenuItem(vt.IsScalar
                            ? $"编辑并写出字段 ({fieldName})"
                            : $"激活/编辑字段 ({fieldName})");
                        act.Click += (s, a) =>
                        {
                            if (vt.IsScalar) StartItemEditing(hit.Item);
                            else ActivateAbsentField(hit.Item);
                        };
                        cms.Items.Add(act);
                        if (isMax)
                        {
                            var del = new ToolStripMenuItem($"彻底删除尾部字段 ({fieldName})");
                            del.Click += (s, a) => PromptDeleteTableField(vt);
                            cms.Items.Add(del);
                        }
                    }
                    else
                    {
                        var edit = new ToolStripMenuItem($"编辑字段内容 ({fieldName})");
                        edit.Click += (s, a) => StartItemEditing(hit.Item);
                        cms.Items.Add(edit);

                        var clear = new ToolStripMenuItem("清空此字段（恢复缺省 / 指针则为 NULL）");
                        clear.Click += (s, a) => ClearTableField(vt);
                        cms.Items.Add(clear);

                        if (isMax)
                        {
                            var del = new ToolStripMenuItem($"彻底删除尾部字段 ({fieldName})");
                            del.Click += (s, a) => PromptDeleteTableField(vt);
                            cms.Items.Add(del);
                        }
                    }
                }
                else if (isVector)
                {
                    var vec = (BtlVector)vt.Container;
                    var edit = new ToolStripMenuItem("编辑元素内容");
                    edit.Click += (s, a) => StartItemEditing(hit.Item);
                    cms.Items.Add(edit);

                    if (vt.Key == vec.V.Count - 1 && vec.V.Count > 0)
                    {
                        var del = new ToolStripMenuItem($"删除尾部数组元素 [索引 #{vt.Key}]");
                        del.Click += (s, a) => PromptDeleteVectorItem(vec, vt.Key);
                        cms.Items.Add(del);
                    }
                }
                else if (isStruct)
                {
                    var st = (BtlStruct)vt.Container;
                    var edit = new ToolStripMenuItem("编辑成员内容");
                    edit.Click += (s, a) => StartItemEditing(hit.Item);
                    cms.Items.Add(edit);
                    if (vt.Key == st.V.Count - 1 && st.V.Count > 0)
                    {
                        var del = new ToolStripMenuItem($"删除尾部成员 [索引 #{vt.Key}]");
                        del.Click += (s, a) =>
                        {
                            if (MessageBox.Show($"确定删除 struct 尾部成员 #{vt.Key}？", "确认删除",
                                    MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes)
                                return;
                            if (!TryFrontEdit(() => FrontEdit.RemoveStructTail(st, vt.Key))) return;
                            NotifyChanged(refreshTree: false);
                        };
                        cms.Items.Add(del);
                    }
                }

                cms.Items.Add(new ToolStripSeparator());
            }

            var add = new ToolStripMenuItem("新建扩展字段 / 数组元素...");
            add.Click += (s, a) => PromptAddNewField();
            cms.Items.Add(add);
            cms.Show(_list, e.Location);
        }

        void StartInlineEdit(ListViewItem item, Rectangle bounds)
        {
            CommitInlineEdit();
            _editingItem = item;
            _inlineEdit.Bounds = bounds;
            _inlineEdit.Text = item.SubItems.Count > 3 ? item.SubItems[3].Text : "";
            _inlineEdit.Visible = true;
            _inlineEdit.BringToFront();
            _inlineEdit.Focus();
            _inlineEdit.SelectAll();
        }

        void StartItemEditing(ListViewItem item)
        {
            if (item?.Tag is not ValueTag vt) return;
            string fieldName = item.SubItems.Count > 1 ? item.SubItems[1].Text : IdLabel(vt.Key);
            string regType = item.SubItems.Count > 2 ? item.SubItems[2].Text : vt.Type;
            string currentValue = item.SubItems.Count > 3 ? item.SubItems[3].Text : "";

            if (IsNavigableTypeName(regType))
            {
                NavigateToPath(vt.Path);
                return;
            }

            string newValueStr = ShowEditDialog(fieldName, regType, currentValue);
            if (newValueStr != null && (vt.IsAbsent || newValueStr != currentValue))
                ApplyScalarEdit(vt, newValueStr, item);
        }

        void ActivateAbsentField(ListViewItem item)
        {
            if (item?.Tag is not ValueTag vt || vt.Container is not BtlTable tbl) return;
            string parentSchema = _shown?.Tag is NodeTag nt ? nt.SchemaType : "";
            var created = ShowAddFieldDialog(tbl, vt.Key, lockId: true,
                SuggestedPickerType(parentSchema, vt.Key), SuggestedVectorElem(parentSchema, vt.Key));
            if (created == null) return;
            if (!TryFrontEdit(() => FrontEdit.SetField(tbl, created.Value.fieldId, created.Value.node))) return;
            NotifyChanged(refreshTree: IsNavigableNode(created.Value.node));
        }

        void ClearTableField(ValueTag vt)
        {
            if (vt.Container is not BtlTable tbl) return;
            string name = IdLabel(vt.Key);
            if (MessageBox.Show($"确定要将字段 [{name}] (ID: {vt.Key}) 恢复为缺省吗？\n标量会从文件里拿掉（读取仍为 0）；表/向量/字符串会变成 NULL。", "确认清空",
                    MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
                return;
            if (!TryFrontEdit(() => FrontEdit.SetField(tbl, vt.Key, null))) return;
            NotifyChanged(refreshTree: true);
        }

        void PromptDeleteTableField(ValueTag vt)
        {
            if (vt.Container is not BtlTable tbl) return;
            int maxId = GetTableMaxFieldId(tbl);
            if (vt.Key != maxId)
            {
                MessageBox.Show("只能删除尾部（最大 field id）字段。中间字段请用「清空」。",
                    "无法删除", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            string name = IdLabel(vt.Key);
            if (MessageBox.Show(
                    $"确定要彻底删除尾部字段 [{name}] (Field ID: {vt.Key}) 吗？\n删除后该 id 不再出现在表中。",
                    "确认删除字段", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes)
                return;
            if (!TryFrontEdit(() => FrontEdit.SetField(tbl, vt.Key, null))) return;
            NotifyChanged(refreshTree: true);
        }

        void PromptDeleteVectorItem(BtlVector vec, int idx)
        {
            if (idx < 0 || idx >= vec.V.Count) return;
            if (MessageBox.Show($"确定要从数组 Vector 中彻底删除索引为 #{idx} 的元素吗？",
                    "确认删除数组元素", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes)
                return;
            if (!TryFrontEdit(() => FrontEdit.RemoveAt(vec, idx))) return;
            NotifyChanged(refreshTree: true);
        }

        void PromptAddNewField()
        {
            if (_shown?.Tag is not NodeTag tag) return;

            if (tag.Target is BtlTable tbl)
            {
                int nextId = VisibleTableMaxFieldId(tbl, tag.SchemaType) + 1;
                if (nextId > 512)
                {
                    MessageBox.Show("字段 ID 已达到上限 512。", "新建字段", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return;
                }
                var created = ShowAddFieldDialog(tbl, nextId, lockId: false,
                    SuggestedPickerType(tag.SchemaType, nextId), SuggestedVectorElem(tag.SchemaType, nextId));
                if (created == null) return;
                if (!TryFrontEdit(() => FrontEdit.SetField(tbl, created.Value.fieldId, created.Value.node))) return;
                NotifyChanged(refreshTree: true);
            }
            else if (tag.Target is BtlVector vec)
            {
                if (PackedJsonStream.EncIsPackedJson(vec.Enc))
                {
                    MessageBox.Show("这是国家行为树的打包数据，请到「国家AI」页编辑，不要在这里当普通数组追加。",
                        "新建元素", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return;
                }
                object elem = ShowAddVectorItemDialog(vec);
                if (elem == null) return;
                object stored = elem is BtlNode || IsScalarLoose(elem) ? elem : 0;
                if (!TryFrontEdit(() => FrontEdit.Insert(vec, vec.V.Count, stored))) return;
                if (elem is BtlTable && (string.IsNullOrEmpty(vec.Elem) || vec.Elem == "unknown"))
                    vec.Elem = "table";
                else if (elem is BtlStruct && (string.IsNullOrEmpty(vec.Elem) || vec.Elem == "unknown"))
                    vec.Elem = "struct";
                else if (elem is BtlVector && (string.IsNullOrEmpty(vec.Elem) || vec.Elem == "unknown"))
                    vec.Elem = "vector";
                else if (elem is string && (string.IsNullOrEmpty(vec.Elem) || vec.Elem == "unknown"))
                    vec.Elem = "string";
                else if (elem is bool && (string.IsNullOrEmpty(vec.Elem) || vec.Elem == "unknown"))
                    vec.Elem = "bool";
                else if (IsNumericLoose(elem) && (string.IsNullOrEmpty(vec.Elem) || vec.Elem == "unknown"))
                    vec.Elem = GuessLooseType(elem);
                NotifyChanged(refreshTree: true);
            }
            else if (tag.Target is BtlStruct st)
            {
                if (HasFbsNames && !string.IsNullOrEmpty(tag.SchemaType))
                {
                    int layout = _fbs.StructMembers(tag.SchemaType).Count;
                    if (layout > 0 && st.V.Count >= layout)
                    {
                        MessageBox.Show(
                            "这个 struct 的成员个数由 fbs 布局决定，再加的成员写回 BTL 时会被丢掉。",
                            "新建成员", MessageBoxButtons.OK, MessageBoxIcon.Information);
                        return;
                    }
                }
                string memType = SuggestedStructMemberType(tag.SchemaType, st.V.Count) ?? "u16";
                string input = ShowEditDialog("new", memType, "0");
                if (input == null) return;
                try
                {
                    object parsedMember = ParseScalar(memType, input);
                    if (!TryFrontEdit(() => FrontEdit.SetMember(st, st.V.Count, parsedMember))) return;
                    NotifyChanged(refreshTree: false);
                }
                catch (Exception ex)
                {
                    MessageBox.Show("无法解析值：\n" + ex.Message, "注册表编辑", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                }
            }
        }

        bool TryFrontEdit(Action edit)
        {
            try
            {
                edit();
                return true;
            }
            catch (FrontEditException ex)
            {
                MessageBox.Show(ex.Message, "注册表编辑", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return false;
            }
        }

        void NotifyChanged(bool refreshTree)
        {
            _dirty = true;
            DataChanged?.Invoke(this, EventArgs.Empty);
            if (refreshTree)
                RefreshTree(collapseAll: false);
            else if (_shown != null)
                ShowNode(_shown);
        }

        static int GetTableMaxFieldId(BtlTable tbl)
        {
            if (tbl?.F == null || tbl.F.Count == 0) return -1;
            return tbl.F.Keys.Max();
        }

        int SchemaMaxFieldId(string schemaType)
        {
            var schema = ActiveSchema;
            if (schema == null || string.IsNullOrEmpty(schemaType)) return -1;
            var fields = schema.FieldsOf(schemaType);
            if (fields == null || fields.Count == 0) return -1;
            int max = -1;
            foreach (var f in fields)
            {
                if (f.Id > max) max = f.Id;
            }
            return max;
        }

        int VisibleTableMaxFieldId(BtlTable tbl, string schemaType) =>
            Math.Max(GetTableMaxFieldId(tbl), SchemaMaxFieldId(schemaType));

        void AddAbsentFieldRow(int fieldId, string name, string path, BtlTable tbl, string schemaType)
        {
            DescribeAbsentField(schemaType, fieldId, out string type, out string content, out bool isScalar);
            var item = new ListViewItem(new[]
            {
                fieldId.ToString(CultureInfo.InvariantCulture),
                name ?? IdLabel(fieldId),
                type,
                content
            })
            {
                ImageIndex = isScalar ? 3 : 2,
                ForeColor = Color.Gray,
                ToolTipText = isScalar
                    ? "未写出的缺省值（文件里没有这个槽；游戏读取为 0 / false）。黑色的 0 才是文件里写出来的。"
                    : "真正的空指针：表 / 向量 / 字符串未写出。",
                Tag = new ValueTag
                {
                    Path = path,
                    Container = tbl,
                    Key = fieldId,
                    Type = type,
                    IsScalar = isScalar,
                    IsAbsent = true,
                    SchemaType = schemaType
                }
            };
            _list.Items.Add(item);
        }

        void DescribeAbsentField(string schemaType, int fieldId, out string type, out string content, out bool isScalar)
        {
            type = "null";
            content = "NULL";
            isScalar = false;
            if (!SoftSchema.TryGet(schemaType, fieldId, out var hint) || hint == null)
                return;

            switch (hint.Kind)
            {
                case FieldKind.Bool:
                    type = "bool";
                    content = "false";
                    isScalar = true;
                    return;
                case FieldKind.Scalar:
                    type = string.IsNullOrEmpty(hint.ScalarT) ? "u16" : hint.ScalarT;
                    content = FormatValue(SoftSchema.DefaultValue(type));
                    isScalar = true;
                    return;
                case FieldKind.String:
                    type = "string";
                    content = "NULL";
                    return;
                case FieldKind.Table:
                    type = HasFbsNames && !string.IsNullOrEmpty(hint.ChildTable)
                        ? "table (" + hint.ChildTable + ")"
                        : "table";
                    content = "NULL";
                    return;
                case FieldKind.Vector:
                    string elem = hint.VectorElem ?? "?";
                    if (HasFbsNames && !string.IsNullOrEmpty(hint.VectorElemTable))
                        elem = hint.VectorElemTable;
                    type = "vector<" + elem + ">";
                    content = "NULL";
                    return;
                case FieldKind.Struct:
                    type = HasFbsNames && !string.IsNullOrEmpty(hint.StructName)
                        ? "struct (" + hint.StructName + ")"
                        : "struct";
                    content = "NULL";
                    return;
                default:
                    return;
            }
        }

        #region fbs naming

        void BtnLoadFbs_Click(object sender, EventArgs e)
        {
            using var ofd = new OpenFileDialog
            {
                Title = "选择 BTL FlatBuffers Schema (.fbs)",
                Filter = "FlatBuffers Schema (*.fbs)|*.fbs|所有文件 (*.*)|*.*"
            };
            string nearby = FbsSchema.FindNearby();
            if (!string.IsNullOrEmpty(nearby))
            {
                ofd.InitialDirectory = Path.GetDirectoryName(nearby);
                ofd.FileName = Path.GetFileName(nearby);
            }
            if (ofd.ShowDialog(this) != DialogResult.OK) return;
            try
            {
                _fbs = FbsSchema.LoadFile(ofd.FileName);
                _lblFbsStatus.Text = Path.GetFileName(ofd.FileName);
                _lblFbsStatus.ForeColor = Color.DarkGreen;
                RefreshTree(collapseAll: false);
            }
            catch (Exception ex)
            {
                MessageBox.Show("加载 FBS 失败:\n" + ex.Message, "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        bool HasFbsNames => _fbs != null;

        /// <summary>显示名字用用户加载的 fbs；认字段种类（标量 vs 指针）用 SoftSchema，不必点「加载 .fbs」。</summary>
        FbsSchema ActiveSchema => _fbs ?? SoftSchema.Schema;

        string RootNodeTitle() =>
            HasFbsNames && !string.IsNullOrEmpty(_fbs.RootType) ? _fbs.RootType : "table";

        string RootSchemaType() => ActiveSchema?.RootType ?? "";

        string VectorItemTitle(int index, string elemSchema)
        {
            return IdLabel(index);
        }

        string OwnSchemaType(object container, int key, BtlNode node, string parentSchema)
        {
            if (container is BtlTable)
                return ResolveChildSchemaType(parentSchema, key, node);
            if (container is BtlStruct)
                return ResolveStructMemberChildType(parentSchema, key);
            return parentSchema ?? "";
        }

        /// <summary>类型列：永远用 Front 节点自己的种类；加载 fbs 后才在括号里补表名。</summary>
        string TypeLabel(BtlNode node, string schemaType)
        {
            if (node is BtlScalar sc)
                return sc.T ?? "scalar";
            if (node is BtlTable)
                return HasFbsNames && !string.IsNullOrEmpty(schemaType)
                    ? "table (" + schemaType + ")"
                    : "table";
            if (node is BtlVector vec)
            {
                string elem = HasFbsNames && !string.IsNullOrEmpty(schemaType)
                    ? schemaType
                    : (vec.Elem ?? "?");
                return "vector<" + elem + ">";
            }
            if (node is BtlStruct)
                return HasFbsNames && !string.IsNullOrEmpty(schemaType)
                    ? "struct (" + schemaType + ")"
                    : "struct";
            return node?.T ?? "unknown";
        }

        /// <summary>加载 fbs 后名称列只用行尾中文注释；没有注释就仍显示编号。</summary>
        static string CommentLabel(string comment)
        {
            if (string.IsNullOrWhiteSpace(comment)) return null;
            string c = comment.Trim();
            int at = c.IndexOf('@');
            if (at > 0) c = c.Substring(0, at).Trim();
            return string.IsNullOrEmpty(c) ? null : c;
        }

        string FieldDisplayName(string schemaType, int fieldId)
        {
            if (HasFbsNames && _fbs.TryGetField(schemaType, fieldId, out var f))
            {
                string c = CommentLabel(f.Comment);
                if (!string.IsNullOrEmpty(c)) return c;
            }
            return IdLabel(fieldId);
        }

        string StructMemberDisplayName(string structType, int index)
        {
            if (HasFbsNames && !string.IsNullOrEmpty(structType))
            {
                var members = _fbs.StructMembers(structType);
                if (index >= 0 && index < members.Count)
                {
                    string c = CommentLabel(members[index].Comment);
                    if (!string.IsNullOrEmpty(c)) return c;
                }
            }
            return IdLabel(index);
        }

        string ResolveChildSchemaType(string parentType, int fieldId, BtlNode child)
        {
            var schema = ActiveSchema;
            if (schema != null && schema.TryGetField(parentType, fieldId, out var f) && f != null)
            {
                string resolved = schema.ResolveChildType(f.Type);
                if (!string.IsNullOrEmpty(resolved) && (schema.IsTable(resolved) || schema.IsStruct(resolved)))
                    return resolved;
                if (!string.IsNullOrEmpty(resolved) && child is BtlVector)
                    return resolved;
            }

            return "";
        }

        string ResolveStructMemberChildType(string structType, int index)
        {
            var schema = ActiveSchema;
            if (schema != null && !string.IsNullOrEmpty(structType))
            {
                var members = schema.StructMembers(structType);
                if (index >= 0 && index < members.Count)
                {
                    string resolved = schema.ResolveChildType(members[index].Type);
                    if (!string.IsNullOrEmpty(resolved) && (schema.IsTable(resolved) || schema.IsStruct(resolved)))
                        return resolved;
                }
            }
            return "";
        }

        /// <summary>给「新建字段」预选 table/vector/u16 等。种类来自 SoftSchema，不必先加载显示用 fbs。</summary>
        string SuggestedPickerType(string parentSchema, int fieldId)
        {
            var schema = ActiveSchema;
            if (schema == null || string.IsNullOrEmpty(parentSchema)
                || !schema.TryGetField(parentSchema, fieldId, out var f) || f == null)
                return null;
            string t = f.Type;
            if (FbsSchema.IsVectorType(t)) return "vector";
            if (FbsSchema.IsStringType(t)) return "string";
            string child = schema.ResolveChildType(t);
            if (schema.IsTable(child)) return "table";
            if (schema.IsStruct(child)) return "struct";
            string front = SoftSchema.ToFrontScalar(child ?? t);
            if (Array.Exists(FrontScalarTypes, x => string.Equals(x, front, StringComparison.OrdinalIgnoreCase)))
                return front;
            return null;
        }

        string SuggestedVectorElem(string parentSchema, int fieldId)
        {
            var schema = ActiveSchema;
            if (schema == null || string.IsNullOrEmpty(parentSchema)
                || !schema.TryGetField(parentSchema, fieldId, out var f) || f == null)
                return null;
            if (!FbsSchema.IsVectorType(f.Type)) return null;
            string elem = schema.ResolveChildType(f.Type);
            if (FbsSchema.IsStringType(elem)) return "string";
            if (schema.IsTable(elem)) return "table";
            if (schema.IsStruct(elem)) return "struct";
            string front = SoftSchema.ToFrontScalar(elem);
            return string.IsNullOrEmpty(front) ? "u8" : front;
        }

        string SuggestedStructMemberType(string structType, int index)
        {
            var schema = ActiveSchema;
            if (schema == null || string.IsNullOrEmpty(structType)) return null;
            var members = schema.StructMembers(structType);
            if (index < 0 || index >= members.Count) return null;
            string t = members[index].Type;
            if (FbsSchema.IsStringType(t)) return "string";
            if (schema.IsTable(t) || schema.IsStruct(t)) return null;
            string front = SoftSchema.ToFrontScalar(t);
            return string.IsNullOrEmpty(front) ? "u16" : front;
        }

        #endregion

        string ShowEditDialog(string fieldName, string regType, string currentValue)
        {
            using var form = new Form
            {
                Text = $"编辑数据 - {fieldName} ({regType})",
                Size = new Size(380, 180),
                StartPosition = FormStartPosition.CenterParent,
                FormBorderStyle = FormBorderStyle.FixedDialog,
                MaximizeBox = false,
                MinimizeBox = false
            };
            var lbl = new Label
            {
                Text = $"请输入新的 {regType} 数值：",
                Left = 20,
                Top = 15,
                Width = 320
            };
            var txt = new TextBox { Text = currentValue, Left = 20, Top = 40, Width = 325 };
            var ok = new Button { Text = "确定", Left = 175, Width = 80, Top = 80, DialogResult = DialogResult.OK };
            var cancel = new Button { Text = "取消", Left = 265, Width = 80, Top = 80, DialogResult = DialogResult.Cancel };
            form.Controls.Add(lbl);
            form.Controls.Add(txt);
            form.Controls.Add(ok);
            form.Controls.Add(cancel);
            form.AcceptButton = ok;
            form.CancelButton = cancel;
            return form.ShowDialog(this) == DialogResult.OK ? txt.Text.Trim() : null;
        }

        (int fieldId, BtlNode node)? ShowAddFieldDialog(BtlTable currentTbl, int defaultFieldId, bool lockId,
            string suggestedType = null, string vectorElem = null)
        {
            using var form = new Form
            {
                Text = "新建扩展字段",
                Size = new Size(400, 250),
                StartPosition = FormStartPosition.CenterParent,
                FormBorderStyle = FormBorderStyle.FixedDialog,
                MaximizeBox = false,
                MinimizeBox = false
            };

            var lblId = new Label { Text = "字段 ID:", Left = 20, Top = 20, Width = 110 };
            decimal idValue = Math.Min(512, Math.Max(0, defaultFieldId));
            var nudId = new NumericUpDown
            {
                Left = 140,
                Top = 16,
                Width = 200,
                Minimum = 0,
                Maximum = 512,
                Value = idValue,
                Enabled = !lockId
            };

            var lblType = new Label { Text = "数据类型:", Left = 20, Top = 60, Width = 110 };
            var cbType = new ComboBox
            {
                Left = 140,
                Top = 56,
                Width = 200,
                DropDownStyle = ComboBoxStyle.DropDownList
            };
            cbType.Items.AddRange(FrontScalarTypes);

            var lblVal = new Label { Text = "初始值:", Left = 20, Top = 100, Width = 110 };
            var txtVal = new TextBox { Text = "0", Left = 140, Top = 96, Width = 200 };

            void ApplyTypeDefault()
            {
                string tName = cbType.SelectedItem as string ?? "";
                if (tName is "table" or "vector" or "struct")
                {
                    txtVal.Enabled = false;
                    txtVal.Text = "(自动创建空节点)";
                }
                else if (tName == "string")
                {
                    txtVal.Enabled = true;
                    txtVal.Text = "";
                }
                else if (tName == "bool")
                {
                    txtVal.Enabled = true;
                    txtVal.Text = "false";
                }
                else
                {
                    txtVal.Enabled = true;
                    txtVal.Text = "0";
                }
            }

            int defIdx = Array.FindIndex(FrontScalarTypes, t =>
                string.Equals(t, suggestedType, StringComparison.OrdinalIgnoreCase));
            if (defIdx < 0) defIdx = Array.FindIndex(FrontScalarTypes, t => t == "u16");
            cbType.SelectedIndex = defIdx >= 0 ? defIdx : 0;
            ApplyTypeDefault();
            cbType.SelectedIndexChanged += (s, e) => ApplyTypeDefault();

            var ok = new Button { Text = "确定", Left = 175, Width = 80, Top = 150, DialogResult = DialogResult.OK };
            var cancel = new Button { Text = "取消", Left = 260, Width = 80, Top = 150, DialogResult = DialogResult.Cancel };
            form.Controls.Add(lblId);
            form.Controls.Add(nudId);
            form.Controls.Add(lblType);
            form.Controls.Add(cbType);
            form.Controls.Add(lblVal);
            form.Controls.Add(txtVal);
            form.Controls.Add(ok);
            form.Controls.Add(cancel);
            form.AcceptButton = ok;
            form.CancelButton = cancel;

            if (form.ShowDialog(this) != DialogResult.OK) return null;

            int fId = (int)nudId.Value;
            if (currentTbl != null && currentTbl.F.ContainsKey(fId) && currentTbl.F[fId] != null)
            {
                if (MessageBox.Show($"字段 ID {fId} 已有数据，确定覆盖？", "覆盖字段",
                        MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes)
                    return null;
            }

            string typeName = cbType.SelectedItem as string ?? "u16";
            try
            {
                string vecElem = string.Equals(typeName, "vector", StringComparison.OrdinalIgnoreCase)
                    ? (vectorElem ?? "u8")
                    : null;
                BtlNode node = CreateNodeFromType(typeName, txtVal.Text.Trim(), vecElem);
                return (fId, node);
            }
            catch (Exception ex)
            {
                MessageBox.Show("无法创建字段：\n" + ex.Message, "注册表编辑", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return null;
            }
        }

        object ShowAddVectorItemDialog(BtlVector vec)
        {
            string detected = vec.Elem;
            if (string.IsNullOrEmpty(detected) || detected == "unknown")
            {
                if (vec.V.Count > 0)
                {
                    object first = vec.V[0];
                    if (first is BtlTable) detected = "table";
                    else if (first is BtlStruct) detected = "struct";
                    else if (first is BtlVector) detected = "vector";
                    else if (first is string) detected = "string";
                    else if (first is bool) detected = "bool";
                    else if (first is BtlScalar sc) detected = sc.T ?? "u16";
                    else detected = GuessLooseType(first);
                }
                else
                    detected = "u16";
            }

            using var form = new Form
            {
                Text = "添加数组元素",
                Size = new Size(400, 220),
                StartPosition = FormStartPosition.CenterParent,
                FormBorderStyle = FormBorderStyle.FixedDialog,
                MaximizeBox = false,
                MinimizeBox = false
            };

            var lblType = new Label { Text = "元素数据类型:", Left = 20, Top = 20, Width = 110 };
            var cbType = new ComboBox
            {
                Left = 140,
                Top = 16,
                Width = 200,
                DropDownStyle = ComboBoxStyle.DropDownList
            };
            cbType.Items.AddRange(FrontScalarTypes);

            var lblVal = new Label { Text = "初始元素值:", Left = 20, Top = 60, Width = 110 };
            var txtVal = new TextBox { Text = "0", Left = 140, Top = 56, Width = 200 };

            void ApplyTypeDefault()
            {
                string tName = cbType.SelectedItem as string ?? "";
                if (tName is "table" or "vector" or "struct")
                {
                    txtVal.Enabled = false;
                    txtVal.Text = "(自动创建空节点)";
                }
                else if (tName == "string")
                {
                    txtVal.Enabled = true;
                    txtVal.Text = "";
                }
                else if (tName == "bool")
                {
                    txtVal.Enabled = true;
                    txtVal.Text = "false";
                }
                else
                {
                    txtVal.Enabled = true;
                    txtVal.Text = "0";
                }
            }

            int matched = Array.FindIndex(FrontScalarTypes, t =>
                string.Equals(t, detected, StringComparison.OrdinalIgnoreCase));
            cbType.SelectedIndex = matched >= 0 ? matched : Array.FindIndex(FrontScalarTypes, t => t == "u16");
            bool lockType = vec.V.Count > 0 && matched >= 0;
            cbType.Enabled = !lockType;
            ApplyTypeDefault();
            cbType.SelectedIndexChanged += (s, e) => ApplyTypeDefault();

            var ok = new Button { Text = "确定", Left = 175, Width = 80, Top = 110, DialogResult = DialogResult.OK };
            var cancel = new Button { Text = "取消", Left = 260, Width = 80, Top = 110, DialogResult = DialogResult.Cancel };
            form.Controls.Add(lblType);
            form.Controls.Add(cbType);
            form.Controls.Add(lblVal);
            form.Controls.Add(txtVal);
            form.Controls.Add(ok);
            form.Controls.Add(cancel);
            form.AcceptButton = ok;
            form.CancelButton = cancel;

            if (form.ShowDialog(this) != DialogResult.OK) return null;
            string typeName = cbType.SelectedItem as string ?? "u16";
            if (!string.IsNullOrEmpty(vec.Elem) && vec.Elem != "unknown"
                && !string.Equals(NormalizePickerType(vec.Elem), NormalizePickerType(typeName), StringComparison.OrdinalIgnoreCase))
            {
                if (MessageBox.Show($"这个数组的元素类型是 {vec.Elem}，要添加 {typeName} 吗？类型混用写回可能出错。",
                        "类型不一致", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes)
                    return null;
            }
            try
            {
                BtlNode node = CreateNodeFromType(typeName, txtVal.Text.Trim());
                if (node is BtlScalar sc) return sc.V;
                return node;
            }
            catch (Exception ex)
            {
                MessageBox.Show("无法创建元素：\n" + ex.Message, "注册表编辑", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return null;
            }
        }

        static string NormalizePickerType(string t)
        {
            if (string.IsNullOrEmpty(t)) return t;
            t = t.Trim().ToLowerInvariant();
            if (t.StartsWith("table")) return "table";
            if (t.StartsWith("vector")) return "vector";
            if (t.StartsWith("struct")) return "struct";
            return t;
        }

        static BtlNode CreateNodeFromType(string typeName, string initialText, string vectorElem = null)
        {
            string t = (typeName ?? "u16").ToLowerInvariant();
            return t switch
            {
                "table" => BtlFrontJson.NewTable(),
                "vector" => new BtlVector { T = "vector", Elem = string.IsNullOrEmpty(vectorElem) ? "u8" : vectorElem },
                "struct" => new BtlStruct { T = "struct" },
                "string" or "str" => BtlFrontJson.Scalar("string", initialText ?? ""),
                "bool" or "boolean" => BtlFrontJson.Scalar("bool", ParseScalar("bool", string.IsNullOrEmpty(initialText) ? "false" : initialText)),
                _ => BtlFrontJson.Scalar(t, ParseScalar(t, string.IsNullOrEmpty(initialText) ? "0" : initialText))
            };
        }

        static bool IsScalarLoose(object o) =>
            o is byte or sbyte or short or ushort or int or uint or long or ulong or float or double or bool or string or decimal;

        static bool IsNumericLoose(object o) =>
            o is byte or sbyte or short or ushort or int or uint or long or ulong or float or double or decimal;

        void ApplyScalarEdit(ValueTag vt, string text, ListViewItem row)
        {
            object parsed;
            try
            {
                parsed = ParseScalar(vt.Type, text);
            }
            catch (Exception ex)
            {
                MessageBox.Show("无法解析值：\n" + ex.Message, "注册表编辑", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            bool applied = false;
            if (!TryFrontEdit(() =>
                {
                    if (vt.Scalar != null)
                    {
                        FrontEdit.SetHeldScalar(vt.Scalar, parsed);
                        applied = true;
                    }
                    else if (vt.Container is BtlVector vec && vt.Key >= 0 && vt.Key < vec.V.Count)
                    {
                        FrontEdit.SetAt(vec, vt.Key, parsed);
                        applied = true;
                    }
                    else if (vt.Container is BtlStruct st && vt.Key >= 0 && vt.Key < st.V.Count)
                    {
                        FrontEdit.SetMember(st, vt.Key, parsed);
                        applied = true;
                    }
                    else if (vt.Container is BtlTable tbl)
                    {
                        FrontEdit.SetScalar(tbl, vt.Key, vt.Type, parsed);
                        applied = true;
                    }
                }))
                return;
            if (!applied) return;

            row.SubItems[3].Text = FormatValue(parsed);
            if (vt.IsAbsent)
            {
                vt.IsAbsent = false;
                NotifyChanged(refreshTree: false);
                return;
            }
            _dirty = true;
            DataChanged?.Invoke(this, EventArgs.Empty);
        }

        static object ParseScalar(string type, string text)
        {
            string t = (type ?? "").ToLowerInvariant();
            if (t is "bool" or "boolean")
            {
                if (bool.TryParse(text, out bool b)) return b;
                if (text == "1" || text == "0") return text == "1";
                throw new FormatException("需要 true/false 或 0/1");
            }
            if (t is "string" or "str")
                return text;

            if (t is "f32" or "float")
                return float.Parse(text, CultureInfo.InvariantCulture);
            if (t is "f64" or "double")
                return double.Parse(text, CultureInfo.InvariantCulture);

            long n = long.Parse(text, NumberStyles.Integer, CultureInfo.InvariantCulture);
            return t switch
            {
                "u8" => checked((byte)n),
                "i8" => checked((sbyte)n),
                "u16" => checked((ushort)n),
                "i16" => checked((short)n),
                "u32" => checked((uint)n),
                "i32" => checked((int)n),
                "u64" => checked((ulong)n),
                "i64" => n,
                _ => n
            };
        }

        #endregion

        #region Path helpers

        static string JoinPath(string parent, string segment)
        {
            if (string.IsNullOrEmpty(parent) || parent == "/")
                return "/" + segment;
            return parent.TrimEnd('/') + "/" + segment;
        }

        static string NormalizePath(string path)
        {
            path = (path ?? "").Trim().Replace('\\', '/');
            if (path.Equals("Root", StringComparison.OrdinalIgnoreCase) || path == "")
                return "/";
            if (path.StartsWith("Root/", StringComparison.OrdinalIgnoreCase))
                path = path.Substring(4);
            if (!path.StartsWith("/"))
                path = "/" + path;
            if (path.Length > 1) path = path.TrimEnd('/');
            return path;
        }

        void AddressBar_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeyCode != Keys.Enter) return;
            e.SuppressKeyPress = true;
            string input = _addressBar.Text.Trim();
            if (!NavigateToPath(input))
                MessageBox.Show("找不到路径: \"" + input + "\"", "路径跳转", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        TreeNode FindByPath(TreeNodeCollection nodes, string path)
        {
            path = NormalizePath(path);
            foreach (TreeNode n in nodes)
            {
                if (n.Tag is NodeTag tag && string.Equals(NormalizePath(tag.Path), path, StringComparison.OrdinalIgnoreCase))
                    return n;
                // 先展开占位再搜
                if (n.Nodes.Count == 1 && n.Nodes[0].Tag == null && n.Tag is NodeTag nt)
                    BuildChildren(n, nt.Target, nt.Path, nt.SchemaType);
                var found = FindByPath(n.Nodes, path);
                if (found != null) return found;
            }
            return null;
        }

        TreeNode EnsurePathBuilt(string path)
        {
            path = NormalizePath(path);
            if (path == "/") return _tree.Nodes.Count > 0 ? _tree.Nodes[0] : null;

            TreeNode current = _tree.Nodes.Count > 0 ? _tree.Nodes[0] : null;
            if (current == null) return null;

            string[] parts = path.Trim('/').Split('/', StringSplitOptions.RemoveEmptyEntries);
            string walk = "/";
            foreach (string part in parts)
            {
                if (current.Tag is NodeTag nt)
                {
                    if (current.Nodes.Count == 1 && current.Nodes[0].Tag == null)
                        BuildChildren(current, nt.Target, nt.Path, nt.SchemaType);
                    else if (current.Nodes.Count == 0 && HasNavigableChildren(nt.Target))
                        BuildChildren(current, nt.Target, nt.Path, nt.SchemaType);
                }

                walk = JoinPath(walk, part);
                TreeNode next = null;
                foreach (TreeNode child in current.Nodes)
                {
                    if (child.Tag is NodeTag ct && string.Equals(NormalizePath(ct.Path), NormalizePath(walk), StringComparison.OrdinalIgnoreCase))
                    {
                        next = child;
                        break;
                    }
                }
                if (next == null) return null;
                current.Expand();
                current = next;
            }
            return current;
        }

        void SaveExpanded(TreeNodeCollection nodes, HashSet<string> set)
        {
            foreach (TreeNode n in nodes)
            {
                if (n.IsExpanded && n.Tag is NodeTag tag)
                    set.Add(NormalizePath(tag.Path));
                SaveExpanded(n.Nodes, set);
            }
        }

        void RestoreExpanded(TreeNodeCollection nodes, HashSet<string> set)
        {
            foreach (TreeNode n in nodes)
            {
                if (n.Tag is NodeTag tag && set.Contains(NormalizePath(tag.Path)))
                {
                    if (n.Nodes.Count == 1 && n.Nodes[0].Tag == null)
                        BuildChildren(n, tag.Target, tag.Path, tag.SchemaType);
                    n.Expand();
                }
                RestoreExpanded(n.Nodes, set);
            }
        }

        string FindAgentPath(int cellIdx)
        {
            if (_doc?.Root?.F.TryGetValue(6, out BtlNode aiNode) != true || aiNode is not BtlTable ai)
                return null;
            if (!ai.F.TryGetValue(0, out BtlNode agentsNode) || agentsNode is not BtlVector agents)
                return null;

            for (int i = 0; i < agents.V.Count; i++)
            {
                if (agents.V[i] is not BtlTable agent) continue;
                if (!agent.F.TryGetValue(0, out BtlNode infoNode) || infoNode is not BtlStruct info) continue;
                if (info.V.Count == 0) continue;
                if (ToInt(info.V[0]) == cellIdx)
                    return "/6/0/i" + i.ToString(CultureInfo.InvariantCulture);
            }
            return null;
        }

        string FindTriggerPath(int cellIdx)
        {
            if (_doc?.Root?.F.TryGetValue(5, out BtlNode trigNode) != true || trigNode is not BtlTable trig)
                return null;
            if (!trig.F.TryGetValue(0, out BtlNode eventsNode) || eventsNode is not BtlVector events)
                return null;

            for (int i = 0; i < events.V.Count; i++)
            {
                if (events.V[i] is not BtlTable ev) continue;
                // TriggerEvent：常见 field 0 = tile / cell；兼容 struct 或标量
                if (ev.F.TryGetValue(0, out BtlNode tileNode))
                {
                    int tile = ExtractCellIdx(tileNode);
                    if (tile == cellIdx)
                        return "/5/0/i" + i.ToString(CultureInfo.InvariantCulture);
                }
                // 部分关卡建筑挂在子表
                foreach (var kv in ev.F)
                {
                    if (kv.Value is BtlTable sub)
                    {
                        int tile = TryFindTileInTable(sub);
                        if (tile == cellIdx)
                            return "/5/0/i" + i.ToString(CultureInfo.InvariantCulture);
                    }
                }
            }
            return null;
        }

        static int TryFindTileInTable(BtlTable tbl)
        {
            if (tbl.F.TryGetValue(0, out BtlNode n))
                return ExtractCellIdx(n);
            return int.MinValue;
        }

        static int ExtractCellIdx(BtlNode node)
        {
            if (node is BtlScalar sc) return ToInt(sc.V);
            if (node is BtlStruct st && st.V.Count > 0) return ToInt(st.V[0]);
            return int.MinValue;
        }

        static int ToInt(object v)
        {
            if (v == null) return int.MinValue;
            try { return Convert.ToInt32(v, CultureInfo.InvariantCulture); }
            catch { return int.MinValue; }
        }

        #endregion

        #region Icons

        static ImageList CreateRegistryImageList()
        {
            var imgList = new ImageList
            {
                ImageSize = new Size(16, 16),
                ColorDepth = ColorDepth.Depth32Bit
            };

            var closed = new Bitmap(16, 16);
            using (var g = Graphics.FromImage(closed))
            {
                g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                using (var tab = new SolidBrush(Color.FromArgb(235, 185, 50)))
                    g.FillRectangle(tab, 2, 3, 6, 3);
                using (var body = new SolidBrush(Color.FromArgb(245, 205, 70)))
                    g.FillRectangle(body, 1, 5, 14, 9);
                using (var pen = new Pen(Color.FromArgb(180, 140, 20)))
                {
                    g.DrawRectangle(pen, 1, 5, 14, 9);
                    g.DrawRectangle(pen, 2, 3, 6, 3);
                }
            }

            var open = new Bitmap(16, 16);
            using (var g = Graphics.FromImage(open))
            {
                g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                using (var tab = new SolidBrush(Color.FromArgb(235, 185, 50)))
                    g.FillRectangle(tab, 2, 2, 6, 3);
                using (var back = new SolidBrush(Color.FromArgb(215, 165, 30)))
                    g.FillRectangle(back, 2, 4, 12, 8);
                var flap = new[] { new Point(0, 7), new Point(3, 14), new Point(15, 14), new Point(13, 7) };
                using (var front = new SolidBrush(Color.FromArgb(250, 215, 80)))
                    g.FillPolygon(front, flap);
                using (var pen = new Pen(Color.FromArgb(180, 140, 20)))
                    g.DrawPolygon(pen, flap);
            }

            var strIcon = new Bitmap(16, 16);
            using (var g = Graphics.FromImage(strIcon))
            {
                using (var bg = new SolidBrush(Color.FromArgb(210, 50, 45)))
                    g.FillRectangle(bg, 1, 2, 14, 12);
                using (var font = new Font("Segoe UI", 7f, FontStyle.Bold))
                    g.DrawString("ab", font, Brushes.White, new PointF(1f, 1f));
            }

            var numIcon = new Bitmap(16, 16);
            using (var g = Graphics.FromImage(numIcon))
            {
                using (var bg = new SolidBrush(Color.FromArgb(40, 115, 205)))
                    g.FillRectangle(bg, 1, 2, 14, 12);
                using (var font = new Font("Segoe UI", 6.5f, FontStyle.Bold))
                    g.DrawString("01", font, Brushes.White, new PointF(1f, 1.5f));
            }

            imgList.Images.Add(closed);
            imgList.Images.Add(open);
            imgList.Images.Add(strIcon);
            imgList.Images.Add(numIcon);
            return imgList;
        }

        #endregion
    }
}
