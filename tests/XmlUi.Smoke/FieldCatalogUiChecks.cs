using BtlCore.Fb;
using BtlCore.Front;
using BtlCore.Scripting;
using BtldMapEditor;

internal static class FieldCatalogUiChecks
{
    static void Check(bool ok, string message) { if (!ok) throw new InvalidOperationException(message); }
    static IEnumerable<Control> All(Control root)
    {
        foreach (Control child in root.Controls) { yield return child; foreach (var descendant in All(child)) yield return descendant; }
    }
    static T Named<T>(Control root, string name) where T : Control => All(root).OfType<T>().Single(c => c.Name == name);

    public static void Run(string root)
    {
        var context = new SchemaContext(FbsSchema.LoadFromText(File.ReadAllText(Path.Combine(root, "tests/FrontEdit.Tests/Fixtures/battle-legacy.fbs"))
            .Replace("extra_table_12:AIAgentTable12", "extra_table_12:AIAgentTable11")));
        var catalog = FieldCatalog.Builtin.Overlay(FieldCatalog.Parse("""
            {"version":1,"revision":"ui-fixture.r2","fields":[
              {"id":"unit.general","owner":"AIAgent","path":"/12","kind":"table"},
              {"id":"building.dx","owner":"TriggerEvent","parent":"building.data","path":".5"},
              {"id":"building.dy","owner":"TriggerEvent","parent":"building.data","path":".4"}]}
            """));
        var layout = LayoutLoader.Load(Path.Combine(root, "EditorLayout"), null, context);
        Check(layout.Ok, string.Join("\n", layout.Errors));
        var host = new ScriptHost(); host.ConfigureFields(catalog);
        foreach (var script in layout.Bundle.Scripts) host.Execute(script.Text, script.Name);
        var doc = new BtlFrontDocument { Context = context, Root = BtlFrontJson.NewTable() };
        var unit = UnitEdits.Create(0, 0, 1, 101, 1, 0, 10, 30,doc.Context);
        FrontEdit.EnsureVec(FrontEdit.EnsureTable(doc.Root, 6), 0, "table").V.Add(unit);
        FrontEdit.SetScalar(FrontEdit.EnsureTable(unit, 11), 0, "u16", (ushort)77);
        var general = FrontEdit.EnsureTable(unit, 12);
        FrontEdit.SetScalar(general, 0, "u16", (ushort)44);
        FrontEdit.SetScalar(general, 99, "u8", (byte)9);
        var ev = SiteEdits.CreateBuilding(0, 1, 0, 0, null, 1, -2, null);
        FrontEdit.EnsureVec(FrontEdit.EnsureTable(doc.Root, 5), 0, "table").V.Add(ev);
        host.Load(doc);
        using var form = new Form { ShowInTaskbar = false, Opacity = 0,
            StartPosition = FormStartPosition.Manual, Location = new Point(-32000, -32000), Size = new Size(1100, 1100) };
        form.Show();
        LayoutPageControl view = null;
        void Show(string id)
        {
            view?.Dispose(); form.Controls.Clear();
            view = new LayoutPageControl(layout.Bundle.Tabs.Single(t => t.Id == id).Page, host, () => view.Reload(doc));
            view.ErrorOccurred += ex => throw new InvalidOperationException("catalog UI", ex);
            form.Controls.Add(view); view.SetCell(0, refresh: false); view.Reload(doc); Application.DoEvents();
        }
        string before = BtlFrontJson.Serialize(doc);
        Show("unit");
        // 两个部队栏目存在同名输入框，使用第一个（地图部队）栏目。
        var idInput = All(view).OfType<NullableNumericUpDown>().First(n => n.Name == "general_id");
        var enabled = All(view).OfType<CheckBox>().First(n => n.Name == "general");
        var active = All(view).OfType<CheckBox>().First(n => n.Name == "general_active");
        Check(enabled.Checked && idInput.NullableValue == 44 && active.CheckState == CheckState.Indeterminate,
            "overridden optional group is displayed with absent bool");
        Check(before == BtlFrontJson.Serialize(doc), "catalog UI read is pure");
        idInput.NullableValue = 0; active.CheckState = CheckState.Unchecked;
        All(view).OfType<Button>().First(b => b.Text == "保存选中修改").PerformClick();
        Check((ushort)((BtlScalar)general.F[0]).V == 0 && (bool)((BtlScalar)general.F[1]).V == false, "UI stores explicit zero/false at overridden group");
        Check((ushort)((BtlScalar)((BtlTable)unit.F[11]).F[0]).V == 77 && general.F.ContainsKey(99), "UI preserves old group and unknown members");
        enabled.Checked = false;
        All(view).OfType<Button>().First(b => b.Text == "保存选中修改").PerformClick();
        Check(!unit.F.ContainsKey(12) && unit.F.ContainsKey(11), "presence switch deletes only overridden group");

        Show("site");
        var building = All(view).OfType<GroupBox>().Single(g => g.Text == "建筑配置");
        Check(Named<NullableNumericUpDown>(building, "dx").NullableValue == -2, "building XML reads overridden signed member");
        Named<NullableNumericUpDown>(building, "dx").NullableValue = -7;
        All(building).OfType<Button>().Single(b => b.Text == "保存选中修改").PerformClick();
        var data = (BtlStruct)((BtlTable)ev.F[3]).F[0];
        Check(Convert.ToInt32(data.V[4]) == 1 && Convert.ToInt32(data.V[5]) == -7, "building XML and Lua agree on overridden member writes");
        Check(doc.FieldCatalogUses.Single().Sha256 == catalog.Fingerprint, "UI commits record the effective catalog");
        Console.WriteLine("Field catalog UI PASS: optional group relocation, zero/false/absence, unknown preservation, building member override and provenance.");
    }
}
