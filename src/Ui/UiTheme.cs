using System;
using System.Drawing;
using System.Windows.Forms;

namespace HotkeyHound.Ui
{
    /// <summary>
    /// 统一的界面字体解析。用户反馈字体不清晰，根因大概率是缺少 DPI 感知导致系统位图缩放，
    /// 另外也要解决「找不到微软雅黑」时的回退：Microsoft YaHei UI → Microsoft YaHei → 系统默认。
    /// </summary>
    public static class UiTheme
    {
        private static Font _base;

        public static Font BaseFont()
        {
            if (_base != null) return _base;
            string[] families = new string[] { "Microsoft YaHei UI", "Microsoft YaHei", "Segoe UI" };
            foreach (string name in families)
            {
                try
                {
                    var f = new Font(name, 9F, FontStyle.Regular, GraphicsUnit.Point);
                    if (string.Equals(f.Name, name, StringComparison.OrdinalIgnoreCase))
                    {
                        _base = f;
                        return _base;
                    }
                    f.Dispose();
                }
                catch (Exception) { }
            }
            try { _base = SystemFonts.DefaultFont; }
            catch (Exception) { }
            if (_base == null) _base = new Font(FontFamily.GenericSansSerif, 9F);
            return _base;
        }

        /// <summary>给控件（及其未显式设置字体的子控件）套上统一字体。</summary>
        public static void ApplyFont(Control control)
        {
            if (control == null) return;
            try
            {
                control.Font = BaseFont();
                var container = control as ContainerControl;
                if (container != null) container.AutoScaleMode = AutoScaleMode.Font;
            }
            catch (Exception) { }
        }

        /// <summary>把期望尺寸夹到主屏工作区内，避免窗口比屏幕还大。</summary>
        public static Size ClampToWorkingArea(Size desired, int margin)
        {
            try
            {
                Rectangle wa = Screen.PrimaryScreen.WorkingArea;
                int w = Math.Min(desired.Width, Math.Max(480, wa.Width - margin));
                int h = Math.Min(desired.Height, Math.Max(360, wa.Height - margin));
                return new Size(w, h);
            }
            catch (Exception) { return desired; }
        }
    }
}
