using BtlCore.Fb;
using BtlCore.Scripting;
using Xunit;

namespace BtlCore.Front.Tests;

public class UnitUidToolTests
{
    static ScriptHost Host(BtlFrontDocument doc)
    {
        var layout = LayoutLoader.Load(Path.Combine(EditorScripts.Root(), "EditorLayout"), null);
        Assert.True(layout.Ok, string.Join("\n", layout.Errors));
        var host = new ScriptHost();
        foreach (var script in layout.Bundle.Scripts) host.Execute(script.Text, script.Name);
        host.Load(doc);
        return host;
    }

    static BtlFrontDocument Sample(params (int Cell, int Uid)[] units)
    {
        var schemaHost = new ScriptHost(); EditorScripts.Load(schemaHost);
        var doc = new BtlFrontDocument { Root = BtlFrontJson.NewTable(), Context = schemaHost.CreateSchemaContext() };
        var map = FrontEdit.EnsureTable(doc.Root, 1);
        FrontEdit.SetField(map, 0, FrontEdit.StructFromFbs(doc.Context, "Size", (ushort)4, (ushort)2, (ushort)0, (ushort)0, (ushort)4, (ushort)2));
        FrontEdit.EnsureVec(map, 2, "u16").V.AddRange(Enumerable.Repeat((object)(ushort)0, 8));
        var rows = FrontEdit.EnsureVec(FrontEdit.EnsureTable(doc.Root, 6), 0, "table");
        foreach (var (cell, uid) in units)
        {
            var unit = UnitEdits.Create(unchecked((ushort)cell), 1, (ushort)uid, 101, 1, 0, 73, 100, doc.Context);
            FrontEdit.SetScalar(unit, 98, "u16", (ushort)(500 + rows.V.Count));
            rows.V.Add(unit);
        }
        return doc;
    }

    static BtlVector Units(BtlFrontDocument doc) => (BtlVector)((BtlTable)doc.Root.F[6]).F[0];
    static int[] Ids(BtlFrontDocument doc) => Units(doc).V.Cast<BtlTable>().Select(row => Convert.ToInt32(((BtlStruct)row.F[0]).V[2])).ToArray();
    static int Number(BtlFrontDocument doc, string path)
    {
        int dot = path.LastIndexOf('.'), slash = path.LastIndexOf('/'), bracket = path.LastIndexOf('[');
        if (dot > slash && dot > bracket)
            return Convert.ToInt32(((BtlStruct)ResearchEdits.Node(doc, path[..dot])).V[int.Parse(path[(dot + 1)..])]);
        if (path.EndsWith(']'))
            return Convert.ToInt32(((BtlVector)ResearchEdits.Node(doc, path[..bracket])).V[int.Parse(path[(bracket + 1)..^1])]);
        return Convert.ToInt32(((BtlScalar)ResearchEdits.Node(doc, path)).V);
    }
    static void Set(BtlFrontDocument doc, string path, int value)
    {
        int dot = path.LastIndexOf('.'), slash = path.LastIndexOf('/');
        if (dot > slash) ((BtlStruct)ResearchEdits.Node(doc, path[..dot])).V[int.Parse(path[(dot + 1)..])] = (ushort)value;
        else
        {
            var table = (BtlTable)ResearchEdits.Node(doc, path[..slash]);
            FrontEdit.SetScalar(table, int.Parse(path[(slash + 1)..]), "u16", (ushort)value);
        }
    }
    static void Target(BtlFrontDocument doc, int row, int uid) =>
        FrontEdit.SetScalar(FrontEdit.EnsureTable((BtlTable)Units(doc).V[row], 3), 5, "u16", (ushort)uid);
    static void Run(ScriptHost host) => host.RunTool("assign_unit_uids");

    [Fact]
    public void Default_menu_only_registers_the_parameterless_uid_tool()
    {
        var tool = Assert.Single(Host(Sample((0, 5))).Tools);
        Assert.Equal("assign_unit_uids", tool.Id);
        Assert.Equal("map", tool.Target);
        Assert.Empty(tool.Parameters);
        Assert.Contains("很多地方与 UID 有关", tool.Confirmation);
        Assert.Contains("是否确认继续", tool.Confirmation);
    }

    [Fact]
    public void Sorts_by_cell_with_stable_ties_preserving_rows_and_all_other_data_and_supports_undo()
    {
        var doc = Sample((7, 200), (0, 300), (3, 400), (3, 500), (9, 44), (-1, 45));
        var host = Host(doc);
        // 有符号的地图外地块标记也不应参与地图内排序。
        Assert.Equal(-1, Number(doc, "/6/0[5]/0.0"));
        string before = BtlFrontJson.Serialize(doc);
        var expected = BtlFrontJson.CloneDocument(doc);
        int[] expectedIds = { 4, 1, 2, 3, 44, 45 };
        for (int i = 0; i < expectedIds.Length; i++) Set(expected, $"/6/0[{i}]/0.2", expectedIds[i]);
        Set(expected, "/6/2", 46);
        var originalRows = Units(doc).V.ToArray();
        Run(host);
        Assert.Equal(expectedIds, Ids(doc));
        Assert.True(BtlFrontJson.ContentEquals(expected, doc));
        for (int i = 0; i < originalRows.Length; i++) Assert.Same(originalRows[i], Units(doc).V[i]);
        Assert.Equal(1, host.UndoFrames);
        string after = BtlFrontJson.Serialize(doc);
        Assert.True(doc.Editing.Undo()); Assert.Equal(before, BtlFrontJson.Serialize(doc));
        Assert.True(doc.Editing.Redo()); Assert.Equal(after, BtlFrontJson.Serialize(doc));
        host.Load(doc); Run(host);
        Assert.Equal(after, BtlFrontJson.Serialize(doc)); Assert.Equal(1, host.UndoFrames);
    }

    [Fact]
    public void Offmap_collisions_follow_onmap_range_without_displacing_reserved_ids()
    {
        var doc = Sample((4, 30), (0, 40), (-1, 1), (8, 4), (-1, 4), (-1, 0), (-1, 65535));
        var host = Host(doc);
        Run(host);
        Assert.Equal(new[] { 2, 1, 3, 4, 5, 6, 7 }, Ids(doc));
        Assert.Equal(8, Number(doc, "/6/2"));
    }

    [Fact]
    public void Remaps_all_known_references_once_including_uid_swaps_and_offmap_collisions()
    {
        var doc = Sample((5, 1), (0, 2), (-1, 3));
        var groups = FrontEdit.EnsureVec((BtlTable)doc.Root.F[6], 1, "table");
        var group = BtlFrontJson.NewTable(); groups.V.Add(group);
        FrontEdit.EnsureVec(group, 1, "u16").V.AddRange(new object[] { (ushort)1, (ushort)2, (ushort)3, (ushort)0, (ushort)600 });
        var routes = FrontEdit.EnsureVec((BtlTable)doc.Root.F[6], 3, "table");
        var route = BtlFrontJson.NewTable(); routes.V.Add(route);
        FrontEdit.EnsureVec(route, 1, "u16").V.Add((ushort)2);
        var events = FrontEdit.EnsureVec(FrontEdit.EnsureTable(doc.Root, 5), 0, "table");
        var building = BtlFrontJson.NewTable(); events.V.Add(building);
        FrontEdit.SetScalar(FrontEdit.EnsureTable(FrontEdit.EnsureTable(building, 3), 2), 5, "u16", (ushort)1);
        foreach (int field in new[] { 0, 1 })
        {
            var queues = FrontEdit.EnsureVec(FrontEdit.EnsureTable(doc.Root, 8), field, "table");
            var queue = BtlFrontJson.NewTable(); queues.V.Add(queue);
            var entries = FrontEdit.EnsureVec(queue, 1, "table");
            var entry = BtlFrontJson.NewTable(); entries.V.Add(entry);
            FrontEdit.SetScalar(entry, 1, "u16", (ushort)2);
        }
        FrontEdit.SetScalar(FrontEdit.EnsureTable((BtlTable)Units(doc).V[0], 3), 5, "u16", (ushort)2);
        // 私有未知字段中的数值不应跟随替换。
        FrontEdit.SetScalar(doc.Root, 90, "u16", (ushort)1);
        var host = Host(doc);
        Run(host);
        Assert.Equal(new[] { 2, 1, 3 }, Ids(doc));
        Assert.Equal(1, Number(doc, "/6/0[0]/3/5"));
        Assert.Equal(2, Number(doc, "/5/0[0]/3/2/5"));
        Assert.Equal(new[] { 2, 1, 3, 0, 600 }, Enumerable.Range(0, 5).Select(i => Number(doc, $"/6/1[0]/1[{i}]")).ToArray());
        Assert.Equal(1, Number(doc, "/6/3[0]/1[0]"));
        Assert.Equal(1, Number(doc, "/8/0[0]/1[0]/1"));
        Assert.Equal(1, Number(doc, "/8/1[0]/1[0]/1"));
        Assert.Equal(1, Number(doc, "/90"));
        Assert.Null(ResearchEdits.Node(doc, "/6/0[1]/3"));
        var built = FrontToBtl.Build(doc); Assert.True(built.Ok, string.Join("\n", built.Issues));
        var read = BtlToFront.FromBytes(built.Bytes, doc.Context); Assert.True(read.Ok, string.Join("\n", read.Issues));
        Assert.Equal(Ids(doc), Ids(read.Document));
        Assert.Equal(2, Number(read.Document, "/5/0[0]/3/2/5"));
    }

    [Fact]
    public void Remaps_references_to_displaced_offmap_units()
    {
        var doc = Sample((0, 11), (-1, 1));
        var host = Host(doc);
        Target(doc, 0, 1);
        Run(host);
        Assert.Equal(new[] { 1, 2 }, Ids(doc));
        Assert.Equal(2, Number(doc, "/6/0[0]/3/5"));
    }

    [Fact]
    public void Ambiguous_referenced_duplicates_are_rejected_without_partial_writes()
    {
        var doc = Sample((2, 17), (0, 17));
        var host = Host(doc); Target(doc, 0, 17);
        string before = BtlFrontJson.Serialize(doc);
        var error = Assert.Throws<FrontEditException>(() => Run(host));
        Assert.Contains("被多个单位共用", error.Message);
        Assert.Equal(before, BtlFrontJson.Serialize(doc)); Assert.Equal(0, host.UndoFrames);
    }

    [Fact]
    public void Unreferenced_duplicates_and_zero_ids_can_be_repaired()
    {
        var doc = Sample((3, 0), (0, 0), (2, 7), (1, 7)); var host = Host(doc);
        Target(doc, 0, 0);
        Run(host);
        Assert.Equal(new[] { 4, 1, 3, 2 }, Ids(doc));
        Assert.Equal(0, Number(doc, "/6/0[0]/3/5"));
    }

    [Fact]
    public void Empty_onmap_set_is_rejected_without_touching_offmap_units_or_counter()
    {
        var doc = Sample((-1, 1), (8, 2)); var host = Host(doc);
        string before = BtlFrontJson.Serialize(doc);
        Assert.Contains("地图内没有", Assert.Throws<FrontEditException>(() => Run(host)).Message);
        Assert.Equal(before, BtlFrontJson.Serialize(doc)); Assert.Equal(0, host.UndoFrames);
    }
}
