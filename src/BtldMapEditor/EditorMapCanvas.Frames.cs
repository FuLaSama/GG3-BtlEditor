using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;

namespace BtldMapEditor;

public partial class EditorMapCanvas
{
    Bitmap _sceneFrame,_sceneSpare;
    int[] _sceneCopyRow;
    readonly Dictionary<(float,FontStyle),Font> _paintFonts=new();
    Font PaintFont(float size,FontStyle style)
    {
        if(!_paintFonts.TryGetValue((size,style),out var font))_paintFonts[(size,style)]=font=new Font("Segoe UI",size,style);
        return font;
    }
    Point _sceneScroll;
    int _sceneContentVersion;
    (int Version,int Radius,MapRenderMode Mode,bool Ids,bool Coordinates,bool Offsets,int Selected,int Preview) _sceneKey;

    protected override void OnPaint(PaintEventArgs e)
    {
        if(!HasMap || _cells.Count==0)
        {
            e.Graphics.Clear(BackColor);DrawWelcomeText(e.Graphics);
        }
        else if(VisualStyle==MapVisualStyle.Sketch && (long)ClientSize.Width*ClientSize.Height<=6_000_000
            && ClientSize.Width>0 && ClientSize.Height>0)
        {
            DrawSketchFrame(e.Graphics);
        }
        else
        {
            e.Graphics.Clear(BackColor);var state=e.Graphics.Save();
            try {PaintMap(e.Graphics,e.ClipRectangle);}finally {e.Graphics.Restore(state);}
        }
        base.OnPaint(e);
    }
    void DrawSketchFrame(Graphics target)
    {
        var key=(_sceneContentVersion,Radius,RenderMode,ShowCellIds,ShowCellCoordinates,ShowCellOffsets,SelectedCellIndex,DropPreviewIndex);
        if(_sceneFrame!=null && (_sceneFrame.Size!=ClientSize || Math.Abs(_sceneFrame.HorizontalResolution-target.DpiX)>.1f))DropSceneFrames();
        bool full=_sceneFrame==null || _sceneKey!=key;
        Point scroll=AutoScrollPosition;
        if(full)
        {
            _sceneFrame??=NewSceneFrame(target);
            using var graphics=Graphics.FromImage(_sceneFrame);
            PaintScenePart(graphics,ClientRectangle);
        }
        else if(scroll!=_sceneScroll)
        {
            int dx=scroll.X-_sceneScroll.X,dy=scroll.Y-_sceneScroll.Y;
            var overlap=Rectangle.Intersect(ClientRectangle,new Rectangle(dx,dy,ClientSize.Width,ClientSize.Height));
            _sceneSpare??=NewSceneFrame(target);
            if(overlap.Width>0 && overlap.Height>0)CopySceneOverlap(overlap,dx,dy);
            using(var graphics=Graphics.FromImage(_sceneSpare))
            {
                if(overlap.Width<=0 || overlap.Height<=0)PaintScenePart(graphics,ClientRectangle);
                else
                {
                    PaintScenePart(graphics,new Rectangle(0,0,ClientSize.Width,overlap.Top));
                    PaintScenePart(graphics,new Rectangle(0,overlap.Bottom,ClientSize.Width,ClientSize.Height-overlap.Bottom));
                    PaintScenePart(graphics,new Rectangle(0,overlap.Top,overlap.Left,overlap.Height));
                    PaintScenePart(graphics,new Rectangle(overlap.Right,overlap.Top,ClientSize.Width-overlap.Right,overlap.Height));
                }
            }
            (_sceneFrame,_sceneSpare)=(_sceneSpare,_sceneFrame);
        }
        _sceneKey=key;_sceneScroll=scroll;
        target.DrawImageUnscaled(_sceneFrame,0,0);
    }
    Bitmap NewSceneFrame(Graphics target)
    {
        var image=new Bitmap(ClientSize.Width,ClientSize.Height,PixelFormat.Format32bppPArgb);
        image.SetResolution(target.DpiX,target.DpiY);return image;
    }
    void CopySceneOverlap(Rectangle destination,int dx,int dy)
    {
        var source=new Rectangle(destination.X-dx,destination.Y-dy,destination.Width,destination.Height);
        if(_sceneCopyRow==null || _sceneCopyRow.Length<source.Width)_sceneCopyRow=new int[source.Width];
        var from=_sceneFrame.LockBits(source,ImageLockMode.ReadOnly,PixelFormat.Format32bppPArgb);
        try
        {
            var to=_sceneSpare.LockBits(destination,ImageLockMode.WriteOnly,PixelFormat.Format32bppPArgb);
            try
            {
                for(int row=0;row<source.Height;row++)
                {
                    Marshal.Copy(IntPtr.Add(from.Scan0,row*from.Stride),_sceneCopyRow,0,source.Width);
                    Marshal.Copy(_sceneCopyRow,0,IntPtr.Add(to.Scan0,row*to.Stride),source.Width);
                }
            }
            finally {_sceneSpare.UnlockBits(to);}
        }
        finally {_sceneFrame.UnlockBits(from);}
    }
    void PaintScenePart(Graphics graphics,Rectangle clip)
    {
        if(clip.Width<=0 || clip.Height<=0)return;
        var state=graphics.Save();
        try
        {
            graphics.SetClip(clip);
            using(var background=new SolidBrush(BackColor))graphics.FillRectangle(background,clip);
            PaintMap(graphics,clip);
        }
        finally {graphics.Restore(state);}
    }
    void DropSceneFrames()
    {
        _sceneFrame?.Dispose();_sceneFrame=null;_sceneSpare?.Dispose();_sceneSpare=null;_sceneCopyRow=null;
    }
    protected override void OnBackColorChanged(EventArgs e) {base.OnBackColorChanged(e);DropSceneFrames();}
}
