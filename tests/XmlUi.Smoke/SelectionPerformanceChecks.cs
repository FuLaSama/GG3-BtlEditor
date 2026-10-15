using System.Diagnostics;
using System.Text.Json;
using BtlCore.Fb;
using BtlCore.Front;
using BtlCore.Scripting;
using BtldMapEditor;

internal static class SelectionPerformanceChecks
{
    static IEnumerable<Control> All(Control root)
    {
        foreach(Control child in root.Controls) { yield return child; foreach(var c in All(child))yield return c; }
    }
    public static void Run(string root,LayoutBundle layout,bool baseline=false)
    {
        var host=new ScriptHost();foreach(var script in layout.Scripts)host.Execute(script.Text,script.Name);
        var doc=new BtlFrontDocument {Context=host.CreateSchemaContext(),Root=BtlFrontJson.NewTable()};
        var map=FrontEdit.EnsureTable(doc.Root,1);
        FrontEdit.SetField(map,0,FrontEdit.StructFromFbs(doc.Context,"Size",(ushort)2,(ushort)1,(ushort)0,(ushort)0,(ushort)2,(ushort)1));
        var units=FrontEdit.EnsureVec(FrontEdit.EnsureTable(doc.Root,6),0,"table");
        for(ushort i=0;i<2;i++)
        {
            var unit=UnitEdits.Create(i,0,(ushort)(10+i),101,256,0,(ushort)(10+i),20,doc.Context);
            FrontEdit.SetScalar(unit,1,"u16",(ushort)(30+i));
            var state=FrontEdit.EnsureTable(unit,8);FrontEdit.SetScalar(state,0,"u8",(byte)i);
            var buffs=FrontEdit.EnsureVec(state,2,"table");var buff=BtlFrontJson.NewTable();FrontEdit.SetScalar(buff,0,"u16",(ushort)(40+i));buffs.V.Add(buff);
            units.V.Add(unit);
        }
        host.Load(doc);
        using var form=new Form {ShowInTaskbar=false,Opacity=0,StartPosition=FormStartPosition.Manual,
            Location=new Point(-32000,-32000),Size=new Size(580,900),AutoScroll=true};
        using var view=new LayoutPageControl(layout.Tabs.Single(t=>t.Id=="unit").Page,host,()=>{});
        view.ErrorOccurred+=e=>throw new InvalidOperationException("selection performance UI",e);
        form.Controls.Add(view);form.Show();view.Reload(doc);view.SetCell(0);
        void Expand(string name)=>All(view).OfType<CollapsibleGroup>().Single(c=>c.Name==name).SetCollapsed(false);
        Expand("unit/unit_details");Expand("unit/unit_details/detail/6/0[]/8");Expand("unit/unit_details/detail/6/0[]/8/2");
        view.SetCell(1);view.SetCell(0);Application.DoEvents();
        var original=All(view).OfType<TextBox>().Single(c=>c.Name=="detail.value/6/0[0]/1");
        var originalList=All(view).OfType<ListView>().Single(c=>c.Name=="detail.list/6/0[0]/8/2");
        int created=0,disposed=0;
        void Track(Control control)
        {
            control.Disposed+=(_,_)=>disposed++;
            control.ControlAdded+=(_,e)=>{created++;Track(e.Control);};
            foreach(Control child in control.Controls)Track(child);
        }
        Track(view);string before=BtlFrontJson.Serialize(doc);
        long allocation=GC.GetAllocatedBytesForCurrentThread();var watch=Stopwatch.StartNew();
        const int iterations=24;
        for(int i=0;i<iterations;i++) {view.SetCell((i+1)%2);Application.DoEvents();}
        watch.Stop();allocation=GC.GetAllocatedBytesForCurrentThread()-allocation;
        var report=new {iterations,elapsed_ms=watch.Elapsed.TotalMilliseconds,average_ms=watch.Elapsed.TotalMilliseconds/iterations,
            allocated_bytes=allocation,controls_created=created,controls_disposed=disposed};
        string output=Path.Combine(root,"artifacts/selection-performance");Directory.CreateDirectory(output);
        File.WriteAllText(Path.Combine(output,baseline ? "before.json" : "after.json"),JsonSerializer.Serialize(report,new JsonSerializerOptions{WriteIndented=true}));
        Console.WriteLine("Selection performance: "+JsonSerializer.Serialize(report));
        if(before!=BtlFrontJson.Serialize(doc))throw new InvalidOperationException("selection cannot mutate document");
        if(baseline)return;
        if(created!=0 || disposed!=0 || original.IsDisposed)throw new InvalidOperationException("object switching must reuse existing controls");
        if(!ReferenceEquals(original,All(view).Single(c=>c.Name=="detail.value/6/0[0]/1")))throw new InvalidOperationException("scalar editor identity changed");
        if(!ReferenceEquals(originalList,All(view).Single(c=>c.Name=="detail.list/6/0[0]/8/2")))throw new InvalidOperationException("detail list identity changed");
        view.SetCell(1);
        var box=All(view).OfType<TextBox>().Single(c=>c.Name=="detail.value/6/0[1]/1");box.Text="88";
        All(view).OfType<Button>().Single(c=>c.Name=="detail.apply/6/0[1]/1").PerformClick();
        if(Convert.ToInt32(((BtlScalar)((BtlTable)units.V[1]).F[1]).V)!=88 || Convert.ToInt32(((BtlScalar)((BtlTable)units.V[0]).F[1]).V)!=30)
            throw new InvalidOperationException("reused editor must commit to current selected object");
        box.Focus();box.SelectionStart=1;view.Reload(doc);
        if(!box.Focused || box.SelectionStart!=1)throw new InvalidOperationException("refresh must preserve focus and caret in reused editor");
        form.AutoScrollPosition=new Point(0,180);Application.DoEvents();var scroll=form.AutoScrollPosition;
        view.SetCell(0);Application.DoEvents();
        if(form.AutoScrollPosition!=scroll)throw new InvalidOperationException("object selection must preserve sidebar scroll position");
        view.SetCell(1);
        var selectedBuffs=(BtlVector)((BtlTable)((BtlTable)units.V[1]).F[8]).F[2];
        var next=BtlFrontJson.NewTable();FrontEdit.SetScalar(next,0,"u16",(ushort)55);selectedBuffs.V.Add(next);view.Reload(doc);
        originalList.Items[1].Selected=true;Application.DoEvents();
        var buffBox=All(view).OfType<TextBox>().Single(c=>c.Name=="detail.value/6/0[1]/8/2[1]/0");buffBox.Text="66";
        All(view).OfType<Button>().Single(c=>c.Name=="detail.apply/6/0[1]/8/2[1]/0").PerformClick();
        if(Convert.ToInt32(((BtlScalar)((BtlTable)selectedBuffs.V[1]).F[0]).V)!=66 || Convert.ToInt32(((BtlScalar)((BtlTable)selectedBuffs.V[0]).F[0]).V)!=41)
            throw new InvalidOperationException("reused row editor must follow current nested list selection");
        ResearchEdits.Clear(doc,"/6/0[1]",8);view.Reload(doc);
        if(!All(view).OfType<Button>().Any(c=>c.Name=="detail.create/6/0[1]/8" && c.Visible))
            throw new InvalidOperationException("missing optional configuration shows creation state");
        view.SetCell(0);
        if(All(view).OfType<TextBox>().Single(c=>c.Name=="detail.value/6/0[0]/8/0").Text!="0")
            throw new InvalidOperationException("optional editor returns to existing object's values");
        var fold=All(view).OfType<CollapsibleGroup>().Single(c=>c.Name=="unit/unit_details");fold.SetCollapsed(true);
        view.SetCell(1);view.SetCell(0);fold.SetCollapsed(false);
        if(All(view).OfType<TextBox>().Single(c=>c.Name=="detail.value/6/0[0]/1").Text!="30")
            throw new InvalidOperationException("reopening a collapsed group binds current object");
        VerifyListRefresh(root,layout,host,form,view);
        Console.WriteLine("Selection performance checks PASS: stable controls and rows, current-object writes, optional states, focus, scroll and read-only selection.");
    }

    static void VerifyListRefresh(string root,LayoutBundle layout,ScriptHost host,Form form,Control oldView)
    {
        form.Controls.Remove(oldView);
        var doc=StageJson.LoadFile(Path.Combine(root,"src/BtldMapEditor/samples/stage10101.json"),context:host.CreateSchemaContext());
        host.Load(doc);
        using var view=new LayoutPageControl(layout.Tabs.Single(t=>t.Id=="stage").Page,host,()=>{});
        form.Controls.Add(view);view.Reload(doc);Application.DoEvents();
        var list=All(view).OfType<ListView>().First();var item=list.Items[0];item.Selected=true;
        string before=item.SubItems[1].Text;
        ResearchEdits.Set(doc,"/2/0[0]","StageTarget",1,"123");view.Reload(doc);
        if(!ReferenceEquals(item,list.Items[0]) || !item.Selected || item.SubItems[1].Text!="123")
            throw new InvalidOperationException("list refresh must update values without replacing rows or selection");
        doc.Editing.Undo();view.Reload(doc);
        if(!ReferenceEquals(item,list.Items[0]) || !item.Selected || item.SubItems[1].Text!=before)
            throw new InvalidOperationException("undo updates existing row without rebuilding list");
    }
}
