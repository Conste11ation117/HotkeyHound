using System;
using System.Collections.Generic;
using System.Drawing;
using System.Threading;
using System.Windows.Forms;
using HotkeyHound.Core;

namespace HotkeyHound.Ui
{
    /// <summary>
    /// 钩子检测（引擎 C）。
    /// 铁律：本页加载时绝不安装钩子——HookSniffer.Start() 只在用户点「布防」时调用。
    /// 本页只记录与判定，绝不结束或暂停任何进程。
    ///
    /// 2.0.0：
    /// · 布防时若引擎 B 未在监听，先征得同意一并启动（否则「截胡」结论必然误报，
    ///   判定逻辑会诚实降级为「无法判定」）；
    /// · 钩子安装/引擎 B 启动都放到后台线程，界面不再冻结最长 5 秒；
    /// · 顶部说明区改为自动换行堆栈，任何宽度下文字都不截断。
    /// </summary>
    public sealed class HookCheckTab : UserControl
    {
        private readonly MainForm _host;
        private readonly Button _captureButton;
        private readonly Button _armButton;
        private readonly Button _stopButton;
        private readonly TextBox _comboBox;
        private readonly Label _statusLabel;
        private readonly ListView _list;
        private readonly ListBox _candidates;
        private readonly ListBox _comparisons;
        private readonly System.Windows.Forms.Timer _tick;

        /// <summary>捕获组合键时临时使用的低层键盘钩子；捕获到一次就立刻卸载。</summary>
        private HookSniffer _capture;
        private Action<string> _captureHandler;

        private volatile bool _busy;

        public HookCheckTab(MainForm host)
        {
            _host = host;

            // ------------------------------------------------------------ 顶部：自动换行堆栈
            var top = UiTheme.NewStack();

            var title = new Label();
            title.Text = L10n.T("ui.hk.title");
            title.Font = UiTheme.TitleFont(14F);
            title.AutoSize = true;
            title.Margin = new Padding(0, 2, 0, 4);

            var guide = UiTheme.StackLabel(L10n.T("ui.hk.guide"), UiTheme.Warning);

            var inputRow = UiTheme.NewFlowRow();
            var comboCaption = new Label();
            comboCaption.Text = L10n.T("ui.hk.comboCaption");
            comboCaption.AutoSize = true;
            comboCaption.Anchor = AnchorStyles.None;
            comboCaption.Margin = new Padding(0, 8, 2, 0);

            _comboBox = new TextBox();
            _comboBox.Width = 200;
            _comboBox.Text = "Ctrl+Alt+M";
            // 组合键用钩子捕获，不再手输：手输容易少写修饰键，也记不住 Win/Alt 的写法。
            _comboBox.ReadOnly = true;
            _comboBox.Margin = new Padding(0, 4, 8, 4);
            try { _comboBox.Font = UiTheme.MonoFont(10F); }
            catch (Exception) { }

            _captureButton = new Button();
            _captureButton.Text = L10n.T("ui.hk.capture");
            _captureButton.Size = new Size(150, 38);
            _captureButton.Margin = new Padding(0, 2, 8, 2);
            _captureButton.Click += OnCaptureClick;

            _armButton = new Button();
            _armButton.Text = L10n.T("ui.hk.arm");
            _armButton.Font = UiTheme.TitleFont(10F);
            _armButton.Size = new Size(170, 38);
            _armButton.Margin = new Padding(0, 2, 8, 2);
            _armButton.Click += OnArmClick;
            UiTheme.MarkPrimary(_armButton);

            _stopButton = new Button();
            _stopButton.Text = L10n.T("ui.hk.stop");
            _stopButton.Size = new Size(160, 38);
            _stopButton.Margin = new Padding(0, 2, 8, 2);
            _stopButton.Click += OnStopClick;

            inputRow.Controls.Add(comboCaption);
            inputRow.Controls.Add(_comboBox);
            inputRow.Controls.Add(_captureButton);
            inputRow.Controls.Add(_armButton);
            inputRow.Controls.Add(_stopButton);

            _statusLabel = UiTheme.StackLabel(L10n.T("ui.hk.notArmed"), UiTheme.Muted);

            var warning = UiTheme.StackLabel(L10n.T("ui.hk.warn"), UiTheme.Muted);

            UiTheme.StackAdd(top, title);
            UiTheme.StackAdd(top, guide);
            UiTheme.StackAdd(top, inputRow);
            UiTheme.StackAdd(top, _statusLabel);
            UiTheme.StackAdd(top, warning);

            // ------------------------------------------------------------ 结论列表
            _list = new ListView();
            _list.Dock = DockStyle.Fill;
            _list.View = View.Details;
            _list.FullRowSelect = true;
            _list.GridLines = true;
            _list.HideSelection = false;
            _list.Columns.Add(L10n.T("ui.at.col.time"), 70);
            _list.Columns.Add(L10n.T("ui.col.combo"), 110);
            _list.Columns.Add(L10n.T("ui.hk.col.verdict"), 110);
            _list.Columns.Add(L10n.T("ui.col.evidence"), 180);
            _list.Columns.Add(L10n.T("ui.hk.col.note"), 200);
            UiTheme.FillColumn(_list, 4, 240);

            // ------------------------------------------------------------ 右侧：对照排除（可滚动堆栈）
            var side = new Panel();
            side.Dock = DockStyle.Right;
            side.Width = 300;
            side.Padding = new Padding(6, 0, 0, 0);
            side.AutoScroll = true;

            var sideStack = new TableLayoutPanel();
            sideStack.Dock = DockStyle.Top;
            sideStack.AutoSize = true;
            sideStack.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            sideStack.ColumnCount = 1;
            sideStack.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            sideStack.RowCount = 6;
            for (int i = 0; i < 6; i++) sideStack.RowStyles.Add(new RowStyle(SizeType.AutoSize));

            var candTitle = new Label();
            candTitle.Text = L10n.T("ui.hk.candTitle");
            candTitle.Font = UiTheme.TitleFont(9.5F);
            candTitle.AutoSize = true;
            candTitle.Dock = DockStyle.Fill;
            candTitle.Margin = new Padding(0, 4, 0, 2);

            var refreshRow = UiTheme.NewFlowRow();
            var refresh = new Button();
            refresh.Text = L10n.T("ui.hk.refresh");
            refresh.Size = new Size(160, 28);
            refresh.Margin = new Padding(0, 0, 0, 4);
            refresh.Click += delegate (object s, EventArgs e) { RefreshCandidates(); };
            refreshRow.Controls.Add(refresh);

            _candidates = new ListBox();
            _candidates.Dock = DockStyle.Fill;
            _candidates.IntegralHeight = false;
            _candidates.MinimumSize = new Size(0, 132);
            _candidates.Margin = new Padding(0, 0, 0, 6);

            var steps = UiTheme.StackLabel(L10n.T("ui.hk.steps"), UiTheme.Text);

            var recordRow = UiTheme.NewFlowRow();
            var record = new Button();
            record.Text = L10n.T("ui.hk.record");
            record.Size = new Size(160, 30);
            record.Margin = new Padding(0, 2, 0, 4);
            record.Click += OnRecordComparisonClick;
            recordRow.Controls.Add(record);

            _comparisons = new ListBox();
            _comparisons.Dock = DockStyle.Fill;
            _comparisons.IntegralHeight = false;
            _comparisons.MinimumSize = new Size(0, 120);
            _comparisons.Margin = new Padding(0, 0, 0, 2);

            sideStack.Controls.Add(candTitle, 0, 0);
            sideStack.Controls.Add(refreshRow, 0, 1);
            sideStack.Controls.Add(_candidates, 0, 2);
            sideStack.Controls.Add(steps, 0, 3);
            sideStack.Controls.Add(recordRow, 0, 4);
            sideStack.Controls.Add(_comparisons, 0, 5);
            side.Controls.Add(sideStack);

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
            // 判定的两个前提（2.0.0）：结算时引擎 B 是否在监听、占用表是否存在。
            // 缺任何一个，「疑似被截胡」都必须降级为「无法判定」，绝不冤枉用户机器上的软件。
            _host.Sniffer.AttributionActive = delegate () { return _host.Attribution != null && _host.Attribution.IsRunning; };
            _host.Sniffer.ScanDone = delegate () { return _host.Snapshot != null; };

            _tick = new System.Windows.Forms.Timer();
            _tick.Interval = 200;
            _tick.Tick += OnTick;
        }

        // ------------------------------------------------------------ 组合键捕获

        private void OnCaptureClick(object sender, EventArgs e)
        {
            // 再点一次＝取消捕获：不要留着一个静默运行的键盘钩子。
            if (_capture != null)
            {
                StopCapture();
                SetStatus(UiTheme.Muted, L10n.T("ui.hk.notArmed"));
                return;
            }

            var sniffer = new HookSniffer();
            _captureHandler = delegate (string text)
            {
                if (IsDisposed) return;
                UiPost.Post(this, delegate () { ApplyCapturedCombo(text); });
            };
            sniffer.KeySeen += _captureHandler;

            bool ok = false;
            string error = null;
            try
            {
                ok = sniffer.Start();
                if (!ok) error = sniffer.LastError;
            }
            catch (Exception ex) { error = ex.Message; }

            if (!ok)
            {
                sniffer.KeySeen -= _captureHandler;
                _captureHandler = null;
                try { sniffer.Dispose(); }
                catch (Exception) { }
                SetStatus(UiTheme.Error, L10n.F("ui.hk.captureFailed", error ?? L10n.T("common.unknown")));
                return;
            }

            _capture = sniffer;
            SetStatus(UiTheme.Warning, L10n.T("ui.hk.capturing"));
        }

        private void ApplyCapturedCombo(string text)
        {
            StopCapture();
            if (string.IsNullOrEmpty(text)) return;
            _comboBox.Text = text;
            SetStatus(UiTheme.Muted, L10n.F("ui.hk.captured", text));
        }

        /// <summary>供主窗口关闭时调用，确保捕获用的钩子不会留着。</summary>
        public void StopCaptureIfActive()
        {
            StopCapture();
        }

        private void StopCapture()
        {
            var sniffer = _capture;
            _capture = null;
            if (sniffer == null) return;
            if (_captureHandler != null)
            {
                try { sniffer.KeySeen -= _captureHandler; }
                catch (Exception) { }
                _captureHandler = null;
            }
            try { sniffer.Stop(); }
            catch (Exception) { }
            try { sniffer.Dispose(); }
            catch (Exception) { }
        }

        private void SetStatus(Color color, string text)
        {
            if (IsDisposed || _statusLabel == null) return;
            _statusLabel.ForeColor = color;
            _statusLabel.Text = text;
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
            if (_busy) return;

            KeyCombo combo = KeyCombo.Parse(_comboBox.Text);
            if (combo == null)
            {
                MessageBox.Show(this, L10n.T("ui.hk.parseFail"),
                                AppInfo.Title, MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            // 引擎 B 没在监听时，「截胡」与「正常送达」无法区分——先征得同意联动启动。
            bool startB = false;
            if (!_host.Attribution.IsRunning)
            {
                DialogResult answer = MessageBox.Show(this, L10n.T("ui.hk.needB.body"),
                    L10n.T("ui.hk.needB.title"), MessageBoxButtons.YesNo, MessageBoxIcon.Question);
                startB = answer == DialogResult.Yes;
            }

            _busy = true;
            _armButton.Enabled = false;
            _stopButton.Enabled = false;
            SetStatus(UiTheme.Warning, startB ? L10n.T("ui.hk.engineB.starting") : L10n.T("ui.hk.installing"));

            HookSniffer sniffer = _host.Sniffer;
            bool wantB = startB;
            var thread = new Thread(delegate ()
            {
                bool okB = true;
                string errB = null;
                try
                {
                    if (wantB && !_host.Attribution.IsRunning)
                    {
                        okB = _host.Attribution.Start();
                        if (!okB) errB = _host.Attribution.LastError;
                    }
                }
                catch (Exception ex) { okB = false; errB = ex.Message; }

                bool okC = false;
                string errC = null;
                try
                {
                    okC = sniffer.IsRunning || sniffer.Start();
                    if (okC) okC = sniffer.Arm(combo);
                    if (!okC) errC = sniffer.LastError;
                }
                catch (Exception ex) { okC = false; errC = ex.Message; }

                try
                {
                    UiPost.Post(this, delegate () { OnArmFinished(combo, wantB, okB, errB, okC, errC); });
                }
                catch (Exception) { }
            });
            thread.IsBackground = true;
            thread.Name = "HotkeyHound-HookCheckArm";
            thread.Start();
        }

        private void OnArmFinished(KeyCombo combo, bool wantB, bool okB, string errB, bool okC, string errC)
        {
            if (IsDisposed) return;
            _busy = false;
            _armButton.Enabled = true;
            _stopButton.Enabled = true;

            if (wantB)
            {
                if (okB) _host.Log.Write(L10n.T("ui.hk.engineB.coStarted"));
                else _host.Log.Write(L10n.F("ui.hk.engineB.failed", errB ?? L10n.T("common.unknown")));
            }

            if (!okC)
            {
                SetStatus(UiTheme.Error, L10n.F("ui.hk.installFail", errC ?? L10n.T("common.unknown")));
                MessageBox.Show(this, L10n.F("ui.hk.installFailDlg", errC ?? L10n.T("common.unknown")),
                                AppInfo.Title, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            if (!wantB || okB)
            {
                SetStatus(UiTheme.Success, L10n.F("ui.hk.armed", combo.Display(), HookSniffer.ArmWindow.TotalSeconds));
            }
            else
            {
                SetStatus(UiTheme.Warning, L10n.F("ui.hk.armed", combo.Display(), HookSniffer.ArmWindow.TotalSeconds)
                                           + " " + L10n.F("ui.hk.engineB.failed", errB ?? L10n.T("common.unknown")));
            }
            RefreshCandidates();
            _tick.Start();
        }

        private void OnStopClick(object sender, EventArgs e)
        {
            HookSniffer sniffer = _host.Sniffer;
            if (sniffer.IsRunning) sniffer.Stop();
            if (_tick.Enabled) _tick.Stop();
            SetStatus(UiTheme.Muted, L10n.T("ui.hk.stopped"));
        }

        /// <summary>回调来自钩子线程／定时器线程，必须异步回到 UI 线程。</summary>
        private void OnVerdictReady(HookCheckRecord record)
        {
            if (record == null) return;
            SessionRecords.AddHookRecord(record);
            if (IsDisposed) return;
            UiPost.Post(this, delegate () { AppendVerdict(record); });
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
                    case HookVerdict.Inconclusive: item.ForeColor = UiTheme.Warning; break;
                    default: item.ForeColor = UiTheme.Muted; break;
                }
                _list.Items.Add(item);
                item.EnsureVisible();
                UiTheme.TrackRow(_list, item);
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
