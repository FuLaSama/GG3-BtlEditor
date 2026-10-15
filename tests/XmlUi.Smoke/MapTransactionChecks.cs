using System.Reflection;
using BtlCore.Fb;
using BtlCore.Front;
using BtldMapEditor;
using BtldMapEditor.Front;

internal static class MapTransactionChecks
{
    const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    static T Field<T>(object obj, string name) => (T)obj.GetType().GetField(name, Private).GetValue(obj);
    static void Set(object obj, string name, object value) => obj.GetType().GetField(name, Private).SetValue(obj, value);
    static object Call(object obj, string name, params object[] args) => obj.GetType().GetMethod(name, Private).Invoke(obj, args);
    static void Check(bool ok, string message) { if (!ok) throw new InvalidOperationException(message); }

    public static void Run(string root)
    {
        using var form = new MainEditorForm(true) { ShowInTaskbar = false, Opacity = 0,
            StartPosition = FormStartPosition.Manual, Location = new Point(-32000, -32000) };
        form.Show();
        var session = FrontSession.NewMap(3, 1, 0, 0, 3, 1, 20, 1);
        var doc = session.Document;
        FrontNav.Tiles(doc).V[0] = (ushort)(2048 | 512 | 3);
        FrontNav.Attrs(doc).V.Add(FrontNav.NewAttr(60, 250, 100, -100));
        FrontNav.Attrs(doc).V.Add(FrontNav.NewAttr(99, 7, -8, 6)); // 投影外属性尾部
        foreach (ushort cell in new ushort[] { 0, 0, 65535 })
            FrontNav.Agents(doc).V.Add(FrontNav.NewAgent(cell, 1, 1, 1, 1, 0, 10, 10));
        FrontNav.Events(doc).V.Add(FrontNav.NewBuildingEvent(0, 1, 0, 0, null, null, null, null));
        FrontNav.Events(doc).V.Add(FrontNav.NewBuildingEvent(0, 2, 0, 0, null, null, null, null));
        Set(form, "_front", session);
        Call(form, "OnDocumentLoaded"); Call(form, "InitHistory");
        var canvas = Field<EditorMapCanvas>(form, "mapCanvas");
        var tabs = Field<TabControl>(form, "tabControlRight");
        tabs.SelectedTab = tabs.TabPages.Cast<TabPage>().Single(t => t.Name == "terrain");

        // 原生随机操作必须立即写文档，不重建重叠/离图实体及属性尾部。
        var before = BtlFrontJson.CloneDocument(doc);
        Call(form, "PerformGlobalRandomOffsetAction");
        var edited = BtlFrontJson.CloneDocument(doc);
        var expected = BtlFrontJson.CloneDocument(before);
        var expectedAttr = (BtlStruct)FrontNav.Attrs(expected).V[0];
        var editedAttr = (BtlStruct)FrontNav.Attrs(doc).V[0];
        expectedAttr.V[2] = editedAttr.V[2]; expectedAttr.V[3] = editedAttr.V[3];
        Check(BtlFrontJson.ContentEquals(expected, doc), "native offsets preserve all unrelated data");
        Check(!BtlFrontJson.ContentEquals(before, doc), "native offset applied before save");
        Check(doc.Editing.HistoryCount == 2, "native batch is one undo frame");
        Call(form, "PerformUndoAction"); Check(BtlFrontJson.ContentEquals(before, doc), "native undo");
        Call(form, "PerformRedoAction"); Check(BtlFrontJson.ContentEquals(edited, doc), "native redo");

        before = BtlFrontJson.CloneDocument(doc);
        Call(form, "PerformGlobalRandomVariantAction");
        expected = BtlFrontJson.CloneDocument(before);
        for (int i = 0; i < 3; i++)
        {
            ushort oldTile = (ushort)FrontNav.ToI64(FrontNav.Tiles(before).V[i]);
            ushort tile = (ushort)FrontNav.ToI64(FrontNav.Tiles(doc).V[i]);
            Check((oldTile & ~0xF8) == (tile & ~0xF8), "variant preserves climate and unknown flag bits");
            FrontNav.Tiles(expected).V[i] = tile;
        }
        ((BtlStruct)FrontNav.Attrs(expected).V[0]).V[1] = ((BtlStruct)FrontNav.Attrs(doc).V[0]).V[1];
        Check(BtlFrontJson.ContentEquals(expected, doc), "variant preserves extra attrs and overlapping entities");

        // 实际鼠标事件：第一次落笔就在组内，松手和失去捕获都关闭历史分组。
        Set(form, "_copiedTerrain", (ushort?)5);
        canvas.BrushMode = true;
        Point PointOf(int index)
        {
            Check(canvas.TryGetCellCenter(index, out float x, out float y), "cell center available");
            return new Point((int)x + canvas.AutoScrollPosition.X, (int)y + canvas.AutoScrollPosition.Y);
        }
        var p1 = PointOf(1); var p2 = PointOf(2);
        before = BtlFrontJson.CloneDocument(doc);
        int frames = doc.Editing.HistoryCount;
        Call(canvas, "OnMouseDown", new MouseEventArgs(MouseButtons.Left, 1, p1.X, p1.Y, 0));
        Check(doc.Editing.IsGroupingHistory && (ushort)FrontNav.ToI64(FrontNav.Tiles(doc).V[1]) == 5,
            $"first brush point commits inside group (group={doc.Editing.IsGroupingHistory}, tile={FrontNav.ToI64(FrontNav.Tiles(doc).V[1])}, brush={canvas.BrushMode}, capture={canvas.Capture}, keys={Control.ModifierKeys})");
        Call(canvas, "OnMouseMove", new MouseEventArgs(MouseButtons.Left, 0, p2.X, p2.Y, 0));
        Check((ushort)FrontNav.ToI64(FrontNav.Tiles(doc).V[2]) == 5, "second brush point commits immediately");
        Check(doc.Editing.HistoryCount == frames, "drag does not append per-cell history");
        Call(canvas, "OnMouseUp", new MouseEventArgs(MouseButtons.Left, 1, p2.X, p2.Y, 0));
        Check(!doc.Editing.IsGroupingHistory && doc.Editing.HistoryCount == frames + 1, "mouse release records one frame");
        Call(form, "PerformUndoAction"); Check(BtlFrontJson.ContentEquals(before, doc), "one undo restores entire brush stroke");
        Set(form, "_copiedTerrain", (ushort?)6);
        Call(canvas, "OnMouseDown", new MouseEventArgs(MouseButtons.Left, 1, p1.X, p1.Y, 0));
        canvas.Capture = false;
        Check(!doc.Editing.IsGroupingHistory, "capture loss completes gesture");
        Call(canvas, "OnMouseMove", new MouseEventArgs(MouseButtons.Left, 0, p2.X, p2.Y, 0));
        Check((ushort)FrontNav.ToI64(FrontNav.Tiles(doc).V[2]) != 6, "capture loss stops further painting");
        Call(form, "PerformUndoAction"); Check(BtlFrontJson.ContentEquals(before, doc), "interrupted stroke remains undoable");
        Call(canvas, "OnMouseDown", new MouseEventArgs(MouseButtons.Left, 1, p1.X, p1.Y, 0));
        Call(form, "PerformUndoAction");
        Call(canvas, "OnMouseMove", new MouseEventArgs(MouseButtons.Left, 0, p2.X, p2.Y, 0));
        Check(!doc.Editing.IsGroupingHistory && BtlFrontJson.ContentEquals(before, doc), "undo during dragging stops further painting");

        // 原生批次失败时，文档及地图中的引用都应恢复，不能留下部分修改。
        try
        {
            Call(form, "RunNativeEdit", (Action)(() =>
            {
                FrontNav.SetMember(FrontNav.ChildStruct(FrontNav.TableItems(FrontNav.Agents(doc))[0], 0), 6, (ushort)999);
                throw new InvalidOperationException("injected native failure");
            }));
            throw new Exception("failure was swallowed");
        }
        catch (TargetInvocationException ex) when (ex.InnerException is InvalidDataException) { }
        Check(BtlFrontJson.ContentEquals(before, doc), "native failure restores complete document");
        Check(ReferenceEquals(canvas.Cells[0].Unit, FrontNav.RebuildCells(doc)[0].Unit), "native rollback refreshes canvas references");

        // 过期投影中的值不能污染任何保存/查看入口。
        canvas.Cells[0].Terrain ^= 1;
        canvas.Cells[0].Unit = null;
        canvas.Cells[0].TriggerBldg = null;
        string json = BtlFrontJson.Serialize(doc);
        int history = doc.Editing.HistoryCount;
        Check((string)Call(form, "SnapshotFrontJson") == json, "snapshot reads document only");
        Call(form, "RefreshJsonEditor");
        string output = Path.Combine(root, "artifacts", "map-transactions"); Directory.CreateDirectory(output);
        string binaryPath = Path.Combine(output, "saved.btl");
        byte[] expectedBytes = FrontToBtl.Build(doc).Bytes;
        Call(form, "SaveBtlFile", binaryPath);
        Check(expectedBytes.SequenceEqual(File.ReadAllBytes(binaryPath)), "BTL save ignores stale canvas");
        foreach (bool raw in new[] { false, true })
        {
            string path = Path.Combine(output, raw ? "ids.json" : "named.json");
            Call(form, "ExportStageJson", path, raw);
            Check(File.ReadAllText(path) == (raw ? StageJson.ToFieldIds(doc) : StageJson.ToNamed(doc)), "JSON export ignores stale canvas");
        }
        Check(json == BtlFrontJson.Serialize(doc) && history == doc.Editing.HistoryCount, "view and export do not mutate data or history");

        // 真实注册表行提交与原生修改共享历史；回滚后刷新所有旧节点引用。
        var leftTabs = Field<TabControl>(form, "tabControlLeft");
        leftTabs.SelectedTab = leftTabs.TabPages.Cast<TabPage>().Single(t => t.Text == "注册表模式");
        var viewer = Field<FrontRegistryViewer>(form, "fbRegistryViewer");
        Field<CheckBox>(viewer, "_editCurrent").Checked = true;
        Check(viewer.NavigateToPath("/"), "registry root");
        var row = Field<ListView>(viewer, "_list").Items[0];
        before = BtlFrontJson.CloneDocument(doc);
        int previousIndex = doc.Editing.HistoryIndex;
        int commits = doc.Editing.CommitCount;
        Call(viewer, "ApplyScalarEdit", row.Tag, "321", row);
        Check(FrontNav.ScalarI64(doc.Root, 0) == 321, "registry scalar committed");
        Check(doc.Editing.HistoryIndex == previousIndex + 1 && doc.Editing.CommitCount == commits + 1
            && !doc.Editing.CanRedo, "registry adds one shared history frame and replaces redo branch");
        Call(form, "PerformUndoAction"); Check(BtlFrontJson.ContentEquals(before, doc), "registry undo");
        row = Field<ListView>(viewer, "_list").Items[0];
        Call(viewer, "ApplyScalarEdit", row.Tag, "322", row);
        Check(FrontNav.ScalarI64(doc.Root, 0) == 322, "registry after undo edits current nodes");
        string beforeFailure = BtlFrontJson.Serialize(doc);
        frames = doc.Editing.HistoryCount;
        try
        {
            Call(viewer, "TryFrontEdit", (Action)(() =>
            {
                FrontEdit.SetScalar(doc.Root, 0, "u16", (ushort)999);
                throw new InvalidOperationException("injected registry failure");
            }));
            throw new Exception("failure was swallowed");
        }
        catch (TargetInvocationException ex) when (ex.InnerException is InvalidOperationException) { }
        Check(beforeFailure == BtlFrontJson.Serialize(doc) && frames == doc.Editing.HistoryCount, "registry failure rolls back without history");
        row = Field<ListView>(viewer, "_list").Items[0];
        Call(viewer, "ApplyScalarEdit", row.Tag, "323", row);
        Check(FrontNav.ScalarI64(doc.Root, 0) == 323, "registry failure leaves current node bindings usable");
        Check(ReferenceEquals(canvas.Cells[0].Unit, FrontNav.RebuildCells(doc)[0].Unit), "canvas references current document after rollback");
        Console.WriteLine("Map transactions PASS: native preservation, gesture grouping/capture loss, pure exports, registry shared history and rollback.");
    }
}
