using System.Reflection;
using BtlCore.Fb;
using BtlCore.Front;
using BtlCore.Scripting;
using BtldMapEditor;
using BtldMapEditor.Front;

internal static class ScriptFormatUiChecks
{
    const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    static T Field<T>(object obj, string name) => (T)obj.GetType().GetField(name, Private).GetValue(obj);
    static object Call(object obj, string name, params object[] args) => obj.GetType().GetMethod(name, Private).Invoke(obj, args);
    static void Check(bool ok, string message) { if (!ok) throw new InvalidOperationException(message); }

    public static void Run(string root)
    {
        using var form = new MainEditorForm(true) { ShowInTaskbar = false, Opacity = 0,
            StartPosition = FormStartPosition.Manual, Location = new Point(-32000, -32000) };
        form.Show();
        var host = Field<ScriptHost>(form, "_stageHost");
        host.Execute(File.ReadAllText(Path.Combine(root, "EditorLayout/examples/research_packet.lua")), "research_packet.lua");
        host.Execute(File.ReadAllText(Path.Combine(root, "EditorLayout/examples/research_objects.lua")), "research_objects.lua");
        var sample = FrontSession.NewMap(2, 1, 0, 0, 2, 1, 20, 1, host.CreateSchemaContext());
        var bytes = new byte[] {123,7,0,251,252,253,254,255};
        FrontEdit.Fill(FrontEdit.EnsureVec(sample.Document.Root, 31, "u8"), bytes.Cast<object>());
        var units = FrontEdit.EnsureVec(FrontEdit.EnsureTable(sample.Document.Root, 6), 0, "table");
        units.V.Add(UnitEdits.Create(0, 0, 1, 101, 0, 0, 10, 20,sample.Document.Context));
        units.V.Add(UnitEdits.Create(1, 0, 2, 101, 0, 0, 10, 20,sample.Document.Context));
        string input = Path.Combine(Path.GetTempPath(), "lua-format-" + Guid.NewGuid() + ".btl");
        string output = input + ".out.btl";
        try
        {
            sample.SaveBtl(input);
            Check(BtlToFront.FromFile(input).HasUnresolvedData, "fixture needs the Lua type declaration");
            var opening = (Task)Call(form, "OpenPath", input);
            while (!opening.IsCompleted) { Application.DoEvents(); Thread.Sleep(1); }
            opening.GetAwaiter().GetResult();
            var session = Field<FrontSession>(form, "_front");
            Check(session?.CanExport == true, "async open applies Lua schema before parsing");
            var before = session.ToJson();
            Call(form, "RebuildToolsMenu");
            var menu = Field<ToolStripMenuItem>(form, "_toolsMenu");
            Check(menu.DropDownItems.Cast<ToolStripMenuItem>().Any(t => t.Name == "example.research_packet_value"), "example adds generated menu entry");
            Call(form, "ExecuteTool", "example.research_packet_value", new ScriptArgs { Input = new() { ["value"] = 513 } });
            Check(((BtlVector)session.Document.Root.F[31]).V.Select(Convert.ToByte).SequenceEqual(new byte[] {123,1,2,251,252,253,254,255}),
                "generated tool writes payload and preserves unknown tail");
            string after = session.ToJson();
            Call(form, "PerformUndoAction"); Check(session.ToJson() == before, "codec edit undo and provenance");
            Call(form, "PerformRedoAction"); Check(session.ToJson() == after, "codec edit redo and provenance");
            Check(menu.DropDownItems.Cast<ToolStripMenuItem>().Any(t => t.Name == "example.research_promote"), "object extension adds generated menu entry");
            Call(form, "ExecuteTool", "example.research_promote", new ScriptArgs { Input = new() { ["delta"] = 5 } });
            Check(((BtlVector)session.Document.Root.F[35]).V.Count == 2, "Lua-defined collection created through tool");
            var editedUnits = (BtlVector)((BtlTable)session.Document.Root.F[6]).F[0];
            Check(editedUnits.V.Cast<BtlTable>().All(u => Convert.ToInt32(((BtlScalar)u.F[33]).V) == 5), "Lua-defined unit field and method called through tool");
            string objectEdit = session.ToJson();
            Call(form, "PerformUndoAction"); Check(session.ToJson() == after, "object tool undo restores fields and removes new collection");
            Call(form, "PerformRedoAction"); Check(session.ToJson() == objectEdit, "object tool redo");
            Call(form, "SaveBtlFile", output);
            Call(form, "ApplyBtlFile", output);
            Call(form, "OnDocumentLoaded");
            Check(Field<FrontSession>(form, "_front").CanExport, "synchronous reopen also applies Lua schema");
            Check(Convert.ToInt32(((BtlVector)host.Document.Root.F[31]).V[2]) == 2, "reopened payload contains saved value");
            Check(((BtlVector)host.Document.Root.F[35]).V.Count == 2, "reopened file preserves Lua-defined collection");
            Check(((BtlVector)((BtlTable)host.Document.Root.F[6]).F[0]).V.Cast<BtlTable>()
                .All(u => Convert.ToInt32(((BtlScalar)u.F[33]).V) == 5), "reopened file preserves Lua-defined field");
        }
        finally
        {
            File.Delete(input);
            File.Delete(output);
        }
        Console.WriteLine("Lua format/object UI PASS: asynchronous open, generated tools, codec, custom fields/collections, undo/redo, save and reopen.");
    }
}
