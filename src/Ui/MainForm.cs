using System;
using System.Collections.Generic;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;
using HotkeyHound.Core;

namespace HotkeyHound.Ui
{
    public sealed class MainForm : Form
    {
        public LogSink Log { get; private set; }

        private ScanSnapshot _snapshot;
        private TabControl _tabs;
        private TextBox _logBox;
        private StatusStrip _status;
        private ToolStripStatusLabel _statusLabel;
        private ToolStrip _toolbar;

        private OverviewTab _overviewTab;
        private TruthTab _truthTab;
        private ReportTab _reportTab;
        private AttributionTab _attributionTab;
        private HookCheckTab _hookCheckTab;
        private KeyMonitorTab _keyMonitorTab;
        private HookSuspectsTab _hookSuspectsTab;
        private Control _banner;

        private readonly AttributionEngine _attribution;
        private readonly HookSniffer _sniffer;

        private bool _scanning;

        public event Action SnapshotChanged;

        /// <summary>
        /// 文档截图模式：跑一次扫描，把七个页面逐页渲染成 PNG，然后退出。
        /// 用 DrawToBitmap 渲染，不需要窗口在前台，也**不安装任何钩子**。
        /// </summary>
        public MainForm(string screenshotDir)
            : this()
        {
            _screenshotDir = screenshotDir;
            if (!string.IsNullOrEmpty(screenshotDir))
            {
                Shown += OnScreenshotShown;
            }
        }

        private readonly string _screenshotDir;

        /// <summary>截图模式没有控制台，把关键步骤写进目标目录，失败时能查原因。</summary>
        private static void ScreenshotTrace(string dir, string message)
        {
            try
            {
                if (string.IsNullOrEmpty(dir)) return;
                System.IO.Directory.CreateDirectory(dir);
                System.IO.File.AppendAllText(System.IO.Path.Combine(dir, "screenshot.log"),
                    DateTime.Now.ToString("HH:mm:ss.fff") + "  " + message + "\r\n",
                    new System.Text.UTF8Encoding(true));
            }
            catch (Exception) { }
        }

        private void OnScreenshotShown(object sender, EventArgs e)
        {
            ScreenshotTrace(_screenshotDir, L10n.T("ui.screenshot.windowShown"));
            StartScan(delegate (int done, int total) { }, delegate (ScanSnapshot snapshot, Exception error)
            {
                ScreenshotTrace(_screenshotDir,
                                L10n.F("ui.screenshot.scanDone",
                                       error == null ? L10n.T("ui.screenshot.scanOk") : error.ToString()));
                // 直接在 UI 线程上截：CapturePages 内部逐页 DoEvents，不再依赖 WinForms Timer
                //（Timer 只被局部变量引用时可能在第一次 Tick 前就被回收）。
                try
                {
                    CapturePages(_screenshotDir, "");
                    // 第二遍：宽屏（模拟最大化）。2.0.0 的列宽自适应/文字换行是否生效，
                    // 靠这一遍的 PNG 验证：备注列必须吃满剩余宽度，说明文字不许截断。
                    try
                    {
                        ClientSize = UiTheme.ClampToWorkingArea(new Size(1920, 1080), 20);
                        Application.DoEvents();
                        CapturePages(_screenshotDir, "-wide");
                    }
                    catch (Exception ex)
                    {
                        ScreenshotTrace(_screenshotDir, L10n.F("ui.screenshot.captureFailed", ex));
                    }
                    ScreenshotTrace(_screenshotDir, L10n.T("ui.screenshot.finished"));
                }
                catch (Exception ex)
                {
                    Log.Write(L10n.F("ui.screenshot.modeFailed", ex.Message));
                    ScreenshotTrace(_screenshotDir, L10n.F("ui.screenshot.captureFailed", ex));
                }
                Close();
            });
        }

        private void CapturePages(string dir, string suffix)
        {
            System.IO.Directory.CreateDirectory(dir);
            ScreenshotTrace(dir, L10n.F("ui.screenshot.tabs",
                                        _tabs == null ? -1 : _tabs.TabPages.Count,
                                        ClientSize.Width, ClientSize.Height));
            string[] names = new string[]
            {
                "01-overview", "02-attribution", "03-hookcheck", "04-keymonitor",
                "05-truth", "06-hooksuspects", "07-report"
            };
            for (int i = 0; i < _tabs.TabPages.Count && i < names.Length; i++)
            {
                _tabs.SelectedIndex = i;
                Application.DoEvents();
                System.Threading.Thread.Sleep(400);
                Application.DoEvents();
                int w = ClientSize.Width;
                int h = ClientSize.Height;
                ScreenshotTrace(dir, L10n.F("ui.screenshot.render", names[i], i, w, h));
                if (w <= 0 || h <= 0) break;
                using (var bmp = new Bitmap(w, h))
                {
                    DrawToBitmap(bmp, new Rectangle(0, 0, w, h));
                    bmp.Save(System.IO.Path.Combine(dir, names[i] + suffix + ".png"), System.Drawing.Imaging.ImageFormat.Png);
                }
                Log.Write(L10n.F("ui.screenshot.rendered", names[i]));
            }
        }

        public MainForm()
        {
            UiTheme.EnsureInitialized();
            Log = new LogSink();
            Log.LineAdded += OnLogLine;

            // 构造引擎对象本身不碰钩子；钩子只在用户点「开始监听」/「布防」/「开始监视」时才安装。
            _attribution = new AttributionEngine();
            _sniffer = new HookSniffer();
            _attribution.Logged += OnEngineLog;
            _sniffer.Logged += OnEngineLog;
            // 引擎 B 的归属报告接给引擎 C 的当前布防会话，用于「正常送达」分支判定
            _attribution.RecordReceived += delegate (AttributionRecord r) { _sniffer.NoteAttribution(r); };

            Text = L10n.F("ui.title", AppInfo.Title, AppInfo.Description);
            // 窗口不超过屏幕工作区：避免窗口比屏幕还大导致内容看不全。
            ClientSize = UiTheme.ClampToWorkingArea(new Size(1080, 720), 60);
            MinimumSize = new Size(760, 520);
            StartPosition = FormStartPosition.CenterScreen;
            KeyPreview = true;
            BackColor = UiTheme.Background;
            ForeColor = UiTheme.Text;
            try { UiTheme.ApplyFont(this); }
            catch (Exception) { }
            try { Icon = UiTheme.LoadAppIcon(); }
            catch (Exception) { }
        }

        public ScanSnapshot Snapshot
        {
            get { return _snapshot; }
        }

        public AttributionEngine Attribution
        {
            get { return _attribution; }
        }

        public HookSniffer Sniffer
        {
            get { return _sniffer; }
        }

        private void OnEngineLog(string line)
        {
            Log.Write(line);
        }

        /// <summary>按需拼接顶部提示条文本；没有需要提示的内容时返回 null（提示条不显示）。</summary>
        private static string BuildBannerText()
        {
            var parts = new List<string>();
            if (ProcessIntegrity.IsBelowMedium)
            {
                parts.Add(L10n.F("ui.banner.low", ProcessIntegrity.Describe(ProcessIntegrity.Current)));
            }
            if (!AppInfo.IsAdministrator())
            {
                parts.Add(L10n.T("ui.banner.notAdmin"));
            }
            if (parts.Count == 0) return null;
            return L10n.T("ui.banner.prefix") + string.Join("　", parts.ToArray());
        }

        public bool IsScanning
        {
            get { return _scanning; }
        }

        protected override void OnLoad(EventArgs e)
        {
            base.OnLoad(e);
            BuildLayout();
            Log.Write(L10n.F("ui.log.started", AppInfo.Version));
            Log.Write(L10n.F("ui.log.admin", AppInfo.IsAdministrator() ? L10n.T("ui.log.adminYes") : L10n.T("ui.log.adminNo")));
            Log.Write(L10n.T("ui.log.howto"));
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            if (e.KeyCode == Keys.F1)
            {
                OpenHelp();
                e.Handled = true;
                return;
            }
            base.OnKeyDown(e);
        }

        private void BuildLayout()
        {
            var split = new SplitContainer();
            split.Dock = DockStyle.Fill;
            split.Orientation = Orientation.Horizontal;
            split.SplitterDistance = 560;
            split.Panel2MinSize = 90;
            split.FixedPanel = FixedPanel.Panel2;

            _tabs = new ThemedTabControl();
            _tabs.Dock = DockStyle.Fill;

            _overviewTab = new OverviewTab(this);
            _truthTab = new TruthTab(this);
            _attributionTab = new AttributionTab(this);
            _hookCheckTab = new HookCheckTab(this);
            _keyMonitorTab = new KeyMonitorTab(this);
            _hookSuspectsTab = new HookSuspectsTab(this);
            _reportTab = new ReportTab(this);

            _tabs.TabPages.Add(MakePage(L10n.T("ui.tab.overview"), _overviewTab));
            _tabs.TabPages.Add(MakePage(L10n.T("ui.tab.attribution"), _attributionTab));
            _tabs.TabPages.Add(MakePage(L10n.T("ui.tab.hookcheck"), _hookCheckTab));
            _tabs.TabPages.Add(MakePage(L10n.T("ui.tab.keymonitor"), _keyMonitorTab));
            _tabs.TabPages.Add(MakePage(L10n.T("ui.tab.truth"), _truthTab));
            _tabs.TabPages.Add(MakePage(L10n.T("ui.tab.hooksuspects"), _hookSuspectsTab));
            _tabs.TabPages.Add(MakePage(L10n.T("ui.tab.report"), _reportTab));

            split.Panel1.Controls.Add(_tabs);

            var logPanel = new GroupBox();
            logPanel.Text = L10n.T("ui.log.group");
            logPanel.Dock = DockStyle.Fill;
            logPanel.Padding = new Padding(6);

            _logBox = new TextBox();
            _logBox.Multiline = true;
            _logBox.ReadOnly = true;
            _logBox.ScrollBars = ScrollBars.Vertical;
            _logBox.Dock = DockStyle.Fill;
            try { _logBox.Font = UiTheme.MonoFont(8.5F); }
            catch (Exception) { }
            logPanel.Controls.Add(_logBox);
            split.Panel2.Controls.Add(logPanel);

            _status = new StatusStrip();
            _statusLabel = new ToolStripStatusLabel(L10n.T("ui.status.ready"));
            var statusSpring = new ToolStripStatusLabel("");
            statusSpring.Spring = true;
            var versionLabel = new ToolStripStatusLabel(
                AppInfo.Title + " v" + AppInfo.Version + " · " + L10n.LangDisplay(L10n.Current));
            versionLabel.ForeColor = UiTheme.Muted;
            _status.Items.Add(_statusLabel);
            _status.Items.Add(statusSpring);
            _status.Items.Add(versionLabel);

            _banner = BuildBanner();

            _toolbar = BuildToolbar();

            // 停靠顺序按「后加入的先贴边」：工具栏在最上，提示条在其下，状态栏在底部，主区填充剩余。
            Controls.Add(split);
            Controls.Add(_status);
            if (_banner != null) Controls.Add(_banner);
            Controls.Add(_toolbar);

            UiTheme.ApplyTree(this);
        }

        private ToolStrip BuildToolbar()
        {
            var bar = new ToolStrip();
            bar.GripStyle = ToolStripGripStyle.Hidden;
            bar.Dock = DockStyle.Top;
            bar.Padding = new Padding(8, 3, 8, 3);
            bar.RenderMode = ToolStripRenderMode.Professional;
            bar.ImageScalingSize = new Size(20, 20);

            // 页眉：图标 + 标题 + 版本靠左，帮助靠右。v1.3.1 只有一个孤零零的帮助按钮。
            var titleItem = new ToolStripLabel(AppInfo.Title);
            titleItem.Font = UiTheme.TitleFont(11F);
            titleItem.ForeColor = UiTheme.Accent;
            try
            {
                Icon icon = UiTheme.LoadAppIcon();
                if (icon != null) titleItem.Image = icon.ToBitmap();
            }
            catch (Exception) { }

            var versionItem = new ToolStripLabel("v" + AppInfo.Version);
            versionItem.ForeColor = UiTheme.Muted;

            var helpButton = new ToolStripButton(L10n.T("ui.help.button"));
            helpButton.DisplayStyle = ToolStripItemDisplayStyle.Text;
            helpButton.ToolTipText = L10n.T("ui.help.button") + " (F1)";
            // .NET Framework 的 ToolStripItem 没有 Spring（那是 ToolStripStatusLabel 独有的），
            // 靠右对齐用 Alignment 实现。
            helpButton.Alignment = ToolStripItemAlignment.Right;
            helpButton.Click += delegate (object s, EventArgs e) { OpenHelp(); };

            bar.Items.Add(titleItem);
            bar.Items.Add(versionItem);
            bar.Items.Add(helpButton);
            return bar;
        }

        private Control BuildBanner()
        {
            string text = BuildBannerText();
            bool showElevate = !AppInfo.IsAdministrator();
            if (text == null && !showElevate) return null;

            // 自动高度的两列堆栈：左边说明文字（自动换行，多长都不截断），右边提权按钮。
            var banner = new TableLayoutPanel();
            banner.Dock = DockStyle.Top;
            banner.AutoSize = true;
            banner.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            banner.BackColor = UiTheme.BannerBack;
            banner.ForeColor = UiTheme.BannerText;
            banner.ColumnCount = 2;
            banner.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            banner.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            banner.RowCount = 1;
            banner.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            banner.Padding = new Padding(10, 6, 10, 6);
            banner.Margin = new Padding(0);

            var label = UiTheme.StackLabel(text ?? "", UiTheme.BannerText);
            label.TextAlign = ContentAlignment.MiddleLeft;
            label.Margin = new Padding(2, 4, 8, 4);
            banner.Controls.Add(label, 0, 0);

            if (showElevate)
            {
                var elevate = new Button();
                elevate.Text = L10n.T("ui.banner.elevate");
                elevate.Size = new Size(140, 34);
                elevate.Anchor = AnchorStyles.Right;
                elevate.Margin = new Padding(0, 3, 0, 3);
                elevate.Click += delegate (object s, EventArgs e) { RestartAsAdministrator(); };
                UiTheme.MarkPrimary(elevate);
                banner.Controls.Add(elevate, 1, 0);
            }
            return banner;
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            // 关闭时确保钩子被卸载（若用户正在监听/布防/监视）
            try { if (_attribution.IsRunning) _attribution.Stop(); }
            catch (Exception) { }
            try { if (_sniffer.IsRunning) _sniffer.Stop(); }
            catch (Exception) { }
            try { if (_keyMonitorTab != null) _keyMonitorTab.StopMonitor(false); }
            catch (Exception) { }
            base.OnFormClosing(e);
        }

        private static TabPage MakePage(string title, Control content)
        {
            var page = new TabPage(title);
            page.Padding = new Padding(8);
            content.Dock = DockStyle.Fill;
            page.Controls.Add(content);
            return page;
        }

        private void OnLogLine(string line)
        {
            if (IsDisposed) return;
            if (InvokeRequired)
            {
                try { BeginInvoke(new Action<string>(OnLogLine), line); }
                catch (Exception) { }
                return;
            }
            try
            {
                _logBox.AppendText(line + Environment.NewLine);
            }
            catch (Exception) { }
        }

        public void SetStatus(string text)
        {
            if (IsDisposed) return;
            if (InvokeRequired)
            {
                try { BeginInvoke(new Action<string>(SetStatus), text); }
                catch (Exception) { }
                return;
            }
            _statusLabel.Text = text;
        }

        /// <summary>打开帮助窗口（F1 或工具栏按钮）；本操作不安装任何钩子。</summary>
        public void OpenHelp()
        {
            try
            {
                using (var form = new HelpForm(L10n.Current))
                {
                    form.ShowDialog(this);
                }
            }
            catch (Exception ex)
            {
                Log.Write(L10n.F("ui.log.helpFailed", ex.Message));
                MessageBox.Show(this, L10n.F("ui.log.helpFailed", ex.Message), AppInfo.Title,
                                MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        /// <summary>在后台线程跑完整的 A + D + 真相表流程，界面不冻结。</summary>
        public void StartScan(Action<int, int> progress, Action<ScanSnapshot, Exception> finished)
        {
            if (_scanning) return;
            _scanning = true;
            var thread = new Thread(delegate ()
            {
                ScanSnapshot snapshot = null;
                Exception error = null;
                try
                {
                    snapshot = ScanRunner.Run(Log, progress, CancellationToken.None);
                }
                catch (Exception ex)
                {
                    error = ex;
                    Log.Write(L10n.F("ui.ov.scanFailed", ex.Message));
                }

                try
                {
                    BeginInvoke(new Action(delegate ()
                    {
                        _scanning = false;
                        if (snapshot != null) _snapshot = snapshot;
                        // 先广播给各标签页刷新，再通知调用方，避免调用方读到还没刷新的界面
                        Action handler = SnapshotChanged;
                        if (handler != null && snapshot != null) handler();
                        if (finished != null) finished(snapshot, error);
                    }));
                }
                catch (Exception) { }
            });
            thread.IsBackground = true;
            thread.Name = "HotkeyHound-Scan";
            thread.Start();
        }

        /// <summary>
        /// 以管理员身份重启（只在用户点了按钮时发生）。用 ShellExecuteEx + runas 并保留原命令行参数；
        /// 用户在 UAC 里点「否」时识别为「已取消」，给出清晰反馈而不是抛出异常。
        ///
        /// Windows 不允许给运行中的进程原地提权，重启是唯一的提权方式——所以这里要保证重启
        /// 一定成功交接（2.0.0 修复「点了没反应」）：
        /// ① 先释放单实例互斥体（否则新实例启动即静默退出）；
        /// ② 成功后不弹任何模态框，直接 Hide + Close（模态框会把旧进程拖住，拉长交接窗口）；
        /// ③ 失败/取消时把互斥体拿回来，本实例继续正常运行。
        /// </summary>
        public void RestartAsAdministrator()
        {
            if (AppInfo.IsAdministrator())
            {
                MessageBox.Show(this, L10n.T("cli.elevate.already"), AppInfo.Title,
                                MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            Program.ReleaseSingleInstance();
            bool ok = Elevation.RequestRestart(null);
            if (ok)
            {
                Log.Write(L10n.T("ui.restart.handoff"));
                SetStatus(L10n.T("ui.restart.handoff"));
                try { Hide(); }
                catch (Exception) { }
                Close();
                return;
            }

            Program.ReacquireSingleInstance();
            if (Elevation.LastWasCancelled)
            {
                MessageBox.Show(this, L10n.T("ui.restart.cancelled"), AppInfo.Title,
                                MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            MessageBox.Show(this, L10n.F("ui.restart.failed", Elevation.LastError), AppInfo.Title,
                            MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }
}
