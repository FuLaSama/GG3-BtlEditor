using BtlCore.Front;
using BtlCore.Fb;
using BtlCore.Scripting;
using BtldMapEditor;

internal static class CollapsibleLayoutChecks
{
    static IEnumerable<Control> All(Control root)
    {
        foreach(Control child in root.Controls) { yield return child; foreach(var c in All(child))yield return c; }
    }
    static void Check(bool ok,string message) { if(!ok)throw new InvalidOperationException(message); }
    public static void Run(string root,LayoutBundle layout,bool render)
    {
        var host=new ScriptHost(); foreach(var script in layout.Scripts)host.Execute(script.Text,script.Name);
        var doc=StageJson.LoadFile(Path.Combine(root,"src/BtldMapEditor/samples/stage10101.json"),context:host.CreateSchemaContext());
        host.Load(doc); string before=BtlFrontJson.Serialize(doc);
        using var small=new Font("Microsoft YaHei UI",9f);
        using var normal=new Font("Microsoft YaHei UI",12f);
        using var large=new Font("Microsoft YaHei UI",16f);
        using var form=new Form {ShowInTaskbar=false,Opacity=0,StartPosition=FormStartPosition.Manual,
            Location=new Point(-32000,-32000),Size=new Size(540,900),AutoScroll=true};
        form.Show();
        foreach(var font in new[]{small,normal,large})
        foreach(string id in new[]{"stage","faction","unit"})
        {
            float points=font.SizeInPoints;
            form.Font=font;
            using var view=new LayoutPageControl(layout.Tabs.Single(t=>t.Id==id).Page,host,()=>{});
            form.Controls.Add(view);view.Reload(doc);Application.DoEvents();
            foreach(var list in All(view).OfType<ListView>())
            {
                Check(list.Width>view.Width*0.7,$"{id}: list must use available sidebar width at {points}pt");
                Check(list.Height>=100,$"{id}: list retains usable height");
                var save=All(list.Parent).OfType<Button>().FirstOrDefault(b=>b.AccessibleName==null);
                Check(save==null || save.Width>30,$"{id}: commands remain visible");
            }
            foreach(var fold in All(view).OfType<CollapsibleGroup>().ToArray())
            {
                var header=fold.Controls.OfType<Button>().Single();
                int textHeight=TextRenderer.MeasureText(fold.Title,header.Font).Height;
                Check(header.Height>=textHeight+4,$"{id}: header must fit font at {points}pt ({header.Height} < {textHeight+4})");
                fold.SetCollapsed(true);Application.DoEvents();
                Check(fold.Height==header.Height,$"{id}: collapsed content releases height");
                fold.SetCollapsed(false);Application.DoEvents();
                Check(fold.Height>=header.Height,$"{id}: can reopen group");
            }
            foreach(var list in All(view).OfType<ListView>())
            foreach(var button in All(list.Parent).OfType<Button>().Where(b=>b.AccessibleName==null))
            {
                var top=list.Parent.PointToClient(button.PointToScreen(Point.Empty));
                Check(top.Y>=0 && top.Y+button.Height<=list.Parent.Height,$"{id}: command {button.Text} fits content height at {points}pt");
                Check(top.X>=0 && top.X+button.Width<=list.Parent.Width,$"{id}: command {button.Text} fits content width at {points}pt");
            }
            if(render && points==12f)
            {
                form.AutoScrollPosition=Point.Empty;Application.DoEvents();
                using var bitmap=new Bitmap(form.ClientSize.Width,form.ClientSize.Height);
                form.DrawToBitmap(bitmap,new Rectangle(Point.Empty,bitmap.Size));
                string output=Path.Combine(root,"artifacts/collapsible-ui");Directory.CreateDirectory(output);
                bitmap.Save(Path.Combine(output,id+".png"));
            }
            form.Controls.Remove(view);
        }
        Check(before==BtlFrontJson.Serialize(doc),"layout, resizing and folding leave document unchanged");
        Console.WriteLine("Collapsible layout checks PASS: visible lists and commands, font sizes, folding and document preservation.");
    }
}
