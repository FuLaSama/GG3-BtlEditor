using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.Json;
using BtlCore.Fb;
using BtlCore.Front;
using BtlCore.Scripting;
using BtldMapEditor;
using BtldMapEditor.Front;

internal static class CanvasNavigationChecks
{
    static void Check(bool ok,string message) {if(!ok)throw new InvalidOperationException(message);}
    static void Mouse(EditorMapCanvas canvas,string method,MouseEventArgs args) => typeof(EditorMapCanvas)
        .GetMethod(method,BindingFlags.Instance|BindingFlags.NonPublic).Invoke(canvas,new object[]{args});
    public static void Run(string root,LayoutBundle layout,bool baseline=false,bool render=false)
    {
        GameSettings.SetExternalDataDir(Path.Combine(root,"dist"));GameSettings.LoadAllSettings();
        var host=new ScriptHost();foreach(var script in layout.Scripts)host.Execute(script.Text,script.Name);
        string path=Path.Combine(root,"战役相关文件/BTL字段相关研究结果/原版BTL文件/conquest1.btl");
        if(!File.Exists(path))path=Path.Combine(root,"战役相关文件/BTL/分析用原版样本/conquest1.btl");
        var doc=File.Exists(path) ? BtlToFront.FromFile(path,host.CreateSchemaContext()).Document : FrontNav.NewMap(100,100,0,0,100,100,20,1);
        string before=BtlFrontJson.Serialize(doc);long revision=doc.Editing.Revision;
        using var form=new Form {ShowInTaskbar=false,Opacity=0,StartPosition=FormStartPosition.Manual,
            Location=new Point(-32000,-32000),Size=new Size(1000,760)};
        using var canvas=new EditorMapCanvas{Dock=DockStyle.Fill,Document=doc};form.Controls.Add(canvas);form.Show();Application.DoEvents();
        Check(canvas.ShowCellIds && !canvas.ShowCellCoordinates && !canvas.ShowCellOffsets,"default overlays show cell IDs only");
        VerifyCellLabels(canvas);
        var reports=new List<object>();
        foreach(var style in new[]{MapVisualStyle.Sketch,MapVisualStyle.Sprite})
        {
            canvas.VisualStyle=style;canvas.Radius=25;canvas.AutoScrollPosition=new Point(1000,600);
            using var image=new Bitmap(canvas.Width,canvas.Height);canvas.DrawToBitmap(image,canvas.ClientRectangle);
            if(style==MapVisualStyle.Sprite)Check(canvas.SpriteLoadError==null,"sprite assets load: "+canvas.SpriteLoadError);
            var times=new List<double>();double positionMs=0,paintMs=0;long allocated=GC.GetAllocatedBytesForCurrentThread();
            for(int i=0;i<32;i++)
            {
                var watch=Stopwatch.StartNew();canvas.AutoScrollPosition=new Point(1000+i*12,600+i*8);
                double positioned=watch.Elapsed.TotalMilliseconds;positionMs+=positioned;
                canvas.DrawToBitmap(image,canvas.ClientRectangle);watch.Stop();paintMs+=watch.Elapsed.TotalMilliseconds-positioned;times.Add(watch.Elapsed.TotalMilliseconds);
            }
            allocated=GC.GetAllocatedBytesForCurrentThread()-allocated;
            var report=new{style=style.ToString(),frames=times.Count,average_ms=times.Average(),maximum_ms=times.Max(),p95_ms=times.Order().ElementAt((int)(times.Count*.95)),average_position_ms=positionMs/times.Count,average_paint_ms=paintMs/times.Count,allocated_bytes=allocated};
            reports.Add(report);Console.WriteLine("Canvas pan: "+JsonSerializer.Serialize(report));
            if(render)
            {
                string output=Path.Combine(root,"artifacts/canvas-navigation");Directory.CreateDirectory(output);
                image.Save(Path.Combine(output,style.ToString().ToLowerInvariant()+".png"));
            }
        }
        string reportDir=Path.Combine(root,"artifacts/canvas-navigation");Directory.CreateDirectory(reportDir);
        File.WriteAllText(Path.Combine(reportDir,baseline ? "before.json" : "after.json"),JsonSerializer.Serialize(reports,new JsonSerializerOptions{WriteIndented=true}));
        if(baseline)return;
        VerifyFramePixels(canvas);
        VerifyRegionPixels(doc);
        foreach(var style in new[]{MapVisualStyle.Sketch,MapVisualStyle.Sprite})
        {
            canvas.VisualStyle=style;
            foreach(Point anchor in new[]{new Point(120,140),new Point(520,330),new Point(850,550)})
            {
                canvas.Radius=25;canvas.AutoScrollPosition=new Point(1700,1300);
                var oldScroll=canvas.AutoScrollPosition;int radius=canvas.Radius;
                double worldX=(anchor.X-oldScroll.X-10)/(double)radius,worldY=(anchor.Y-oldScroll.Y-10)/(double)radius;
                int hit=canvas.HitTestCell(anchor);
                Mouse(canvas,"OnMouseWheel",new HandledMouseEventArgs(MouseButtons.None,0,anchor.X,anchor.Y,120));
                var nextScroll=canvas.AutoScrollPosition;
                Check(Math.Abs(worldX*canvas.Radius+10+nextScroll.X-anchor.X)<=.6,"zoom keeps mouse world X");
                Check(Math.Abs(worldY*canvas.Radius+10+nextScroll.Y-anchor.Y)<=.6,"zoom keeps mouse world Y");
                Check(hit<0 || canvas.HitTestCell(anchor)==hit,"zoom preserves cell under mouse");
                Mouse(canvas,"OnMouseWheel",new HandledMouseEventArgs(MouseButtons.None,0,anchor.X,anchor.Y,-120));
                Check(Math.Abs(canvas.AutoScrollPosition.X-oldScroll.X)<=1 && Math.Abs(canvas.AutoScrollPosition.Y-oldScroll.Y)<=1,"zoom roundtrip stays anchored");
            }
        }
        canvas.AutoScrollPosition=new Point(1200,900);Application.DoEvents();
        var start=canvas.AutoScrollPosition;
        Mouse(canvas,"OnMouseDown",new MouseEventArgs(MouseButtons.Right,1,500,350,0));
        Mouse(canvas,"OnMouseMove",new MouseEventArgs(MouseButtons.Right,0,540,375,0));
        Check(canvas.AutoScrollPosition==new Point(start.X+40,start.Y+25),"right drag tracks mouse movement");
        Check(GetUpdateRect(canvas.Handle,out var dirty,false) && dirty.Left==0 && dirty.Top==0
            && dirty.Right>=canvas.ClientSize.Width && dirty.Bottom>=canvas.ClientSize.Height,"pan invalidates complete viewport without copying old screen strips");
        Mouse(canvas,"OnMouseUp",new MouseEventArgs(MouseButtons.Right,0,540,375,0));
        Check(!canvas.Capture,"right drag releases capture");
        foreach(int command in new[]{0,1,2,3,6,7})
        {
            SendMessage(canvas.Handle,0x0114,new IntPtr(command),IntPtr.Zero);
            Check(-canvas.AutoScrollPosition.X>=0 && -canvas.AutoScrollPosition.X<=Math.Max(0,canvas.AutoScrollMinSize.Width-canvas.ClientSize.Width),"native scrollbar stays within view bounds");
        }
        canvas.Radius=5;canvas.AutoScrollPosition=Point.Empty;
        Mouse(canvas,"OnMouseWheel",new HandledMouseEventArgs(MouseButtons.None,0,300,300,-120));Check(canvas.Radius==5,"minimum zoom clamps");
        canvas.Radius=300;
        Mouse(canvas,"OnMouseWheel",new HandledMouseEventArgs(MouseButtons.None,0,300,300,120));Check(canvas.Radius==300,"maximum zoom clamps");
        canvas.Radius=25;
        Mouse(canvas,"OnMouseWheel",new HandledMouseEventArgs(MouseButtons.None,0,300,300,60));Check(canvas.Radius==25,"partial wheel delta accumulates");
        Mouse(canvas,"OnMouseWheel",new HandledMouseEventArgs(MouseButtons.None,0,300,300,60));Check(canvas.Radius==28,"partial wheel deltas make one notch");
        canvas.AutoScrollPosition=new Point(int.MaxValue,int.MaxValue);
        Check(-canvas.AutoScrollPosition.X<=Math.Max(0,canvas.AutoScrollMinSize.Width-canvas.ClientSize.Width),"horizontal pan clamps at map edge");
        canvas.AutoScrollPosition=new Point(-20,-30);Check(canvas.AutoScrollPosition==Point.Empty,"negative scroll clamps at origin");
        canvas.Document=FrontNav.NewMap(2,2,0,0,2,2,10,1);canvas.Radius=5;
        canvas.AutoScrollPosition=new Point(1000,1000);
        Check(canvas.AutoScrollPosition==Point.Empty,"map smaller than viewport has no stale scroll offset");
        canvas.Document=doc;canvas.Radius=25;
        var oldSize=form.Size;form.Size=new Size(760,600);Application.DoEvents();form.Size=oldSize;Application.DoEvents();
        Check(-canvas.AutoScrollPosition.X<=Math.Max(0,canvas.AutoScrollMinSize.Width-canvas.ClientSize.Width),"resize updates camera bounds");
        Check(before==BtlFrontJson.Serialize(doc) && revision==doc.Editing.Revision,"pan and zoom cannot modify document or history");
        Console.WriteLine("Canvas navigation checks PASS: mouse-anchored zoom, pan deltas, full viewport repaint, boundaries and document preservation.");
    }
    static void VerifyCellLabels(EditorMapCanvas canvas)
    {
        canvas.Radius=25;canvas.AutoScrollPosition=new Point(1000,600);
        foreach(var style in new[]{MapVisualStyle.Sketch,MapVisualStyle.Sprite})
        {
            canvas.VisualStyle=style;canvas.ShowCellIds=false;
            using var plain=new Bitmap(canvas.Width,canvas.Height);
            using var ids=new Bitmap(canvas.Width,canvas.Height);
            using var combined=new Bitmap(canvas.Width,canvas.Height);
            using var restored=new Bitmap(canvas.Width,canvas.Height);
            canvas.DrawToBitmap(plain,canvas.ClientRectangle);
            canvas.ShowCellIds=true;canvas.DrawToBitmap(ids,canvas.ClientRectangle);
            Check(!PixelBuffer.FromBitmap(plain).Argb.SequenceEqual(PixelBuffer.FromBitmap(ids).Argb),"cell IDs render immediately in "+style);
            canvas.ShowCellCoordinates=true;canvas.DrawToBitmap(combined,canvas.ClientRectangle);
            Check(!PixelBuffer.FromBitmap(ids).Argb.SequenceEqual(PixelBuffer.FromBitmap(combined).Argb),"ID and coordinate overlays can be enabled together in "+style);
            canvas.ShowCellCoordinates=false;canvas.ShowCellIds=false;canvas.DrawToBitmap(restored,canvas.ClientRectangle);
            Check(PixelBuffer.FromBitmap(plain).Argb.SequenceEqual(PixelBuffer.FromBitmap(restored).Argb),"disabling overlays restores clean pixels in "+style);
        }
        canvas.ShowCellIds=true;
        Console.WriteLine("Cell labels PASS: defaults, independent ID switch, combined coordinates, both appearances and cached repaint.");
    }
    static void VerifyFramePixels(EditorMapCanvas canvas)
    {
        canvas.VisualStyle=MapVisualStyle.Sketch;
        foreach(int radius in new[]{12,25,55})
        {
            canvas.Radius=radius;canvas.AutoScrollPosition=new Point(1100,900);
            using var incremental=new Bitmap(canvas.Width,canvas.Height);using var fresh=new Bitmap(canvas.Width,canvas.Height);
            canvas.DrawToBitmap(incremental,canvas.ClientRectangle);
            foreach(var delta in new[]{new Point(7,9),new Point(-13,4),new Point(6,-12),new Point(1500,1000)})
            {
                var scroll=canvas.AutoScrollPosition;canvas.AutoScrollPosition=new Point(-scroll.X+delta.X,-scroll.Y+delta.Y);
                canvas.DrawToBitmap(incremental,canvas.ClientRectangle);
                typeof(EditorMapCanvas).GetMethod("DropSceneFrames",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(canvas,null);
                canvas.DrawToBitmap(fresh,canvas.ClientRectangle);
                var a=PixelBuffer.FromBitmap(incremental);var b=PixelBuffer.FromBitmap(fresh);
                // GDI+ 在大坐标平移和多层抗锯齿混合处会有极少量 2 级舍入差。
                // 关闭原有偏移文字后，这些原本被文字盖住的边缘也参与比较；同时限制误差幅度和数量。
                int maximum=Enumerable.Range(0,a.Argb.Length).Where(i=>a.Argb[i]!=b.Argb[i])
                    .Select(i=>new[]{0,8,16,24}.Max(shift=>Math.Abs(((a.Argb[i]>>shift)&255)-((b.Argb[i]>>shift)&255))))
                    .DefaultIfEmpty(0).Max();
                int roundedTwice=Enumerable.Range(0,a.Argb.Length).Count(i=>new[]{0,8,16,24}
                    .Any(shift=>Math.Abs(((a.Argb[i]>>shift)&255)-((b.Argb[i]>>shift)&255))>1));
                Check(maximum<=2 && roundedTwice<=a.Argb.Length/10000,
                    $"retained sketch frame matches fresh paint within GDI rounding at radius {radius}, delta {delta}; maximum={maximum}, twice-rounded={roundedTwice}");
            }
        }
        canvas.Radius=25;canvas.AutoScrollPosition=new Point(1100,900);
        using(var incremental=new Bitmap(canvas.Width,canvas.Height))
        using(var fresh=new Bitmap(canvas.Width,canvas.Height))
        {
            foreach(var mode in new[]{MapRenderMode.All,MapRenderMode.TerrainOnly,MapRenderMode.Reinforcements})
            {
                canvas.RenderMode=mode;canvas.ShowCellCoordinates=!canvas.ShowCellCoordinates;
                canvas.SelectedCellIndex=canvas.HitTestCell(new Point(400,300));canvas.DropPreviewIndex=canvas.HitTestCell(new Point(500,400));
                canvas.DrawToBitmap(incremental,canvas.ClientRectangle);
                typeof(EditorMapCanvas).GetMethod("DropSceneFrames",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(canvas,null);
                canvas.DrawToBitmap(fresh,canvas.ClientRectangle);
                Check(PixelBuffer.FromBitmap(incremental).Argb.SequenceEqual(PixelBuffer.FromBitmap(fresh).Argb),"view flags invalidate retained scene");
            }
            canvas.RenderMode=MapRenderMode.All;canvas.SelectedCellIndex=-1;canvas.DropPreviewIndex=-1;canvas.ShowCellCoordinates=true;
            canvas.DrawToBitmap(incremental,canvas.ClientRectangle);
            int index=canvas.HitTestCell(new Point(400,300));
            if(index>=0)
            {
                canvas.Cells[index].Terrain^=512;canvas.NotifyContentChanged();
                canvas.DrawToBitmap(incremental,canvas.ClientRectangle);
                typeof(EditorMapCanvas).GetMethod("DropSceneFrames",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(canvas,null);
                canvas.DrawToBitmap(fresh,canvas.ClientRectangle);
                Check(PixelBuffer.FromBitmap(incremental).Argb.SequenceEqual(PixelBuffer.FromBitmap(fresh).Argb),"content changes invalidate retained scene");
                canvas.Cells[index].Terrain^=512;canvas.NotifyContentChanged();
            }
        }
        Console.WriteLine("Retained frame pixels PASS: horizontal/vertical/diagonal pans and large jumps match fresh redraws.");
    }
    static void VerifyRegionPixels(BtlFrontDocument doc)
    {
        Check(TerrainSpriteRenderer.TryCreate(out var renderer,out var error),"region renderer: "+error);
        using(renderer)
        {
            var scene=FrontTerrain.FromCells(doc,FrontNav.RebuildCells(doc));
            foreach(int step in new[]{1,3,17})
            {
                int width=155*step,height=129*step;
                int x=TerrainGeometry.CanvasWidth(scene.Width)/3/step*step,y=TerrainGeometry.CanvasHeight(scene.Height)/3/step*step;
                foreach(var delta in new[]{Point.Empty,new Point(7*step,9*step),new Point(-13*step,4*step),new Point(6*step,-12*step),new Point(220*step,140*step)})
                {
                    x+=delta.X;y+=delta.Y;
                    var region=new Rectangle(x,y,width,height);
                    var reused=PixelBuffer.FromBitmap(renderer.RenderRegion(scene,true,true,region,step,reuseOverlap:true));
                    var fresh=PixelBuffer.FromBitmap(renderer.RenderRegion(scene,true,true,region,step));
                    Check(reused.Argb.SequenceEqual(fresh.Argb),$"incremental sprite region matches fresh rendering at step {step}, delta {delta}");
                }
            }
        }
        Console.WriteLine("Incremental region pixels PASS: overlap copies and exposed strips match fresh rendering at multiple sample scales.");
    }
    [StructLayout(LayoutKind.Sequential)]struct NativeRect {public int Left,Top,Right,Bottom;}
    [DllImport("user32.dll")][return:MarshalAs(UnmanagedType.Bool)]
    static extern bool GetUpdateRect(IntPtr window,out NativeRect rect,[MarshalAs(UnmanagedType.Bool)]bool erase);
    [DllImport("user32.dll")]static extern IntPtr SendMessage(IntPtr window,int message,IntPtr wparam,IntPtr lparam);
}
