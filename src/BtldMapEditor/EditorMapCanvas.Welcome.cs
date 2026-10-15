using System.Drawing.Drawing2D;

namespace BtldMapEditor;

public partial class EditorMapCanvas
{
    const string RepositoryUrl="https://github.com/FuLaSama/GG3-BtlEditor";
    Bitmap _githubMark;
    float WelcomeFooterTop=>ClientSize.Height-72*(DeviceDpi/96f);

    void DrawRepositoryLink(Graphics graphics)
    {
        if(_githubMark==null)
        {
            using var stream=typeof(EditorMapCanvas).Assembly.GetManifestResourceStream("BtldMapEditor.Assets.GitHub.png");
            using var image=Image.FromStream(stream);
            _githubMark=new Bitmap(image);
        }
        float scale=DeviceDpi/96f,iconSize=32*scale,gap=10*scale;
        float x=45,y=Math.Max(0,ClientSize.Height-28*scale-iconSize);
        var font=PaintFont(11,FontStyle.Regular);
        SizeF text=graphics.MeasureString("GitHub",font);
        var bounds=new RectangleF(x-6*scale,y-6*scale,iconSize+gap+text.Width+12*scale,iconSize+12*scale);
        var state=graphics.Save();
        try
        {
            using var background=new SolidBrush(BackColor);
            using var ink=new SolidBrush(Color.FromArgb(30,41,59));
            graphics.FillRectangle(background,0,Math.Max(0,WelcomeFooterTop),ClientSize.Width,ClientSize.Height);
            graphics.InterpolationMode=InterpolationMode.HighQualityBicubic;
            graphics.DrawImage(_githubMark,new RectangleF(x,y,iconSize,iconSize));
            graphics.DrawString("GitHub",font,ink,x+iconSize+gap,y+(iconSize-text.Height)/2);
        }
        finally {graphics.Restore(state);}
        _welcomeLinkRegions.Add(new LinkRegion{Bounds=bounds,Url=RepositoryUrl});
    }
}
