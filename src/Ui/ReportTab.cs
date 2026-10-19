using System;
using System.Drawing;
using System.IO;
using System.Windows.Forms;
using HotkeyHound.Core;

namespace HotkeyHound.Ui
{
    public sealed class ReportTab : UserControl
    {
        private readonly MainForm _host;
        private readonly TextBox _pathBox;
        private readonly Button _openButton;
        private readonly Button _generateButton;
        private readonly Label _releaseNote;
        private readonly Label _limits;
        private readonly Label _reportInfo;
        private readonly ComboBox _langBox;
        private readonly ComboBox _themeBox;
        private bool _langInitializing = true;
        private bool _themeInitializing = true;

        public ReportTab(MainForm host)
        {
            _host = host;

            // 整页用流式堆栈（不再用绝对坐标/锚点）：窗口多窄都不截断，矮了还能滚动。
            var body = new Panel();
            body.Dock = DockStyle.Fill;
            body.AutoScroll = true;

            var stack = UiTheme.NewStack();
            stack.Padding = new Padding(14, 8, 14, 10);

            var heading = new Label();
            heading.Text = L10n.T("ui.rt.heading");
            heading.Font = UiTheme.TitleFont(15F);
            heading.AutoSize = true;
            heading.Margin = new Padding(0, 2, 0, 10);

            var pathRow = new TableLayoutPanel();
            pathRow.Dock = DockStyle.Fill;
            pathRow.AutoSize = true;
            pathRow.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            pathRow.ColumnCount = 4;
            pathRow.RowCount = 1;
            pathRow.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            pathRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            pathRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            pathRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            pathRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            try { pathRow.BackColor = Color.Transparent; }
            catch (Exception) { }

            var pathLabel = new Label();
            pathLabel.Text = L10n.T("ui.rt.pathLabel");
            pathLabel.AutoSize = true;
            pathLabel.Dock = DockStyle.Fill;
            pathLabel.Margin = new Padding(0, 6, 8, 0);

            _pathBox = new TextBox();
            _pathBox.Dock = DockStyle.Fill;
            _pathBox.Margin = new Padding(0, 4, 8, 4);

            _openButton = new Button();
            _openButton.Text = L10n.T("ui.rt.open");
            _openButton.Size = new Size(110, 28);
            _openButton.Margin = new Padding(0, 4, 8, 4);
            _openButton.Click += delegate (object s, EventArgs e)
            {
                string p = _pathBox.Text.Trim();
                if (p.Length == 0 || !File.Exists(p))
                {
                    MessageBox.Show(this, L10n.T("ui.rt.noReport"), AppInfo.Title,
                                    MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return;
                }
                OverviewTab.OpenPath(p);
            };

            _generateButton = new Button();
            _generateButton.Text = L10n.T("ui.rt.generate");
            _generateButton.Size = new Size(110, 28);
            _generateButton.Margin = new Padding(0, 4, 0, 4);
            _generateButton.Click += delegate (object s, EventArgs e) { Generate(); };

            pathRow.Controls.Add(pathLabel, 0, 0);
            pathRow.Controls.Add(_pathBox, 1, 0);
            pathRow.Controls.Add(_openButton, 2, 0);
            pathRow.Controls.Add(_generateButton, 3, 0);

            _reportInfo = UiTheme.StackLabel(L10n.T("ui.rt.notGenerated"), UiTheme.Muted);

            // 语言切换：放在「报告与关于」页。切换后提示需要重启（界面文本在启动时已构建）。
            var langRow = UiTheme.NewFlowRow();
            var langLabel = new Label();
            langLabel.Text = L10n.T("ui.rt.langLabel");
            langLabel.AutoSize = true;
            langLabel.Anchor = AnchorStyles.Left;
            langLabel.Margin = new Padding(0, 2, 8, 2);
            _langBox = new ComboBox();
            _langBox.DropDownStyle = ComboBoxStyle.DropDownList;
            _langBox.Width = 200;
            _langBox.Margin = new Padding(0, 2, 8, 2);
            _langBox.Items.Add(L10n.LangDisplay(L10n.Chinese));
            _langBox.Items.Add(L10n.LangDisplay(L10n.English));
            _langBox.SelectedIndex = L10n.Current == L10n.Chinese ? 0 : 1;
            _langBox.SelectedIndexChanged += OnLanguageChanged;
            langRow.Controls.Add(langLabel);
            langRow.Controls.Add(_langBox);

            // 主题切换：与语言一样写进同一个 settings.ini；WinForms 原生控件换肤不可靠，提示重启。
            var themeRow = UiTheme.NewFlowRow();
            var themeLabel = new Label();
            themeLabel.Text = L10n.T("ui.rt.themeLabel");
            themeLabel.AutoSize = true;
            themeLabel.Anchor = AnchorStyles.Left;
            themeLabel.Margin = new Padding(0, 2, 8, 2);
            _themeBox = new ComboBox();
            _themeBox.DropDownStyle = ComboBoxStyle.DropDownList;
            _themeBox.Width = 200;
            _themeBox.Margin = new Padding(0, 2, 8, 2);
            _themeBox.Items.Add(L10n.T("ui.rt.themeSystem"));
            _themeBox.Items.Add(L10n.T("ui.rt.themeLight"));
            _themeBox.Items.Add(L10n.T("ui.rt.themeDark"));
            _themeBox.SelectedIndex = ThemeIndexOf(UiTheme.Mode);
            _themeBox.SelectedIndexChanged += OnThemeChanged;
            themeRow.Controls.Add(themeLabel);
            themeRow.Controls.Add(_themeBox);

            _releaseNote = UiTheme.StackLabel(
                L10n.F("ui.rt.release", AppInfo.Title, AppInfo.Version, AppInfo.VersionNote, AppInfo.Framework,
                       AppInfo.MachineName, AppInfo.UserName,
                       AppInfo.IsAdministrator() ? L10n.T("common.yes") : L10n.T("common.no")),
                UiTheme.Text);
            _releaseNote.Margin = new Padding(0, 10, 0, 4);

            _limits = UiTheme.StackLabel(L10n.T("ui.rt.limits"), UiTheme.Muted);

            UiTheme.StackAdd(stack, heading);
            UiTheme.StackAdd(stack, pathRow);
            UiTheme.StackAdd(stack, _reportInfo);
            UiTheme.StackAdd(stack, langRow);
            UiTheme.StackAdd(stack, themeRow);
            UiTheme.StackAdd(stack, _releaseNote);
            UiTheme.StackAdd(stack, _limits);

            body.Controls.Add(stack);
            Controls.Add(body);

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

        private void Generate()
        {
            ScanSnapshot s = _host.Snapshot;
            if (s == null)
            {
                MessageBox.Show(this, L10n.T("ui.rt.needScan"), AppInfo.Title,
                                MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            string path = _pathBox.Text.Trim();
            if (path.Length == 0)
            {
                path = Path.Combine(Environment.CurrentDirectory, ReportBuilder.DefaultFileName());
            }
            try
            {
                ReportBuilder.Write(s, path);
                _pathBox.Text = Path.GetFullPath(path);
                var info = new FileInfo(path);
                _reportInfo.Text = L10n.F("ui.rt.generated", info.LastWriteTime.ToString("yyyy-MM-dd HH:mm:ss"), info.Length);
                _host.Log.Write(L10n.F("cli.scan.reportWritten", info.FullName));
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, L10n.F("ui.rt.writeFailed", ex.Message), AppInfo.Title,
                                MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }
    }
}
