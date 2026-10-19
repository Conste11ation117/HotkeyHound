using System;
using System.Collections.Generic;
using System.Drawing;
using System.Threading;
using System.Windows.Forms;
using HotkeyHound.Core;

namespace HotkeyHound.Ui
{
    /// <summary>
    /// 第 7 页：按键监视。
    /// 铁律：本页加载时绝不安装任何钩子——KeyMonitor.Start() 只在用户点「开始监视」时调用，
    /// 「停止监视」立刻 UnhookWindowsHookEx 并注销 Raw Input。本页只观察，不吞键、不结束进程。
    /// 2.0.0：顶部说明区改为自动换行堆栈；判定卡片与修饰键卡片改垂直堆栈，长文本不再被固定高度截断；
    /// 「说明」列吃满剩余宽度；列表开双缓冲。
    /// </summary>
    public sealed class KeyMonitorTab : UserControl
    {
        private readonly MainForm _host;
        private readonly KeyMonitor _monitor;
        private readonly Button _startButton;
        private readonly Button _stopButton;
        private readonly Button _clearButton;
        private readonly Button _refreshButton;
        private readonly Label _status;
        private Label _verdictTitle;
        private Label _verdictKey;
        private Label _verdictLayers;
        private Label _verdictEvidence;
        private Label _verdictNote;
        private Label _modStatus;
        private Label _modNote;
        private readonly ListView _events;
        private ListView _remap;
        private ListBox _drivers;

        private bool _busy;
        private WindowSubclass _windowSubclass;

        public KeyMonitorTab(MainForm host)
        {
            _host = host;
            _monitor = new KeyMonitor();
            _monitor.Logged += delegate (string line) { if (_host != null) _host.Log.Write(line); };
            _monitor.KeyEvent += OnKeyEvent;
            _monitor.VerdictReady += OnVerdictReady;
            _monitor.ModifiersChanged += OnModifiersChanged;

            // ------------------------------------------------------------ 顶部：自动换行堆栈
            var top = UiTheme.NewStack();

            var title = new Label();
            title.Text = L10n.T("ui.km.title");
            title.Font = UiTheme.TitleFont(11F);
            title.AutoSize = true;
            title.Margin = new Padding(0, 2, 0, 2);

            var hint = UiTheme.StackLabel(L10n.T("ui.km.hint"), UiTheme.Muted);

            _status = UiTheme.StackLabel(L10n.T("ui.km.notStarted"), UiTheme.Muted);

            var buttonRow = UiTheme.NewFlowRow();

            _startButton = new Button();
            _startButton.Text = L10n.T("ui.km.start");
            _startButton.Font = UiTheme.TitleFont(9.5F);
            _startButton.Size = new Size(300, 36);
            _startButton.Margin = new Padding(0, 2, 10, 2);
            _startButton.Click += OnStartClick;
            UiTheme.MarkPrimary(_startButton);

            _stopButton = new Button();
            _stopButton.Text = L10n.T("ui.km.stop");
            _stopButton.Size = new Size(200, 36);
            _stopButton.Margin = new Padding(0, 2, 10, 2);
            _stopButton.Enabled = false;
            _stopButton.Click += OnStopClick;

            _clearButton = new Button();
            _clearButton.Text = L10n.T("ui.km.clear");
            _clearButton.Size = new Size(96, 36);
            _clearButton.Margin = new Padding(0, 2, 10, 2);
            _clearButton.Click += OnClearClick;

            _refreshButton = new Button();
            _refreshButton.Text = L10n.T("ui.km.refresh");
            _refreshButton.Size = new Size(140, 36);
            _refreshButton.Margin = new Padding(0, 2, 0, 2);
            _refreshButton.Click += delegate (object s, EventArgs e) { RefreshReadOnly(); };

            buttonRow.Controls.Add(_startButton);
            buttonRow.Controls.Add(_stopButton);
            buttonRow.Controls.Add(_clearButton);
            buttonRow.Controls.Add(_refreshButton);

            UiTheme.StackAdd(top, title);
            UiTheme.StackAdd(top, hint);
            UiTheme.StackAdd(top, _status);
            UiTheme.StackAdd(top, buttonRow);

            // ------------------------------------------------------------ 事件列表
            _events = new ListView();
            _events.Dock = DockStyle.Fill;
            _events.View = View.Details;
            _events.FullRowSelect = true;
            _events.GridLines = false;
            _events.HideSelection = false;
            _events.Columns.Add(L10n.T("ui.km.col.time"), 108);
            _events.Columns.Add(L10n.T("ui.km.col.layer"), 110);
            _events.Columns.Add(L10n.T("ui.km.col.key"), 130);
            _events.Columns.Add(L10n.T("ui.km.col.scancode"), 90);
            _events.Columns.Add(L10n.T("ui.km.col.flags"), 160);
            _events.Columns.Add(L10n.T("ui.km.col.desc"), 360);
            // 说明列吃满剩余宽度（2.0.0：修最大化时截断）。
            UiTheme.FillColumn(_events, 5, 220);

            // 事件列表与详情区：详情区贴底，高度按「页面高度 - 顶部说明高度 - 事件列表最小高度」算，
            // 保证任何窗口尺寸下两边都看得见、不会互相压住。
            var details = new SplitContainer();
            details.Dock = DockStyle.Bottom;
            details.Height = 240;
            details.Orientation = Orientation.Vertical;
            details.FixedPanel = FixedPanel.Panel1;
            // 构造时容器还没实际宽度，不能设置大的 MinSize / SplitterDistance（会抛异常）。
            details.SizeChanged += delegate (object s, EventArgs e)
            {
                try
                {
                    if (details.Width < 500) return;
                    int wanted = (int)(details.Width * 0.42);
                    if (wanted < 300) wanted = 300;
                    if (wanted > 460) wanted = 460;
                    if (wanted > details.Width - 340) wanted = Math.Max(280, details.Width - 340);
                    details.SplitterDistance = wanted;
                }
                catch (Exception) { }
            };
            details.Panel1.Controls.Add(BuildVerdictPanel());
            details.Panel2.Controls.Add(BuildReadOnlyPanel());

            SizeChanged += delegate (object s, EventArgs e)
            {
                try
                {
                    int topHeight = top.Height;
                    int avail = ClientSize.Height - topHeight;
                    int h = avail - 130;
                    if (h < 200) h = 200;
                    if (h > 420) h = 420;
                    if (avail - h < 90) h = Math.Max(120, avail - 90);
                    if (details.Height != h) details.Height = h;
                }
                catch (Exception) { }
            };

            Controls.Add(_events);
            Controls.Add(details);
            Controls.Add(top);

            RefreshReadOnly();
            ShowIdleVerdict();
            UpdateModifierLabel(null);
        }

        public KeyMonitor Monitor { get { return _monitor; } }

        private Control BuildVerdictPanel()
        {
            // 卡片按内容自动增高，修饰键框吃掉剩余高度——判定文字再长也不会被裁掉，
            // 也不会压到下面的框里。整块放进可滚动面板：窗口太矮时滚动查看，而不是被切掉。
            var host = new Panel();
            host.Dock = DockStyle.Fill;
            host.AutoScroll = true;

            var layout = new TableLayoutPanel();
            layout.Dock = DockStyle.Top;
            layout.AutoSize = true;
            layout.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            layout.ColumnCount = 1;
            layout.RowCount = 2;
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));

            var card = new CardPanel();
            card.Dock = DockStyle.Top;
            card.AutoSize = true;
            card.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            card.Margin = new Padding(0, 0, 0, 4);

            var stack = new TableLayoutPanel();
            stack.Dock = DockStyle.Top;
            stack.AutoSize = true;
            stack.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            stack.BackColor = Color.Transparent;
            stack.ColumnCount = 1;
            stack.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            stack.RowCount = 5;
            for (int i = 0; i < 5; i++) stack.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            stack.Padding = new Padding(UiTheme.SpaceS);

            _verdictTitle = new Label();
            _verdictTitle.AutoSize = true;
            _verdictTitle.Dock = DockStyle.Top;
            _verdictTitle.Margin = new Padding(0, 0, 0, 4);
            _verdictTitle.Font = UiTheme.TitleFont(12F);
            _verdictTitle.Text = L10n.T("ui.km.verdict.title");

            _verdictKey = UiTheme.StackLabel("", UiTheme.Text);
            _verdictLayers = UiTheme.StackLabel("", UiTheme.Muted);
            _verdictEvidence = UiTheme.StackLabel("", UiTheme.Muted);
            _verdictNote = UiTheme.StackLabel("", UiTheme.Text);

            stack.Controls.Add(_verdictTitle, 0, 0);
            stack.Controls.Add(_verdictKey, 0, 1);
            stack.Controls.Add(_verdictLayers, 0, 2);
            stack.Controls.Add(_verdictEvidence, 0, 3);
            stack.Controls.Add(_verdictNote, 0, 4);
            card.Controls.Add(stack);

            var modBox = new GroupBox();
            modBox.Dock = DockStyle.Fill;
            modBox.Margin = new Padding(0);
            modBox.Text = L10n.T("ui.km.mod.title");
            modBox.MinimumSize = new Size(0, 96);
            modBox.Padding = new Padding(UiTheme.SpaceS);

            var modStack = new TableLayoutPanel();
            modStack.Dock = DockStyle.Fill;
            modStack.BackColor = Color.Transparent;
            modStack.ColumnCount = 1;
            modStack.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            modStack.RowCount = 2;
            modStack.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            modStack.RowStyles.Add(new RowStyle(SizeType.AutoSize));

            _modStatus = UiTheme.StackLabel("", UiTheme.Muted);
            _modNote = UiTheme.StackLabel(L10n.T("ui.km.mod.note"), UiTheme.Muted);

            modStack.Controls.Add(_modStatus, 0, 0);
            modStack.Controls.Add(_modNote, 0, 1);
            modBox.Controls.Add(modStack);

            layout.Controls.Add(card, 0, 0);
            layout.Controls.Add(modBox, 0, 1);
            host.Controls.Add(layout);
            return host;
        }

        private Control BuildReadOnlyPanel()
        {
            var split = new SplitContainer();
            split.Dock = DockStyle.Fill;
            split.Orientation = Orientation.Horizontal;
            split.SizeChanged += delegate (object s, EventArgs e)
            {
                try
                {
                    if (split.Height < 150) return;
                    int wanted = split.Height / 2;
                    if (wanted < 64) wanted = 64;
                    if (wanted > 110) wanted = 110;
                    split.SplitterDistance = wanted;
                }
                catch (Exception) { }
            };

            var remapBox = new GroupBox();
            remapBox.Dock = DockStyle.Fill;
            remapBox.Text = L10n.T("ui.km.remap.title");
            remapBox.Padding = new Padding(UiTheme.SpaceS);

            _remap = new ListView();
            _remap.Dock = DockStyle.Fill;
            _remap.View = View.Details;
            _remap.FullRowSelect = true;
            _remap.GridLines = false;
            _remap.HideSelection = false;
            _remap.Columns.Add(L10n.T("ui.km.remap.input"), 170);
            _remap.Columns.Add(L10n.T("ui.km.remap.output"), 170);
            _remap.Columns.Add(L10n.T("ui.km.col.scancode"), 120);
            UiTheme.FillColumn(_remap, 2, 90);
            remapBox.Controls.Add(_remap);

            var driverBox = new GroupBox();
            driverBox.Dock = DockStyle.Fill;
            driverBox.Text = L10n.T("ui.km.drivers.title");
            driverBox.Padding = new Padding(UiTheme.SpaceS);

            _drivers = new ListBox();
            _drivers.Dock = DockStyle.Fill;
            _drivers.IntegralHeight = false;
            UiTheme.EnableDoubleBuffer(_drivers);
            driverBox.Controls.Add(_drivers);

            split.Panel1.Controls.Add(remapBox);
            split.Panel2.Controls.Add(driverBox);
            return split;
        }

        private void ShowIdleVerdict()
        {
            _verdictTitle.Text = L10n.T("ui.km.verdict.idle");
            _verdictTitle.ForeColor = UiTheme.Muted;
            _verdictKey.Text = "";
            _verdictLayers.Text = "";
            _verdictEvidence.Text = "";
            _verdictNote.Text = L10n.T("ui.km.verdict.idleNote");
            _verdictNote.ForeColor = UiTheme.Muted;
        }

        // ------------------------------------------------------------ 按钮

        private void OnStartClick(object sender, EventArgs e)
        {
            if (_busy || _monitor.IsRunning) return;
            _busy = true;
            _startButton.Enabled = false;
            _status.ForeColor = UiTheme.Warning;
            _status.Text = L10n.T("ui.km.starting");

            _monitor.IsGlobalHotkey = delegate (KeyCombo combo) { return CheckGlobalHotkey(combo); };
            _monitor.AttributionActive = delegate () { return _host.Attribution.IsRunning; };
            _monitor.AttributionLookup = delegate (KeyCombo combo, DateTime since)
            {
                AttributionRecord[] records = _host.Attribution.Records;
                for (int i = records.Length - 1; i >= 0; i--)
                {
                    AttributionRecord r = records[i];
                    if (r == null || r.Combo == null) continue;
                    if (r.Time < since) continue;
                    if (r.Combo.Normalize() == combo.Normalize()) return r;
                }
                return null;
            };

            var thread = new Thread(delegate ()
            {
                bool ok = false;
                string error = null;
                try { ok = _monitor.Start(); }
                catch (Exception ex) { error = ex.Message; }
                try { BeginInvoke(new Action(delegate () { OnStartFinished(ok, error); })); }
                catch (Exception) { }
            });
            thread.IsBackground = true;
            thread.Name = "HotkeyHound-KeyMonitorStart";
            thread.Start();
        }

        private void OnStartFinished(bool ok, string error)
        {
            if (IsDisposed) return;
            _busy = false;
            if (!ok)
            {
                _startButton.Enabled = true;
                _stopButton.Enabled = false;
                _status.ForeColor = UiTheme.Error;
                _status.Text = L10n.F("ui.km.startFailed", error ?? _monitor.LastError ?? L10n.T("common.unknown"));
                MessageBox.Show(this,
                    L10n.F("ui.km.startFailedDetail", error ?? _monitor.LastError ?? L10n.T("common.unknown")),
                    AppInfo.Title, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            // 窗口消息层必须在拥有窗口的 UI 线程上挂接。
            AttachWindowMessages();
            _stopButton.Enabled = true;
            _status.ForeColor = UiTheme.Success;
            _status.Text = L10n.F("ui.km.running",
                _monitor.RawReady ? L10n.T("ui.km.yes") : L10n.T("ui.km.no"),
                _monitor.HookReady ? L10n.T("ui.km.yes") : L10n.T("ui.km.no"));
            RefreshReadOnly();
        }

        private void OnStopClick(object sender, EventArgs e)
        {
            if (_busy) return;
            StopMonitor(true);
        }

        /// <summary>供主窗口关闭时调用；同步停止，确保钩子一定被卸载。</summary>
        public void StopMonitor(bool updateUi)
        {
            DetachWindowMessages();
            try { _monitor.Stop(); }
            catch (Exception) { }
            if (updateUi && !IsDisposed)
            {
                _busy = false;
                _startButton.Enabled = true;
                _stopButton.Enabled = false;
                _status.ForeColor = UiTheme.Muted;
                _status.Text = L10n.T("ui.km.stopped");
            }
        }

        private void OnClearClick(object sender, EventArgs e)
        {
            _events.Items.Clear();
            ShowIdleVerdict();
            if (!_monitor.IsRunning) _status.Text = L10n.T("ui.km.cleared");
        }

        private bool CheckGlobalHotkey(KeyCombo combo)
        {
            if (combo == null) return false;
            ScanSnapshot snapshot = _host.Snapshot;
            if (snapshot != null && snapshot.Occupancy != null)
            {
                foreach (OccupancyResult r in snapshot.Occupancy)
                {
                    if (r.Combo != null && r.Combo.Normalize() == combo.Normalize()) return r.Occupied;
                }
                return false;
            }
            if (!KeyMonitorLogic.LooksLikeGlobalHotkey(combo)) return false;
            uint[] mods = ModifiersOf(combo);
            uint err;
            return OccupancyScanner.Probe(mods, combo.Vk, out err);
        }

        private static uint[] ModifiersOf(KeyCombo combo)
        {
            var list = new List<uint>();
            if (combo.Alt) list.Add(NativeHotkey.MOD_ALT);
            if (combo.Ctrl) list.Add(NativeHotkey.MOD_CONTROL);
            if (combo.Shift) list.Add(NativeHotkey.MOD_SHIFT);
            if (combo.Win) list.Add(NativeHotkey.MOD_WIN);
            return list.ToArray();
        }

        // ------------------------------------------------------------ 事件回调（必须异步回 UI 线程）

        private void OnKeyEvent(KeyLayerEvent record)
        {
            if (record == null || IsDisposed) return;
            try { BeginInvoke(new Action<KeyLayerEvent>(AppendEvent), record); }
            catch (Exception) { }
        }

        private void OnVerdictReady(KeyMonitorVerdictRecord record)
        {
            if (record == null || IsDisposed) return;
            try { BeginInvoke(new Action<KeyMonitorVerdictRecord>(AppendVerdict), record); }
            catch (Exception) { }
        }

        private void OnModifiersChanged(ModifierSnapshot[] states)
        {
            if (IsDisposed) return;
            try { BeginInvoke(new Action<ModifierSnapshot[]>(UpdateModifierLabel), states); }
            catch (Exception) { }
        }

        private void AppendEvent(KeyLayerEvent record)
        {
            if (IsDisposed) return;
            try
            {
                var item = new ListViewItem(record.Time.ToString("HH:mm:ss.fff"));
                item.SubItems.Add(record.LayerName);
                item.SubItems.Add(record.KeyName);
                item.SubItems.Add("0x" + record.ScanCode.ToString("X2"));
                item.SubItems.Add(record.FlagsText);
                item.SubItems.Add(record.Description);
                item.UseItemStyleForSubItems = true;
                switch (record.Layer)
                {
                    case KeyMonitorLayer.RawInput: item.ForeColor = UiTheme.Muted; break;
                    case KeyMonitorLayer.LowLevelHook: item.ForeColor = UiTheme.Text; break;
                    default: item.ForeColor = UiTheme.Info; break;
                }
                _events.Items.Add(item);
                if (_events.Items.Count > 1200) _events.Items.RemoveAt(0);
                item.EnsureVisible();
            }
            catch (Exception) { }
        }

        private void AppendVerdict(KeyMonitorVerdictRecord record)
        {
            if (IsDisposed) return;
            try
            {
                _verdictTitle.Text = record.VerdictName;
                switch (record.Verdict)
                {
                    case KeyMonitorVerdict.Delivered: _verdictTitle.ForeColor = UiTheme.Success; break;
                    case KeyMonitorVerdict.Inconclusive: _verdictTitle.ForeColor = UiTheme.Warning; break;
                    case KeyMonitorVerdict.Unknown: _verdictTitle.ForeColor = UiTheme.Muted; break;
                    default: _verdictTitle.ForeColor = UiTheme.Error; break;
                }

                _verdictKey.Text = L10n.F("ui.km.verdict.key",
                    record.Combo == null ? VkNames.Name(record.Vk) : record.Combo.Display());
                _verdictKey.ForeColor = UiTheme.Text;
                _verdictLayers.Text = L10n.F("km.layers.value",
                    record.SeenRaw ? L10n.T("km.layers.yes") : L10n.T("km.layers.no"),
                    record.SeenHook ? L10n.T("km.layers.yes") : L10n.T("km.layers.no"),
                    record.SeenWindow ? L10n.T("km.layers.yes") : L10n.T("km.layers.no"));
                _verdictLayers.ForeColor = UiTheme.Muted;
                _verdictEvidence.Text = L10n.F("ui.km.verdict.evidence", record.Evidence);
                _verdictEvidence.ForeColor = UiTheme.Muted;
                _verdictNote.Text = L10n.F("ui.km.verdict.note", record.Note)
                    + (string.IsNullOrEmpty(record.Owner) ? "" : "\r\n" + L10n.F("ui.km.verdict.owner", record.Owner));
                _verdictNote.ForeColor = UiTheme.Text;
            }
            catch (Exception) { }
        }

        private void UpdateModifierLabel(ModifierSnapshot[] states)
        {
            if (IsDisposed || _modStatus == null) return;
            if (states == null)
            {
                _modStatus.Text = L10n.T("ui.km.mod.none");
                _modStatus.ForeColor = UiTheme.Muted;
                return;
            }
            var stuck = new List<string>();
            foreach (ModifierSnapshot s in states)
            {
                if (s != null && s.Stuck) stuck.Add(s.Name + "：" + s.StateText);
            }
            if (stuck.Count == 0)
            {
                _modStatus.Text = L10n.T("ui.km.mod.none");
                _modStatus.ForeColor = UiTheme.Success;
            }
            else
            {
                _modStatus.Text = L10n.F("ui.km.mod.some", stuck.Count, string.Join("；", stuck.ToArray()));
                _modStatus.ForeColor = UiTheme.Warning;
            }
        }

        // ------------------------------------------------------------ 窗口消息层

        private void AttachWindowMessages()
        {
            if (_windowSubclass != null) return;
            try
            {
                var sub = new WindowSubclass(_monitor);
                sub.AssignHandle(_host.Handle);
                _windowSubclass = sub;
            }
            catch (Exception ex)
            {
                _host.Log.Write(L10n.F("km.error.attach", ex.Message));
            }
        }

        private void DetachWindowMessages()
        {
            WindowSubclass sub = _windowSubclass;
            _windowSubclass = null;
            if (sub == null) return;
            try { sub.ReleaseHandle(); }
            catch (Exception) { }
        }

        /// <summary>只在本程序窗口聚焦时才能看到 WM_KEYDOWN/UP；钩子线程的观察不受影响。</summary>
        private sealed class WindowSubclass : NativeWindow
        {
            private readonly KeyMonitor _monitor;

            public WindowSubclass(KeyMonitor monitor)
            {
                _monitor = monitor;
            }

            protected override void WndProc(ref Message m)
            {
                try
                {
                    if (m.Msg == 0x0100 || m.Msg == 0x0101 || m.Msg == 0x0104 || m.Msg == 0x0105)
                        _monitor.FeedWindowMessage(m.Msg, m.WParam.ToInt64(), m.LParam.ToInt64());
                }
                catch (Exception) { }
                base.WndProc(ref m);
            }
        }

        // ------------------------------------------------------------ 只读信息

        private void RefreshReadOnly()
        {
            try
            {
                _remap.BeginUpdate();
                try
                {
                    _remap.Items.Clear();
                    List<ScanCodeMapping> map = KeyMonitor.ReadScancodeMap();
                    foreach (ScanCodeMapping m in map)
                    {
                        var item = new ListViewItem(m.InputName);
                        item.SubItems.Add(m.OutputName);
                        item.SubItems.Add("0x" + m.Input.ToString("X2") + " → 0x" + m.Output.ToString("X2"));
                        _remap.Items.Add(item);
                    }
                    if (map.Count == 0)
                    {
                        var empty = new ListViewItem(L10n.T("ui.km.remap.empty"));
                        empty.ForeColor = UiTheme.Muted;
                        _remap.Items.Add(empty);
                    }
                }
                finally { _remap.EndUpdate(); }
            }
            catch (Exception) { }

            try
            {
                _drivers.BeginUpdate();
                try
                {
                    _drivers.Items.Clear();
                    List<KeyboardFilterDriver> drivers = KeyMonitor.ReadKeyboardDrivers();
                    foreach (KeyboardFilterDriver d in drivers)
                    {
                        string group = string.IsNullOrEmpty(d.Group) ? L10n.T("common.none") : d.Group;
                        string image = string.IsNullOrEmpty(d.ImagePath) ? L10n.T("common.none") : d.ImagePath;
                        _drivers.Items.Add(d.Name + "　|　" + group + "　|　" + d.StartName + "　|　" + image);
                    }
                    if (drivers.Count == 0)
                    {
                        _drivers.Items.Add(L10n.T("ui.km.drivers.empty"));
                    }
                }
                finally { _drivers.EndUpdate(); }
            }
            catch (Exception) { }
        }
    }
}
