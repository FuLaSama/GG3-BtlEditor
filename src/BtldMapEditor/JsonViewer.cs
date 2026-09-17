/*
 * JsonViewer.cs  （BtldMapEditor）
 *
 * 带行号的只读 JSON 文本框。关卡真相是 BtlFront，不是这里的字符串。
 * 左侧若还留着 JSON Tab，内容应是调试导出或 Annotate 投影。
 * 自绘行号：WM_PAINT 之后在左边条上画数字。
 */
using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace BtldMapEditor
{
    public class JsonViewer : RichTextBox
    {
        private const int WM_PAINT = 15;

        public JsonViewer()
        {
            this.DoubleBuffered = true;
        }

        protected override void WndProc(ref Message m)
        {
            base.WndProc(ref m);

            if (m.Msg == WM_PAINT)
            {
                using (Graphics g = Graphics.FromHwnd(this.Handle))
                {
                    DrawBraceMatching(g);
                }
            }
        }

        protected override void OnSelectionChanged(EventArgs e)
        {
            base.OnSelectionChanged(e);
            this.Invalidate();
        }

        protected override void OnVScroll(EventArgs e)
        {
            base.OnVScroll(e);
            this.Invalidate();
        }

        protected override void OnHScroll(EventArgs e)
        {
            base.OnHScroll(e);
            this.Invalidate();
        }

        private void DrawBraceMatching(Graphics g)
        {
            int caret = this.SelectionStart;
            string text = this.Text;
            if (string.IsNullOrEmpty(text)) return;

            int braceIdx = -1;
            char braceChar = '\0';

            // Check current or preceding char for braces or brackets
            if (caret < text.Length && (text[caret] == '{' || text[caret] == '}' || text[caret] == '[' || text[caret] == ']'))
            {
                braceIdx = caret;
                braceChar = text[caret];
            }
            else if (caret > 0 && (text[caret - 1] == '{' || text[caret - 1] == '}' || text[caret - 1] == '[' || text[caret - 1] == ']'))
            {
                braceIdx = caret - 1;
                braceChar = text[caret - 1];
            }

            if (braceIdx == -1) return;

            int matchIdx = -1;
            if (braceChar == '{' || braceChar == '}')
            {
                matchIdx = FindMatchingBrace(text, braceIdx, '{', '}');
            }
            else if (braceChar == '[' || braceChar == ']')
            {
                matchIdx = FindMatchingBrace(text, braceIdx, '[', ']');
            }

            if (matchIdx == -1) return;

            // Get character screen coordinates
            Point p1 = GetPositionFromCharIndex(braceIdx);
            Point p2 = GetPositionFromCharIndex(matchIdx);

            int charHeight = this.Font.Height;
            int charWidth = 8; // default fallback for Consolas 10f

            // Measure exact character width in pixel space
            try
            {
                Size sz = TextRenderer.MeasureText(g, braceChar.ToString(), this.Font, new Size(100, 100), TextFormatFlags.NoPadding);
                if (sz.Width > 0) charWidth = sz.Width;
            }
            catch { }

            // Define bounding rectangles for the two matching elements
            Rectangle r1 = new Rectangle(p1.X, p1.Y, charWidth, charHeight);
            Rectangle r2 = new Rectangle(p2.X, p2.Y, charWidth, charHeight);

            // 1. Draw matching brace borders and light backgrounds (indigo color theme matching current map editor UI)
            using (Brush bgBrush = new SolidBrush(Color.FromArgb(40, 99, 102, 241))) // Light indigo fill
            using (Pen borderPen = new Pen(Color.FromArgb(180, 99, 102, 241), 1.2f))  // Solid indigo border
            {
                g.FillRectangle(bgBrush, r1);
                g.DrawRectangle(borderPen, r1);

                g.FillRectangle(bgBrush, r2);
                g.DrawRectangle(borderPen, r2);
            }

            // 2. Draw vertical indentation guide line
            int openIdx = Math.Min(braceIdx, matchIdx);
            int closeIdx = Math.Max(braceIdx, matchIdx);

            Point pOpen = GetPositionFromCharIndex(openIdx);
            Point pClose = GetPositionFromCharIndex(closeIdx);

            // Draw line from bottom of opening brace to top of closing brace
            // Centered horizontally within character width
            float lineX = pOpen.X + charWidth / 2f - 0.5f;
            float startY = pOpen.Y + charHeight;
            float endY = pClose.Y;

            if (startY < endY)
            {
                using (Pen linePen = new Pen(Color.FromArgb(120, 99, 102, 241), 1f) { DashStyle = DashStyle.Dot })
                {
                    g.DrawLine(linePen, lineX, startY, lineX, endY);
                }
            }
        }

        private int FindMatchingBrace(string text, int index, char openCh, char closeCh)
        {
            int depth = 0;
            if (text[index] == openCh)
            {
                for (int i = index; i < text.Length; i++)
                {
                    if (text[i] == openCh) depth++;
                    else if (text[i] == closeCh)
                    {
                        depth--;
                        if (depth == 0) return i;
                    }
                }
            }
            else if (text[index] == closeCh)
            {
                for (int i = index; i >= 0; i--)
                {
                    if (text[i] == closeCh) depth++;
                    else if (text[i] == openCh)
                    {
                        depth--;
                        if (depth == 0) return i;
                    }
                }
            }
            return -1;
        }
    }
}
