using System.Runtime.InteropServices;

namespace BtldMapEditor;

public partial class EditorMapCanvas
{
    Point _viewOffset;
    Size _virtualSize;
    bool _updatingScrollbars;
    int _wheelRemainder;

    // 保留原有调用约定：读取负坐标，设置正坐标。偏移只影响绘制，不搬移屏幕像素。
    public Point AutoScrollPosition
    {
        get=>new(-_viewOffset.X,-_viewOffset.Y);
        set=>SetViewOffset(value);
    }
    public Size AutoScrollMinSize
    {
        get=>_virtualSize;
        set {if(_virtualSize==value)return;_virtualSize=value;UpdateViewportScrollbars();Invalidate();}
    }
    public void ZoomAt(Point anchor,int radius)
    {
        int next=Math.Clamp(radius,5,300);if(next==Radius)return;
        double worldX=(anchor.X+_viewOffset.X-10)/(double)Radius;
        double worldY=(anchor.Y+_viewOffset.Y-10)/(double)Radius;
        Radius=next;
        SetViewOffset(new Point((int)Math.Round(worldX*next+10-anchor.X),(int)Math.Round(worldY*next+10-anchor.Y)));
        if(_isPanning) {_panStartClient=anchor;_panStartScroll=_viewOffset;}
    }
    void SetViewOffset(Point value)
    {
        var next=new Point(Math.Clamp(value.X,0,Math.Max(0,_virtualSize.Width-ClientSize.Width)),
            Math.Clamp(value.Y,0,Math.Max(0,_virtualSize.Height-ClientSize.Height)));
        if(next==_viewOffset)return;
        _viewOffset=next;
        if(IsHandleCreated)
        {
            SetBar(0,_virtualSize.Width,ClientSize.Width,next.X,false);
            SetBar(1,_virtualSize.Height,ClientSize.Height,next.Y,false);
        }
        Invalidate();
    }
    protected override CreateParams CreateParams
    {
        get {var value=base.CreateParams;value.Style|=0x00100000|0x00200000;return value;}
    }
    protected override void OnHandleCreated(EventArgs e) {base.OnHandleCreated(e);UpdateViewportScrollbars();}
    protected override void OnResize(EventArgs e) {base.OnResize(e);UpdateViewportScrollbars();}
    protected override void OnPaintBackground(PaintEventArgs e) { } // OnPaint 一次填充完整背景，再提交双缓冲帧。
    void UpdateViewportScrollbars()
    {
        if(_updatingScrollbars)return;
        _updatingScrollbars=true;
        try
        {
            if(IsHandleCreated)
            {
                for(int pass=0;pass<3;pass++)
                {
                    var size=ClientSize;
                    SetBar(0,_virtualSize.Width,size.Width,_viewOffset.X,true);
                    SetBar(1,_virtualSize.Height,ClientSize.Height,_viewOffset.Y,true);
                    if(size==ClientSize)break;
                }
            }
            SetViewOffset(_viewOffset);
        }
        finally {_updatingScrollbars=false;}
    }
    void SetBar(int bar,int extent,int page,int position,bool range)
    {
        var info=new ScrollInfo {Size=Marshal.SizeOf<ScrollInfo>(),Mask=range ? 0x7u : 0x4u,
            Minimum=0,Maximum=Math.Max(0,extent-1),Page=(uint)Math.Max(1,page),Position=position};
        SetScrollInfo(Handle,bar,ref info,range);
        if(!range)RedrawWindow(Handle,IntPtr.Zero,IntPtr.Zero,0x0400|0x0001); // 合并滚动条和客户区的重绘。
    }
    protected override void WndProc(ref Message message)
    {
        if(message.Msg is 0x0114 or 0x0115)
        {
            bool horizontal=message.Msg==0x0114;int command=(int)(message.WParam.ToInt64()&0xffff);
            int old=horizontal ? _viewOffset.X : _viewOffset.Y;
            int page=horizontal ? ClientSize.Width : ClientSize.Height;
            var info=new ScrollInfo{Size=Marshal.SizeOf<ScrollInfo>(),Mask=0x10};
            int position=command switch
            {
                0=>old-Math.Max(8,Radius),1=>old+Math.Max(8,Radius),
                2=>old-page,3=>old+page,6=>0,7=>int.MaxValue,
                4 or 5=>GetScrollInfo(Handle,horizontal ? 0 : 1,ref info) ? info.TrackPosition : old,
                _=>old
            };
            SetViewOffset(horizontal ? new Point(position,_viewOffset.Y) : new Point(_viewOffset.X,position));
            message.Result=IntPtr.Zero;return;
        }
        base.WndProc(ref message);
    }
    [StructLayout(LayoutKind.Sequential)]
    struct ScrollInfo {public int Size;public uint Mask;public int Minimum,Maximum;public uint Page;public int Position,TrackPosition;}
    [DllImport("user32.dll")]static extern int SetScrollInfo(IntPtr window,int bar,ref ScrollInfo info,[MarshalAs(UnmanagedType.Bool)]bool redraw);
    [DllImport("user32.dll")][return:MarshalAs(UnmanagedType.Bool)]static extern bool GetScrollInfo(IntPtr window,int bar,ref ScrollInfo info);
    [DllImport("user32.dll")][return:MarshalAs(UnmanagedType.Bool)]static extern bool RedrawWindow(IntPtr window,IntPtr rect,IntPtr region,uint flags);
}
