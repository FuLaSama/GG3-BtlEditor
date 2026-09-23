using System.Text.Json.Nodes;
using BtlCore.Front;
using BtlCore.Scripting;
using BtldMapEditor.Front;

namespace BtldMapEditor
{
    /// <summary>
    /// 国家行为树选项卡。数据就是 Root 字段 10 向量里的 JsonObject
    ///（enc=country_ai_bt），TreeView.Tag 指向这些对象，改界面即改 Document。
    /// 没有 BTTreeModel。
    /// </summary>
    public partial class MainEditorForm
    {
        static JsonObject AsJsonObj(object v) => v as JsonObject;

        static string JStr(JsonObject o, string key)
        {
            if (o == null || !o.TryGetPropertyValue(key, out var n) || n == null) return "";
            if (n is JsonValue v && v.TryGetValue(out string s)) return s ?? "";
            return n.ToString();
        }

        static int? JInt(JsonObject o, string key)
        {
            if (o == null || !o.TryGetPropertyValue(key, out var n) || n is not JsonValue v) return null;
            if (v.TryGetValue(out int i)) return i;
            if (v.TryGetValue(out long l)) return (int)l;
            return null;
        }

        static List<int> JIntList(JsonObject o, string key)
        {
            var list = new List<int>();
            if (o == null) return list;
            if (o[key] is JsonValue one)
            {
                if (one.TryGetValue(out int i)) list.Add(i);
                return list;
            }
            if (o[key] is not JsonArray arr) return list;
            foreach (var item in arr)
            {
                if (item is JsonValue v && v.TryGetValue(out int n)) list.Add(n);
            }
            return list;
        }

        /// <summary>用向量里现成的 JsonObject 填列表，不再反序列化成另一套类。</summary>
        private void RefreshCountryBtTab()
        {
            lvBtTrees.Items.Clear();
            tvBtNodes.Nodes.Clear();
            var vec = FrontNav.CountryAiBt(Doc);
            if (vec?.V == null) return;
            foreach (var item in vec.V)
            {
                var tree = AsJsonObj(item);
                if (tree == null) continue;
                var row = new ListViewItem(JInt(tree, "btid")?.ToString() ?? "");
                row.SubItems.Add(JStr(tree, "name"));
                row.SubItems.Add(JStr(tree, "agent"));
                row.SubItems.Add(JStr(tree, "class"));
                row.SubItems.Add(JInt(tree, "id")?.ToString() ?? "");
                row.Tag = tree;
                lvBtTrees.Items.Add(row);
            }
        }

        bool TryGetSelectedBtTree(out JsonObject tree, out int treeIndex)
        {
            tree = null;
            treeIndex = -1;
            var vec = FrontNav.CountryAiBt(Doc);
            if (vec?.V == null || lvBtTrees.SelectedIndices.Count == 0) return false;
            int idx = lvBtTrees.SelectedIndices[0];
            if (idx < 0 || idx >= vec.V.Count) return false;
            tree = AsJsonObj(vec.V[idx]);
            if (tree == null) return false;
            treeIndex = idx;
            return true;
        }

        private void LvBtTreesSelectedIndexChanged(object sender, EventArgs e)
        {
            if (!TryGetSelectedBtTree(out var tree, out _)) return;
            nudBtTid.NullableValue = JInt(tree, "btid");
            nudBtId.NullableValue = JInt(tree, "id");
            txtBtName.Text = JStr(tree, "name");
            txtBtAgent.Text = JStr(tree, "agent");
            txtBtClass.Text = JStr(tree, "class");
            tvBtNodes.Nodes.Clear();
            if (tree["node"] is JsonArray kids)
            {
                foreach (var child in kids)
                {
                    if (child is JsonObject n)
                        tvBtNodes.Nodes.Add(BuildBtTreeNode(n));
                }
            }
            if (tvBtNodes.Nodes.Count > 0)
                tvBtNodes.Nodes[0].Expand();
        }

        private void TvBtNodesAfterSelect(object sender, TreeViewEventArgs e)
        {
            if (e.Node?.Tag is not JsonObject node) return;
            nudBtNodeId.NullableValue = JInt(node, "id");
            txtBtNodeClass.Text = JStr(node, "class");
            txtBtMethod.Text = JStr(node, "method");
            txtBtParams.Text = string.Join(", ", JIntList(node, "params"));
            txtBtRounds.Text = string.Join(", ", JIntList(node, "rounds"));
            nudBtResult.NullableValue = JInt(node, "result");
            nudBtCount.NullableValue = JInt(node, "count");
        }

        static TreeNode BuildBtTreeNode(JsonObject node)
        {
            string label = JStr(node, "class");
            if (string.IsNullOrEmpty(label)) label = JStr(node, "method");
            var tn = new TreeNode(string.IsNullOrEmpty(label) ? "(node)" : label) { Tag = node };
            if (node["node"] is JsonArray kids)
            {
                foreach (var child in kids)
                {
                    if (child is JsonObject n)
                        tn.Nodes.Add(BuildBtTreeNode(n));
                }
            }
            return tn;
        }

        static int JsonChildCount(JsonObject obj) => obj?["node"] is JsonArray arr ? arr.Count : 0;

        static List<int> ParseBtIntList(string text)
        {
            var list = new List<int>();
            if (string.IsNullOrWhiteSpace(text)) return list;
            foreach (var part in text.Split(new[] { ',', ' ', ';', '\t' }, StringSplitOptions.RemoveEmptyEntries))
            {
                if (int.TryParse(part.Trim(), out int v)) list.Add(v);
            }
            return list;
        }

        private void AddBtTreeClick(object sender, EventArgs e)
        {
            if (!HasDoc) return;
            if (!RunEdit("add_tree", new ScriptArgs())) return;
            RefreshCountryBtTab();
            if (lvBtTrees.Items.Count > 0)
                lvBtTrees.Items[lvBtTrees.Items.Count - 1].Selected = true;
            AddHistoryState();
        }

        private void AddBtNodeTreeClick(object sender, EventArgs e)
        {
            if (!TryGetSelectedBtTree(out var tree, out int treeIndex))
            {
                MessageBox.Show("请先在“行为树列表”中选择一棵行为树。", "提示", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            int rootIndex = JsonChildCount(tree);
            if (!RunEdit("add_root", new ScriptArgs { Index = treeIndex })) return;
            RefreshCountryBtTab();
            if (lvBtTrees.Items.Count > treeIndex)
                lvBtTrees.Items[treeIndex].Selected = true;
            SelectBtNodePath(treeIndex, rootIndex);
            AddHistoryState();
        }

        private void AddBtNodeClick(object sender, EventArgs e)
        {
            if (!TryGetSelectedBtTree(out _, out int treeIndex))
            {
                MessageBox.Show("请先在“行为树列表”中选择一棵行为树。", "提示", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            if (tvBtNodes.SelectedNode?.Tag is not JsonObject)
            {
                MessageBox.Show("请先在“节点树”中选择一个节点，再添加子节点。", "提示", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            var path = GetBtNodePath(tvBtNodes.SelectedNode);
            int childIndex = JsonChildCount(tvBtNodes.SelectedNode.Tag as JsonObject);
            if (!RunEdit("add_child", new ScriptArgs
            {
                Index = treeIndex,
                Input = EditInput(("path", EditList(path)))
            })) return;
            path.Add(childIndex);
            RefreshCountryBtTab();
            if (lvBtTrees.Items.Count > treeIndex)
                lvBtTrees.Items[treeIndex].Selected = true;
            SelectBtNodePath(treeIndex, path.ToArray());
            AddHistoryState();
        }

        private void DeleteBtNodeTreeClick(object sender, EventArgs e)
        {
            if (!TryGetSelectedBtTree(out var tree, out int treeIndex))
            {
                MessageBox.Show("请先在“行为树列表”中选择一棵行为树。", "提示", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            TreeNode selected = tvBtNodes.SelectedNode;
            if (selected?.Tag is not JsonObject || selected.Parent != null)
            {
                MessageBox.Show("请先在“节点树”中选中一个根节点树。", "提示", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            int childCount = JsonChildCount(tree);
            int rootIndex = selected.Index;
            if (rootIndex < 0 || rootIndex >= childCount) return;
            if (MessageBox.Show("确定要删除选中的节点树吗？", "确认删除", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes)
                return;
            if (!RunEdit("delete_child", new ScriptArgs
            {
                Index = treeIndex,
                Input = EditInput(("index", rootIndex))
            })) return;
            RefreshCountryBtTab();
            if (lvBtTrees.Items.Count > treeIndex)
                lvBtTrees.Items[treeIndex].Selected = true;
            int selectIndex = Math.Min(rootIndex, childCount - 2);
            if (selectIndex >= 0) SelectBtNodePath(treeIndex, selectIndex);
            AddHistoryState();
        }

        private void DeleteBtNodeClick(object sender, EventArgs e)
        {
            if (!TryGetSelectedBtTree(out _, out int treeIndex))
            {
                MessageBox.Show("请先在“行为树列表”中选择一棵行为树。", "提示", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            TreeNode selected = tvBtNodes.SelectedNode;
            if (selected?.Tag is not JsonObject || selected.Parent == null || selected.Parent.Tag is not JsonObject parent)
            {
                MessageBox.Show("请先在“节点树”中选中一个子节点。", "提示", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            int childCount = JsonChildCount(parent);
            int childIndex = selected.Index;
            if (childIndex < 0 || childIndex >= childCount) return;
            if (MessageBox.Show("确定要删除选中的节点吗？", "确认删除", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes)
                return;
            var parentPath = GetBtNodePath(selected.Parent);
            if (!RunEdit("delete_child", new ScriptArgs
            {
                Index = treeIndex,
                Input = EditInput(("path", EditList(parentPath)), ("index", childIndex))
            })) return;
            RefreshCountryBtTab();
            if (lvBtTrees.Items.Count > treeIndex)
                lvBtTrees.Items[treeIndex].Selected = true;
            if (childCount > 1)
            {
                parentPath.Add(Math.Min(childIndex, childCount - 2));
                SelectBtNodePath(treeIndex, parentPath.ToArray());
            }
            else if (parentPath.Count > 0)
            {
                SelectBtNodePath(treeIndex, parentPath.ToArray());
            }
            AddHistoryState();
        }

        static List<int> GetBtNodePath(TreeNode node)
        {
            var path = new List<int>();
            for (TreeNode current = node; current != null; current = current.Parent)
                path.Insert(0, current.Index);
            return path;
        }

        private void SelectBtNodePath(int treeIndex, params int[] path)
        {
            if (treeIndex < 0 || treeIndex >= lvBtTrees.Items.Count || path == null || path.Length == 0) return;
            TreeNodeCollection nodes = tvBtNodes.Nodes;
            TreeNode selected = null;
            foreach (int p in path)
            {
                if (p < 0 || p >= nodes.Count) return;
                selected = nodes[p];
                nodes = selected.Nodes;
            }
            if (selected != null)
            {
                tvBtNodes.SelectedNode = selected;
                selected.EnsureVisible();
            }
        }

        private void DeleteBtTreeClick(object sender, EventArgs e)
        {
            var vec = FrontNav.CountryAiBt(Doc);
            if (vec?.V == null || lvBtTrees.SelectedIndices.Count == 0) return;
            int idx = lvBtTrees.SelectedIndices[0];
            if (idx < 0 || idx >= vec.V.Count) return;
            if (MessageBox.Show("确定要删除选中的行为树吗？", "确认删除", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes)
                return;
            if (!RunEdit("delete_tree", new ScriptArgs { Index = idx })) return;
            RefreshCountryBtTab();
            if (lvBtTrees.Items.Count > 0)
                lvBtTrees.Items[Math.Min(idx, lvBtTrees.Items.Count - 1)].Selected = true;
            AddHistoryState();
        }

        /// <summary>把当前树头和选中节点的控件值写回 JsonObject（就在 Document 上）。</summary>
        private void SaveBtTreeClick(object sender, EventArgs e)
        {
            if (!TryGetSelectedBtTree(out _, out int idx)) return;
            var input = EditInput(
                ("btid", (int)nudBtTid.Value),
                ("id", nudBtId.NullableValue.HasValue ? (int)nudBtId.NullableValue.Value : null),
                ("name", txtBtName.Text),
                ("agent", txtBtAgent.Text),
                ("class", txtBtClass.Text));
            if (tvBtNodes.SelectedNode?.Tag is JsonObject)
            {
                input["path"] = EditList(GetBtNodePath(tvBtNodes.SelectedNode));
                input["node_id"] = nudBtNodeId.NullableValue.HasValue ? (int)nudBtNodeId.NullableValue.Value : null;
                input["node_class"] = txtBtNodeClass.Text;
                input["method"] = txtBtMethod.Text;
                input["params"] = EditList(ParseBtIntList(txtBtParams.Text));
                input["rounds"] = EditList(ParseBtIntList(txtBtRounds.Text));
                input["result"] = nudBtResult.NullableValue.HasValue ? (int)nudBtResult.NullableValue.Value : null;
                input["count"] = nudBtCount.NullableValue.HasValue ? (int)nudBtCount.NullableValue.Value : null;
            }
            if (!RunEdit("save_bt", new ScriptArgs { Index = idx, Input = input })) return;
            RefreshCountryBtTab();
            if (idx < lvBtTrees.Items.Count)
                lvBtTrees.Items[idx].Selected = true;
            AddHistoryState();
            MessageBox.Show("行为树修改已保存到当前关卡数据！", "成功", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
    }
}
