using BtlCore.Fb;
using BtlCore.Front;
using BtlCore.Scripting;
using BtldMapEditor;

internal static class ResearchDetailsChecks
{
    static IEnumerable<Control> All(Control root)
    {
        foreach(Control child in root.Controls) { yield return child; foreach(var c in All(child))yield return c; }
    }
    static T Named<T>(Control root,string name) where T:Control => All(root).OfType<T>().Single(c=>c.Name==name);
    static void Check(bool ok,string message) { if(!ok)throw new InvalidOperationException(message); }
    public static void Run(string root,LayoutBundle layout,bool render)
    {
        Console.WriteLine("Testing research detail controls...");
        var host=new ScriptHost(); foreach(var script in layout.Scripts)host.Execute(script.Text,script.Name);
        var doc=new BtlFrontDocument {Context=host.CreateSchemaContext(),Root=BtlFrontJson.NewTable()};
        var map=FrontEdit.EnsureTable(doc.Root,1);
        FrontEdit.SetField(map,0,FrontEdit.StructFromFbs(doc.Context,"Size",(ushort)2,(ushort)1,(ushort)0,(ushort)0,(ushort)2,(ushort)1));
        var units=FrontEdit.EnsureVec(FrontEdit.EnsureTable(doc.Root,6),0,"table");
        units.V.Add(UnitEdits.Create(0,0,10,101,256,0,10,20,doc.Context));
        units.V.Add(UnitEdits.Create(65535,0,11,101,256,0,10,20,doc.Context));
        var unit=(BtlTable)units.V[0]; FrontEdit.SetScalar(unit,99,"u32",(uint)12345);
        host.Load(doc); doc.Editing.ResetHistory();
        using var form=new Form {ShowInTaskbar=false,Opacity=0,StartPosition=FormStartPosition.Manual,
            Location=new Point(-32000,-32000),Size=new Size(900,1000),AutoScroll=true};
        LayoutPageControl view=null;
        view=new LayoutPageControl(layout.Tabs.Single(t=>t.Id=="unit").Page,host,()=>view.Reload(doc));
        view.ErrorOccurred+=e=>throw new InvalidOperationException("research detail UI",e);
        Console.WriteLine("Mounting research detail view...");
        form.Controls.Add(view); form.Show(); view.Reload(doc); view.SetCell(0); Application.DoEvents();
        string before=BtlFrontJson.Serialize(doc); int snapshots=doc.Editing.SnapshotCount;
        CollapsibleGroup Fold(string name)=>Named<CollapsibleGroup>(view,name);
        void Expand(string name) { Fold(name).SetCollapsed(false); Application.DoEvents(); }
        void Apply(string path,string value)
        {
            Named<TextBox>(view,"detail.value"+path).Text=value;
            Named<Button>(view,"detail.apply"+path).PerformClick(); Application.DoEvents();
        }
        Console.WriteLine("Expanding research detail view...");
        var main=Fold("unit/unit_details"); Check(main.Collapsed,"advanced section starts collapsed");
        int closed=view.Height; main.SetCollapsed(false); Application.DoEvents();
        Check(view.Height>closed,"expanded details take layout space");
        main.SetCollapsed(true); Application.DoEvents(); Check(view.Height==closed,"collapse releases layout space");
        Expand("unit/unit_details");
        Check(before==BtlFrontJson.Serialize(doc) && snapshots==doc.Editing.SnapshotCount,"folding cannot edit or create history");
        Console.WriteLine("Applying research scalar..."); Apply("/6/0[0]/1","7");
        Check(Convert.ToInt32(((BtlScalar)unit.F[1]).V)==7,"new scalar edits selected unit");
        Check(!Fold("unit/unit_details").Collapsed,"detail fold survives commit and refresh");
        Check(Convert.ToUInt32(((BtlScalar)unit.F[99]).V)==12345,"detail edit retains unexposed fields");
        Console.WriteLine("Creating research child table..."); Expand("unit/unit_details/detail/6/0[]/12");
        Check(!unit.F.ContainsKey(12),"opening an unwritten child does not create it");
        Named<Button>(view,"detail.create/6/0[0]/12").PerformClick(); Application.DoEvents();
        Apply("/6/0[0]/12/0","42");
        Expand("unit/unit_details/detail/6/0[]/12/1");
        Named<TextBox>(view,"detail.array/6/0[0]/12/1").Text="0, 1, 255";
        Named<Button>(view,"detail.apply/6/0[0]/12/1").PerformClick(); Application.DoEvents();
        var binding=(BtlTable)unit.F[12];
        Check(Convert.ToInt32(((BtlScalar)binding.F[0]).V)==42 && ((BtlVector)binding.F[1]).V.Count==3,"nested binding and byte array edits reach document");
        Console.WriteLine("Editing research table vector..."); Expand("unit/unit_details/detail/6/0[]/8");
        Named<Button>(view,"detail.create/6/0[0]/8").PerformClick(); Application.DoEvents();
        Expand("unit/unit_details/detail/6/0[]/8/2");
        Named<Button>(view,"detail.add/6/0[0]/8/2").PerformClick(); Application.DoEvents();
        Apply("/6/0[0]/8/2[0]/0","24");
        Check(Convert.ToInt32(((BtlScalar)((BtlTable)((BtlVector)((BtlTable)unit.F[8]).F[2]).V[0]).F[0]).V)==24,"table vector row is editable after add");
        Check(doc.Editing.Undo(),"detail edits share undo history"); view.Reload(doc);
        Check(Named<TextBox>(view,"detail.value/6/0[0]/8/2[0]/0").Text=="","undo refreshes nested editor");
        Check(doc.Editing.Redo(),"detail edit redo");view.Reload(doc);
        units=(BtlVector)((BtlTable)doc.Root.F[6]).F[0]; unit=(BtlTable)units.V[0];
        if(render)
        {
            form.Width=580; Application.DoEvents();
            var details=Fold("unit/unit_details");
            using var image=new Bitmap(details.Width,Math.Min(details.Height,4000)); details.DrawToBitmap(image,new Rectangle(0,0,image.Width,image.Height));
            string output=Path.Combine(root,"artifacts/xml-ui");Directory.CreateDirectory(output); image.Save(Path.Combine(output,"research-details.png"));
        }
        // A filtered row uses its original vector index, including signed -1 cell values.
        var outside=All(view).OfType<CollapsibleGroup>().Single(c=>c.Title=="地图外部队 / 增援部队");
        var list=All(outside).OfType<ListView>().Single(); Check(list.Items.Count==1,"signed off-map unit appears in list");
        list.Items[0].Selected=true; Application.DoEvents();
        Apply("/6/0[1]/1","9");
        Check(Convert.ToInt32(((BtlScalar)((BtlTable)units.V[1]).F[1]).V)==9,"advanced fields follow off-map row selection");
        Check(Convert.ToInt32(((BtlScalar)unit.F[1]).V)==7,"off-map detail edit preserves map unit");
        view.SetCell(1); view.SetCell(0);
        Check(Named<TextBox>(view,"detail.value/6/0[0]/1").Text=="7","map selection resumes after off-map selection");
        Console.WriteLine("Research detail UI checks PASS: folding, lazy creation, nested lists, signed off-map selection, sibling preservation, undo/redo.");
    }
}
