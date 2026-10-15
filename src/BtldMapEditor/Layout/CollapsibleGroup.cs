namespace BtldMapEditor;

/// <summary>纯显示状态；按需创建内容，折叠不修改文档。</summary>
public sealed class CollapsibleGroup : UserControl
{
    readonly DisclosureHeader header;
    readonly Panel body;
    readonly Func<Control> create;
    string title;
    Control content;
    bool arranging;
    public string Title => title;
    public void SetTitle(string value)
    {
        if(title==value)return;
        title=value;header.Text=value;header.AccessibleName=value;
    }
    public bool Collapsed {get;private set;}
    public event Action<bool> StateChanged;
    public CollapsibleGroup(string title,bool collapsed,Func<Control> create)
    {
        this.title=title; this.create=create;
        DoubleBuffered=true;Dock=DockStyle.Top; Margin=new Padding(0,0,0,6);
        header=new DisclosureHeader {Text=title,AccessibleName=title};
        body=new Panel();
        Controls.Add(body);Controls.Add(header);
        header.Click+=(_,_)=>SetCollapsed(!Collapsed);
        SetCollapsed(collapsed,false);
    }
    public override Size GetPreferredSize(Size proposedSize) => new(proposedSize.Width>0 ? proposedSize.Width : Width,Height);
    protected override void OnLayout(LayoutEventArgs e)
    {
        if(arranging || header==null || body==null)return;
        arranging=true;
        try {
            int heading=header.GetPreferredSize(new Size(ClientSize.Width,0)).Height;
            header.SetBounds(0,0,ClientSize.Width,heading);
            int wanted=0;
            if(content!=null && !Collapsed) {
                int indent=(int)Math.Round(8*DeviceDpi/96f);
                body.SetBounds(indent,heading,Math.Max(1,ClientSize.Width-indent),body.Height);
                body.PerformLayout();content.PerformLayout();
                wanted=content.Height;
                body.Height=wanted;
            }
            if(Height!=heading+wanted)Height=heading+wanted;
            base.OnLayout(e);
        } finally {arranging=false;}
    }
    protected override void OnFontChanged(EventArgs e) {base.OnFontChanged(e);PerformLayout();}
    protected override void OnDpiChangedAfterParent(EventArgs e) {base.OnDpiChangedAfterParent(e);PerformLayout();}
    public void SetCollapsed(bool collapsed,bool notify=true)
    {
        Collapsed=collapsed;
        if(!collapsed && content==null) {
            content=create();
            // Top 停靠约束宽度；AutoSize 的 GroupBox/TableLayoutPanel 只计算高度，不能自行缩成零宽。
            content.Dock=DockStyle.Top;body.Controls.Add(content);
            content.SizeChanged+=(_,_)=>PerformLayout();
        }
        body.Visible=!collapsed;
        header.Expanded=!collapsed;
        header.AccessibleDescription=collapsed ? "已收起，按下展开" : "已展开，按下收起";
        PerformLayout();
        if(notify)StateChanged?.Invoke(collapsed);
    }
}
