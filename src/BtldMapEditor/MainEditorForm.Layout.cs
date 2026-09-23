using BtlCore.Front;
using BtlCore.Scripting;

namespace BtldMapEditor
{
    public partial class MainEditorForm
    {
        TableLayoutPanel _stageSlot;
        LayoutPageControl _stageLayout;
        ScriptHost _stageHost;
        string _missingEditAction;

        /// <summary>一次点击交给已加载的 Lua。写完重投影格子，避免随后的 SyncGrid 用旧格子盖掉脚本结果。</summary>
        bool RunEdit(string action, ScriptArgs args)
        {
            if (!HasDoc) return false;
            if (_stageHost == null || !_stageHost.CanRun(action))
            {
                if (_missingEditAction != action)
                {
                    _missingEditAction = action;
                    MessageBox.Show("没有可用的脚本动作：" + action, "布局问题", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                }
                return false;
            }
            _stageHost.Load(Doc);
            try
            {
                _stageHost.Run(action, args ?? new ScriptArgs());
            }
            catch (Exception ex)
            {
                MessageBox.Show(action + "\n" + ex.GetBaseException().Message, "脚本", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return false;
            }
            mapCanvas?.ReloadCells(Doc);
            return true;
        }

        static Dictionary<string, object> EditInput(params (string Key, object Value)[] pairs)
        {
            var input = new Dictionary<string, object>(pairs.Length);
            foreach (var pair in pairs)
                input[pair.Key] = pair.Value;
            return input;
        }

        static object[] EditList<T>(IEnumerable<T> values)
        {
            var list = new List<object>();
            if (values != null)
            {
                foreach (var value in values)
                    list.Add(value);
            }
            return list.ToArray();
        }

        static int EditIndex(BtlVector vec, object item)
        {
            if (vec?.V == null || item == null) return -1;
            return vec.V.IndexOf(item);
        }

        void CreateStageSlot(TableLayoutPanel parent)
        {
            _stageSlot = new TableLayoutPanel
            {
                Dock = DockStyle.Top,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                ColumnCount = 1,
                Margin = new Padding(0)
            };
            _stageSlot.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
            parent.Controls.Add(_stageSlot);
        }

        void TryInstallStageLayout()
        {
            string builtin = FindLayoutDir("EditorLayout");
            string user = FindLayoutDir("EditorLayout.user");
            var result = LayoutLoader.Load(builtin, Directory.Exists(user) ? user : null);
            if (!result.Ok)
            {
                ShowLayoutErrors(result.Errors);
                return;
            }
            var tab = result.Bundle.Tabs.Find(t => t.Id == "stage");
            if (tab?.Page == null)
            {
                ShowLayoutErrors(new List<string> { "布局里没有 id 为 stage 的页面" });
                return;
            }
            var host = new ScriptHost();
            var scriptErrors = new List<string>();
            foreach (var script in result.Bundle.Scripts)
            {
                try { host.Execute(script.Text, script.Name); }
                catch (Exception ex) { scriptErrors.Add(script.Name + ": " + ex.Message); }
            }
            LayoutLoader.MarkMissingActions(tab.Page, host.CanRun, host.CanGet);
            if (HasDoc) host.Load(Doc);
            var view = new LayoutPageControl(tab.Page, host, OnStageLayoutCommitted);
            if (HasDoc) view.Reload(Doc);
            DetachLegacyStageLists();
            _stageSlot.Controls.Clear();
            _stageSlot.RowCount = 1;
            _stageSlot.RowStyles.Clear();
            _stageSlot.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            _stageSlot.Controls.Add(view, 0, 0);
            _stageLayout = view;
            _stageHost = host;
            if (scriptErrors.Count > 0)
                ShowLayoutErrors(scriptErrors);
        }

        void OnStageLayoutCommitted()
        {
            OnDocumentLoaded();
        }

        void DetachLegacyStageLists()
        {
            lvTargets = null;
            lvWeathers = null;
            lvReinforces = null;
            nudTargetType = null;
            nudTargetValue = null;
            nudTargetParam1 = null;
            nudTargetParam2 = null;
            nudTargetFlag = null;
            nudWeatherType = null;
            nudWeatherStart = null;
            nudWeatherDuration = null;
            nudRpCellIdx = null;
            nudRpFactionId = null;
            nudRpFlag = null;
            chkRpIsKey = null;
        }

        void ReloadLayoutClick(object sender, EventArgs e)
        {
            var previous = _stageLayout;
            try
            {
                TryInstallStageLayout();
            }
            catch (Exception ex)
            {
                ShowLayoutErrors(new List<string> { ex.Message });
                if (previous != null && _stageLayout == null)
                    _stageLayout = previous;
            }
        }

        static void ShowLayoutErrors(List<string> errors)
        {
            if (errors == null || errors.Count == 0) return;
            MessageBox.Show(string.Join("\n", errors), "布局问题", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }

        static string FindLayoutDir(string name)
        {
            string game = GameSettings.GetGameDataRoot();
            if (!string.IsNullOrEmpty(game))
            {
                string sibling = Path.GetFullPath(Path.Combine(game, "..", name));
                if (Directory.Exists(sibling)) return sibling;
            }
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir != null)
            {
                string candidate = Path.Combine(dir.FullName, name);
                if (Directory.Exists(candidate)) return candidate;
                dir = dir.Parent;
            }
            return Path.Combine(AppContext.BaseDirectory, name);
        }
    }
}
