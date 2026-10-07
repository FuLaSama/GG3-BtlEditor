using System.Diagnostics;
using System.Reflection;
using System.Text.Json;
using BtlCore.Fb;
using BtlCore.Front;
using BtlCore.Scripting;
using BtldMapEditor;
using BtldMapEditor.Front;

internal static class Program
{
    static readonly Dictionary<string, object> Results = new();
    [STAThread]
    static int Main(string[] args)
    {
        try
        {
            ApplicationConfiguration.Initialize();
            Application.SetUnhandledExceptionMode(UnhandledExceptionMode.ThrowException);
            string root=Root();
            Directory.CreateDirectory(Path.Combine(root,"artifacts/performance"));
            if(args.Contains("--sprites") || args.Contains("--verify")) GameSettings.SetExternalDataDir(Path.Combine(root, "dist"));
            GameSettings.LoadAllSettings();
            string path=args.FirstOrDefault(a=>a.EndsWith(".btl",StringComparison.OrdinalIgnoreCase))
                ?? Path.Combine(root,"战役相关文件/BTL/分析用原版样本/conquest1.btl");
            var session=Measure("read_btl",()=>FrontSession.FromBtl(path));
            var doc=session.Document;
            Results["file"]=Path.GetFileName(path);
            Results["width"]=FrontNav.MapWidth(doc); Results["height"]=FrontNav.MapHeight(doc);
            Results["units"]=FrontNav.Agents(doc)?.V.Count??0;
            Results["events"]=FrontNav.Events(doc)?.V.Count??0;
            Console.WriteLine($"Map {Results["width"]}x{Results["height"]}, units {Results["units"]}, events {Results["events"]}");
            var cells=Measure("project_cells",()=>FrontNav.RebuildCells(doc));
            Measure("sync_unchanged",()=>{FrontNav.SyncGrid(doc,cells);return true;});
            Measure("clone_document",()=>BtlFrontJson.CloneDocument(doc));
            Measure("serialize_json",()=>BtlFrontJson.Serialize(doc));
            using var form=Measure("create_window",()=>new MainEditorForm(true)
            {ShowInTaskbar=false,Opacity=0,StartPosition=FormStartPosition.Manual,Location=new Point(-32000,-32000)});
            form.Show();
            Set(form,"_front",session); Set(form,"_isInitialLoading",true);
            Measure("load_ui",()=>{Call(form,"OnDocumentLoaded");return true;});
            Measure("initial_history",()=>{Call(form,"InitHistory");return true;});
            Set(form,"_isInitialLoading",false);
            var canvas=Field<EditorMapCanvas>(form,"mapCanvas");
            using var bitmap=new Bitmap(canvas.Width,canvas.Height);
            Measure("sketch_first_paint",()=>{canvas.DrawToBitmap(bitmap,new Rectangle(Point.Empty,bitmap.Size));return true;});
            Measure("select_cell",()=>{Call(form,"CellSelectedClick",0);return true;});
            Measure("refresh_pages",()=>{Call(form,"RefreshEditorPages");return true;});
            var host=Field<ScriptHost>(form,"_stageHost");
            try
            {
                Measure("tool_all_units",()=>host.RunTool("heal_all"));

            }
            catch(Exception e){if(args.Contains("--verify"))throw; Results["tool_error"]=e.GetBaseException().Message;Console.WriteLine("Tool error: "+e.GetBaseException().Message);}
            if(args.Contains("--sprites"))
            {
                canvas.VisualStyle=MapVisualStyle.Sprite;
                Measure("sprite_first_paint",()=>{canvas.DrawToBitmap(bitmap,new Rectangle(Point.Empty,bitmap.Size));return true;});
                Measure("sprite_cached_paint",()=>{canvas.DrawToBitmap(bitmap,new Rectangle(Point.Empty,bitmap.Size));return true;});
                Results["sprite_error"]=canvas.SpriteLoadError;
                if (canvas.SpriteLoadError != null) throw new Exception(canvas.SpriteLoadError);
                var renderer=Field<TerrainSpriteRenderer>(canvas,"_spriteRenderer");
                Results["sprite_cache_width"]=renderer.MapBitmap.Width;
                Results["sprite_cache_height"]=renderer.MapBitmap.Height;
                canvas.AutoScrollPosition=new Point(1000,600);
                Measure("sprite_scroll_paint",()=>{canvas.DrawToBitmap(bitmap,new Rectangle(Point.Empty,bitmap.Size));return true;});
                canvas.AutoScrollPosition=new Point(1100,700);
                Measure("sprite_warm_scroll",()=>{canvas.DrawToBitmap(bitmap,new Rectangle(Point.Empty,bitmap.Size));return true;});
                bitmap.Save(Path.Combine(root,"artifacts/performance/viewport-"+Path.GetFileNameWithoutExtension(path)+".png"));
                canvas.Radius=5;
                Measure("sprite_zoom_out",()=>{canvas.DrawToBitmap(bitmap,new Rectangle(Point.Empty,bitmap.Size));return true;});
                canvas.Radius=100;
                Measure("sprite_zoom_in",()=>{canvas.DrawToBitmap(bitmap,new Rectangle(Point.Empty,bitmap.Size));return true;});
            }
            if(args.Contains("--verify"))
            {
                VerifyRendering();
                VerifySync(doc);
                var open=(Task)Call(form,"OpenPath",path);
                bool yielded=!open.IsCompleted;
                while(!open.IsCompleted){Application.DoEvents();Thread.Sleep(1);}
                open.GetAwaiter().GetResult();
                if(!yielded || !Field<MenuStrip>(form,"menuStrip").Enabled || canvas.Cells.Count!=cells.Count)
                    throw new Exception("background opening failed");
                Console.WriteLine("Background open PASS: returned to message loop, loaded cells and re-enabled UI.");
                VerifyHistory(form);
            }
            string label=args.FirstOrDefault(a=>a.StartsWith("--label="))?[8..]??"current";
            string output=Path.Combine(root,"artifacts/performance"); Directory.CreateDirectory(output);
            File.WriteAllText(Path.Combine(output,label+"-"+Path.GetFileNameWithoutExtension(path)+".json"),JsonSerializer.Serialize(Results,new JsonSerializerOptions{WriteIndented=true}));
            return 0;
        }
        catch(Exception error){Console.Error.WriteLine(error);return 1;}
    }
    static void VerifyRendering()
    {
        var doc=FrontNav.NewMap(12,10,0,0,12,10,0,1);
        var cells=FrontNav.RebuildCells(doc);
        var ids=GameSettings.MapTerrains.Values.Where(t=>t.Images.Any(n=>!string.IsNullOrEmpty(n))).Select(t=>t.TerrainId).ToArray();
        if(ids.Length==0) throw new Exception("Terrain definitions unavailable");
        foreach(var cell in cells)
        {
            cell.Terrain=(ushort)((cell.Index%5)|((cell.Index%12)<<3)|(cell.Index%3==0?512:0));
            if(cell.Index%3!=0)
            {
                cell.Terrain|=2048;
                cell.AttrA2=FrontNav.NewAttr((byte)ids[cell.Index%ids.Length],(byte)(cell.Index%3),(sbyte)(cell.Index%70-35),(sbyte)(cell.Index%60-30));
            }
        }
        cells[30].TriggerBldg=FrontNav.NewBuildingEvent(30,101,1,0,1,127,-128,0);
        cells[60].TriggerFort=FrontNav.NewFortEvent(60,3,0);
        FrontNav.SyncGrid(doc,cells);
        var decoded=FrontTerrain.FromCells(doc,cells);
        if(!TerrainSpriteRenderer.TryCreate(out var renderer,out var error)) throw new Exception(error);
        using(renderer)
        {
            var reference=PixelBuffer.FromBitmap(renderer.Render(decoded,true,true));
            foreach(var region in new[]{new Rectangle(0,0,713,517),new Rectangle(579,653,1013,809),new Rectangle(reference.Width-607,reference.Height-419,607,419)})
                foreach(int step in new[]{1,3,17})
                {
                    var part=PixelBuffer.FromBitmap(renderer.RenderRegion(decoded,true,true,region,step));
                    for(int y=0;y<part.Height;y++) for(int x=0;x<part.Width;x++)
                    {
                        int gx=region.X+x*step,gy=region.Y+y*step;
                        if(part[x,y]!=reference[gx,gy]) throw new Exception($"Viewport mismatch at {gx},{gy}, step {step}");
                    }
                }
            decoded.Cells[40].Sea=!decoded.Cells[40].Sea;
            decoded.Cells[40].T=4;
            var dirty=PixelBuffer.FromBitmap(renderer.Render(decoded,true,true));
            decoded.Cells[40].T=2;
            renderer.RenderDirty(decoded,true,true,new[]{40});
            dirty=PixelBuffer.FromBitmap(renderer.MapBitmap);
            var full=PixelBuffer.FromBitmap(renderer.Render(decoded,true,true));
            if(!dirty.Argb.SequenceEqual(full.Argb)) throw new Exception("dirty rendering differs from full rendering");
        }
        Console.WriteLine("Viewport pixels PASS: full image matches regions and zoom samples, with coasts, climates, offsets and entities.");
    }
    static void VerifySync(BtlFrontDocument doc)
    {
        var cells=FrontNav.RebuildCells(doc);
        var units=FrontNav.Agents(doc); var attrs=FrontNav.Attrs(doc);
        if(units.V.Count>0) units.V.Insert(0,BtlFrontJson.Clone((BtlNode)units.V[0]));
        var extra=FrontNav.NewAttr(99,1,0,0); attrs.V.Add(extra);
        cells=FrontNav.RebuildCells(doc);
        var before=BtlFrontJson.CloneDocument(doc);
        FrontNav.SyncGrid(doc,cells);
        if(!BtlFrontJson.ContentEquals(before,doc)) throw new Exception("unchanged grid lost duplicates or trailing attributes");
        cells[0].Terrain^=512;
        FrontNav.SyncGrid(doc,cells);
        if((ushort)FrontNav.ToI64(FrontNav.Tiles(doc).V[0])!=cells[0].Terrain) throw new Exception("canvas edit not synchronized");
        var binary=FrontToBtl.Build(doc);
        if(!binary.Ok || !BtlToFront.FromBytes(binary.Bytes).Ok) throw new Exception("optimized document did not roundtrip");
        Console.WriteLine("Grid sync PASS: no-op preserves unprojected data, changed grid and BTL roundtrip work.");
    }
    static void VerifyHistory(MainEditorForm form)
    {
        var host=Field<ScriptHost>(form,"_stageHost");
        var doc=host.Document;
        var info=FrontNav.ChildStruct((BtlTable)FrontNav.Agents(doc).V[0],0);
        info.V[6]=(ushort)0; Call(form,"OnDocumentLoaded"); Call(form,"InitHistory");
        var before=BtlFrontJson.CloneDocument(doc);
        Call(form,"ExecuteTool","heal_all",new ScriptArgs());
        if(FrontNav.MemberU16(info,6)!=FrontNav.MemberU16(info,7)) throw new Exception("tool UI commit failed");
        Call(form,"PerformUndoAction");
        if(!BtlFrontJson.ContentEquals(before,host.Document)) throw new Exception("large document undo failed");
        Call(form,"PerformRedoAction");
        if(BtlFrontJson.ContentEquals(before,host.Document)) throw new Exception("large document redo failed");
        for(int i=0;i<40;i++)
        {
            host.Document.Root.F[99]=BtlFrontJson.Scalar("u16",(ushort)i);
            Call(form,"AddHistoryState");
        }
        var history=Field<List<BtlFrontDocument>>(form,"_history");
        if(Field<long>(form,"_historyBytes")>128L*1024*1024 || history.Count<2 || history.Count>100)
            throw new Exception("history memory budget failed");
        Console.WriteLine($"Large history PASS: tool commit, undo/redo, memory budget ({history.Count} retained frames).");
    }
    static T Measure<T>(string name,Func<T> action)
    {
        GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();
        long allocated=GC.GetAllocatedBytesForCurrentThread();
        var watch=Stopwatch.StartNew(); var result=action();watch.Stop();
        double ms=watch.Elapsed.TotalMilliseconds;
        long bytes=GC.GetAllocatedBytesForCurrentThread()-allocated;
        Results[name]=new{ms,allocated_bytes=bytes};
        Console.WriteLine($"{name}: {ms:F1} ms, {bytes/1048576.0:F2} MiB allocated");
        return result;
    }
    static object Call(object target,string method,params object[] args)=>target.GetType().GetMethod(method,BindingFlags.Instance|BindingFlags.NonPublic).Invoke(target,args);
    static T Field<T>(object target,string name)=>(T)target.GetType().GetField(name,BindingFlags.Instance|BindingFlags.NonPublic).GetValue(target);
    static void Set(object target,string name,object value)=>target.GetType().GetField(name,BindingFlags.Instance|BindingFlags.NonPublic).SetValue(target,value);
    static string Root(){for(var dir=new DirectoryInfo(AppContext.BaseDirectory);dir!=null;dir=dir.Parent)if(File.Exists(Path.Combine(dir.FullName,"BtldMapEditor.sln")))return dir.FullName;throw new Exception("Repository root missing");}
}
