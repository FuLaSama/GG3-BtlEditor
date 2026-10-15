using BtlCore.Front;
using BtlCore.Scripting;
using BtldMapEditor;

internal static class IndependentDetailsChecks
{
    static IEnumerable<Control> All(Control root)
    {
        foreach(Control child in root.Controls) {yield return child;foreach(var nested in All(child))yield return nested;}
    }
    static T Named<T>(Control root,string name) where T:Control => All(root).OfType<T>().Single(c=>c.Name==name);
    static void Check(bool ok,string message) {if(!ok)throw new InvalidOperationException(message);}
    public static void Run(string root,LayoutBundle layout)
    {
        CellDetailsChecks.Run(root,layout);
        foreach(string page in new[]{"faction"})
        {
            var host=new ScriptHost();foreach(var script in layout.Scripts)host.Execute(script.Text,script.Name);
            var doc=new BtlFrontDocument{Root=BtlFrontJson.NewTable(),Context=host.CreateSchemaContext()};
            var map=FrontEdit.EnsureTable(doc.Root,1);
            FrontEdit.SetField(map,0,FrontEdit.StructFromFbs(doc.Context,"Size",(ushort)4,(ushort)1,(ushort)0,(ushort)0,(ushort)4,(ushort)1));
            int tableId=4;
            string path="/4/0",section="faction_details";
            var rows=FrontEdit.EnsureVec(FrontEdit.EnsureTable(doc.Root,tableId),0,"table");
            for(int i=0;i<2;i++)
            {
                var row=BtlFrontJson.NewTable();rows.V.Add(row);
                var info=FrontEdit.StructFromFbs(doc.Context,"FactionMetadata");
                info.V[0]=(ushort)(10+i);info.V[1]=(ushort)(20+i);FrontEdit.SetField(row,0,info);
                FrontEdit.SetScalar(row,9,"u8",(byte)(30+i));
            }
            host.Load(doc);doc.Editing.ResetHistory();
            using var form=new Form{ShowInTaskbar=false,Opacity=0,StartPosition=FormStartPosition.Manual,
                Location=new Point(-32000,-32000),Size=new Size(900,1000),AutoScroll=true};
            LayoutPageControl view=null;
            view=new LayoutPageControl(layout.Tabs.Single(t=>t.Id==page).Page,host,()=>view.Reload(doc));
            view.ErrorOccurred+=e=>throw new InvalidOperationException("independent "+page,e);
            form.Controls.Add(view);form.Show();view.Reload(doc);view.SetCell(3); // 未选择建筑/势力。
            string before=BtlFrontJson.Serialize(doc);int history=doc.Editing.HistoryCount;
            var fold=Named<CollapsibleGroup>(view,page+"/"+section);fold.SetCollapsed(false);Application.DoEvents();
            var picker=Named<ComboBox>(view,"detail.browse"+path);
            Check(picker.Items.Count==2 && picker.SelectedIndex==0,page+" details open without external selection");
            int field=9;
            Check(Named<TextBox>(view,"detail.value"+path+"[0]/"+field).Text=="30",page+" defaults to first object");
            picker.SelectedIndex=1;Application.DoEvents();
            Check(Named<TextBox>(view,"detail.value"+path+"[1]/"+field).Text=="31",page+" object picker switches binding");
            Check(view.SelectedRow(path)==1,"detail picker synchronizes faction list");
            Check(before==BtlFrontJson.Serialize(doc) && history==doc.Editing.HistoryCount,"browsing does not create data or undo frames");
            Named<TextBox>(view,"detail.value"+path+"[1]/"+field).Text="42";
            Named<Button>(view,"detail.apply"+path+"[1]/"+field).PerformClick();Application.DoEvents();
            Check(Convert.ToInt32(((BtlScalar)((BtlTable)rows.V[1]).F[field]).V)==42,"detail edits chosen object");
            Check(picker.SelectedIndex==1,"chosen object survives commit and reload");
            Check(doc.Editing.Undo(),"detail edit can be undone");view.Reload(doc);
            Check(Named<TextBox>(view,"detail.value"+path+"[1]/"+field).Text=="31","undo updates chosen detail");
            {
                var list=All(view).OfType<CollapsibleGroup>().Single(g=>g.Title=="势力");
                var listView=All(list).OfType<ListView>().Single();
                listView.SelectedIndices.Clear();listView.Items[0].Selected=true;Application.DoEvents();
                Check(picker.SelectedIndex==0,"faction list selection still follows into details");
            }
            // 删除后夹取有效下标，空列表展示空状态，不在打开详情时自动创建对象。
            var current=(BtlVector)((BtlTable)doc.Root.F[tableId]).F[0];current.V.Clear();
            view.Reload(doc);Application.DoEvents();
            Check(!picker.Enabled && picker.SelectedIndex==-1,"empty list disables object picker");
            Check(All(view).OfType<Label>().Any(c=>c.Visible && c.Text.Contains("当前没有可查看的对象")),"empty detail explains absence");
            Check(current.V.Count==0,"empty details cannot create phantom objects");
        }
        Console.WriteLine("Independent faction details PASS: no-selection display, picker/list following, commit, undo and empty lists.");
    }
}
