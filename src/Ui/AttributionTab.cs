using System;
using System.Drawing;
using System.Text;
using System.Threading;
using System.Windows.Forms;
using HotkeyHound.Core;

namespace HotkeyHound.Ui
{
    /// <summary>
    /// 按键归属（引擎 B）。
    /// 铁律：本页加载时绝不安装任何钩子——AttributionEngine.Start() 只在
    /// 用户点「开始监听」时才被调用，停止时立刻 UnhookWindowsHookEx。
    /// </summary>
    public sealed class AttributionTab : UserControl
    {
        private readonly MainForm _host;
        private readonly Button _toggleButton;
        private readonly Button _clearButton;
        private readonly Button _copyButton;
        private readonly Label _statusLabel;
        private readonly Label _warningLabel;
        private readonly Label _hint;
        private readonly ListView _list;

        public AttributionTab(MainForm host)
        {
            _host = host;

            var top = new Panel();
            top.Dock = DockStyle.Top;
            top.Height = 132;

            var title = new Label();
            title.Text = L10n.T("ui.at.title");
            title.Font = new Font(Font.FontFamily, 12F, FontStyle.Bold);
            title.Location = new Point(0, 6);
            title.AutoSize = true;

            _hint = new Label();
            _hint.Location = new Point(2, 34);
            _hint.Size = new Size(1000, 34);
            _hint.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            _hint.ForeColor = Color.DimGray;
            _hint.Text = L10n.T("ui.at.hint");

            _toggleButton = new Button();
            _toggleButton.Text = L10n.T("ui.at.start");
            _toggleButton.Font = new Font(Font.FontFamily, 11F, FontStyle.Bold);
            _toggleButton.Size = new Size(150, 40);
            _toggleButton.Location = new Point(0, 74);
            _toggleButton.Click += OnToggleClick;

            _clearButton = new Button();
            _clearButton.Text = L10n.T("ui.at.clear");
            _clearButton.Size = new Size(90, 40);
            _clearButton.Location = new Point(160, 74);
            _clearButton.Click += OnClearClick;

            _copyButton = new Button();
            _copyButton.Text = L10n.T("ui.at.copy");
            _copyButton.Size = new Size(130, 40);
            _copyButton.Location = new Point(258, 74);
            _copyButton.Click += OnCopyClick;

            _statusLabel = new Label();
            _statusLabel.Location = new Point(398, 86);
            _statusLabel.AutoSize = true;
            _statusLabel.ForeColor = Color.FromArgb(60, 90, 60);
            _statusLabel.Text = L10n.T("ui.at.notStarted");

            _warningLabel = new Label();
            _warningLabel.Location = new Point(622, 74);
            _warningLabel.Size = new Size(380, 52);
            _warningLabel.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            _warningLabel.Text = BuildWarningText();

            top.Controls.Add(title);
            top.Controls.Add(_hint);
            top.Controls.Add(_toggleButton);
            top.Controls.Add(_clearButton);
            top.Controls.Add(_copyButton);
            top.Controls.Add(_statusLabel);
            top.Controls.Add(_warningLabel);

            _list = new ListView();
            _list.Dock = DockStyle.Fill;
            _list.View = View.Details;
            _list.FullRowSelect = true;
            _list.GridLines = true;
            _list.HideSelection = false;
            _list.Columns.Add(L10n.T("ui.at.col.time"), 90);
            _list.Columns.Add(L10n.T("ui.at.col.process"), 170);
            _list.Columns.Add(L10n.T("ui.at.col.pid"), 70);
            _list.Columns.Add(L10n.T("ui.col.combo"), 160);
            _list.Columns.Add(L10n.T("ui.col.path"), 320);
            _list.Columns.Add(L10n.T("ui.at.col.elevated"), 80);

            Controls.Add(_list);
            Controls.Add(top);

            _host.Attribution.RecordReceived += OnRecordReceived;
        }

        private static string BuildWarningText()
        {
            var sb = new StringBuilder();
            if (ProcessIntegrity.IsBelowMedium)
            {
                sb.Append(L10n.F("ui.at.warn.low", ProcessIntegrity.Describe(ProcessIntegrity.Current))).Append("\r\n");
            }
            if (!AppInfo.IsAdministrator())
            {
                sb.Append(L10n.T("ui.at.warn.notAdmin"));
            }
            if (sb.Length == 0) sb.Append(L10n.T("ui.at.warn.ok"));
            return sb.ToString();
        }

        /// <summary>回调来自钩子线程，必须异步回到 UI 线程（回调里同步 Invoke 会拖住钩子线程，拖慢全系统输入）。</summary>
        private void OnRecordReceived(AttributionRecord record)
        {
            if (record == null) return;
            SessionRecords.AddAttribution(record);
            if (IsDisposed) return;
            try { BeginInvoke(new Action<AttributionRecord>(AppendRecord), record); }
            catch (Exception) { }
        }

        private void AppendRecord(AttributionRecord record)
        {
            if (IsDisposed) return;
            try
            {
                var item = new ListViewItem(record.Time.ToString("HH:mm:ss"));
                item.SubItems.Add(string.IsNullOrEmpty(record.ProcessName) ? L10n.T("attr.unknownProc") : record.ProcessName);
                item.SubItems.Add(record.Pid.ToString());
                item.SubItems.Add(record.Combo == null ? L10n.T("attr.unknownCombo") : record.Combo.Display());
                item.SubItems.Add(string.IsNullOrEmpty(record.ProcessPath) ? L10n.T("ui.at.unknownPath") : record.ProcessPath);
                item.SubItems.Add(record.IsElevated ? L10n.T("common.yes") : L10n.T("common.no"));
                if (record.IsElevated) item.ForeColor = Color.FromArgb(150, 90, 10);
                _list.Items.Add(item);
                item.EnsureVisible();
                _statusLabel.Text = L10n.F("ui.at.captured", _list.Items.Count);
            }
            catch (Exception) { }
        }

        private void OnToggleClick(object sender, EventArgs e)
        {
            AttributionEngine engine = _host.Attribution;
            if (engine.IsRunning)
            {
                engine.Stop();
                _toggleButton.Text = L10n.T("ui.at.start");
                _statusLabel.ForeColor = Color.FromArgb(60, 90, 60);
                _statusLabel.Text = L10n.T("ui.at.stopped");
                return;
            }

            _toggleButton.Enabled = false;
            _statusLabel.ForeColor = Color.FromArgb(150, 90, 10);
            _statusLabel.Text = L10n.T("ui.at.installing");
            // Start() 最多等 5 秒，放后台线程避免冻结界面
            var thread = new Thread(delegate ()
            {
                bool ok = false;
                string error = null;
                try { ok = engine.Start(); }
                catch (Exception ex) { error = ex.Message; }
                try
                {
                    BeginInvoke(new Action(delegate () { OnStartFinished(engine, ok, error); }));
                }
                catch (Exception) { }
            });
            thread.IsBackground = true;
            thread.Name = "HotkeyHound-AttributionStart";
            thread.Start();
        }

        private void OnStartFinished(AttributionEngine engine, bool ok, string error)
        {
            if (IsDisposed) return;
            _toggleButton.Enabled = true;
            if (ok)
            {
                _toggleButton.Text = L10n.T("ui.at.stop");
                _statusLabel.ForeColor = Color.FromArgb(60, 90, 60);
                _statusLabel.Text = L10n.T("ui.at.listening");
            }
            else
            {
                _toggleButton.Text = L10n.T("ui.at.start");
                _statusLabel.ForeColor = Color.FromArgb(170, 40, 30);
                _statusLabel.Text = L10n.F("ui.at.startFailed", error ?? engine.LastError ?? L10n.T("common.unknown"));
                MessageBox.Show(this,
                    L10n.F("ui.at.startFailedDetail", error ?? engine.LastError),
                    AppInfo.Title, MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        private void OnClearClick(object sender, EventArgs e)
        {
            _host.Attribution.Clear();
            SessionRecords.ClearAttribution();
            _list.Items.Clear();
            _statusLabel.Text = L10n.T("ui.at.cleared");
        }

        private void OnCopyClick(object sender, EventArgs e)
        {
            var sb = new StringBuilder();
            sb.Append(L10n.T("ui.at.copyHeader")).Append("\r\n");
            foreach (ListViewItem item in _list.Items)
            {
                for (int i = 0; i < item.SubItems.Count; i++)
                {
                    if (i > 0) sb.Append('\t');
                    sb.Append(item.SubItems[i].Text);
                }
                sb.Append("\r\n");
            }
            if (_list.Items.Count == 0) sb.Append(L10n.T("ui.at.noRecords")).Append("\r\n");
            try
            {
                Clipboard.SetText(sb.ToString());
                _statusLabel.Text = L10n.F("ui.at.copied", _list.Items.Count);
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, L10n.F("ui.at.copyFailed", ex.Message), AppInfo.Title,
                                MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }
    }
}
