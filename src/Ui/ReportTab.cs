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

            var heading = new Label();
            heading.Text = L10n.T("ui.rt.heading");
            heading.Font = UiTheme.TitleFont(15F);
            heading.Location = new Point(16, 16);
            heading.AutoSize = true;

            var pathLabel = new Label();
            pathLabel.Text = L10n.T("ui.rt.pathLabel");
            pathLabel.Location = new Point(18, 62);
            pathLabel.AutoSize = true;

            _pathBox = new TextBox();
            _pathBox.Location = new Point(100, 58);
            _pathBox.Width = 610;
            _pathBox.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;

            _openButton = new Button();
            _openButton.Text = L10n.T("ui.rt.open");
            _openButton.Location = new Point(722, 56);
            _openButton.Size = new Size(110, 26);
            _openButton.Anchor = AnchorStyles.Top | AnchorStyles.Right;
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
            _generateButton.Location = new Point(840, 56);
            _generateButton.Size = new Size(110, 26);
            _generateButton.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            _generateButton.Click += delegate (object s, EventArgs e) { Generate(); };

            _reportInfo = new Label();
            _reportInfo.Location = new Point(18, 92);
            _reportInfo.AutoSize = true;
            _reportInfo.ForeColor = UiTheme.Muted;
            _reportInfo.Text = L10n.T("ui.rt.notGenerated");

            // 语言切换：放在「报告与关于」页。切换后提示需要重启（界面文本在启动时已构建）。
            var langLabel = new Label();
            langLabel.Text = L10n.T("ui.rt.langLabel");
            langLabel.Location = new Point(18, 118);
            langLabel.AutoSize = true;

            _langBox = new ComboBox();
            _langBox.DropDownStyle = ComboBoxStyle.DropDownList;
            _langBox.Location = new Point(150, 114);
            _langBox.Width = 200;
            _langBox.Items.Add(L10n.LangDisplay(L10n.Chinese));
            _langBox.Items.Add(L10n.LangDisplay(L10n.English));
            _langBox.SelectedIndex = L10n.Current == L10n.Chinese ? 0 : 1;
            _langBox.SelectedIndexChanged += OnLanguageChanged;

            // 主题切换：与语言一样写进同一个 settings.ini；WinForms 原生控件换肤不可靠，提示重启。
            var themeLabel = new Label();
            themeLabel.Text = L10n.T("ui.rt.themeLabel");
            themeLabel.Location = new Point(18, 146);
            themeLabel.AutoSize = true;

            _themeBox = new ComboBox();
            _themeBox.DropDownStyle = ComboBoxStyle.DropDownList;
            _themeBox.Location = new Point(150, 142);
            _themeBox.Width = 200;
            _themeBox.Items.Add(L10n.T("ui.rt.themeSystem"));
            _themeBox.Items.Add(L10n.T("ui.rt.themeLight"));
            _themeBox.Items.Add(L10n.T("ui.rt.themeDark"));
            _themeBox.SelectedIndex = ThemeIndexOf(UiTheme.Mode);
            _themeBox.SelectedIndexChanged += OnThemeChanged;

            _releaseNote = new Label();
            _releaseNote.Location = new Point(18, 184);
            _releaseNote.AutoSize = true;
            _releaseNote.MaximumSize = new Size(1000, 0);
            _releaseNote.Text =
                L10n.F("ui.rt.release", AppInfo.Title, AppInfo.Version, AppInfo.VersionNote, AppInfo.Framework,
                       AppInfo.MachineName, AppInfo.UserName,
                       AppInfo.IsAdministrator() ? L10n.T("common.yes") : L10n.T("common.no"));

            _limits = new Label();
            _limits.Location = new Point(18, 282);
            _limits.AutoSize = true;
            _limits.MaximumSize = new Size(1000, 0);
            _limits.Text = L10n.T("ui.rt.limits");

            Controls.Add(heading);
            Controls.Add(pathLabel);
            Controls.Add(_pathBox);
            Controls.Add(_openButton);
            Controls.Add(_generateButton);
            Controls.Add(_reportInfo);
            Controls.Add(langLabel);
            Controls.Add(_langBox);
            Controls.Add(themeLabel);
            Controls.Add(_themeBox);
            Controls.Add(_releaseNote);
            Controls.Add(_limits);

            _langInitializing = false;
            _themeInitializing = false;

            Resize += delegate (object s, EventArgs e)
            {
                _pathBox.Width = Math.Max(200, ClientSize.Width - 460);
            };
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
