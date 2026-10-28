using System;
using System.Collections.Generic;
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
                Background = Color.FromArgb(20, 22, 27);
                Surface = Color.FromArgb(28, 32, 39);
                SurfaceAlt = Color.FromArgb(37, 42, 51);
                Text = Color.FromArgb(232, 235, 242);
                Muted = Color.FromArgb(154, 164, 180);
                Accent = Color.FromArgb(108, 140, 255);
                OnAccent = Color.FromArgb(14, 19, 32);
                Success = Color.FromArgb(90, 205, 145);
                Warning = Color.FromArgb(242, 184, 92);
                Error = Color.FromArgb(255, 123, 114);
                Info = Color.FromArgb(139, 177, 255);
                Border = Color.FromArgb(56, 63, 76);
                BannerBack = Color.FromArgb(54, 43, 24);
                BannerText = Color.FromArgb(246, 205, 139);
                ButtonFace = Color.FromArgb(36, 41, 50);
                ButtonHover = Color.FromArgb(46, 52, 63);
                ButtonActive = Color.FromArgb(57, 64, 77);
            }
            else
            {
                Background = Color.FromArgb(244, 246, 251);
                Surface = Color.White;
                SurfaceAlt = Color.FromArgb(235, 239, 247);
                Text = Color.FromArgb(26, 31, 43);
                Muted = Color.FromArgb(95, 105, 124);
                Accent = Color.FromArgb(57, 92, 214);
                OnAccent = Color.White;
                Success = Color.FromArgb(21, 112, 63);
                Warning = Color.FromArgb(145, 91, 8);
                Error = Color.FromArgb(174, 47, 39);
                Info = Color.FromArgb(55, 83, 174);
                Border = Color.FromArgb(216, 221, 231);
                BannerBack = Color.FromArgb(255, 246, 229);
                BannerText = Color.FromArgb(132, 60, 18);
                ButtonFace = Color.FromArgb(251, 252, 254);
                ButtonHover = Color.FromArgb(239, 243, 250);
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

        /// <summary>侧栏导航项：用 Tag 记住选中态，配色在 ApplyTree 里统一处理。</summary>
        public static void MarkNavigation(Button button, bool selected)
        {
            if (button == null) return;
            button.Tag = selected ? "nav-selected" : "nav";
        }

        /// <summary>
        /// 按钮按文字宽度自适应：写死宽度是「生成 HTML 报…」「布防（安装钩…」这类截字的根因。
        /// 下限保留原设计宽度（但不超过 180，免得超长按钮把行撑爆），缺字时由 AutoSize 补齐。
        /// </summary>
        public static void AutoSizeButton(Button button)
        {
            if (button == null) return;
            string tag = button.Tag as string;
            if (tag == "nav" || tag == "nav-selected") return;
            try
            {
                int minWidth = Math.Min(button.Width > 0 ? button.Width : 120, 180);
                int minHeight = button.Height > 0 ? button.Height : 34;
                button.MinimumSize = new Size(Math.Max(button.MinimumSize.Width, minWidth),
                                              Math.Max(button.MinimumSize.Height, minHeight));
                button.AutoSize = true;
                button.AutoSizeMode = AutoSizeMode.GrowAndShrink;
                button.Padding = new Padding(Math.Max(10, button.Padding.Left), Math.Max(4, button.Padding.Top),
                                             Math.Max(10, button.Padding.Right), Math.Max(4, button.Padding.Bottom));
            }
            catch (Exception) { }
        }

        // ------------------------------------------------------------------ 布局助手（2.0.0）
        // 目标只有一个：任何窗口宽度、任何语言下，文字都不许被截断。
        // 顶部说明区一律用「垂直堆栈 + AutoSize 行」，换行文本自动撑高；
        // 列表的最后一列吃掉剩余宽度，最大化时不再左边截断、右边留白。

        /// <summary>垂直堆栈容器：Dock=Top、整体自动增高，逐行 AutoSize。</summary>
        public static TableLayoutPanel NewStack()
        {
            var tlp = new TableLayoutPanel();
            tlp.Dock = DockStyle.Top;
            tlp.AutoSize = true;
            tlp.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            tlp.ColumnCount = 1;
            tlp.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            tlp.RowCount = 0;
            tlp.Padding = new Padding(0, 4, 0, 6);
            tlp.Margin = new Padding(0);
            // 透明底：容器的默认底色比页面浅，会在按钮右侧露出一块白框。
            try { tlp.BackColor = Color.Transparent; }
            catch (Exception) { }
            return tlp;
        }

        /// <summary>往堆栈里追加一行（AutoSize），并放入控件。</summary>
        public static void StackAdd(TableLayoutPanel stack, Control child)
        {
            if (stack == null || child == null) return;
            stack.RowCount++;
            stack.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            stack.Controls.Add(child, 0, stack.RowCount - 1);
        }

        /// <summary>会自动换行的说明标签：配合 NewStack/StackAdd，在行内按实际宽度换行、行高自动增长。</summary>
        public static Label StackLabel(string text, Color fore)
        {
            var label = new Label();
            label.Text = text ?? "";
            label.AutoSize = true;
            label.Dock = DockStyle.Fill;
            label.ForeColor = fore;
            label.Margin = new Padding(0, 2, 0, 2);
            return label;
        }

        /// <summary>水平按钮行：自动高度，窗口太窄时自动折行，按钮永远完整可见。</summary>
        public static FlowLayoutPanel NewFlowRow()
        {
            var flow = new FlowLayoutPanel();
            flow.AutoSize = true;
            flow.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            flow.Dock = DockStyle.Fill;
            flow.WrapContents = true;
            flow.FlowDirection = FlowDirection.LeftToRight;
            flow.Margin = new Padding(0);
            flow.Padding = new Padding(0);
            // 透明底：否则按钮行右侧会露出一块比页面更浅的底色。
            try { flow.BackColor = Color.Transparent; }
            catch (Exception) { }
            return flow;
        }

        /// <summary>
        /// 让 ListView 的每一列都按「内容实际宽度」自适应：表头与最长的单元格都完整显示，
        /// 整张表放不下就出现横向滚动条（滚到右边就能把备注看完），放得下就让最后一列
        /// 吃满剩余宽度——右边不会多出一条空白。
        /// minWidth 参数只为兼容旧调用点保留，不再作为下限（下限会把备注压成一条窄缝）。
        /// </summary>
        public static void FillColumn(ListView list, int index, int minWidth)
        {
            if (list == null || index < 0) return;

            // 表头宽度：英文表头往往比中文长（Process running / Config state），
            // 只按数据算会出现连表头都读不全的情况。
            int columns = list.Columns.Count;
            int[] headerWidths = new int[columns];
            for (int i = 0; i < columns; i++)
            {
                headerWidths[i] = TextRenderer.MeasureText(list.Columns[i].Text, list.Font).Width + 28;
            }

            EventHandler fit = delegate (object s, EventArgs e)
            {
                try
                {
                    if (list.IsDisposed || list.Columns.Count <= index) return;
                    if (!list.IsHandleCreated || list.ClientSize.Width <= 8) return;

                    int[] need = MeasureColumns(list, headerWidths);
                    if (need == null) return;

                    int used = 0;
                    for (int i = 0; i < need.Length; i++)
                    {
                        if (i != index) used += need[i];
                    }

                    // ClientSize 已经扣掉了纵向滚动条，直接用；再减一次就会在最后一列右边留白。
                    int viewport = list.ClientSize.Width;
                    int last = Math.Max(need[index], viewport - used);
                    if (last < 60) last = 60;

                    for (int i = 0; i < need.Length && i < list.Columns.Count; i++)
                    {
                        int w = i == index ? last : need[i];
                        if (list.Columns[i].Width != w) list.Columns[i].Width = w;
                    }
                }
                catch (Exception) { }
            };
            Fitters[list] = fit;
            list.Resize += fit;
            list.HandleCreated += fit;
            // 滚动条出现/消失、页面切换都会改变 ClientSize；只在 Resize 上算会留下陈旧的列宽。
            list.ClientSizeChanged += fit;
            // 页面是「按需显示」的：隐藏期间完成的布局会让列宽按偏窄的宽度算一次，
            // 之后显示时既没有 Resize 也没有 HandleCreated，列就会一直停留在被压扁的状态。
            list.VisibleChanged += fit;
            list.ParentChanged += fit;
            fit(null, null);
            try
            {
                // 布局完成后再算一次（构造期控件还没有最终宽度）。
                UiPost.Post(list, delegate () { fit(null, null); });
            }
            catch (Exception) { }
        }

        private sealed class ColumnFit
        {
            public int Count = -1;
            public int[] Needs;
        }

        /// <summary>每个列表上次测量的结果：行数没变就直接复用，避免每次缩放都全量测量。</summary>
        private static readonly Dictionary<ListView, ColumnFit> ColumnFits = new Dictionary<ListView, ColumnFit>();

        /// <summary>量出每列需要的宽度（表头与最长的单元格取大者）。行多时只抽样前 300 行。</summary>
        private static int[] MeasureColumns(ListView list, int[] headerWidths)
        {
            ColumnFit cache;
            if (ColumnFits.TryGetValue(list, out cache) && cache.Count == list.Items.Count && cache.Needs != null)
            {
                return cache.Needs;
            }

            int columns = Math.Min(headerWidths.Length, list.Columns.Count);
            int[] need = new int[columns];
            for (int i = 0; i < columns; i++) need[i] = headerWidths[i];

            try
            {
                int rows = Math.Min(list.Items.Count, 1000);
                for (int r = 0; r < rows; r++)
                {
                    ListViewItem item = list.Items[r];
                    for (int c = 0; c < columns; c++)
                    {
                        string text = c < item.SubItems.Count ? item.SubItems[c].Text : null;
                        if (string.IsNullOrEmpty(text)) continue;
                        int w = TextRenderer.MeasureText(text, list.Font).Width + 22;
                        if (w > need[c]) need[c] = w;
                    }
                }
            }
            catch (Exception) { }

            // 单列上限：避免一条超长路径把整张表撑到几千像素。
            for (int i = 0; i < columns; i++) if (need[i] > 1200) need[i] = 1200;

            ColumnFits[list] = new ColumnFit { Count = list.Items.Count, Needs = need };
            return need;
        }


        /// <summary>每个列表记住自己的列宽计算器：行内容变化后由 RefitColumns 触发重算。</summary>
        private static readonly Dictionary<ListView, EventHandler> Fitters = new Dictionary<ListView, EventHandler>();

        /// <summary>行内容变化后调用一次，让最后的文字列按新内容重新测量宽度（备注不再被截）。</summary>
        public static void RefitColumns(ListView list)
        {
            if (list == null || list.IsDisposed) return;
            EventHandler fit;
            if (Fitters.TryGetValue(list, out fit))
            {
                try { fit(null, null); }
                catch (Exception) { }
            }
        }

        /// <summary>ListView/ListBox 双缓冲：几千行滚动刷新不再闪烁（反射打开受保护属性）。</summary>
        public static void EnableDoubleBuffer(Control control)
        {
            try
            {
                System.Reflection.PropertyInfo prop = typeof(Control).GetProperty("DoubleBuffered",
                    System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
                if (prop != null) prop.SetValue(control, true, null);
            }
            catch (Exception) { }
        }

        /// <summary>
        /// 非堆栈场景的换行标签自动高度：按当前宽度实测换行后的文本高度并重设 Height，
        /// 用于 banner、报告页等绝对定位的长文本标签。
        /// </summary>
        public static void AutoWrapHeight(Label label, int minHeight)
        {
            if (label == null) return;
            label.AutoSize = false;
            EventHandler adjust = delegate (object s, EventArgs e)
            {
                try
                {
                    if (label.IsDisposed || label.Width <= 0 || string.IsNullOrEmpty(label.Text)) return;
                    Size sz = TextRenderer.MeasureText(label.Text, label.Font,
                        new Size(label.Width, int.MaxValue),
                        TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix | TextFormatFlags.TextBoxControl);
                    int h = Math.Max(minHeight, sz.Height + 4);
                    if (label.Height != h) label.Height = h;
                }
                catch (Exception) { }
            };
            label.Resize += adjust;
            label.TextChanged += adjust;
            adjust(null, null);
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
                if (c is CardPanel)
                {
                    // 卡片自己画 Surface 底 + 圆角边框；BackColor 参与子控件的透明模拟，不许覆盖。
                    c.ForeColor = Text;
                }
                else if (c is Form || c is UserControl || c is TabPage || c is SplitContainer || c is Panel)
                {
                    string panelTag = c.Tag as string;
                    // 页眉/侧栏/提示条自己声明了底色：这里只同步前景，别把它刷成页面底色。
                    if (panelTag == "header" || panelTag == "surface") c.BackColor = Surface;
                    else if (panelTag == "sidebar") c.BackColor = SurfaceAlt;
                    else if (panelTag == "banner") c.BackColor = BannerBack;
                    // 明确写成 Transparent 的堆栈/流式行要保留透明，否则卡片内部会被刷成页面底色。
                    else if (c.BackColor != Color.Transparent) c.BackColor = c is GroupBox ? Surface : Background;
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
                    if (c is ListView) EnableDoubleBuffer(c);
                    TryDarkNative(c);
                }
                else if (c is Button)
                {
                    var b = (Button)c;
                    string tag = b.Tag as string;
                    bool primary = tag == "primary";
                    bool navSelected = tag == "nav-selected";
                    bool nav = navSelected || tag == "nav";
                    b.FlatStyle = FlatStyle.Flat;
                    b.UseVisualStyleBackColor = false;
                    // 文案里的 & 是「Report & about」的一部分，不是快捷键助记符。
                    b.UseMnemonic = false;
                    // 先按文字量出宽度再配色：写死宽度的按钮会被语言/字体撑破并截字。
                    AutoSizeButton(b);
                    b.BackColor = primary ? Accent : (nav ? (navSelected ? SurfaceAlt : Color.Transparent) : ButtonFace);
                    b.ForeColor = primary ? OnAccent : (navSelected ? Accent : Text);
                    b.FlatAppearance.BorderColor = primary ? Accent : (navSelected ? Accent : Border);
                    b.FlatAppearance.BorderSize = navSelected ? 1 : 1;
                    b.FlatAppearance.MouseOverBackColor = primary ? ControlPaint.Light(Accent, 0.15f)
                        : (nav ? SurfaceAlt : ButtonHover);
                    b.FlatAppearance.MouseDownBackColor = primary ? ControlPaint.Dark(Accent, 0.08f)
                        : (nav ? SurfaceAlt : ButtonActive);
                    if (nav) b.TextAlign = ContentAlignment.MiddleLeft;
                }
                else if (c is Label || c is RadioButton || c is CheckBox)
                {
                    if (IsDefaultForeColor(c.ForeColor)) c.ForeColor = Text;
                    var label = c as Label;
                    if (label != null) label.UseMnemonic = false;
                }
                else if (c is StatusStrip)
                {
                    var status = (StatusStrip)c;
                    status.BackColor = SurfaceAlt;
                    status.ForeColor = Text;
                    status.SizingGrip = false;
                    foreach (ToolStripItem item in status.Items)
                    {
                        if (IsDefaultForeColor(item.ForeColor)) item.ForeColor = Text;
                    }
                }
                else if (c is ToolStrip)
                {
                    var strip = (ToolStrip)c;
                    strip.BackColor = SurfaceAlt;
                    strip.ForeColor = Text;
                    strip.RenderMode = ToolStripRenderMode.System;
                    foreach (ToolStripItem item in strip.Items)
                    {
                        if (IsDefaultForeColor(item.ForeColor)) item.ForeColor = Text;
                    }
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
