using BtlCore.Fb;
using BtlCore.Front;
using BtlCore.Scripting;
using BtldMapEditor;

internal static class CellDetailsChecks
{
    static IEnumerable<Control> All(Control root)
    {
        foreach(Control child in root.Controls) {yield return child;foreach(var nested in All(child))yield return nested;}
    }
    static T Named<T>(Control root,string name) where T:Control => All(root).OfType<T>().Single(c=>c.Name==name);
    static void Check(bool ok,string message) {if(!ok)throw new InvalidOperationException(message);}
    public static void Run(string root,LayoutBundle layout)
    {
        var host=new ScriptHost();foreach(var script in layout.Scripts)host.Execute(script.Text,script.Name);
        var doc=new BtlFrontDocument{Root=BtlFrontJson.NewTable(),Context=host.CreateSchemaContext()};
        var map=FrontEdit.EnsureTable(doc.Root,1);
        FrontEdit.SetField(map,0,FrontEdit.StructFromFbs(doc.Context,"Size",(ushort)4,(ushort)1,(ushort)0,(ushort)0,(ushort)4,(ushort)1));
        FrontEdit.EnsureVec(map,2,"u16").V.AddRange(Enumerable.Repeat((object)(ushort)0,4));
        var rows=FrontEdit.EnsureVec(FrontEdit.EnsureTable(doc.Root,5),0,"table");
        for(int i=0;i<2;i++)
        {
            var row=BtlFrontJson.NewTable();rows.V.Add(row);
            FrontEdit.SetScalar(row,0,"u16",(ushort)i);FrontEdit.SetScalar(row,1,"u16",(ushort)(30+i));
            if(i==0)FrontEdit.SetField(row,5,FrontEdit.StructFromFbs(doc.Context,"Inline3",(byte)1,(byte)2,(byte)3));
            else FrontEdit.SetScalar(FrontEdit.EnsureTable(row,4),0,"u8",(byte)2);
        }
        host.Load(doc);doc.Editing.ResetHistory();
        using var form=new Form{ShowInTaskbar=false,Opacity=0,StartPosition=FormStartPosition.Manual,
            Location=new Point(-32000,-32000),Size=new Size(900,1000),AutoScroll=true};
        LayoutPageControl view=null;
        view=new LayoutPageControl(layout.Tabs.Single(t=>t.Id=="site").Page,host,()=>view.Reload(host.Document));
        var errors=new List<Exception>();view.ErrorOccurred+=errors.Add;
        form.Controls.Add(view);form.Show();view.Reload(doc);view.SetCell(3);
        Named<CollapsibleGroup>(view,"site/event_details").SetCollapsed(false);Application.DoEvents();
        Check(!All(view).OfType<ComboBox>().Any(c=>c.Name=="detail.browse/5/0"),"cell detail has no object picker");
        string before=BtlFrontJson.Serialize(doc);int frames=doc.Editing.HistoryCount;
        Check(Named<Label>(view,"detail.cell/5/0").Text=="地块 ID：3","empty detail identifies selected map cell");
        Check(Named<TextBox>(view,"detail.value/5/0[2]/1").Visible,"empty cell exposes unwritten fields");
        var fire=Named<CollapsibleGroup>(view,"site/event_details/detail/5/0[]/5");
        fire.SetCollapsed(false);Application.DoEvents();
        Check(Named<Button>(view,"detail.create/5/0[2]/5").Enabled,"empty cell can configure fire without a building");
        view.SetCell(0);
        Check(Named<TextBox>(view,"detail.value/5/0[0]/5.2").Text=="3","existing fire on building-free cell is shown");
        view.SetCell(1);
        Check(Named<TextBox>(view,"detail.value/5/0[1]/1").Text=="31","fort-only cell uses its own data");
        view.SetCell(3);
        Check(before==BtlFrontJson.Serialize(doc) && frames==doc.Editing.HistoryCount,"visiting empty cells never creates records or history");
        // 无效输入时，首次补记录必须和实际写入一起回滚。
        Named<TextBox>(view,"detail.value/5/0[2]/1").Text="65536";
        Named<Button>(view,"detail.apply/5/0[2]/1").PerformClick();Application.DoEvents();
        Check(errors.Count==1 && before==BtlFrontJson.Serialize(doc) && frames==doc.Editing.HistoryCount,
            "invalid first edit rolls back provisional cell record");errors.Clear();
        Named<Button>(view,"detail.create/5/0[2]/5").PerformClick();Application.DoEvents();
        Check(errors.Count==0,"creating fire on empty cell succeeds");
        rows=(BtlVector)((BtlTable)doc.Root.F[5]).F[0];var created=(BtlTable)rows.V[2];
        Check(rows.V.Count==3 && Convert.ToInt32(((BtlScalar)created.F[0]).V)==3,"new record belongs to clicked empty cell");
        Check(created.F.Keys.Order().SequenceEqual(new[]{0,5}),"first write creates only cell ID and fire, without building or fort");
        Check(doc.Editing.HistoryCount==frames+1,"record and fire creation use one undo frame");
        Check(doc.Editing.Undo(),"undo first cell edit");view.Reload(doc);
        Check(before==BtlFrontJson.Serialize(doc),"undo removes newly created record completely");
        Check(doc.Editing.Redo(),"redo first cell edit");view.Reload(doc);
        Named<TextBox>(view,"detail.value/5/0[2]/5.2").Text="7";
        Named<Button>(view,"detail.apply/5/0[2]/5.2").PerformClick();Application.DoEvents();
        rows=(BtlVector)((BtlTable)doc.Root.F[5]).F[0];created=(BtlTable)rows.V[2];
        Check(Convert.ToInt32(((BtlStruct)created.F[5]).V[2])==7,"fire edits reach selected empty cell");
        view.SetCell(0);
        Check(Named<TextBox>(view,"detail.value/5/0[0]/5.2").Text=="3","neighbor cell data stays intact");
        // 首次普通标量写入同样只补地块记录，不要求先创建建筑。
        view.SetCell(2);Named<TextBox>(view,"detail.value/5/0[3]/1").Text="42";
        Named<Button>(view,"detail.apply/5/0[3]/1").PerformClick();Application.DoEvents();
        rows=(BtlVector)((BtlTable)doc.Root.F[5]).F[0];
        Check(((BtlTable)rows.V[3]).F.Keys.Order().SequenceEqual(new[]{0,1}),"scalar first edit creates minimal cell record");
        Check(errors.Count==0,"all valid cell edits succeed");
        var built=FrontToBtl.Build(doc);Check(built.Ok,string.Join(";",built.Issues));
        var read=BtlToFront.FromBytes(built.Bytes,doc.Context);Check(read.Ok,string.Join(";",read.Issues));
        var exported=(BtlVector)((BtlTable)read.Document.Root.F[5]).F[0];
        Check(Convert.ToInt32(((BtlScalar)((BtlTable)exported.V[2]).F[0]).V)==3
            && Convert.ToInt32(((BtlStruct)((BtlTable)exported.V[2]).F[5]).V[2])==7,"empty-cell fire survives BTL export and reopen");
        // 整个地标表缺省的全新地图，也可浏览空格并在第一次修改时正确建表。
        var empty=new BtlFrontDocument{Root=BtlFrontJson.NewTable(),Context=doc.Context};
        empty.Root.F[1]=BtlFrontJson.Clone(map);host.Load(empty);view.Reload(empty);view.SetCell(3);
        string emptyBefore=BtlFrontJson.Serialize(empty);
        Check(!empty.Root.F.ContainsKey(5),"opening fresh map does not create trigger table");
        Named<TextBox>(view,"detail.value/5/0[0]/1").Text="9";
        Named<Button>(view,"detail.apply/5/0[0]/1").PerformClick();Application.DoEvents();
        Check(errors.Count==0 && ((BtlVector)((BtlTable)empty.Root.F[5]).F[0]).V.Count==1,"first edit creates absent trigger table");
        Check(empty.Editing.Undo(),"undo fresh map detail edit");view.Reload(empty);
        Check(emptyBefore==BtlFrontJson.Serialize(empty),"undo restores absent trigger table");
        Console.WriteLine("Cell details PASS: current map cell, building-free fire, missing rows/table, minimal first writes, rollback, one-step undo and BTL roundtrip.");
    }
}
