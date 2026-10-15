using System.Drawing.Drawing2D;

namespace BtldMapEditor;

/// <summary>轻量分组标题；保留按钮的键盘、焦点和可访问性行为。</summary>
sealed class DisclosureHeader : Button
{
    bool hovered;
    bool expanded;
    public bool Expanded {get=>expanded;set {expanded=value;Invalidate();}}
    float DpiFactor => DeviceDpi/96f;
    public DisclosureHeader()
    {
        SetStyle(ControlStyles.UserPaint|ControlStyles.AllPaintingInWmPaint|ControlStyles.OptimizedDoubleBuffer,true);
        FlatStyle=FlatStyle.Flat;FlatAppearance.BorderSize=0;UseVisualStyleBackColor=false;
        Cursor=Cursors.Hand;
    }
    public override Size GetPreferredSize(Size proposedSize)
    {
        var text=TextRenderer.MeasureText(Text,Font,Size.Empty,TextFormatFlags.SingleLine|TextFormatFlags.NoPrefix);
        return new Size(text.Width+(int)Math.Ceiling(36*DpiFactor),Math.Max((int)Math.Ceiling(32*DpiFactor),text.Height+(int)Math.Ceiling(12*DpiFactor)));
    }
    protected override void OnMouseEnter(EventArgs e) {hovered=true;Invalidate();base.OnMouseEnter(e);}
    protected override void OnMouseLeave(EventArgs e) {hovered=false;Invalidate();base.OnMouseLeave(e);}
    protected override void OnFontChanged(EventArgs e) {base.OnFontChanged(e);Parent?.PerformLayout();Invalidate();}
    protected override void OnDpiChangedAfterParent(EventArgs e) {base.OnDpiChangedAfterParent(e);Parent?.PerformLayout();Invalidate();}
    protected override void OnPaint(PaintEventArgs e)
    {
        var g=e.Graphics;
        using(var background=new SolidBrush(hovered ? SystemColors.ControlLight : SystemColors.Control))
            g.FillRectangle(background,ClientRectangle);
        using(var line=new Pen(SystemColors.ControlLight))g.DrawLine(line,0,Height-1,Width,Height-1);
        float x=10*DpiFactor,y=Height/2f,r=3.5f*DpiFactor;
        using(var arrow=new Pen(Enabled ? SystemColors.ControlDarkDark : SystemColors.GrayText,Math.Max(1.4f,1.4f*DpiFactor)))
        {
            g.SmoothingMode=SmoothingMode.AntiAlias;
            g.DrawLines(arrow,Expanded ? new[]{new PointF(x-r,y-r/2),new PointF(x,y+r/2),new PointF(x+r,y-r/2)}
                : new[]{new PointF(x-r/2,y-r),new PointF(x+r/2,y),new PointF(x-r/2,y+r)});
            g.SmoothingMode=SmoothingMode.None;
        }
        int left=(int)Math.Ceiling(25*DpiFactor);
        TextRenderer.DrawText(g,Text,Font,new Rectangle(left,0,Math.Max(0,Width-left-4),Height-1),
            Enabled ? ForeColor : SystemColors.GrayText,
            TextFormatFlags.Left|TextFormatFlags.VerticalCenter|TextFormatFlags.SingleLine|TextFormatFlags.EndEllipsis|TextFormatFlags.NoPrefix);
        if(Focused && ShowFocusCues)ControlPaint.DrawFocusRectangle(g,new Rectangle(2,2,Math.Max(0,Width-5),Math.Max(0,Height-5)));
    }
}
