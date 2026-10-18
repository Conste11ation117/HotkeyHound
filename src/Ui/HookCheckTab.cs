using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;
using HotkeyHound.Core;

namespace HotkeyHound.Ui
{
    /// <summary>
    /// 钩子检测（引擎 C）。
    /// 铁律：本页加载时绝不安装钩子——HookSniffer.Start() 只在用户点「布防」时调用。
    /// 本页只记录与判定，绝不结束或暂停任何进程。
    /// </summary>
    public sealed class HookCheckTab : UserControl
    {
        private readonly MainForm _host;
        private readonly Button _armButton;
        private readonly Button _stopButton;
        private readonly TextBox _comboBox;
        private readonly Label _statusLabel;
        private readonly Label _warningLabel;
        private readonly ListView _list;
        private readonly ListBox _candidates;
        private readonly ListBox _comparisons;
        private readonly Timer _tick;

        public HookCheckTab(MainForm host)
        {
            _host = host;

            var top = new Panel();
            top.Dock = DockStyle.Top;
            top.Height = 150;

            var title = new Label();
            title.Text = L10n.T("ui.hk.title");
            title.Font = UiTheme.TitleFont(12F);
            title.Location = new Point(0, 6);
            title.AutoSize = true;

            var guide = new Label();
            guide.Location = new Point(2, 34);
            guide.Size = new Size(100, 20);
            guide.AutoSize = false;
            guide.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            guide.ForeColor = UiTheme.Warning;
            guide.Text = L10n.T("ui.hk.guide");

            _comboBox = new TextBox();
            _comboBox.Location = new Point(0, 62);
            _comboBox.Width = 200;
            _comboBox.Text = "Ctrl+Alt+M";
            try { _comboBox.Font = new Font("Consolas", 10F); }
            catch (Exception) { }

            _armButton = new Button();
            _armButton.Text = L10n.T("ui.hk.arm");
            _armButton.Font = UiTheme.TitleFont(10F);
            _armButton.Size = new Size(160, 38);
            _armButton.Location = new Point(210, 58);
            _armButton.Click += OnArmClick;

            _stopButton = new Button();
            _stopButton.Text = L10n.T("ui.hk.stop");
            _stopButton.Size = new Size(150, 38);
            _stopButton.Location = new Point(378, 58);
            _stopButton.Click += OnStopClick;

            _statusLabel = new Label();
            _statusLabel.Location = new Point(540, 68);
            _statusLabel.AutoSize = true;
            _statusLabel.ForeColor = UiTheme.Success;
            _statusLabel.Text = L10n.T("ui.hk.notArmed");

            _warningLabel = new Label();
            _warningLabel.Location = new Point(2, 100);
            _warningLabel.Size = new Size(100, 42);
            _warningLabel.AutoSize = false;
            _warningLabel.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            _warningLabel.Text = L10n.T("ui.hk.warn");

            top.Controls.Add(title);
            top.Controls.Add(guide);
            top.Controls.Add(_comboBox);
            top.Controls.Add(_armButton);
            top.Controls.Add(_stopButton);
            top.Controls.Add(_statusLabel);
            top.Controls.Add(_warningLabel);

            _list = new ListView();
            _list.Dock = DockStyle.Fill;
            _list.View = View.Details;
            _list.FullRowSelect = true;
            _list.GridLines = true;
            _list.HideSelection = false;
            _list.Columns.Add(L10n.T("ui.at.col.time"), 90);
            _list.Columns.Add(L10n.T("ui.col.combo"), 150);
            _list.Columns.Add(L10n.T("ui.hk.col.verdict"), 150);
            _list.Columns.Add(L10n.T("ui.col.evidence"), 300);
            _list.Columns.Add(L10n.T("ui.hk.col.note"), 400);

            var side = new Panel();
            side.Dock = DockStyle.Right;
            side.Width = 330;
            side.AutoScroll = true;

            var candTitle = new Label();
            candTitle.Text = L10n.T("ui.hk.candTitle");
            candTitle.Font = UiTheme.TitleFont(9.5F);
            candTitle.Location = new Point(4, 6);
            candTitle.AutoSize = true;

            var refresh = new Button();
            refresh.Text = L10n.T("ui.hk.refresh");
            refresh.Size = new Size(150, 26);
            refresh.Location = new Point(4, 30);
            refresh.Click += delegate (object s, EventArgs e) { RefreshCandidates(); };

            _candidates = new ListBox();
            _candidates.Location = new Point(4, 62);
            _candidates.Size = new Size(300, 130);
            _candidates.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            _candidates.IntegralHeight = false;

            var steps = new Label();
            steps.Location = new Point(4, 200);
            steps.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            steps.Size = new Size(300, 110);
            steps.Text = L10n.T("ui.hk.steps");

            var record = new Button();
            record.Text = L10n.T("ui.hk.record");
            record.Size = new Size(150, 30);
            record.Location = new Point(4, 316);
            record.Click += OnRecordComparisonClick;

            _comparisons = new ListBox();
            _comparisons.Location = new Point(4, 352);
            _comparisons.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Bottom;
            _comparisons.Size = new Size(300, 120);

            side.Controls.Add(candTitle);
            side.Controls.Add(refresh);
            side.Controls.Add(_candidates);
            side.Controls.Add(steps);
            side.Controls.Add(record);
            side.Controls.Add(_comparisons);

            // 先加 Fill 的列表，再加 Right/Top 面板（WinForms 按此顺序反向布局）
            Controls.Add(_list);
            Controls.Add(side);
            Controls.Add(top);

            _host.Sniffer.VerdictReady += OnVerdictReady;
            // 引擎 A 的占用表供判定「是不是全局热键」用
            _host.Sniffer.IsOccupiedByEngineA = delegate (KeyCombo combo)
            {
                ScanSnapshot s = _host.Snapshot;
                if (s == null || s.Occupancy == null || combo == null) return false;
                foreach (OccupancyResult r in s.Occupancy)
                {
                    if (r.Combo != null && r.Combo.Normalize() == combo.Normalize()) return r.Occupied;
                }
                return false;
            };

            _tick = new Timer();
            _tick.Interval = 200;
            _tick.Tick += OnTick;
        }

        private void OnTick(object sender, EventArgs e)
        {
            if (IsDisposed) return;
            ProbeSession session = _host.Sniffer.Current;
            if (session == null || session.Settled)
            {
                if (_tick.Enabled) _tick.Stop();
                return;
            }
            _statusLabel.Text = L10n.F("ui.hk.armedTick", session.Combo.Display(), session.SecondsLeft);
        }

        private void OnArmClick(object sender, EventArgs e)
        {
            KeyCombo combo = KeyCombo.Parse(_comboBox.Text);
            if (combo == null)
            {
                MessageBox.Show(this, L10n.T("ui.hk.parseFail"),
                                AppInfo.Title, MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            HookSniffer sniffer = _host.Sniffer;
            _armButton.Enabled = false;
            try
            {
                if (!sniffer.IsRunning)
                {
                    _statusLabel.ForeColor = UiTheme.Warning;
                    _statusLabel.Text = L10n.T("ui.hk.installing");
                    if (!sniffer.Start())
                    {
                        _statusLabel.ForeColor = UiTheme.Error;
                        _statusLabel.Text = L10n.F("ui.hk.installFail", sniffer.LastError ?? L10n.T("common.unknown"));
                        MessageBox.Show(this, L10n.F("ui.hk.installFailDlg", sniffer.LastError),
                                        AppInfo.Title, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                        return;
                    }
                }

                if (!sniffer.Arm(combo))
                {
                    _statusLabel.ForeColor = UiTheme.Error;
                    _statusLabel.Text = L10n.F("ui.hk.armFail", sniffer.LastError ?? L10n.T("common.unknown"));
                    return;
                }

                _statusLabel.ForeColor = UiTheme.Success;
                _statusLabel.Text = L10n.F("ui.hk.armed", combo.Display(), HookSniffer.ArmWindow.TotalSeconds);
                RefreshCandidates();
                _tick.Start();
            }
            finally
            {
                _armButton.Enabled = true;
            }
        }

        private void OnStopClick(object sender, EventArgs e)
        {
            HookSniffer sniffer = _host.Sniffer;
            if (sniffer.IsRunning) sniffer.Stop();
            if (_tick.Enabled) _tick.Stop();
            _statusLabel.ForeColor = UiTheme.Muted;
            _statusLabel.Text = L10n.T("ui.hk.stopped");
        }

        /// <summary>回调来自钩子线程／定时器线程，必须异步回到 UI 线程。</summary>
        private void OnVerdictReady(HookCheckRecord record)
        {
            if (record == null) return;
            SessionRecords.AddHookRecord(record);
            if (IsDisposed) return;
            try { BeginInvoke(new Action<HookCheckRecord>(AppendVerdict), record); }
            catch (Exception) { }
        }

        private void AppendVerdict(HookCheckRecord record)
        {
            if (IsDisposed) return;
            try
            {
                var item = new ListViewItem(record.Time.ToString("HH:mm:ss"));
                item.SubItems.Add(record.Combo == null ? L10n.T("attr.unknownCombo") : record.Combo.Display());
                item.SubItems.Add(HookCheckRecord.VerdictName(record.Verdict));
                item.SubItems.Add(record.Evidence);
                item.SubItems.Add(record.Note);
                switch (record.Verdict)
                {
                    case HookVerdict.Delivered: item.ForeColor = UiTheme.Success; break;
                    case HookVerdict.NotGlobal: item.ForeColor = UiTheme.Info; break;
                    case HookVerdict.SwallowedUpstream:
                    case HookVerdict.SwallowedBeforeHotkey: item.ForeColor = UiTheme.Error; break;
                    default: item.ForeColor = UiTheme.Muted; break;
                }
                _list.Items.Add(item);
                item.EnsureVisible();
                _statusLabel.Text = L10n.F("ui.hk.verdictPrefix", HookCheckRecord.VerdictName(record.Verdict));
            }
            catch (Exception) { }
        }

        private void RefreshCandidates()
        {
            List<string> running = CandidateProcesses.Running();
            _candidates.BeginUpdate();
            try
            {
                _candidates.Items.Clear();
                foreach (string name in running) _candidates.Items.Add(name);
                if (running.Count == 0) _candidates.Items.Add(L10n.T("ui.hk.noCandidates"));
            }
            finally
            {
                _candidates.EndUpdate();
            }
        }

        private void OnRecordComparisonClick(object sender, EventArgs e)
        {
            try
            {
                List<string> running = CandidateProcesses.Running();
                ComparisonSnapshot snap = _host.Sniffer.RecordComparison(
                    L10n.F("ui.hk.compare.label", DateTime.Now.ToString("HH:mm:ss")), running);
                SessionRecords.AddComparison(snap);
                _comparisons.Items.Add(snap.ToText());
                MessageBox.Show(this, L10n.F("ui.hk.compare.saved", snap.Lines.Count),
                    AppInfo.Title, MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, L10n.F("ui.hk.compare.failed", ex.Message), AppInfo.Title,
                                MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }
    }
}
