using BtlCore.Front;
using BtlCore.Scripting;

namespace BtldMapEditor;

public partial class MainEditorForm
{
    ToolStripMenuItem _toolsMenu;

    void RebuildToolsMenu()
    {
        if (_toolsMenu == null)
        {
            _toolsMenu = new ToolStripMenuItem("脚本工具");
            _toolsMenu.DropDownOpening += (_, _) =>
            {
                foreach (ToolStripItem item in _toolsMenu.DropDownItems) item.Enabled = HasDoc && item.Tag is ScriptTool;
            };
            menuStrip.Items.Add(_toolsMenu);
        }
        foreach (var item in _toolsMenu.DropDownItems.Cast<ToolStripItem>().ToArray()) item.Dispose();
        _toolsMenu.DropDownItems.Clear();
        foreach (var tool in _stageHost.Tools)
        {
            var captured = tool;
            var item = new ToolStripMenuItem(tool.Title + "（" + ScriptToolDialog.ScopeLabel(tool.Target) + "）")
            {
                Name = tool.Id, Tag = tool, ToolTipText = tool.Description, Enabled = HasDoc
            };
            item.Click += (_, _) => OpenTool(captured);
            _toolsMenu.DropDownItems.Add(item);
        }
        if (_stageHost.Tools.Count == 0)
            _toolsMenu.DropDownItems.Add(new ToolStripMenuItem("将 Lua 工具放入 EditorLayout.user/tools 后重新加载布局") { Enabled = false });
    }

    ScriptArgs ToolSelection() => new()
    {
        CellIndex = _selectedCellIdx >= 0 ? _selectedCellIdx : null,
        UnitIndex = ActivePageId == "unit" ? _editorPages.GetValueOrDefault("unit")?.SelectedRow("/6/0") : null,
        FactionIndex = ActivePageId == "faction" ? _editorPages.GetValueOrDefault("faction")?.SelectedRow("/4/0") : null
    };

    void OpenTool(ScriptTool tool)
    {
        if (!HasDoc) return;
        var selection = ToolSelection();
        if (tool.Parameters.Count == 0)
        {
            if (!ConfirmTool(tool)) return;
            try { ExecuteTool(tool.Id, selection); }
            catch (Exception ex) { ShowToolError(ex); }
            return;
        }
        using var dialog = new ScriptToolDialog(tool, ToolChoices, input =>
        {
            if (!ConfirmTool(tool)) return;
            selection.Input = input;
            ExecuteTool(tool.Id, selection);
        });
        if (_strictLayout) dialog.ErrorOccurred += ex => throw new InvalidOperationException("脚本工具失败", ex);
        dialog.ShowDialog(this);
    }

    bool ConfirmTool(ScriptTool tool) => string.IsNullOrEmpty(tool.Confirmation)
        || MessageBox.Show(this, tool.Confirmation, tool.Title, MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes;

    int ExecuteTool(string id, ScriptArgs selection)
    {
        if (!HasDoc) throw new FrontEditException("请先打开关卡");
        _stageHost.Load(Doc);
        int count;
        try { count = _stageHost.RunTool(id, selection); }
        catch
        {
            mapCanvas.ReloadCells(Doc);
            RefreshEditorPages();
            RefreshSelectedTerrainUi();
            throw;
        }
        OnXmlCommitted();
        statusLabel.Text = "工具执行完成，处理 " + count + " 个对象；可用撤销恢复修改";
        return count;
    }

    void ShowToolError(Exception error)
    {
        if (_strictLayout) throw new InvalidOperationException("脚本工具失败", error);
        MessageBox.Show(this, error.Message, "脚本工具", MessageBoxButtons.OK, MessageBoxIcon.Warning);
    }

    IReadOnlyList<ToolChoice> ToolChoices(ToolParameter parameter)
    {
        if (parameter.Kind == "choice") return parameter.Choices;
        if (parameter.Kind == "faction")
        {
            var result = new List<ToolChoice>();
            var root = Doc?.Root.F.GetValueOrDefault(4) as BtlTable;
            var factions = root?.F.GetValueOrDefault(0) as BtlVector;
            if (factions == null) return result;
            foreach (var row in factions.V.OfType<BtlTable>())
            {
                if (row.F.GetValueOrDefault(0) is not BtlStruct info || info.V.Count < 2) continue;
                int id = Convert.ToInt32(info.V[0]), country = Convert.ToInt32(info.V[1]);
                result.Add(new ToolChoice("势力 " + id + " · " + GameSettings.GetCountryName(country), id));
            }
            return result;
        }
        var names = parameter.Kind == "unit_type" ? GameSettings.Units : GameSettings.Generals;
        return names.OrderBy(p => p.Key).Select(p => new ToolChoice(p.Value + "（" + p.Key + "）", p.Key)).ToArray();
    }
}
