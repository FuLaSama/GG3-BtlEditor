using System.Reflection;
using BtlCore.Fb;
using BtlCore.Front;
using BtldMapEditor;
using BtldMapEditor.Front;

internal static class Program
{
    static readonly BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    static T Field<T>(object obj, string name) => (T)obj.GetType().GetField(name, Private).GetValue(obj);
    static object Call(object obj, string name, params object[] args) => obj.GetType().GetMethod(name, Private).Invoke(obj, args);
    static void Check(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
    static string FindRoot()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir != null; dir = dir.Parent)
            if (File.Exists(Path.Combine(dir.FullName, "schema/battle.fbs"))) return dir.FullName;
        throw new InvalidOperationException("repository root not found");
    }

    [STAThread]
    static int Main(string[] args)
    {
        try
        {
            ApplicationConfiguration.Initialize();
            Application.SetUnhandledExceptionMode(UnhandledExceptionMode.ThrowException);
            string root = FindRoot();
            string output = Path.Combine(root, "artifacts/registry-smoke"); Directory.CreateDirectory(output);
            string validPath = Path.Combine(output, "valid.btl"), badPath = Path.Combine(output, "bad.btl");
            byte[] originalBytes = SyntheticBtl.MinimalStage(); File.WriteAllBytes(validPath, originalBytes);
            File.WriteAllBytes(badPath, new byte[3]);
            var valid = FrontSession.FromBtl(validPath);
            Check(valid.CanExport, "synthetic valid file is editable/exportable");
            string before = BtlFrontJson.Serialize(valid.Document);

            using var form = new Form { Size = new Size(1200, 760), ShowInTaskbar = false, Opacity = 0,
                StartPosition = FormStartPosition.Manual, Location = new Point(-32000, -32000) };
            using var viewer = new FrontRegistryViewer(); form.Controls.Add(viewer); form.Show();
            viewer.LoadSession(valid); Application.DoEvents();
            Check(viewer.IsOriginalView && !viewer.CanEdit, "original file is the default and read-only");
            Check(Field<Button>(viewer, "_btnLoadFbs").Visible && Field<ComboBox>(viewer, "_sourceChoice").Visible, "source and schema controls visible in original view");
            var schemaButton = Field<Button>(viewer, "_btnLoadFbs");
            Check(schemaButton.RectangleToScreen(schemaButton.ClientRectangle).Bottom <=
                Field<BinaryRegistryView>(viewer, "_binaryView").RectangleToScreen(Field<BinaryRegistryView>(viewer, "_binaryView").ClientRectangle).Top,
                "toolbars do not overlap original view");
            Check(viewer.NavigateToPath("/1/2/i1"), "scalar vector element navigation");
            Check(!viewer.NavigateToPath("/missing"), "nonexistent path returns without scanning entire tree");
            Check(before == BtlFrontJson.Serialize(valid.Document), "browsing does not mutate current document");
            Check(originalBytes.SequenceEqual(valid.Original.CopyBytes()), "source bytes unchanged");

            var interpretation = viewer.Inspection;
            var names = FbsSchema.LoadFromText("table ExternalRoot { renamed:short; // @alias(version) 中文版本\n } root_type ExternalRoot;", "names.fbs");
            viewer.ApplySchema(names, namesOnly: true);
            Check(ReferenceEquals(interpretation, viewer.Inspection), "names-only import does not reparse");
            Check(viewer.Inspection.Find("/0").Type == "ushort", "names-only import does not relabel actual types");
            Field<ComboBox>(viewer, "_sourceChoice").SelectedIndex = 1;
            var namedRow = Field<ListView>(viewer, "_list").Items[0];
            Check(namedRow.SubItems[1].Text == "renamed · 中文版本" && namedRow.SubItems[2].Text == "u16", "current document naming follows external root and keeps Chinese after alias without changing type");
            viewer.ApplySchema(names);
            Check(viewer.Inspection.Find("/0").Type == "short", "interpretation applies supplied type");
            Check(viewer.Inspection.Find("/1").State == BinaryValueState.Unknown, "fields omitted by new schema remain as raw nodes");
            Check(before == BtlFrontJson.Serialize(valid.Document), "schema import does not replace editing document");
            var goodView = viewer.Inspection;
            try { viewer.ApplySchema(FbsSchema.LoadFromText("table Root { broken:Unknown; } root_type Root;")); throw new Exception("unsupported schema accepted"); }
            catch (InvalidDataException) { }
            Check(ReferenceEquals(goodView, viewer.Inspection), "failed schema preserves previous view");

            var choice = Field<ComboBox>(viewer, "_sourceChoice"); choice.SelectedIndex = 1;
            Check(!viewer.CanEdit, "current document is read-only until explicitly enabled");
            bool mutation = false;
            Check(!(bool)Call(viewer, "TryFrontEdit", (Action)(() => mutation = true)), "readonly mutation gate");
            Check(!mutation, "read-only write callback was not invoked");
            Field<CheckBox>(viewer, "_editCurrent").Checked = true;
            Check(viewer.CanEdit && (bool)Call(viewer, "TryFrontEdit", (Action)(() => mutation = true)), "explicit current-document edit remains available");
            choice.SelectedIndex = 0; Check(!viewer.CanEdit, "switching back to original prevents writes");

            var bad = FrontSession.FromBtl(badPath);
            viewer.LoadSession(bad); Check(!bad.CanExport && !viewer.CanEdit, "bad file inspectable but not editable/exportable");
            Check(viewer.Inspection.Root.State == BinaryValueState.Invalid, "bad file shows diagnostic root");
            try { bad.SaveBtl(Path.Combine(output, "must-not-exist.btl")); throw new Exception("bad file export accepted"); }
            catch (InvalidDataException) { }
            Check(!File.Exists(Path.Combine(output, "must-not-exist.btl")), "blocked export writes no file");

            // 大向量的列表是虚拟化的；树预览限制不影响按路径进入任意元素。
            var large = new BtlFrontDocument { Root = BtlFrontJson.NewTable() };
            var bytesVector = BtlFrontJson.NewVector("u8");
            bytesVector.V.AddRange(Enumerable.Repeat<object>((byte)1, 100_000)); large.Root.F[10] = bytesVector;
            string largePath = Path.Combine(output, "large.btl"); File.WriteAllBytes(largePath, FrontToBtl.Build(large).Bytes);
            viewer.LoadSession(FrontSession.FromBtl(largePath));
            Check(viewer.NavigateToPath("/10/i99999"), "large vector far element navigation");
            Check(viewer.NavigateToPath("/10/i80000") && viewer.NavigateToPath("/10/i99999"), "repeated vector navigation preserves tree entries");
            Check(viewer.NavigateToPath("/10"), "large vector parent navigation");
            var binary = Field<BinaryRegistryView>(viewer, "_binaryView");
            Check(Field<ListView>(binary, "_list").VirtualMode && Field<ListView>(binary, "_list").VirtualListSize == 100_000, "all vector elements available without eager list rows");

            // 主窗体切页、撤销保留来源；查看不隐式同步画布。
            using var editor = new MainEditorForm(true) { ShowInTaskbar = false, Opacity = 0,
                StartPosition = FormStartPosition.Manual, Location = new Point(-32000, -32000) };
            editor.Show(); Call(editor, "ApplyBtlFile", validPath); Call(editor, "OnDocumentLoaded"); Call(editor, "InitHistory");
            var session = Field<FrontSession>(editor, "_front");
            var source = session.Original;
            var tabs = Field<TabControl>(editor, "tabControlLeft");
            var registryTab = tabs.TabPages.Cast<TabPage>().Single(p => p.Text == "注册表模式");
            string beforeTab = BtlFrontJson.Serialize(session.Document);
            Field<EditorMapCanvas>(editor, "mapCanvas").Cells[0].Terrain ^= 1; // 未同步的画布变化
            tabs.SelectedTab = registryTab;
            Check(beforeTab == BtlFrontJson.Serialize(session.Document), "tab switch does not SyncGrid");
            var editorViewer = Field<FrontRegistryViewer>(editor, "fbRegistryViewer");
            Field<TreeView>(Field<BinaryRegistryView>(editorViewer, "_binaryView"), "_tree").Focus();
            foreach (Keys key in new[] { Keys.B, Keys.V, Keys.O, Keys.Delete, Keys.Control | Keys.Z,
                Keys.Control | Keys.Y, Keys.Control | Keys.V, Keys.Control | Keys.Shift | Keys.Z })
            {
                var message = Message.Create(IntPtr.Zero, 0, IntPtr.Zero, IntPtr.Zero);
                Check((bool)Call(editor, "ProcessCmdKey", message, key), "readonly registry consumes editing shortcut: " + key);
            }
            Check(beforeTab == BtlFrontJson.Serialize(session.Document), "readonly shortcuts do not mutate document");
            session.Editing.Execute(() => FrontEdit.SetScalar(session.Document.Root, 0, "u16", (ushort)333));
            Call(editor, "PerformUndoAction");
            Check(beforeTab == BtlFrontJson.Serialize(session.Document), "shared history restores the document");
            Check(ReferenceEquals(source, Field<FrontSession>(editor, "_front").Original), "undo preserves original source and safety state");
            Check(Field<FrontRegistryViewer>(editor, "fbRegistryViewer").IsOriginalView, "undo preserves original view");

            // 实际打开异常文件进入只读注册表，导出入口关闭。
            var open = (Task)Call(editor, "OpenPath", badPath);
            while (!open.IsCompleted) { Application.DoEvents(); Thread.Sleep(1); }
            open.GetAwaiter().GetResult();
            Check(tabs.SelectedTab == registryTab && !Field<ToolStripMenuItem>(editor, "exportItem").Enabled, "invalid open selects inspector and disables export");
            Check(!Field<EditorMapCanvas>(editor, "mapCanvas").Enabled, "invalid file cannot be edited through map");

            if (args.Contains("--samples")) CheckSamples(root);
            if (args.Contains("--render"))
            {
                viewer.LoadSession(valid); viewer.NavigateToPath("/1"); Application.DoEvents();
                using var bitmap = new Bitmap(form.Width, form.Height); form.DrawToBitmap(bitmap, new Rectangle(Point.Empty, form.Size));
                bitmap.Save(Path.Combine(output, "registry.png"));
                string errorSample = Path.Combine(root, "战役相关文件/BTL/分析用原版样本/stage10404.btl");
                if (File.Exists(errorSample))
                {
                    viewer.LoadSession(FrontSession.FromBtl(errorSample)); viewer.NavigateToPath("/5/0/i13/3");
                    Application.DoEvents(); form.DrawToBitmap(bitmap, new Rectangle(Point.Empty, form.Size));
                    bitmap.Save(Path.Combine(output, "registry-error.png"));
                }
            }
            Check(originalBytes.SequenceEqual(File.ReadAllBytes(validPath)), "test browsing leaves source file bytes unchanged");
            Console.WriteLine("Registry smoke PASS: readonly, schema/name imports, original provenance, invalid export gate, virtual vectors, tab/undo integration.");
            return 0;
        }
        catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
    }

    static void CheckSamples(string root)
    {
        string directory = Path.Combine(root, "战役相关文件/BTL/分析用原版样本");
        if (!Directory.Exists(directory)) { Console.WriteLine("Original sample directory unavailable; skipped."); return; }
        int count = 0, blocked = 0, warnings = 0, invalid = 0, zeroPadding = 0;
        foreach (string path in Directory.GetFiles(directory, "*.btl"))
        {
            var session = FrontSession.FromBtl(path); count++;
            if (!session.CanExport)
            {
                blocked++;
                Console.WriteLine("Retained unresolved sample " + Path.GetFileName(path) + ": " + string.Join(" | ",
                    session.Issues.Where(i => i.Severity != IssueSeverity.Info || i.Message.Contains("未知字段")).Take(3).Select(i => i.ToString())));
            }
            warnings += session.Issues.Count(i => i.Severity != IssueSeverity.Info);
            var stack = new Stack<BinaryInspectionNode>(); stack.Push(session.Original.Root);
            while (stack.Count > 0)
            {
                var node = stack.Pop();
                if (node.State == BinaryValueState.Invalid) invalid++;
                if (node.State == BinaryValueState.ZeroPadding) zeroPadding++;
                if (node.Kind is "table" or "struct" || node.Kind == "vector" && !node.Type.Contains("ushort") && !node.Type.Contains("ubyte"))
                    for (int i = 0; i < node.ChildCount; i++) stack.Push(node.GetChild(i));
            }
            Check(session.Original.CopyBytes().SequenceEqual(File.ReadAllBytes(path)), "sample bytes unchanged: " + path);
            if (count % 100 == 0) Console.WriteLine("Inspected original samples: " + count);
        }
        Console.WriteLine($"Original samples: {count}, blocked exports={blocked}, read warnings/errors={warnings}, inspector invalid fields={invalid}, zero-padding fields retained={zeroPadding}");
    }
}
