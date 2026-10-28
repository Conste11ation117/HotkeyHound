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
    /// 用户点「开始监听」或确认「自动归属」时才被调用，停止时立刻 UnhookWindowsHookEx。
    /// 2.0.0：顶部说明区改为自动换行堆栈（窄窗口/任何宽度都不截断），
    /// 路径列与说明列吃满剩余宽度；业务逻辑与 v1.3.1 完全一致。
    /// </summary>
    public sealed class AttributionTab : UserControl
    {
        private readonly MainForm _host;
        private readonly Button _toggleButton;
        private readonly Button _clearButton;
        private readonly Button _copyButton;
        private readonly Button _autoButton;
        private readonly Label _statusLabel;
        private readonly Label _autoStatus;
        private readonly ListView _list;
        private readonly ListView _autoList;

        private volatile bool _autoRunning;
        private volatile bool _autoAbort;
        private int _autoPlanned;

        public AttributionTab(MainForm host)
        {
            _host = host;

            // ------------------------------------------------------------ 顶部：自动换行堆栈
            var top = UiTheme.NewStack();

            var title = new Label();
            title.Text = L10n.T("ui.at.title");
            title.Font = UiTheme.TitleFont(14F);
            title.AutoSize = true;
            title.Margin = new Padding(0, 2, 0, 4);

            var hint = UiTheme.StackLabel(L10n.T("ui.at.hint"), UiTheme.Muted);
            var missHint = UiTheme.StackLabel(L10n.T("ui.at.missHint"), UiTheme.Muted);

            var buttonRow = UiTheme.NewFlowRow();

            _toggleButton = new Button();
            _toggleButton.Text = L10n.T("ui.at.start");
            _toggleButton.Font = UiTheme.TitleFont(11F);
            _toggleButton.Size = new Size(150, 40);
            _toggleButton.Margin = new Padding(0, 2, 10, 2);
            _toggleButton.Click += OnToggleClick;

            _clearButton = new Button();
            _clearButton.Text = L10n.T("ui.at.clear");
            _clearButton.Size = new Size(90, 40);
            _clearButton.Margin = new Padding(0, 2, 10, 2);
            _clearButton.Click += OnClearClick;

            _copyButton = new Button();
            _copyButton.Text = L10n.T("ui.at.copy");
            _copyButton.Size = new Size(130, 40);
            _copyButton.Margin = new Padding(0, 2, 10, 2);
            _copyButton.Click += OnCopyClick;

            buttonRow.Controls.Add(_toggleButton);
            buttonRow.Controls.Add(_clearButton);
            buttonRow.Controls.Add(_copyButton);

            _statusLabel = UiTheme.StackLabel(L10n.T("ui.at.notStarted"), UiTheme.Muted);

            var autoRow = UiTheme.NewFlowRow();
            _autoButton = new Button();
            _autoButton.Text = L10n.T("ui.at.autoButton");
            _autoButton.Size = new Size(480, 34);
            _autoButton.Margin = new Padding(0, 2, 10, 2);
            _autoButton.Click += OnAutoClick;
            autoRow.Controls.Add(_autoButton);

            _autoStatus = UiTheme.StackLabel("", UiTheme.Muted);
            // 空状态标签不该占一整行（否则开始按钮和结果表之间会出现一块无意义空白）。
            _autoStatus.Visible = false;
            _autoStatus.TextChanged += delegate
            {
                bool show = !string.IsNullOrEmpty(_autoStatus.Text);
                if (_autoStatus.Visible != show) _autoStatus.Visible = show;
            };

            UiTheme.StackAdd(top, title);
            UiTheme.StackAdd(top, hint);
            UiTheme.StackAdd(top, missHint);
            UiTheme.StackAdd(top, buttonRow);
            UiTheme.StackAdd(top, _statusLabel);
            UiTheme.StackAdd(top, autoRow);
            UiTheme.StackAdd(top, _autoStatus);

            // ------------------------------------------------------------ 手动记录列表
            _list = new ListView();
            _list.Dock = DockStyle.Fill;
            _list.View = View.Details;
            _list.FullRowSelect = true;
            _list.GridLines = true;
            _list.HideSelection = false;
            _list.Columns.Add(L10n.T("ui.at.col.time"), 90);
            _list.Columns.Add(L10n.T("ui.at.col.process"), 170);
            _list.Columns.Add(L10n.T("ui.at.col.pid"), 70);
            _list.Columns.Add(L10n.T("ui.at.col.bitness"), 80);
            _list.Columns.Add(L10n.T("ui.col.combo"), 160);
            _list.Columns.Add(L10n.T("ui.col.path"), 300);
            _list.Columns.Add(L10n.T("ui.at.col.elevated"), 96);
            // 路径列吃满剩余宽度（2.0.0：修最大化时截断）。
            UiTheme.FillColumn(_list, 5, 200);

            var autoHeader = new Label();
            autoHeader.Dock = DockStyle.Top;
            autoHeader.Height = 24;
            autoHeader.AutoSize = false;
            autoHeader.Text = L10n.T("ui.at.autoListTitle");
            autoHeader.ForeColor = UiTheme.Muted;
            autoHeader.TextAlign = ContentAlignment.MiddleLeft;

            // ------------------------------------------------------------ 自动归属结果
            _autoList = new ListView();
            _autoList.Dock = DockStyle.Fill;
            _autoList.View = View.Details;
            _autoList.FullRowSelect = true;
            _autoList.GridLines = true;
            _autoList.HideSelection = false;
            _autoList.Columns.Add(L10n.T("ui.col.combo"), 160);
            _autoList.Columns.Add(L10n.T("ui.at.autoCol.outcome"), 100);
            _autoList.Columns.Add(L10n.T("ui.at.autoCol.owner"), 180);
            _autoList.Columns.Add(L10n.T("ui.at.autoCol.note"), 460);
            UiTheme.FillColumn(_autoList, 3, 220);

            var split = new SplitContainer();
            split.Dock = DockStyle.Fill;
            split.Orientation = Orientation.Horizontal;
            split.FixedPanel = FixedPanel.Panel2;
            split.Panel2MinSize = 110;
            split.Panel1.Controls.Add(_list);
            split.Panel2.Controls.Add(_autoList);
            split.Panel2.Controls.Add(autoHeader);
            split.SizeChanged += delegate (object s, EventArgs e)
            {
                try
                {
                    int wanted = Math.Max(140, split.Height / 3);
                    if (split.Height - wanted >= split.Panel2MinSize) split.SplitterDistance = split.Height - wanted;
                }
                catch (Exception) { }
            };

            Controls.Add(split);
            Controls.Add(top);

            _host.Attribution.RecordReceived += OnRecordReceived;
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
                item.SubItems.Add(string.IsNullOrEmpty(record.Bitness) ? "?" : record.Bitness);
                item.SubItems.Add(record.Combo == null ? L10n.T("attr.unknownCombo") : record.Combo.Display());
                item.SubItems.Add(string.IsNullOrEmpty(record.ProcessPath) ? L10n.T("ui.at.unknownPath") : record.ProcessPath);
                item.SubItems.Add(record.IsElevated ? L10n.T("common.yes") : L10n.T("common.no"));
                if (record.IsElevated) item.ForeColor = UiTheme.Warning;
                _list.Items.Add(item);
                item.EnsureVisible();
                _statusLabel.Text = L10n.F("ui.at.captured", _list.Items.Count);
            }
            catch (Exception) { }
        }

        private void OnToggleClick(object sender, EventArgs e)
        {
            if (_autoRunning)
            {
                _autoAbort = true;
                return;
            }
            AttributionEngine engine = _host.Attribution;
            if (engine.IsRunning)
            {
                engine.Stop();
                _toggleButton.Text = L10n.T("ui.at.start");
                _statusLabel.ForeColor = UiTheme.Muted;
                _statusLabel.Text = L10n.T("ui.at.stopped");
                return;
            }

            _toggleButton.Enabled = false;
            _statusLabel.ForeColor = UiTheme.Warning;
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
                _statusLabel.ForeColor = UiTheme.Success;
                _statusLabel.Text = L10n.T("ui.at.listening")
                                    + L10n.F("ui.at.helper32", engine.Helper32.StatusText);
            }
            else
            {
                _toggleButton.Text = L10n.T("ui.at.start");
                _statusLabel.ForeColor = UiTheme.Error;
                _statusLabel.Text = L10n.F("ui.at.startFailed", error ?? engine.LastError ?? L10n.T("common.unknown"));
                MessageBox.Show(this,
                    L10n.F("ui.at.startFailedDetail", error ?? engine.LastError),
                    AppInfo.Title, MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        private void OnAutoClick(object sender, EventArgs e)
        {
            if (_autoRunning)
            {
                _autoAbort = true;
                _autoStatus.Text = L10n.T("ui.at.autoAborting");
                return;
            }

            ScanSnapshot snapshot = _host.Snapshot;
            if (snapshot == null || snapshot.Occupancy == null)
            {
                MessageBox.Show(this, L10n.T("ui.at.autoNeedScan"), AppInfo.Title,
                                MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            DialogResult answer = MessageBox.Show(this, L10n.T("ui.at.autoConfirmBody"),
                L10n.T("ui.at.autoConfirmTitle"), MessageBoxButtons.YesNo, MessageBoxIcon.Warning,
                MessageBoxDefaultButton.Button2);
            if (answer != DialogResult.Yes) return;

            _autoRunning = true;
            _autoAbort = false;
            _autoPlanned = AutoProbe.Plan(snapshot.Occupancy, AutoProbe.DefaultMax).Plan.Count;
            _autoList.Items.Clear();
            _autoButton.Text = L10n.T("ui.at.autoButtonStop");
            _autoStatus.ForeColor = UiTheme.Warning;
            _autoStatus.Text = L10n.T("ui.at.autoStarting");
            _toggleButton.Enabled = false;

            var thread = new Thread(delegate ()
            {
                bool startedHere = false;
                bool ok = true;
                string error = null;
                AutoProbeResult result = null;
                try
                {
                    AttributionEngine engine = _host.Attribution;
                    if (!engine.IsRunning)
                    {
                        startedHere = true;
                        if (!engine.Start())
                        {
                            ok = false;
                            error = engine.LastError;
                        }
                    }
                    if (ok)
                    {
                        result = AutoProbe.Run(engine, snapshot.Occupancy, AutoProbe.DefaultMax, _host.Log,
                            delegate () { return _autoAbort; },
                            delegate (AutoProbeRecord record)
                            {
                                if (IsDisposed) return;
                                try { BeginInvoke(new Action<AutoProbeRecord>(AppendAutoRecord), record); }
                                catch (Exception) { }
                            });
                    }
                }
                catch (Exception ex)
                {
                    ok = false;
                    error = ex.Message;
                }
                finally
                {
                    if (startedHere)
                    {
                        try { _host.Attribution.Stop(); } catch (Exception) { }
                    }
                }
                try
                {
                    BeginInvoke(new Action(delegate () { OnAutoFinished(ok, error, result); }));
                }
                catch (Exception) { }
            });
            thread.IsBackground = true;
            thread.Name = "HotkeyHound-AutoProbe";
            thread.Start();
        }

        private void AppendAutoRecord(AutoProbeRecord record)
        {
            if (IsDisposed || record == null) return;
            try
            {
                var item = new ListViewItem(record.Combo == null ? L10n.T("common.none") : record.Combo.Display());
                item.SubItems.Add(AutoProbeOutcomeName(record.Outcome));
                item.SubItems.Add(record.Outcome == AutoProbeOutcome.Attributed && !string.IsNullOrEmpty(record.ProcessName)
                    ? record.ProcessName + " (PID " + record.Pid + ")"
                    : L10n.T("ui.at.autoNoOwner"));
                item.SubItems.Add(record.Note);
                switch (record.Outcome)
                {
                    case AutoProbeOutcome.Attributed: item.ForeColor = UiTheme.Success; break;
                    case AutoProbeOutcome.NoResponse: item.ForeColor = UiTheme.Error; break;
                    default: item.ForeColor = UiTheme.Muted; break;
                }
                _autoList.Items.Add(item);
                item.EnsureVisible();
                _autoStatus.Text = L10n.F("ui.at.autoProgress", _autoList.Items.Count, _autoPlanned,
                    record.Combo == null ? "" : record.Combo.Display());
            }
            catch (Exception) { }
        }

        private static string AutoProbeOutcomeName(AutoProbeOutcome outcome)
        {
            switch (outcome)
            {
                case AutoProbeOutcome.Attributed: return L10n.T("auto.outcome.attributed");
                case AutoProbeOutcome.NoResponse: return L10n.T("auto.outcome.noResponse");
                default: return L10n.T("auto.outcome.skipped");
            }
        }

        private void OnAutoFinished(bool ok, string error, AutoProbeResult result)
        {
            if (IsDisposed) return;
            _autoRunning = false;
            _autoButton.Text = L10n.T("ui.at.autoButton");
            _toggleButton.Enabled = true;
            if (!ok)
            {
                _autoStatus.ForeColor = UiTheme.Error;
                _autoStatus.Text = L10n.F("ui.at.autoFailed", error ?? L10n.T("common.unknown"));
                return;
            }
            if (result == null) return;
            _autoStatus.ForeColor = UiTheme.Success;
            _autoStatus.Text = L10n.F("ui.at.autoDone",
                result.CountOf(AutoProbeOutcome.Attributed),
                result.CountOf(AutoProbeOutcome.NoResponse),
                result.SkippedTotal);
            if (result.Aborted) _autoStatus.Text += "　" + L10n.T("ui.at.autoAborted");
        }

        private void OnClearClick(object sender, EventArgs e)
        {
            if (_autoRunning) return;
            _host.Attribution.Clear();
            SessionRecords.ClearAttribution();
            SessionRecords.ClearAutoProbe();
            _list.Items.Clear();
            _autoList.Items.Clear();
            _autoStatus.Text = "";
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
