using System.Reflection;
using BtlCore.Fb;
using BtlCore.Front;
using BtlCore.Scripting;
using BtldMapEditor;

internal static class NullableFieldsChecks
{
    static int checks;
    static void Check(bool ok, string message)
    { if (!ok) throw new InvalidOperationException(message); checks++; }
    static IEnumerable<Control> All(Control root)
    {
        foreach (Control c in root.Controls) { yield return c; foreach (var n in All(c)) yield return n; }
    }
    static T Named<T>(Control root, string name) where T : Control => All(root).OfType<T>().First(c => c.Name == name);
    static Control Group(Control root, string title)
    {
        var control=All(root).First(c => c is GroupBox && c.Text==title || c is CollapsibleGroup fold && fold.Title==title);
        if(control is CollapsibleGroup group)group.SetCollapsed(false);
        return control;
    }
    static void Select(Control group, int row)
    {
        var list = All(group).OfType<ListView>().Single();
        foreach (ListViewItem item in list.Items) item.Selected = false;
        if (row >= 0) list.Items[row].Selected = true;
        Application.DoEvents();
    }
    static void Save(Control group) => All(group).OfType<Button>().Single(b => b.Text == "保存选中修改" || b.Text == "保存部队组").PerformClick();
    static void Leave(Control control) => control.GetType().GetMethod("OnLeave", BindingFlags.Instance | BindingFlags.NonPublic)
        .Invoke(control, new object[] { EventArgs.Empty });
    static BtlBinaryInspection Written(BtlFrontDocument doc)
    {
        var built = FrontToBtl.Build(doc); Check(built.Ok, "edited document builds");
        return new BtlBinaryInspection(built.Bytes, doc.Context.Schema);
    }

    public static void Run(string root, LayoutBundle layout, bool render)
    {
        var host = new ScriptHost(); foreach (var script in layout.Scripts) host.Execute(script.Text, script.Name);
        var doc = new BtlFrontDocument { Root = BtlFrontJson.NewTable(), Context=host.CreateSchemaContext() };
        var map = FrontEdit.EnsureTable(doc.Root, 1);
        FrontEdit.SetField(map, 0, FrontEdit.StructFromFbs("Size", (ushort)4, (ushort)1, (ushort)0, (ushort)0, (ushort)4, (ushort)1));
        var tiles = FrontEdit.EnsureVec(map, 2, "u16"); for (int i = 0; i < 4; i++) tiles.V.Add((ushort)0);
        var ai = FrontEdit.EnsureTable(doc.Root, 6); var agents = FrontEdit.EnsureVec(ai, 0, "table");
        var points = FrontEdit.EnsureVec(FrontEdit.EnsureTable(doc.Root, 3), 1, "table");
        var targets = FrontEdit.EnsureVec(FrontEdit.EnsureTable(doc.Root, 2), 0, "table");
        var routes = FrontEdit.EnsureVec(ai, 1, "table");
        for (int i = 0; i < 3; i++)
        {
            var unit = UnitEdits.Create((ushort)i, 0, (ushort)(i + 1), 101, 1, 0, 0, 20,doc.Context);
            if (i > 0) FrontEdit.SetScalar(unit, 2, "u8", (byte)(i == 1 ? 0 : 7));
            var general = FrontEdit.EnsureTable(unit, 11);
            if (i > 0) FrontEdit.SetScalar(general, 1, "bool", i == 2);
            FrontEdit.SetScalar(general, 2, "u8", (byte)9); agents.V.Add(unit);
            var point = BtlFrontJson.NewTable(); if (i > 0) FrontEdit.SetScalar(point, 2, "bool", i == 2); points.V.Add(point);
            var target = BtlFrontJson.NewTable(); if (i > 0) FrontEdit.SetScalar(target, 4, "u8", (byte)(i == 1 ? 0 : 7)); targets.V.Add(target);
            var route = BtlFrontJson.NewTable(); FrontEdit.SetScalar(route, 0, "u16", (ushort)i);
            if (i > 0) { var cells = FrontEdit.EnsureVec(route, 1, "u16"); if (i == 2) cells.V.Add((ushort)0); }
            routes.V.Add(route);
        }
        host.Load(doc); int commits = 0; var errors = new List<Exception>();
        using var form = new Form { ShowInTaskbar = false, Opacity = 0, StartPosition = FormStartPosition.Manual,
            Location = new Point(-32000, -32000), Size = new Size(1100, 1100) };
        form.Show(); LayoutPageControl view = null;
        LayoutPageControl View(string id)
        {
            view?.Dispose(); form.Controls.Clear();
            view = new LayoutPageControl(layout.Tabs.Single(t => t.Id == id).Page, host, () => { commits++; view.Reload(doc); });
            view.ErrorOccurred += errors.Add;
            form.Controls.Add(view); view.Reload(doc); Application.DoEvents(); return view;
        }
        string before = BtlFrontJson.Serialize(doc);
        var unitView = View("unit"); var basic = Group(unitView, "基础部队信息");
        foreach (int cell in new[] { 0, 1, 2, 0, 3, 1, 0 })
        {
            unitView.SetCell(cell);
            var number = Named<NullableNumericUpDown>(basic, "play_mode");
            Check(number.Text == (cell == 1 ? "0" : cell == 2 ? "7" : ""), "selection distinguishes absent / zero / nonzero");
            Check(number.PlaceholderText == (cell == 3 ? "未选中对象" : cell == 0 ? "未写" : ""), "blank selection status");
            if (cell < 3) Check(Named<NullableNumericUpDown>(basic, "hp").Text == "0", "inline zero remains visible");
        }
        unitView.SetCell(null); Check(Named<NullableNumericUpDown>(basic, "hp").Text == "", "deselection clears previous value");
        var generalGroup = Group(unitView, "将领配置");
        for (int i = 0; i < 3; i++)
        {
            unitView.SetCell(i); var active = Named<CheckBox>(generalGroup, "general_active");
            Check(active.CheckState == (i == 0 ? CheckState.Indeterminate : i == 1 ? CheckState.Unchecked : CheckState.Checked), "bool missing / false / true");
            Check(((Control)Named<NullableNumericUpDown>(generalGroup, "general_param2")).Enabled == (i == 2), "indeterminate bool does not enable gated fields");
        }
        Check(before == BtlFrontJson.Serialize(doc), "selection and refresh preserve document");
        unitView.SetCell(0); int snapshots = host.SnapshotCount; Save(unitView);
        Check(commits == 0 && snapshots == host.SnapshotCount && before == BtlFrontJson.Serialize(doc), "unchanged save is a no-op");
        Named<NullableNumericUpDown>(basic, "hp").Text = "11"; Save(unitView);
        var general0 = (BtlTable)((BtlTable)agents.V[0]).F[11];
        Check(!general0.F.ContainsKey(0) && !general0.F.ContainsKey(1) && Convert.ToInt32(((BtlScalar)general0.F[2]).V) == 9,
            "editing sibling retains absent general ID/bool and disabled stored parameter");
        unitView.SetCell(2); Named<NullableNumericUpDown>(generalGroup, "general_param2").Text = ""; Save(unitView);
        Check(!((BtlTable)((BtlTable)agents.V[2]).F[11]).F.ContainsKey(2), "cleared general scalar is removed");

        var stage = View("stage"); var targetGroup = Group(stage, "关卡目标");
        foreach (int row in new[] { 0, 1, 2, 0 })
        { Select(targetGroup, row); Check(Named<NullableNumericUpDown>(targetGroup, "flag").Text == (row == 0 ? "" : row == 1 ? "0" : "7"), "list selection values"); }
        Select(targetGroup, -1); Check(Named<NullableNumericUpDown>(targetGroup, "flag").PlaceholderText == "未选中对象", "list deselection status");
        Select(targetGroup, 2); var flag = Named<NullableNumericUpDown>(targetGroup, "flag");
        foreach (string invalid in new[] { "abc", "256", "-1", "1.5", "NaN", "Infinity" })
        {
            before = BtlFrontJson.Serialize(doc); int committed = commits, errorCount = errors.Count; snapshots = host.SnapshotCount;
            flag.Text = invalid; Leave(flag); Save(targetGroup);
            Check(flag.Text == invalid, "invalid numeric text remains visible");
            Check(errors.Count == errorCount + 1 && commits == committed && host.SnapshotCount == snapshots,
                "invalid number blocks command before script/history");
            Check(before == BtlFrontJson.Serialize(doc), "invalid number cannot delete existing data");
        }
        flag.Text = "0"; Save(targetGroup); Check(Convert.ToInt32(((BtlScalar)((BtlTable)targets.V[2]).F[4]).V) == 0, "explicit zero stays written");
        flag.Text = ""; Save(targetGroup); Check(!((BtlTable)targets.V[2]).F.ContainsKey(4), "intentional blank deletes optional scalar");
        var pointGroup = Group(stage, "增援点"); Select(pointGroup, 0);
        Named<NullableNumericUpDown>(pointGroup, "flag").Text = "8"; Save(pointGroup);
        Check(!((BtlTable)points.V[0]).F.ContainsKey(2), "editing sibling does not write missing bool");
        var key = Named<CheckBox>(pointGroup, "is_key"); key.CheckState = CheckState.Unchecked; Save(pointGroup);
        Check(Equals(((BtlScalar)((BtlTable)points.V[0]).F[2]).V, false), "explicit false stays written");
        key.CheckState = CheckState.Checked; Save(pointGroup); Check(Equals(((BtlScalar)((BtlTable)points.V[0]).F[2]).V, true), "explicit true stays written");
        key.CheckState = CheckState.Indeterminate; Save(pointGroup); Check(!((BtlTable)points.V[0]).F.ContainsKey(2), "indeterminate removes bool slot");
        ((BtlStruct)map.F[0]).V[2] = (ushort)700; stage.Reload(doc);
        var margin = Named<NullableNumericUpDown>(stage, "left_margin"); Check(margin.Text == "700", "display never clamps source value");
        before = BtlFrontJson.Serialize(doc); Leave(margin); Check(before == BtlFrontJson.Serialize(doc), "unmodified out-of-editor-range value preserved");
        margin.Text = "701"; Leave(margin); Check(margin.Text == "701" && before == BtlFrontJson.Serialize(doc), "action field rejects out-of-range input without reload");
        margin.Text = "2"; Leave(margin); Check(Convert.ToInt32(((BtlStruct)map.F[0]).V[2]) == 2, "valid correction commits action field");
        if (render) Render(Group(stage, "增援点"), root, "nullable-bool");

        var faction = View("faction"); var routeGroup = Group(faction, "部队组");
        for (int i = 0; i < 3; i++)
        { Select(routeGroup, i); Check(Named<TextBox>(routeGroup, "cells").Text == (i == 0 ? "" : i == 1 ? "[]" : "0"), "missing / empty / zero-element vector"); }
        Select(routeGroup, 0); Named<NullableNumericUpDown>(routeGroup, "flag").Text = "3"; Save(routeGroup);
        Check(!((BtlTable)routes.V[0]).F.ContainsKey(1), "editing flag preserves absent vector");
        var cellsBox = Named<TextBox>(routeGroup, "cells"); cellsBox.Text = "[]"; Save(routeGroup);
        Check(((BtlVector)((BtlTable)routes.V[0]).F[1]).V.Count == 0, "[] creates a real empty vector");
        cellsBox.Text = ""; Save(routeGroup); Check(!((BtlTable)routes.V[0]).F.ContainsKey(1), "clearing vector removes its slot");
        before = BtlFrontJson.Serialize(doc); cellsBox.Text = ",,,"; Save(routeGroup);
        Check(before == BtlFrontJson.Serialize(doc) && cellsBox.Text == ",,,", "invalid list does not become empty vector");
        cellsBox.Text = "0, 1"; Save(routeGroup); Check(((BtlVector)((BtlTable)routes.V[0]).F[1]).V.Count == 2, "valid integer list remains writable");

        var events = FrontEdit.EnsureVec(FrontEdit.EnsureTable(doc.Root, 5), 0, "table"); var ev = BtlFrontJson.NewTable();
        FrontEdit.SetScalar(ev, 0, "u16", (ushort)0); var fort = FrontEdit.EnsureTable(ev, 4); FrontEdit.SetScalar(fort, 0, "u8", (byte)2); events.V.Add(ev);
        var site = View("site"); site.SetCell(0); var fortGroup = Group(site, "军事工事配置");
        before = BtlFrontJson.Serialize(doc); Save(fortGroup); Check(before == BtlFrontJson.Serialize(doc), "unchanged fort save retains blanks");
        Named<NullableNumericUpDown>(fortGroup, "fort_id").Text = "3"; Save(fortGroup);
        Check(!ev.F.ContainsKey(1) && !fort.F.ContainsKey(3), "fort sibling edit retains absent scalars");
        Named<NullableNumericUpDown>(fortGroup, "fort_id").Text = ""; Save(fortGroup);
        Check(fort.F.Count == 0 && host.ResolveIndex("fort", 0) == 0, "empty fort object stays selectable");
        Check(Named<NullableNumericUpDown>(fortGroup, "fort_id").PlaceholderText == "未写", "empty object field does not pretend no selection");
        var binary = Written(doc);
        Check(binary.Find("/5/0/i0/4/0").State == BinaryValueState.Absent && binary.Find("/3/1/i0/2").State == BinaryValueState.Absent,
            "UI edits preserve physical missing slots after export");
        if (render) Render(fortGroup, root, "nullable-numbers");
        var terrain = View("terrain");
        Check(Named<NullableNumericUpDown>(terrain, "main_id").PlaceholderText == "未选中对象", "unselected action field status");
        terrain.SetCell(0);
        Check(Named<NullableNumericUpDown>(terrain, "main_id").PlaceholderText == "未写", "selected missing layer status");
        terrain.SetCell(9);
        Check(Named<NullableNumericUpDown>(terrain, "main_id").PlaceholderText == "未选中对象", "invalid cell status");
        using var numberBox = new NullableNumericUpDown { DecimalPlaces = 2 };
        float raw = float.BitIncrement(1f); numberBox.SetDisplayValue(raw);
        Check(numberBox.ReadValue() is float f && BitConverter.SingleToInt32Bits(f) == BitConverter.SingleToInt32Bits(raw), "unmodified float keeps exact source bits");
        int fired = 0; numberBox.ValueChanged += (_, _) => fired++;
        foreach (decimal? value in new decimal?[] { null, 0, 7, null }) { numberBox.NullableValue = value; Check(numberBox.NullableValue == value, "nullable numeric program assignment"); }
        Check(fired == 0, "program assignment has no edit event");
        Console.WriteLine($"Nullable field UI checks PASS: {checks} assertions; display, validation, no-op save, optional scalar/bool/vector writes, physical export.");
    }

    static void Render(Control control, string root, string name)
    {
        control.PerformLayout(); using var bitmap = new Bitmap(control.Width, control.Height);
        control.DrawToBitmap(bitmap, control.ClientRectangle);
        string output = Path.Combine(root, "artifacts", "property-ui-fixed"); Directory.CreateDirectory(output);
        bitmap.Save(Path.Combine(output, name + ".png"));
    }
}
