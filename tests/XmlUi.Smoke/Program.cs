using System.Reflection;
using System.Text.Json.Nodes;
using BtlCore.Fb;
using BtlCore.Front;
using BtlCore.Scripting;
using BtldMapEditor;

internal static class Program
{
    [STAThread]
    static int Main(string[] args)
    {
        try
        {
            ApplicationConfiguration.Initialize();
            Application.SetUnhandledExceptionMode(UnhandledExceptionMode.ThrowException);
            string root = FindRoot();
            var loaded = LayoutLoader.Load(Path.Combine(root, "EditorLayout"), null);
            Check(loaded.Ok, string.Join("\n", loaded.Errors));
            Check(loaded.Bundle.Tabs.All(t => !t.Builtin && t.Page != null), "all pages must be XML");

            Console.WriteLine("Creating XML editor...");
            using var form = new MainEditorForm(true) { ShowInTaskbar = false, Opacity = 0, Location = new Point(-32000, -32000) };
            form.StartPosition = FormStartPosition.Manual;
            form.Show();
            var pages = Field<Dictionary<string, LayoutPageControl>>(form, "_editorPages");
            Check(pages.Count == 6, "six XML editor tabs");
            Check(Descendants(form).OfType<LayoutPageControl>().Count() == 6, "one XML view per tab");
            Check(typeof(MainEditorForm).GetField("gbUnit", BindingFlags.Instance | BindingFlags.NonPublic) == null,
                "legacy controls must be removed");
            Call(form, "ApplyJsonFile", Path.Combine(root, "src/BtldMapEditor/samples/stage10101.json"));
            Console.WriteLine("Loading sample into generated controls...");
            Call(form, "OnDocumentLoaded");
            Call(form, "InitHistory");
            Call(form, "CellSelectedClick", 0);
            var host = Field<ScriptHost>(form, "_stageHost");
            var doc = host.Document;
            string before = BtlFrontJson.Serialize(doc);
            foreach (var page in pages.Values) page.Reload(doc);
            Check(before == BtlFrontJson.Serialize(doc), "page refresh must not modify document");
            Check(pages["stage"].Controls.Count == 1, "stage contains only generated root");
            Check(Descendants(pages["faction"]).OfType<Button>().Any(b => b.Text == "保存路线"), "XML route editor");
            Check(Descendants(pages["terrain"]).OfType<TerrainPalettePanel>().Count() == 4, "four XML palettes");

            // 热加载既不能增加第二套控件，也不能改变文档。
            Console.WriteLine("Testing hot reload...");
            Call(form, "InstallEditorLayout");
            pages = Field<Dictionary<string, LayoutPageControl>>(form, "_editorPages");
            Check(pages.Count == 6 && Descendants(form).OfType<LayoutPageControl>().Count() == 6, "reload must replace old views");
            host = Field<ScriptHost>(form, "_stageHost");
            Check(before == BtlFrontJson.Serialize(host.Document), "layout reload must preserve data");

            // 在真实生成的控件上测试 checkbox 提交、历史和撤销。
            Console.WriteLine("Testing checkbox history...");
            Field<TabControl>(form, "tabControlRight").SelectedTab = pages["stage"].Parent as TabPage;
            var fog = Descendants(pages["stage"]).OfType<CheckBox>().Single(c => c.Text == "启用战争迷雾");
            bool oldFog = fog.Checked;
            int historyBefore = Field<List<BtlFrontDocument>>(form, "_history").Count;
            fog.Checked = !oldFog;
            Check(Field<List<BtlFrontDocument>>(form, "_history").Count == historyBefore + 1, "checkbox commits one history frame");
            Call(form, "PerformUndoAction");
            Check(fog.Checked == oldFog, "undo refreshes XML checkbox");
            Call(form, "PerformRedoAction");
            Check(fog.Checked != oldFog, "redo refreshes XML checkbox");

            Console.WriteLine("Testing automatic Lua tools and history...");
            var toolsMenu = Field<ToolStripMenuItem>(form, "_toolsMenu");
            Check(toolsMenu.DropDownItems.Count == host.Tools.Count && host.Tools.Count == 7, "one automatic menu item per Lua tool");
            var sampleAgents = (BtlVector)((BtlTable)host.Document.Root.F[6]).F[0];
            var sampleInfo = (BtlStruct)((BtlTable)sampleAgents.V[0]).F[0];
            sampleInfo.V[6] = (ushort)0;
            Call(form, "OnDocumentLoaded"); Call(form, "InitHistory");
            Call(form, "CellSelectedClick", Convert.ToInt32(sampleInfo.V[0]));
            string beforeTool = BtlFrontJson.Serialize(host.Document);
            int toolHistory = Field<List<BtlFrontDocument>>(form, "_history").Count;
            toolsMenu.DropDownItems.Cast<ToolStripMenuItem>().Single(t => t.Name == "heal_selected").PerformClick();
            Check(Convert.ToInt32(sampleInfo.V[6]) == Convert.ToInt32(sampleInfo.V[7]), "automatic tool runs from menu");
            Check(Field<List<BtlFrontDocument>>(form, "_history").Count == toolHistory + 1, "tool adds one undo frame");
            Call(form, "PerformUndoAction");
            Check(beforeTool == BtlFrontJson.Serialize(host.Document), "tool undo restores whole document");
            Call(form, "PerformRedoAction");

            var climateTool = host.Tools.Single(t => t.Id == "cell_climate");
            Dictionary<string, object> toolInput = null;
            using (var inputDialog = new ScriptToolDialog(climateTool, p => p.Choices, input =>
            {
                toolInput = input;
                Call(form, "ExecuteTool", climateTool.Id, new ScriptArgs { CellIndex = 0, Input = input });
            })
            { ShowInTaskbar = false, Opacity = 0, StartPosition = FormStartPosition.Manual, Location = new Point(-32000, -32000) })
            {
                inputDialog.ErrorOccurred += error => throw new InvalidOperationException("tool parameter dialog", error);
                inputDialog.Show();
                var choice = Descendants(inputDialog).OfType<ComboBox>().Single();
                Check(choice.Items.Count == 5 && choice.SelectedIndex == 0, "Lua choices and defaults generate controls");
                if (args.Contains("--render")) Render(inputDialog, root, "tool-climate");
                choice.SelectedIndex = 2;
                Descendants(inputDialog).OfType<Button>().Single(b => b.Name == "run_tool").PerformClick();
                Check(Convert.ToInt32(toolInput["climate"]) == 2, "generated choice passes numeric value, not label");
                Check((TerrainEdits.Project(host.Document)[0].Terrain & 7) == 2, "generated parameter dialog commits to actual sample");
            }

            Console.WriteLine("Testing remaining stock tools through generated parameter controls...");
            var actualFaction = (BtlStruct)((BtlTable)((BtlVector)((BtlTable)host.Document.Root.F[4]).F[0]).V[0]).F[0];
            int factionId = Convert.ToInt32(actualFaction.V[0]);
            foreach (string toolId in new[] { "faction_level", "add_gold", "offset_main" })
            {
                int selectedCell = TerrainEdits.Project(host.Document).FindIndex(c => c.Main != null);
                var tool = host.Tools.Single(t => t.Id == toolId);
                var selection = new ScriptArgs { CellIndex = selectedCell, FactionIndex = 0 };
                using var dialog = new ScriptToolDialog(tool,
                    p => p.Kind == "faction" ? new[] { new ToolChoice("实际关卡势力 " + factionId, factionId) } : p.Choices,
                    input => { selection.Input = input; Call(form, "ExecuteTool", tool.Id, selection); })
                    { ShowInTaskbar = false, Opacity = 0, StartPosition = FormStartPosition.Manual, Location = new Point(-32000, -32000) };
                dialog.ErrorOccurred += error => throw new InvalidOperationException("stock tool " + toolId, error);
                dialog.Show();
                long goldBefore = Convert.ToInt64(actualFaction.V[6]);
                foreach (var input in Descendants(dialog).OfType<TextBox>())
                    if (input.Name == "level") input.Text = "23";
                    else if (input.Name == "amount") input.Text = "17";
                    else if (input.Name == "dx") input.Text = "5";
                    else if (input.Name == "dy") input.Text = "-6";
                Descendants(dialog).OfType<Button>().Single(b => b.Name == "run_tool").PerformClick();
                if (toolId == "add_gold") Check(Convert.ToInt64(actualFaction.V[6]) == goldBefore + 17, "gold tool commits generated input");
                if (toolId == "offset_main") Check(Convert.ToInt32(TerrainEdits.Project(host.Document)[selectedCell].Main.V[3]) == -6, "offset tool commits signed input");
                if (toolId == "faction_level")
                    Check(((BtlVector)((BtlTable)host.Document.Root.F[6]).F[0]).V.Cast<BtlTable>()
                        .Where(u => Convert.ToInt32(((BtlStruct)u.F[0]).V[1]) == factionId)
                        .All(u => (Convert.ToInt32(((BtlStruct)u.F[0]).V[4]) & 255) == 23), "faction picker uses actual ID");
            }
            Call(form, "CellSelectedClick", 0);
            toolsMenu.DropDownItems.Cast<ToolStripMenuItem>().Single(t => t.Name == "cell_sea").PerformClick();
            Check((TerrainEdits.Project(host.Document)[0].Terrain & 512) != 0, "sea tool commits from actual menu");
            using (var parameterDialog = new ScriptToolDialog(host.Tools.Single(t => t.Id == "faction_level"),
                p => p.Kind == "faction" ? new[] { new ToolChoice("势力 7", 7) } : p.Choices, _ => { })
                { ShowInTaskbar = false, Opacity = 0, StartPosition = FormStartPosition.Manual, Location = new Point(-32000, -32000) })
            {
                var input = parameterDialog.ReadInput();
                Check(Convert.ToInt32(input["faction"]) == 7 && Convert.ToInt32(input["level"]) == 10,
                    "implicit faction picker and declared numeric default");
                if (args.Contains("--render")) { parameterDialog.Show(); Render(parameterDialog, root, "tool-faction-level"); }
            }

            Console.WriteLine("Testing tool commits preserve vector order and unprojected data...");
            host = Field<ScriptHost>(form, "_stageHost");
            sampleAgents = (BtlVector)((BtlTable)host.Document.Root.F[6]).F[0];
            var first = sampleAgents.V[0]; sampleAgents.V[0] = sampleAgents.V[^1]; sampleAgents.V[^1] = first;
            var mapAttrs = (BtlVector)((BtlTable)host.Document.Root.F[1]).F[3];
            mapAttrs.V.Add(FrontEdit.StructFromFbs("TileAttr", (byte)99, (byte)7, (sbyte)-8, (sbyte)6));
            // 重叠部队无法同时显示，但具名工具及保存仍应保留全部行。
            var duplicate = BtlFrontJson.Clone((BtlTable)sampleAgents.V[0]);
            sampleAgents.V.Add(duplicate);
            Call(form, "OnDocumentLoaded"); Call(form, "InitHistory");
            var expectedToolDoc = BtlFrontJson.CloneDocument(host.Document);
            foreach (var row in ((BtlVector)((BtlTable)expectedToolDoc.Root.F[6]).F[0]).V.Cast<BtlTable>())
            {
                var info = (BtlStruct)row.F[0];
                info.V[6] = info.V[7];
                if (info.Layout.Count == 0) info.Layout.AddRange(Enumerable.Repeat("u16", 10));
            }
            Call(form, "ExecuteTool", "heal_all", new ScriptArgs());
            Check(BtlFrontJson.Serialize(expectedToolDoc) == BtlFrontJson.Serialize(host.Document),
                "tool commit preserves original order, overlapping units, and trailing attributes");
            Check(BtlFrontJson.Serialize(expectedToolDoc) == (string)typeof(MainEditorForm)
                .GetMethod("SnapshotFrontJson", BindingFlags.NonPublic | BindingFlags.Instance).Invoke(form, null),
                "later canvas sync leaves committed document intact");

            // 独立的小样例验证动态树的列表、节点选择和提交。
            Console.WriteLine("Testing packed JSON tree controls...");
            var treeDoc = new BtlFrontDocument { Root = BtlFrontJson.NewTable() };
            var bt = new BtlVector { T = "vector", Elem = "u8", Enc = PackedJsonStream.EncName };
            bt.V.Add(JsonNode.Parse("{\"btid\":1,\"name\":\"test\",\"id\":1,\"class\":\"bt\",\"node\":[{\"class\":\"seq\",\"node\":[{\"class\":\"act\",\"method\":\"Old\"}]}]}"));
            treeDoc.Root.F[10] = bt;
            var treeHost = new ScriptHost();
            foreach (var script in loaded.Bundle.Scripts) treeHost.Execute(script.Text, script.Name);
            treeHost.Load(treeDoc);
            LayoutPageControl treeView = null;
            treeView = new LayoutPageControl(loaded.Bundle.Tabs.Single(t => t.Id == "country").Page, treeHost, () => treeView.Reload(treeDoc));
            treeView.ErrorOccurred += ex => throw new InvalidOperationException("tree UI", ex);
            using var treeForm = new Form { ShowInTaskbar = false, Opacity = 0, StartPosition = FormStartPosition.Manual, Location = new Point(-32000, -32000) };
            treeForm.Controls.Add(treeView);
            treeForm.Show();
            treeView.Reload(treeDoc);
            var list = Descendants(treeView).OfType<ListView>().Single();
            list.Items[0].Selected = true;
            var nodes = Descendants(treeView).OfType<TreeView>().Single();
            Check(nodes.Nodes.Count == 1 && nodes.Nodes[0].Nodes.Count == 1, "packed JSON renders nested nodes");
            nodes.SelectedNode = nodes.Nodes[0].Nodes[0];
            var methodBox = Descendants(treeView).OfType<TextBox>().Single(t => t.Text == "Old");
            methodBox.Text = "Changed";
            Descendants(treeView).OfType<Button>().Single(b => b.Text == "保存树和节点属性").PerformClick();
            Check(((JsonObject)bt.V[0])["node"][0]["node"][0]["method"].ToString() == "Changed", "XML tree command edits selected nested node");

            Console.WriteLine("Testing off-map row mapping and new-map defaults...");
            var unitDoc = new BtlFrontDocument { Root = BtlFrontJson.NewTable() };
            var unitMap = FrontEdit.EnsureTable(unitDoc.Root, 1);
            FrontEdit.SetField(unitMap, 0, FrontEdit.StructFromFbs("Size", (ushort)2, (ushort)1, (ushort)0, (ushort)0, (ushort)2, (ushort)1));
            var tiles = FrontEdit.EnsureVec(unitMap, 2, "u16");
            tiles.V.Add((ushort)0); tiles.V.Add((ushort)0);
            var agents = FrontEdit.EnsureVec(FrontEdit.EnsureTable(unitDoc.Root, 6), 0, "table");
            foreach (ushort cell in new ushort[] { 0, 65535, 1 })
            {
                var agent = BtlFrontJson.NewTable();
                FrontEdit.SetField(agent, 0, FrontEdit.StructFromFbs("AgentInfo", cell, (ushort)0, (ushort)(cell == 65535 ? 2 : cell),
                    (ushort)101, (ushort)256, (ushort)0, (ushort)10, (ushort)20, (ushort)0, (ushort)0));
                agents.V.Add(agent);
            }
            var unitHost = new ScriptHost();
            foreach (var script in loaded.Bundle.Scripts) unitHost.Execute(script.Text, script.Name);
            unitHost.Load(unitDoc);
            LayoutPageControl unitView = null;
            unitView = new LayoutPageControl(loaded.Bundle.Tabs.Single(t => t.Id == "unit").Page, unitHost, () => unitView.Reload(unitDoc));
            unitView.ErrorOccurred += ex => throw new InvalidOperationException("unit UI", ex);
            treeForm.Controls.Clear(); treeForm.Controls.Add(unitView);
            unitView.Reload(unitDoc);
            var outsideList = Descendants(unitView).OfType<ListView>().Single();
            Check(outsideList.Items.Count == 1 && (int)outsideList.Items[0].Tag == 1, "filtered rows keep original agent index");
            outsideList.Items[0].Selected = true;
            Console.WriteLine("Editing selected off-map row...");
            var outsideGroup = Descendants(unitView).OfType<GroupBox>().Single(g => g.Text == "地图外部队 / 增援部队");
            Descendants(outsideGroup).OfType<NullableNumericUpDown>().Single(n => n.Name == "hp").NullableValue = 17;
            Descendants(outsideGroup).OfType<Button>().Single(b => b.Text == "保存选中部队").PerformClick();
            Check(Convert.ToInt32(((BtlStruct)((BtlTable)agents.V[1]).F[0]).V[6]) == 17, "off-map edit targets filtered source row");
            Check(Convert.ToInt32(((BtlStruct)((BtlTable)agents.V[0]).F[0]).V[6]) == 10, "off-map edit preserves on-map row");

            Console.WriteLine("Testing virtual list selection and commits...");
            for (int i = 0; i < 512; i++)
                agents.V.Add(UnitEdits.Create(65535, 1, (ushort)(100 + i), 101, 1, 0, 10, 20));
            unitView.Reload(unitDoc);
            Check(outsideList.VirtualMode && outsideList.VirtualListSize == 513, "large filtered list is virtual");
            Check((int)outsideList.Items[400].Tag == 402, "virtual display index maps to source vector index");
            outsideList.Items[400].Selected = true;
            Descendants(outsideGroup).OfType<NullableNumericUpDown>().Single(n => n.Name == "hp").NullableValue = 23;
            Descendants(outsideGroup).OfType<Button>().Single(b => b.Text == "保存选中部队").PerformClick();
            Check(Convert.ToInt32(((BtlStruct)((BtlTable)agents.V[402]).F[0]).V[6]) == 23, "virtual row commit reaches correct object");
            Check(Convert.ToInt32(((BtlStruct)((BtlTable)agents.V[401]).F[0]).V[6]) == 10, "virtual row commit preserves neighbors");
            agents.V.RemoveRange(3, agents.V.Count - 3);
            unitView.Reload(unitDoc);
            Check(!outsideList.VirtualMode && outsideList.Items.Count == 1, "virtual list can become small again");

            Console.WriteLine("Testing route number and integer-list editors...");
            var routes = FrontEdit.EnsureVec((BtlTable)unitDoc.Root.F[6], 1, "table");
            var route = BtlFrontJson.NewTable();
            FrontEdit.SetScalar(route, 0, "u16", (ushort)1);
            var routeCells = FrontEdit.EnsureVec(route, 1, "u16");
            routeCells.V.Add((ushort)0); routeCells.V.Add((ushort)1);
            routes.V.Add(route);
            LayoutPageControl factionView = null;
            factionView = new LayoutPageControl(loaded.Bundle.Tabs.Single(t => t.Id == "faction").Page, unitHost, () => factionView.Reload(unitDoc));
            factionView.ErrorOccurred += ex => throw new InvalidOperationException("route UI", ex);
            treeForm.Controls.Clear(); treeForm.Controls.Add(factionView);
            factionView.Reload(unitDoc);
            var routeGroup = Descendants(factionView).OfType<GroupBox>().Single(g => g.Text == "路线");
            Descendants(routeGroup).OfType<ListView>().Single().Items[0].Selected = true;
            Descendants(routeGroup).OfType<NullableNumericUpDown>().Single(n => n.Name == "flag").NullableValue = 3;
            Descendants(routeGroup).OfType<TextBox>().Single(n => n.Name == "cells").Text = "1, 0, 1";
            Descendants(routeGroup).OfType<Button>().Single(b => b.Text == "保存路线").PerformClick();
            Check(Convert.ToInt32(((BtlScalar)route.F[0]).V) == 3 && ((BtlVector)route.F[1]).V.Count == 3,
                "route numeric and integer-list values remain typed");

            ScriptArgs created = null;
            var newMapView = new LayoutPageControl(loaded.Bundle.Tabs.Single(t => t.Id == "newMap").Page, unitHost, null,
                (name, values) => { Check(name == "create_map", "XML new-map handler"); created = values; });
            treeForm.Controls.Clear(); treeForm.Controls.Add(newMapView);
            newMapView.Reload(unitDoc);
            newMapView.ErrorOccurred += ex => throw new InvalidOperationException("new map UI", ex);
            Console.WriteLine("Testing new-map command...");
            Descendants(newMapView).OfType<Button>().Single().PerformClick();
            Check(created != null && Convert.ToInt32(created.Input["width"]) == 30 && Convert.ToInt32(created.Input["version"]) == 1,
                "XML dialog defaults and native command input");

            if (args.Contains("--render"))
            {
                string output = Path.Combine(root, "artifacts", "xml-ui");
                Directory.CreateDirectory(output);
                var tabs = Field<TabControl>(form, "tabControlRight");
                foreach (TabPage tab in tabs.TabPages)
                {
                    tabs.SelectedTab = tab;
                    Application.DoEvents();
                    using var bitmap = new Bitmap(form.Width, form.Height);
                    form.DrawToBitmap(bitmap, new Rectangle(Point.Empty, form.Size));
                    bitmap.Save(Path.Combine(output, tab.Name + ".png"));
                }
                Console.WriteLine("Rendered XML pages: " + output);
            }

            Console.WriteLine("XML UI smoke PASS: mount, sample load, read-only refresh, hot reload, checkbox/tool undo/redo, tool parameter UI, vector preservation, nested tree edit, off-map row mapping, route editors, new-map defaults.");
            return 0;
        }
        catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
    }

    static void Check(bool ok, string message) { if (!ok) throw new InvalidOperationException(message); }
    static void Render(Form form, string root, string name)
    {
        Application.DoEvents();
        string output = Path.Combine(root, "artifacts", "xml-ui"); Directory.CreateDirectory(output);
        using var bitmap = new Bitmap(form.Width, form.Height);
        form.DrawToBitmap(bitmap, new Rectangle(Point.Empty, form.Size));
        bitmap.Save(Path.Combine(output, name + ".png"));
    }
    static T Field<T>(object obj, string name) => (T)obj.GetType().GetField(name, BindingFlags.NonPublic | BindingFlags.Instance).GetValue(obj);
    static void Call(object obj, string name, params object[] args) => obj.GetType().GetMethod(name, BindingFlags.NonPublic | BindingFlags.Instance).Invoke(obj, args);
    static IEnumerable<Control> Descendants(Control root)
    {
        foreach (Control child in root.Controls)
        {
            yield return child;
            foreach (var nested in Descendants(child)) yield return nested;
        }
    }
    static string FindRoot()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir != null; dir = dir.Parent)
            if (Directory.Exists(Path.Combine(dir.FullName, "EditorLayout"))) return dir.FullName;
        throw new DirectoryNotFoundException("EditorLayout");
    }
}
