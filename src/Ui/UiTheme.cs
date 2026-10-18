using System;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using HotkeyHound.Core;
using Microsoft.Win32;

namespace HotkeyHound.Ui
{
    public enum ThemeMode
    {
        System = 0,
        Light = 1,
        Dark = 2
    }

    /// <summary>
    /// 全程序的视觉中心：调色板、字体、间距都只在这里定义，页面不许再写死颜色。
    /// 深色/浅色在启动时决定（设置 &gt; 跟随系统），切换后要求重启——WinForms 的
    /// 原生控件（TabControl、ListView 表头）没有可靠的运行时换肤接口。
    /// </summary>
    public static class UiTheme
    {
        public const int SpaceXs = 4;
        public const int SpaceS = 8;
        public const int SpaceM = 12;
        public const int SpaceL = 18;
        public const int SpaceXl = 26;

        public static readonly Padding PagePadding = new Padding(SpaceL, SpaceM, SpaceL, SpaceM);
        public static readonly Padding GroupPadding = new Padding(SpaceM);

        public static ThemeMode Mode { get; private set; }
        public static bool IsDark { get; private set; }

        public static Color Background { get; private set; }
        public static Color Surface { get; private set; }
        public static Color SurfaceAlt { get; private set; }
        public static Color Text { get; private set; }
        public static Color Muted { get; private set; }
        public static Color Accent { get; private set; }
        public static Color OnAccent { get; private set; }
        public static Color Success { get; private set; }
        public static Color Warning { get; private set; }
        public static Color Error { get; private set; }
        public static Color Info { get; private set; }
        public static Color Border { get; private set; }
        public static Color BannerBack { get; private set; }
        public static Color BannerText { get; private set; }
        public static Color ButtonFace { get; private set; }
        public static Color ButtonHover { get; private set; }
        public static Color ButtonActive { get; private set; }

        private static Font _base;
        private static bool _initialized;

        public static void Initialize()
        {
            Initialize(null);
        }

        /// <summary>overrideMode 来自 --theme（只影响本次运行，不写入设置）；为空时读 settings.ini。</summary>
        public static void Initialize(string overrideMode)
        {
            string configured = string.IsNullOrEmpty(overrideMode) ? L10n.ReadSetting("theme") : overrideMode;
            Mode = ParseMode(configured);
            IsDark = Mode == ThemeMode.Dark || (Mode == ThemeMode.System && SystemPrefersDark());
            ApplyPalette();
            _initialized = true;
        }

        public static void EnsureInitialized()
        {
            if (!_initialized) Initialize(null);
        }

        public static string ModeKey(ThemeMode mode)
        {
            switch (mode)
            {
                case ThemeMode.Light: return "light";
                case ThemeMode.Dark: return "dark";
                default: return "system";
            }
        }

        public static string ModeName(ThemeMode mode)
        {
            switch (mode)
            {
                case ThemeMode.Light: return L10n.T("ui.rt.themeLight");
                case ThemeMode.Dark: return L10n.T("ui.rt.themeDark");
                default: return L10n.T("ui.rt.themeSystem");
            }
        }

        public static ThemeMode ParseMode(string value)
        {
            if (string.IsNullOrEmpty(value)) return ThemeMode.System;
            switch (value.Trim().ToLowerInvariant())
            {
                case "light": case "浅色": return ThemeMode.Light;
                case "dark": case "深色": return ThemeMode.Dark;
                default: return ThemeMode.System;
            }
        }

        /// <summary>持久化主题；不立即换肤，调用方提示用户重启。</summary>
        public static bool SetMode(ThemeMode mode)
        {
            Mode = mode;
            return L10n.WriteSetting("theme", ModeKey(mode));
        }

        private static bool SystemPrefersDark()
        {
            try
            {
                object value = Registry.GetValue(
                    @"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize",
                    "AppsUseLightTheme", 1);
                if (value is int) return (int)value == 0;
                int parsed;
                if (value != null && int.TryParse(Convert.ToString(value), out parsed)) return parsed == 0;
            }
            catch (Exception) { }
            return false;
        }

        private static void ApplyPalette()
        {
            if (IsDark)
            {
                Background = Color.FromArgb(23, 25, 29);
                Surface = Color.FromArgb(31, 35, 42);
                SurfaceAlt = Color.FromArgb(38, 43, 52);
                Text = Color.FromArgb(230, 233, 239);
                Muted = Color.FromArgb(154, 163, 176);
                Accent = Color.FromArgb(91, 155, 255);
                OnAccent = Color.FromArgb(12, 20, 34);
                Success = Color.FromArgb(123, 216, 143);
                Warning = Color.FromArgb(240, 180, 90);
                Error = Color.FromArgb(255, 123, 114);
                Info = Color.FromArgb(138, 180, 255);
                Border = Color.FromArgb(54, 60, 71);
                BannerBack = Color.FromArgb(58, 42, 18);
                BannerText = Color.FromArgb(245, 199, 126);
                ButtonFace = Color.FromArgb(43, 48, 58);
                ButtonHover = Color.FromArgb(53, 59, 71);
                ButtonActive = Color.FromArgb(64, 71, 85);
            }
            else
            {
                Background = Color.FromArgb(245, 246, 248);
                Surface = Color.White;
                SurfaceAlt = Color.FromArgb(238, 241, 246);
                Text = Color.FromArgb(27, 29, 33);
                Muted = Color.FromArgb(90, 96, 112);
                Accent = Color.FromArgb(47, 111, 235);
                OnAccent = Color.White;
                Success = Color.FromArgb(20, 110, 40);
                Warning = Color.FromArgb(154, 98, 10);
                Error = Color.FromArgb(158, 40, 32);
                Info = Color.FromArgb(60, 80, 160);
                Border = Color.FromArgb(216, 220, 228);
                BannerBack = Color.FromArgb(255, 246, 229);
                BannerText = Color.FromArgb(140, 55, 20);
                ButtonFace = Color.FromArgb(250, 251, 252);
                ButtonHover = Color.FromArgb(238, 242, 249);
                ButtonActive = Color.FromArgb(226, 233, 244);
            }
        }

        // ------------------------------------------------------------------ 字体

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

        /// <summary>标题字体；调用方负责最终 Dispose 由 WinForms 控件接管。</summary>
        public static Font TitleFont(float size)
        {
            try { return new Font(BaseFont().FontFamily, size, FontStyle.Bold); }
            catch (Exception) { return BaseFont(); }
        }

        public static Font MonoFont(float size)
        {
            try { return new Font("Consolas", size, FontStyle.Regular); }
            catch (Exception) { return BaseFont(); }
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

        /// <summary>主按钮：用强调色，避免页面上所有按钮都长得一样。</summary>
        public static void MarkPrimary(Button button)
        {
            if (button == null) return;
            button.Tag = "primary";
        }

        // ------------------------------------------------------------------ 应用配色

        /// <summary>递归套用调色板。页面里显式设置的语义色（成功/警告/错误）会被保留。</summary>
        public static void ApplyTree(Control root)
        {
            if (root == null) return;
            ApplyOne(root);
            foreach (Control child in root.Controls) ApplyTree(child);
        }

        private static void ApplyOne(Control c)
        {
            try
            {
                if (c is Form || c is UserControl || c is TabPage || c is SplitContainer || c is Panel)
                {
                    c.BackColor = c is GroupBox ? Surface : Background;
                    c.ForeColor = Text;
                }
                else if (c is GroupBox)
                {
                    c.BackColor = Surface;
                    c.ForeColor = Text;
                }
                else if (c is TabControl)
                {
                    c.BackColor = Background;
                    c.ForeColor = Text;
                }
                else if (c is ListView || c is ListBox || c is TextBox || c is ComboBox)
                {
                    c.BackColor = Surface;
                    c.ForeColor = Text;
                    if (c is ComboBox) ((ComboBox)c).FlatStyle = FlatStyle.Flat;
                    if (c is TextBox) ((TextBox)c).BorderStyle = BorderStyle.FixedSingle;
                    TryDarkNative(c);
                }
                else if (c is Button)
                {
                    var b = (Button)c;
                    bool primary = (b.Tag as string) == "primary";
                    b.FlatStyle = FlatStyle.Flat;
                    b.UseVisualStyleBackColor = false;
                    b.BackColor = primary ? Accent : ButtonFace;
                    b.ForeColor = primary ? OnAccent : Text;
                    b.FlatAppearance.BorderColor = primary ? Accent : Border;
                    b.FlatAppearance.MouseOverBackColor = primary ? ControlPaint.Light(Accent, 0.15f) : ButtonHover;
                    b.FlatAppearance.MouseDownBackColor = primary ? ControlPaint.Dark(Accent, 0.08f) : ButtonActive;
                }
                else if (c is Label)
                {
                    if (IsDefaultForeColor(c.ForeColor)) c.ForeColor = Text;
                }
                else if (c is StatusStrip)
                {
                    var status = (StatusStrip)c;
                    status.BackColor = SurfaceAlt;
                    status.ForeColor = Text;
                    status.SizingGrip = false;
                    foreach (ToolStripItem item in status.Items) item.ForeColor = Text;
                }
                else if (c is ToolStrip)
                {
                    var strip = (ToolStrip)c;
                    strip.BackColor = SurfaceAlt;
                    strip.ForeColor = Text;
                    strip.RenderMode = ToolStripRenderMode.System;
                    foreach (ToolStripItem item in strip.Items) item.ForeColor = Text;
                }
                else if (c is ProgressBar)
                {
                    // 原生进度条无法可靠换色，保留系统外观；它不占主要视觉面积。
                }

                if (c is Form) TryDarkTitleBar((Form)c);
                if (c is TabControl) TryDarkNative(c);
            }
            catch (Exception) { }
        }

        private static bool IsDefaultForeColor(Color color)
        {
            if (color == SystemColors.ControlText || color == Color.Black) return true;
            if (color.IsSystemColor) return true;
            return false;
        }

        // ------------------------------------------------------------------ 原生深色标题栏/表头

        [DllImport("uxtheme.dll", CharSet = CharSet.Unicode)]
        private static extern int SetWindowTheme(IntPtr hWnd, string subAppName, string subIdList);

        [DllImport("dwmapi.dll")]
        private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);

        private const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;

        private static void TryDarkNative(Control c)
        {
            if (!IsDark || c == null) return;
            if (!c.IsHandleCreated)
            {
                c.HandleCreated += delegate { TryDarkNative(c); };
                return;
            }
            try { SetWindowTheme(c.Handle, "DarkMode_Explorer", null); }
            catch (Exception) { }
        }

        private static void TryDarkTitleBar(Form form)
        {
            if (!IsDark || form == null || !form.IsHandleCreated) return;
            try
            {
                int enabled = 1;
                DwmSetWindowAttribute(form.Handle, DWMWA_USE_IMMERSIVE_DARK_MODE, ref enabled, sizeof(int));
            }
            catch (Exception) { }
        }

        /// <summary>窗口图标来自 exe 的 Win32 图标资源（build.ps1 的 /win32icon）。</summary>
        public static Icon LoadAppIcon()
        {
            try
            {
                Icon icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath);
                if (icon != null) return icon;
            }
            catch (Exception) { }
            try { return SystemIcons.Application; }
            catch (Exception) { return null; }
        }
    }
}
