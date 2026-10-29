using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace HotkeyHound.Ui
{
    /// <summary>
    /// 品牌渐变面板：蓝 → 紫，色值取自程序图标（assets\hotkeyhound.ico 角点采样：
    /// #4C57EE → #6450EF），让「关于」页的头图和图标是同一套颜色。
    ///
    /// UserPaint 面板：子 Label 的透明背景由 WinForms 回调 OnPaintBackground 绘制，
    /// 所以文字天然落在渐变上，不需要额外处理。
    /// </summary>
    public sealed class GradientPanel : Panel
    {
        private static readonly Color FromColor = Color.FromArgb(0x4C, 0x57, 0xEE);
        private static readonly Color ToColor = Color.FromArgb(0x64, 0x50, 0xEF);

        public GradientPanel()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint
                     | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        }

        protected override void OnPaintBackground(PaintEventArgs e)
        {
            Rectangle r = new Rectangle(0, 0, Width, Height);
            if (r.Width <= 1 || r.Height <= 1) return;
            Graphics g = e.Graphics;
            try
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                using (var br = new LinearGradientBrush(r, FromColor, ToColor, 22F))
                {
                    g.FillRectangle(br, r);
                }
                // 两团极淡的高光：大面渐变不至于太平，呼应图标上的亮部。
                using (var glow = new SolidBrush(Color.FromArgb(24, 255, 255, 255)))
                {
                    int d1 = Math.Max(80, Height);
                    g.FillEllipse(glow, (int)(Width * 0.66) - d1 / 2, -d1 / 2, d1, d1);
                    int d2 = Math.Max(48, Height / 2);
                    g.FillEllipse(glow, (int)(Width * 0.16) - d2 / 2, (int)(Height * 0.85) - d2 / 2, d2, d2);
                }
            }
            catch (Exception)
            {
                using (var solid = new SolidBrush(FromColor))
                {
                    g.FillRectangle(solid, r);
                }
            }
        }
    }
}
