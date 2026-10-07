using BtlCore.Front;
using BtlCore.Scripting;

namespace BtldMapEditor
{
    public partial class MainEditorForm
    {
        readonly Dictionary<string, LayoutPageControl> _editorPages = new(StringComparer.Ordinal);
        ScriptHost _stageHost;
        LayoutBundle _layoutBundle;
        readonly HashSet<string> _staleEditorPages = new(StringComparer.Ordinal);
        string ActivePageId => tabControlRight.SelectedTab?.Name;
        LayoutPageControl TerrainPage => _editorPages.GetValueOrDefault("terrain");
        static readonly HashSet<string> NativeHandlers = new(StringComparer.Ordinal)
        {
            "clear_brush", "random_variant", "random_offset", "global_random_variant", "global_random_offset", "create_map"
        };

        // 先完整创建新布局，再替换；热加载失败时保留当前 XML 页面，不创建旧 UI。
        void InstallEditorLayout()
        {
            var result = LayoutLoader.Load(FindLayoutDir("EditorLayout"), FindLayoutDir("EditorLayout.user"));
            if (!result.Ok) { ShowLayoutErrors(result.Errors); return; }
            var host = new ScriptHost();
            var pages = new Dictionary<string, LayoutPageControl>(StringComparer.Ordinal);
            var tabs = new List<TabPage>();
            var errors = new List<string>();
            try
            {
                foreach (var script in result.Bundle.Scripts)
                {
                    try { host.Execute(script.Text, script.Name); }
                    catch (Exception ex) { throw new InvalidDataException(script.Path + "：" + ex.GetBaseException().Message, ex); }
                }
                if (HasDoc) host.Load(Doc);
                foreach (var tab in result.Bundle.Tabs)
                {
                    if (tab.Builtin || tab.Page == null)
                        throw new InvalidDataException("页面必须使用 XML page，不能使用旧 builtin 页面：" + tab.Id);
                    foreach (var command in tab.Page.Sections.SelectMany(s => s.Commands))
                        if (!string.IsNullOrEmpty(command.Handler) && !NativeHandlers.Contains(command.Handler))
                            throw new InvalidDataException("未知界面动作：" + command.Handler);
                    LayoutLoader.MarkMissingActions(tab.Page, host.CanRun, host.CanGet);
                    if (tab.Placement == "dialog") continue;
                    var view = new LayoutPageControl(tab.Page, host, OnXmlCommitted, RunNativeCommand);
                    if (_strictLayout) view.ErrorOccurred += ex => throw new InvalidOperationException("XML UI 操作失败", ex);
                    pages.Add(tab.Id, view);
                    var page = new TabPage(tab.Title) { Name = tab.Id, AutoScroll = true, Padding = new Padding(6) };
                    page.Controls.Add(view);
                    tabs.Add(page);
                }
            }
            catch (Exception ex)
            {
                foreach (var tab in tabs) tab.Dispose();
                errors.Add(ex.GetBaseException().Message);
                ShowLayoutErrors(errors);
                return;
            }

            string selected = ActivePageId;
            ClearTerrainBrush();
            tabControlRight.SuspendLayout();
            var oldTabs = tabControlRight.TabPages.Cast<TabPage>().ToArray();
            tabControlRight.TabPages.Clear();
            _editorPages.Clear();
            _stageHost = host;
            _layoutBundle = result.Bundle;
            foreach (var pair in pages) _editorPages.Add(pair.Key, pair.Value);
            tabControlRight.TabPages.AddRange(tabs.ToArray());
            foreach (var tab in oldTabs) tab.Dispose();
            tabControlRight.SelectedTab = tabs.Find(t => t.Name == selected) ?? tabs.FirstOrDefault();
            tabControlRight.ResumeLayout(true);
            WireTerrainPalette();
            RebuildToolsMenu();
            RefreshEditorPages();
            RefreshSelectedTerrainUi();
        }

        void WireTerrainPalette()
        {
            var terrain = TerrainPage;
            if (terrain == null) return;
            void SelectBrush(PaletteItem item, PaletteTarget target)
            {
                _terrainBrush = new PaletteDrag { Item = item, Target = target };
                terrain.SetText("brush", "画笔：" + item.Label + " → " + PaletteTargetName(target));
            }
            terrain.PaletteSelected += SelectBrush;
            terrain.PaletteActivated += (item, target) =>
            {
                SelectBrush(item, target);
                if (_selectedCellIdx >= 0) ApplyPaletteToCell(_selectedCellIdx, item, target, true);
            };
            terrain.PaletteClearRequested += target =>
            {
                if (_selectedCellIdx >= 0) ClearPaletteLayer(_selectedCellIdx, target, true);
            };
            terrain.PaletteTargetChanged += target =>
            {
                if (_terrainBrush != null && _terrainBrush.Target != PaletteTarget.Climate && target != PaletteTarget.Climate)
                    SelectBrush(_terrainBrush.Item, target);
                RefreshSelectedTerrainUi();
            };
        }

        void RefreshEditorPages()
        {
            if (!HasDoc) return;
            _stageHost.Load(Doc);
            foreach (var id in _editorPages.Keys) _staleEditorPages.Add(id);
            RefreshActiveEditorPage();
        }

        void RefreshActiveEditorPage()
        {
            if (!HasDoc || ActivePageId == null || !_editorPages.TryGetValue(ActivePageId, out var page)) return;
            if (_staleEditorPages.Remove(ActivePageId))
            {
                page.SetCell(_selectedCellIdx, refresh: false);
                page.Reload(Doc);
            }
            else page.SetCell(_selectedCellIdx);
            RefreshSelectedTerrainUi();
        }

        void OnXmlCommitted()
        {
            if (!HasDoc) return;
            mapCanvas.ReloadCells(Doc);
            if (_selectedCellIdx >= mapCanvas.Cells.Count) _selectedCellIdx = -1;
            RefreshEditorPages();
            RefreshSelectedTerrainUi();
            AddHistoryState();
            RefreshRegistryViewIfVisible();
        }

        bool RunEdit(string action, ScriptArgs args)
        {
            if (!HasDoc || _stageHost == null) return false;
            if (!_stageHost.CanRun(action))
            {
                ShowLayoutErrors(new List<string> { "没有可用的脚本动作：" + action });
                return false;
            }
            _stageHost.Load(Doc);
            try { _stageHost.Run(action, args ?? new ScriptArgs()); }
            catch (Exception ex)
            {
                mapCanvas.ReloadCells(Doc);
                RefreshEditorPages();
                ShowLayoutErrors(new List<string> { action + "：" + ex.GetBaseException().Message });
                return false;
            }
            mapCanvas.ReloadCells(Doc);
            return true;
        }

        void RunNativeCommand(string name, ScriptArgs args)
        {
            switch (name)
            {
                case "clear_brush": ClearTerrainBrush(); break;
                case "random_variant": PerformRandomVariantAction(); break;
                case "random_offset": PerformRandomOffsetAction(); break;
                case "global_random_variant": PerformGlobalRandomVariantAction(); break;
                case "global_random_offset": PerformGlobalRandomOffsetAction(); break;
                default: throw new InvalidOperationException("未知界面动作：" + name);
            }
        }

        void ReloadLayoutClick(object sender, EventArgs e) => InstallEditorLayout();

        void NewMapClick(object sender, EventArgs e)
        {
            var tab = _layoutBundle?.Tabs.Find(t => t.Id == "newMap" && t.Placement == "dialog");
            if (tab?.Page == null) { ShowLayoutErrors(new List<string> { "布局中缺少 newMap 对话框" }); return; }
            using var dialog = new Form { Text = tab.Title, Size = new Size(460, 550), StartPosition = FormStartPosition.CenterParent,
                AutoScroll = true, MinimizeBox = false, MaximizeBox = false };
            var host = new ScriptHost();
            host.Load(new BtlFrontDocument { Root = BtlFrontJson.NewTable() });
            var view = new LayoutPageControl(tab.Page, host, null, (name, args) =>
            {
                if (name != "create_map") throw new InvalidOperationException("新建窗口只支持 create_map");
                ushort Number(string key) => Convert.ToUInt16(args.Input[key] ?? throw new InvalidDataException("请填写 " + key));
                ushort w = Number("width"), h = Number("height"), lm = Number("left"), tm = Number("top");
                ushort pw = Number("play_width"), ph = Number("play_height");
                if (w == 0 || h == 0 || w > 500 || h > 500 || lm + pw > w || tm + ph > h || pw == 0 || ph == 0)
                    throw new InvalidDataException("地图尺寸和可游玩区域不匹配");
                CreateNewMap(w, h, lm, tm, pw, ph, Number("round_limit"), Number("version"), 0);
                dialog.DialogResult = DialogResult.OK;
                dialog.Close();
            });
            dialog.Controls.Add(view);
            view.Reload(host.Document);
            dialog.ShowDialog(this);
        }

        static Dictionary<string, object> EditInput(params (string Key, object Value)[] pairs) => pairs.ToDictionary(p => p.Key, p => p.Value);

        static string FindLayoutDir(string name)
        {
            string game = GameSettings.GetGameDataRoot();
            if (!string.IsNullOrEmpty(game))
            {
                string sibling = Path.GetFullPath(Path.Combine(game, "..", name));
                if (Directory.Exists(sibling)) return sibling;
            }
            for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir != null; dir = dir.Parent)
            {
                string candidate = Path.Combine(dir.FullName, name);
                if (Directory.Exists(candidate)) return candidate;
            }
            return Path.Combine(AppContext.BaseDirectory, name);
        }

        void ShowLayoutErrors(List<string> errors)
        {
            if (_strictLayout && errors.Count > 0) throw new InvalidDataException(string.Join("\n", errors));
            if (errors.Count > 0) MessageBox.Show(string.Join("\n", errors), "布局问题", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }
}
