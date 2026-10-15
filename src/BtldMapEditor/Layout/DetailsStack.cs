namespace BtldMapEditor;

/// <summary>深层详情按已计算的子项高度排列，避免嵌套自动尺寸表格反复测量整棵树。</summary>
sealed class DetailsStack : Panel
{
    bool arranging;
    readonly HashSet<Control> hidden=new();
    public DetailsStack() { DoubleBuffered=true; Height=4; Dock=DockStyle.Top; Margin=new Padding(0); Padding=new Padding(2); }
    public void SetItemVisible(Control child,bool visible)
    {
        bool changed=visible ? hidden.Remove(child) : hidden.Add(child);
        if(changed) {child.Visible=visible;PerformLayout();}
    }
    public override Size GetPreferredSize(Size proposedSize) => new(proposedSize.Width>0 ? proposedSize.Width : Width,Height);
    protected override void OnLayout(LayoutEventArgs e)
    {
        if(arranging)return;
        arranging=true;
        try {
            int width=Math.Max(1,ClientSize.Width-Padding.Horizontal),y=Padding.Top;
            foreach(Control child in Controls)
            {
                if(hidden.Contains(child))continue;
                int available=Math.Max(1,width-child.Margin.Horizontal);
                if(child.Width!=available)child.Width=available;
                int height=child.AutoSize ? child.GetPreferredSize(new Size(available,0)).Height : child.Height;
                child.SetBounds(Padding.Left+child.Margin.Left,y+child.Margin.Top,available,height);
                y+=height+child.Margin.Vertical;
            }
            if(Height!=y+Padding.Bottom)Height=y+Padding.Bottom;
            base.OnLayout(e);
        } finally { arranging=false; }
    }
    public void Add(Control child)
    {
        child.Dock=DockStyle.None; Controls.Add(child); child.SizeChanged+=(_,_)=>PerformLayout(); PerformLayout();
    }
}
