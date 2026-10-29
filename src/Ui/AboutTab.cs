using System;
using System.Drawing;
using System.Windows.Forms;
using HotkeyHound.Core;

namespace HotkeyHound.Ui
{
    /// <summary>
    /// 关于页：品牌渐变头图（图标同款蓝→紫）+ 说人话的多行简介 + 语言/主题设置 + 运行环境 + 已知局限。
    ///
    /// 为什么单独成页：原来「报告与关于」把两种完全不同的用途挤在一起——
    /// 报告是干活用的（生成、打开），关于是阅读用的（版本、作者、局限）。
    /// 文案刻意一行一句：一整段 400 字的说明没人读，bullet 才读得完。
    /// </summary>
    public sealed class AboutTab : UserControl
    {
        private readonly MainForm _host;
        private readonly ComboBox _langBox;
        private readonly ComboBox _themeBox;
        private bool _langInitializing = true;
        private bool _themeInitializing = true;

        public AboutTab(MainForm host)
        {
            _host = host;

            // ------------------------------------------------------------ 头图：图标同款蓝紫渐变
            var hero = new GradientPanel();
            hero.Dock = DockStyle.Top;
            hero.Height = 240;

            // 图标用带 Image 的 Label 而不是 PictureBox：Label 保证支持透明底，
            // PictureBox 在自绘父面板上可能仍画出白方块。
            var icon = new Label();
            icon.Location = new Point(28, 54);
            icon.Size = new Size(64, 64);
            try
            {
                Icon appIcon = UiTheme.LoadAppIcon();
                if (appIcon != null)
                {
                    using (var full = appIcon.ToBitmap())
                    {
                        icon.Image = new Bitmap(full, new Size(64, 64));
                    }
                }
            }
            catch (Exception) { }

            var name = new Label();
            name.Text = AppInfo.Title;
            name.Font = UiTheme.TitleFont(26F);
            name.ForeColor = Color.White;
            name.AutoSize = true;
            name.Location = new Point(112, 46);

            var tagline = new Label();
            tagline.Text = L10n.T("app.description");
            tagline.Font = UiTheme.TitleFont(11.5F);
            tagline.ForeColor = Color.FromArgb(235, 255, 255, 255);
            tagline.AutoSize = true;
            tagline.Location = new Point(114, 112);

            var versionLine = new Label();
            versionLine.Text = "v" + AppInfo.Version + "  ·  " + AppInfo.Codename;
            versionLine.Font = UiTheme.MonoFont(12F);
            versionLine.ForeColor = Color.White;
            versionLine.AutoSize = true;
            versionLine.Location = new Point(114, 148);

            var author = new Label();
            author.Text = L10n.T("ui.rt.author");
            author.Font = UiTheme.TitleFont(11F);
            author.ForeColor = Color.FromArgb(230, 255, 255, 255);
            author.AutoSize = true;
            author.Location = new Point(114, 184);

            hero.Controls.Add(icon);
            hero.Controls.Add(name);
            hero.Controls.Add(tagline);
            hero.Controls.Add(versionLine);
            hero.Controls.Add(author);
            // 子控件透明底：让渐变透出来，否则 Label 会各自画一块白底（主题引擎的锅）。
            foreach (Control hc in hero.Controls)
            {
                try { hc.BackColor = Color.Transparent; }
                catch (Exception) { }
            }

            // ------------------------------------------------------------ 正文：可滚动、一行一句
            var body = new Panel();
            body.Dock = DockStyle.Fill;
            body.AutoScroll = true;

            var stack = UiTheme.NewStack();
            stack.Padding = new Padding(16, 12, 16, 12);

            var heading = new Label();
            heading.Text = L10n.T("ui.ab.heading");
            heading.Font = UiTheme.TitleFont(15F);
            heading.AutoSize = true;
            heading.Margin = new Padding(0, 2, 0, 8);

            var intro = UiTheme.StackLabel(L10n.T("ui.ab.intro"), UiTheme.Text);
            intro.Font = UiTheme.TitleFont(11F);
            intro.Margin = new Padding(0, 0, 0, 10);

            string[] facts = new string[]
            {
                L10n.T("ui.ab.f1"), L10n.T("ui.ab.f2"), L10n.T("ui.ab.f3"),
                L10n.T("ui.ab.f4"), L10n.T("ui.ab.f5")
            };

            var env = UiTheme.StackLabel(
                L10n.F("ui.ab.env", AppInfo.Framework, AppInfo.MachineName, AppInfo.UserName,
                       AppInfo.IsAdministrator() ? L10n.T("common.yes") : L10n.T("common.no")),
                UiTheme.Muted);
            env.Font = UiTheme.MonoFont(9.5F);
            env.Margin = new Padding(0, 10, 0, 2);

            var license = UiTheme.StackLabel(L10n.T("ui.ab.license"), UiTheme.Muted);
            var limits = UiTheme.StackLabel(L10n.T("ui.rt.limits"), UiTheme.Muted);
            limits.Margin = new Padding(0, 10, 0, 2);
            var hint = UiTheme.StackLabel(L10n.T("ui.ab.hint"), UiTheme.Muted);

            UiTheme.StackAdd(stack, heading);
            UiTheme.StackAdd(stack, intro);
            for (int i = 0; i < facts.Length; i++)
            {
                var fact = UiTheme.StackLabel(facts[i], UiTheme.Text);
                fact.Margin = new Padding(10, 1, 0, 1);
                UiTheme.StackAdd(stack, fact);
            }
            UiTheme.StackAdd(stack, env);
            UiTheme.StackAdd(stack, license);
            UiTheme.StackAdd(stack, limits);
            UiTheme.StackAdd(stack, hint);

            body.Controls.Add(stack);
            Controls.Add(body);
            Controls.Add(hero);

            _langInitializing = false;
            _themeInitializing = false;

            // ------------------------------------------------------------ 语言 / 主题设置
            // 两个下拉放在页面底部：它们是操作，简介是阅读物，别混在开头。
            var settingsBox = new GroupBox();
            settingsBox.Text = L10n.T("ui.ab.settings");
            settingsBox.Dock = DockStyle.Bottom;
            settingsBox.Height = 92;
            settingsBox.Padding = new Padding(14, 6, 14, 8);

            var langLabel = new Label();
            langLabel.Text = L10n.T("ui.rt.langLabel");
            langLabel.AutoSize = true;
            langLabel.Location = new Point(16, 30);
            _langBox = new ComboBox();
            _langBox.DropDownStyle = ComboBoxStyle.DropDownList;
            _langBox.Location = new Point(96, 26);
            _langBox.Width = 200;
            _langBox.Items.Add(L10n.LangDisplay(L10n.Chinese));
            _langBox.Items.Add(L10n.LangDisplay(L10n.English));
            _langBox.SelectedIndex = L10n.Current == L10n.Chinese ? 0 : 1;
            _langBox.SelectedIndexChanged += OnLanguageChanged;

            var themeLabel = new Label();
            themeLabel.Text = L10n.T("ui.rt.themeLabel");
            themeLabel.AutoSize = true;
            themeLabel.Location = new Point(330, 30);
            _themeBox = new ComboBox();
            _themeBox.DropDownStyle = ComboBoxStyle.DropDownList;
            _themeBox.Location = new Point(410, 26);
            _themeBox.Width = 200;
            _themeBox.Items.Add(L10n.T("ui.rt.themeSystem"));
            _themeBox.Items.Add(L10n.T("ui.rt.themeLight"));
            _themeBox.Items.Add(L10n.T("ui.rt.themeDark"));
            _themeBox.SelectedIndex = ThemeIndexOf(UiTheme.Mode);
            _themeBox.SelectedIndexChanged += OnThemeChanged;

            settingsBox.Controls.Add(langLabel);
            settingsBox.Controls.Add(_langBox);
            settingsBox.Controls.Add(themeLabel);
            settingsBox.Controls.Add(_themeBox);
            Controls.Add(settingsBox);

            _langInitializing = false;
            _themeInitializing = false;
        }

        private void OnLanguageChanged(object sender, EventArgs e)
        {
            if (_langInitializing) return;
            string target = _langBox.SelectedIndex == 0 ? L10n.Chinese : L10n.English;
            if (target == L10n.Current) return;
            L10n.SetLanguage(target);
            MessageBox.Show(this, L10n.F("ui.rt.langRestart", L10n.LangDisplay(target)), AppInfo.Title,
                            MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        private static int ThemeIndexOf(ThemeMode mode)
        {
            switch (mode)
            {
                case ThemeMode.Light: return 1;
                case ThemeMode.Dark: return 2;
                default: return 0;
            }
        }

        private void OnThemeChanged(object sender, EventArgs e)
        {
            if (_themeInitializing) return;
            ThemeMode mode = _themeBox.SelectedIndex == 1 ? ThemeMode.Light
                            : (_themeBox.SelectedIndex == 2 ? ThemeMode.Dark : ThemeMode.System);
            if (mode == UiTheme.Mode) return;
            UiTheme.SetMode(mode);
            MessageBox.Show(this, L10n.F("ui.rt.themeRestart", UiTheme.ModeName(mode)), AppInfo.Title,
                            MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
    }
}
