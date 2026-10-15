using System.Runtime.InteropServices;

namespace BtldMapEditor;

sealed class BufferedPanel : Panel
{
    public BufferedPanel() {DoubleBuffered=true;}
}
sealed class BufferedTable : TableLayoutPanel
{
    public BufferedTable() {DoubleBuffered=true;}
}
sealed class BufferedListView : ListView
{
    public BufferedListView() {DoubleBuffered=true;}
}

public partial class LayoutPageControl
{
    int _uiUpdateDepth;
    IDisposable BeginUiUpdate() => new UiUpdate(this);
    sealed class UiUpdate : IDisposable
    {
        readonly LayoutPageControl owner;
        readonly bool redraw;
        readonly ScrollableControl scroller;
        readonly Point scroll;
        public UiUpdate(LayoutPageControl owner)
        {
            this.owner=owner;
            if(owner._uiUpdateDepth++!=0)return;
            scroller=owner.Parent as ScrollableControl;
            scroll=scroller?.AutoScrollPosition??Point.Empty;
            redraw=owner.IsHandleCreated && owner.Visible;
            if(redraw)SendMessage(owner.Handle,0x000B,IntPtr.Zero,IntPtr.Zero);
            owner.SuspendLayout();
        }
        public void Dispose()
        {
            if(--owner._uiUpdateDepth!=0)return;
            try
            {
                owner.ResumeLayout(true);
                if(scroller!=null && scroller.AutoScrollPosition!=scroll)
                    scroller.AutoScrollPosition=new Point(-scroll.X,-scroll.Y);
            }
            finally
            {
                if(redraw && !owner.IsDisposed && owner.IsHandleCreated)
                {
                    SendMessage(owner.Handle,0x000B,new IntPtr(1),IntPtr.Zero);
                    RedrawWindow(owner.Handle,IntPtr.Zero,IntPtr.Zero,0x0001|0x0080); // invalidate, all children; one queued repaint
                }
            }
        }
    }
    [DllImport("user32.dll")]
    static extern IntPtr SendMessage(IntPtr window,uint message,IntPtr wparam,IntPtr lparam);
    [DllImport("user32.dll")]
    [return:MarshalAs(UnmanagedType.Bool)]
    static extern bool RedrawWindow(IntPtr window,IntPtr rect,IntPtr region,uint flags);
}
