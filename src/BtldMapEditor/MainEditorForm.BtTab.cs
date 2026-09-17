using System.Text.Json.Nodes;
using BtlCore.Front;
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
        /// <summary>保证 Root/10 存在、elem=u8、enc=country_ai_bt。新建树时用。</summary>
        BtlVector CountryBtVec()
        {
            if (!HasDoc) return null;
            var vec = FrontNav.EnsureVec(Doc.Root, 10, "u8");
            vec.Enc = "country_ai_bt";
            return vec;
        }

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

        static void JSetStr(JsonObject o, string key, string value)
        {
            if (string.IsNullOrWhiteSpace(value)) o.Remove(key);
            else o[key] = value.Trim();
        }

        static void JSetInt(JsonObject o, string key, int? value)
        {
            if (value == null) o.Remove(key);
            else o[key] = value.Value;
        }

        static JsonArray JNodes(JsonObject o)
        {
            if (o["node"] is JsonArray a) return a;
            var n = new JsonArray();
            o["node"] = n;
            return n;
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

        static void JSetIntList(JsonObject o, string key, List<int> list)
        {
            if (list == null || list.Count == 0)
            {
                o.Remove(key);
                return;
            }
            var arr = new JsonArray();
            foreach (var n in list) arr.Add(n);
            o[key] = arr;
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
            var vec = CountryBtVec();
            int nextId = 1;
            foreach (var item in vec.V)
            {
                int id = JInt(AsJsonObj(item), "btid") ?? 0;
                if (id >= nextId) nextId = id + 1;
            }
            vec.V.Add(new JsonObject
            {
                ["btid"] = nextId,
                ["name"] = "新行为树",
                ["agent"] = "CBTCountryAgent",
                ["class"] = "bt",
                ["node"] = new JsonArray()
            });
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
            var kids = JNodes(tree);
            kids.Add(new JsonObject { ["class"] = "seq", ["node"] = new JsonArray() });
            int rootIndex = kids.Count - 1;
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
            if (tvBtNodes.SelectedNode?.Tag is not JsonObject parent)
            {
                MessageBox.Show("请先在“节点树”中选择一个节点，再添加子节点。", "提示", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            var kids = JNodes(parent);
            kids.Add(new JsonObject { ["class"] = "act" });
            var path = GetBtNodePath(tvBtNodes.SelectedNode);
            path.Add(kids.Count - 1);
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
            var kids = JNodes(tree);
            int rootIndex = selected.Index;
            if (rootIndex < 0 || rootIndex >= kids.Count) return;
            if (MessageBox.Show("确定要删除选中的节点树吗？", "确认删除", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes)
                return;
            kids.RemoveAt(rootIndex);
            RefreshCountryBtTab();
            if (lvBtTrees.Items.Count > treeIndex)
                lvBtTrees.Items[treeIndex].Selected = true;
            int selectIndex = Math.Min(rootIndex, kids.Count - 1);
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
            var kids = JNodes(parent);
            int childIndex = selected.Index;
            if (childIndex < 0 || childIndex >= kids.Count) return;
            if (MessageBox.Show("确定要删除选中的节点吗？", "确认删除", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes)
                return;
            var parentPath = GetBtNodePath(selected.Parent);
            kids.RemoveAt(childIndex);
            RefreshCountryBtTab();
            if (lvBtTrees.Items.Count > treeIndex)
                lvBtTrees.Items[treeIndex].Selected = true;
            if (kids.Count > 0)
            {
                parentPath.Add(Math.Min(childIndex, kids.Count - 1));
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
            vec.V.RemoveAt(idx);
            RefreshCountryBtTab();
            if (lvBtTrees.Items.Count > 0)
                lvBtTrees.Items[Math.Min(idx, lvBtTrees.Items.Count - 1)].Selected = true;
            AddHistoryState();
        }

        /// <summary>把当前树头和选中节点的控件值写回 JsonObject（就在 Document 上）。</summary>
        private void SaveBtTreeClick(object sender, EventArgs e)
        {
            if (!TryGetSelectedBtTree(out var tree, out int idx)) return;
            JSetInt(tree, "btid", (int)nudBtTid.Value);
            JSetInt(tree, "id", nudBtId.NullableValue.HasValue ? (int)nudBtId.NullableValue.Value : (int?)null);
            JSetStr(tree, "name", txtBtName.Text);
            JSetStr(tree, "agent", txtBtAgent.Text);
            JSetStr(tree, "class", txtBtClass.Text);
            if (tvBtNodes.SelectedNode?.Tag is JsonObject node)
            {
                JSetInt(node, "id", nudBtNodeId.NullableValue.HasValue ? (int)nudBtNodeId.NullableValue.Value : (int?)null);
                JSetStr(node, "class", txtBtNodeClass.Text);
                JSetStr(node, "method", txtBtMethod.Text);
                JSetIntList(node, "params", ParseBtIntList(txtBtParams.Text));
                JSetIntList(node, "rounds", ParseBtIntList(txtBtRounds.Text));
                JSetInt(node, "result", nudBtResult.NullableValue.HasValue ? (int)nudBtResult.NullableValue.Value : (int?)null);
                JSetInt(node, "count", nudBtCount.NullableValue.HasValue ? (int)nudBtCount.NullableValue.Value : (int?)null);
            }
            RefreshCountryBtTab();
            if (idx < lvBtTrees.Items.Count)
                lvBtTrees.Items[idx].Selected = true;
            AddHistoryState();
            MessageBox.Show("行为树修改已保存到当前关卡数据！", "成功", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
    }
}
