using System;
using System.Drawing;
using System.IO;
using System.Windows.Forms;
using HotkeyHound.Core;

namespace HotkeyHound.Ui
{
    /// <summary>
    /// 报告页：只负责「生成 / 打开 HTML 报告」。版本信息、作者、语言与主题设置
    /// 在 3.0.0 拆到了单独的「关于」页——报告是动作，关于是阅读，不该挤在同一屏。
    /// </summary>
    public sealed class ReportTab : UserControl
    {
        private readonly MainForm _host;
        private readonly TextBox _pathBox;
        private readonly Button _openButton;
        private readonly Button _generateButton;
        private readonly Label _reportInfo;

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
            heading.Font = UiTheme.TitleFont(16F);
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
            UiTheme.MarkPrimary(_generateButton);
            _generateButton.Click += delegate (object s, EventArgs e) { Generate(); };

            pathRow.Controls.Add(pathLabel, 0, 0);
            pathRow.Controls.Add(_pathBox, 1, 0);
            pathRow.Controls.Add(_openButton, 2, 0);
            pathRow.Controls.Add(_generateButton, 3, 0);

            _reportInfo = UiTheme.StackLabel(L10n.T("ui.rt.notGenerated"), UiTheme.Muted);

            UiTheme.StackAdd(stack, heading);
            UiTheme.StackAdd(stack, pathRow);
            UiTheme.StackAdd(stack, _reportInfo);

            body.Controls.Add(stack);
            Controls.Add(body);
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
