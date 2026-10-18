using System;
using System.Drawing;
using System.Windows.Forms;

namespace HotkeyHound.Ui
{
    /// <summary>
    /// 深色模式下 WinForms 原生 TabControl 的表头仍是系统浅色（SetWindowTheme 对 Tab 无效），
    /// 因此整条标签栏由自己画；页面内容仍由 TabPage 子窗口负责。
    /// </summary>
    public sealed class ThemedTabControl : TabControl
    {
        public ThemedTabControl()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, true);
            DrawMode = TabDrawMode.OwnerDrawFixed;
            Appearance = TabAppearance.Normal;
            // OwnerDraw 时 GetTabRect 用的是 ItemSize；不固定宽度会把中文标签压成省略号。
            SizeMode = TabSizeMode.Fixed;
            ItemSize = new Size(132, 28);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.Clear(UiTheme.Background);

            int strip = Math.Max(0, DisplayRectangle.Top);
            using (var stripBrush = new SolidBrush(UiTheme.SurfaceAlt))
                g.FillRectangle(stripBrush, 0, 0, Width, strip);

            for (int i = 0; i < TabPages.Count; i++)
            {
                Rectangle r = GetTabRect(i);
                bool selected = i == SelectedIndex;
                using (var fill = new SolidBrush(selected ? UiTheme.Surface : UiTheme.SurfaceAlt))
                    g.FillRectangle(fill, r);
                using (var pen = new Pen(selected ? UiTheme.Accent : UiTheme.Border))
                    g.DrawRectangle(pen, r.X, r.Y, r.Width - 1, r.Height - 1);
                TextRenderer.DrawText(g, TabPages[i].Text, Font, r,
                    selected ? UiTheme.Text : UiTheme.Muted,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
            }

            using (var border = new Pen(UiTheme.Border))
                g.DrawRectangle(border, 0, strip - 1, Width - 1, Height - strip);
        }

        protected override void OnSelectedIndexChanged(EventArgs e)
        {
            base.OnSelectedIndexChanged(e);
            Invalidate();
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            Invalidate();
        }
    }
}
