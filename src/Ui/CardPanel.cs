using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace HotkeyHound.Ui
{
    /// <summary>
    /// 圆角统计卡片：Surface 底 + Border 细描边，替代 v1.3.1 的 FixedSingle 硬直角框。
    /// 注意：BackColor 参与子控件（透明背景 Label）的绘制模拟，所以 UiTheme.ApplyTree
    /// 对 CardPanel 只同步前景色、绝不覆盖 BackColor。
    /// </summary>
    public sealed class CardPanel : Panel
    {
        public CardPanel()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint
                     | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            BorderStyle = BorderStyle.None;
            UiTheme.EnsureInitialized();
            BackColor = UiTheme.Surface;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(UiTheme.Surface);
            if (Width > 8 && Height > 8)
            {
                using (GraphicsPath path = RoundedRect(new Rectangle(1, 1, Width - 3, Height - 3), 8))
                using (var pen = new Pen(UiTheme.Border))
                {
                    g.DrawPath(pen, path);
                }
            }
            base.OnPaint(e);
        }

        private static GraphicsPath RoundedRect(Rectangle r, int radius)
        {
            var path = new GraphicsPath();
            int d = radius * 2;
            if (d > r.Width) d = r.Width;
            if (d > r.Height) d = r.Height;
            path.AddArc(r.X, r.Y, d, d, 180, 90);
            path.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            path.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            path.CloseFigure();
            return path;
        }
    }
}
