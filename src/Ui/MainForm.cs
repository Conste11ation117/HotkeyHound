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
        private HookSuspectsTab _hookSuspectsTab;
        private Panel _banner;

        private readonly AttributionEngine _attribution;
        private readonly HookSniffer _sniffer;

        private bool _scanning;

        public event Action SnapshotChanged;

        /// <summary>
        /// 文档截图模式：跑一次扫描，把五个页面逐页渲染成 PNG，然后退出。
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

        private void OnScreenshotShown(object sender, EventArgs e)
        {
            StartScan(delegate (int done, int total) { }, delegate (ScanSnapshot snapshot, Exception error)
            {
                var timer = new System.Windows.Forms.Timer();
                timer.Interval = 500;
                timer.Tick += delegate (object s2, EventArgs e2)
                {
                    timer.Stop();
                    try { CapturePages(_screenshotDir); }
                    catch (Exception ex) { Log.Write("截图模式失败：" + ex.Message); }
                    Close();
                };
                timer.Start();
            });
        }

        private void CapturePages(string dir)
        {
            System.IO.Directory.CreateDirectory(dir);
            string[] names = new string[] { "01-overview", "02-attribution", "03-hookcheck", "04-truth", "05-hooksuspects", "06-report" };
            for (int i = 0; i < _tabs.TabPages.Count && i < names.Length; i++)
            {
                _tabs.SelectedIndex = i;
                Application.DoEvents();
                System.Threading.Thread.Sleep(400);
                Application.DoEvents();
                using (var bmp = new Bitmap(ClientSize.Width, ClientSize.Height))
                {
                    DrawToBitmap(bmp, new Rectangle(0, 0, ClientSize.Width, ClientSize.Height));
                    bmp.Save(System.IO.Path.Combine(dir, names[i] + ".png"), System.Drawing.Imaging.ImageFormat.Png);
                }
                Log.Write("已渲染 " + names[i] + ".png");
            }
        }

        public MainForm()
        {
            Log = new LogSink();
            Log.LineAdded += OnLogLine;

            // 构造引擎对象本身不碰钩子；钩子只在用户点「开始监听」/「布防」时才安装。
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
            try { UiTheme.ApplyFont(this); }
            catch (Exception) { }
            try { Icon = SystemIcons.Application; }
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
            if (Environment.Is64BitProcess && Has32BitProcess())
            {
                parts.Add(L10n.T("ui.banner.x64"));
            }
            if (parts.Count == 0) return null;
            return L10n.T("ui.banner.prefix") + string.Join("　", parts.ToArray());
        }

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool IsWow64Process(IntPtr hProcess, out bool wow64);

        /// <summary>是否真的有 32 位进程在运行（决定要不要显示 x64 覆盖不到 32 位的提示）。</summary>
        private static bool Has32BitProcess()
        {
            try
            {
                foreach (System.Diagnostics.Process p in System.Diagnostics.Process.GetProcesses())
                {
                    try
                    {
                        IntPtr h;
                        try { h = p.Handle; }
                        catch (Exception) { h = IntPtr.Zero; }
                        if (h == IntPtr.Zero) continue;
                        bool wow;
                        if (IsWow64Process(h, out wow) && wow) return true;
                    }
                    catch (Exception) { }
                    finally { try { p.Dispose(); } catch (Exception) { } }
                }
            }
            catch (Exception) { }
            return false;
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

            _tabs = new TabControl();
            _tabs.Dock = DockStyle.Fill;

            _overviewTab = new OverviewTab(this);
            _truthTab = new TruthTab(this);
            _attributionTab = new AttributionTab(this);
            _hookCheckTab = new HookCheckTab(this);
            _hookSuspectsTab = new HookSuspectsTab(this);
            _reportTab = new ReportTab(this);

            _tabs.TabPages.Add(MakePage(L10n.T("ui.tab.overview"), _overviewTab));
            _tabs.TabPages.Add(MakePage(L10n.T("ui.tab.attribution"), _attributionTab));
            _tabs.TabPages.Add(MakePage(L10n.T("ui.tab.hookcheck"), _hookCheckTab));
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
            try { _logBox.Font = new Font("Consolas", 8.5F); }
            catch (Exception) { }
            logPanel.Controls.Add(_logBox);
            split.Panel2.Controls.Add(logPanel);

            _status = new StatusStrip();
            _statusLabel = new ToolStripStatusLabel(L10n.T("ui.status.ready"));
            _status.Items.Add(_statusLabel);

            _banner = BuildBanner();

            _toolbar = BuildToolbar();

            // 停靠顺序按「后加入的先贴边」：工具栏在最上，提示条在其下，状态栏在底部，主区填充剩余。
            Controls.Add(split);
            Controls.Add(_status);
            if (_banner != null) Controls.Add(_banner);
            Controls.Add(_toolbar);
        }

        private ToolStrip BuildToolbar()
        {
            var bar = new ToolStrip();
            bar.GripStyle = ToolStripGripStyle.Hidden;
            bar.Dock = DockStyle.Top;
            bar.Padding = new Padding(4, 2, 4, 2);

            var helpButton = new ToolStripButton(L10n.T("ui.help.button"));
            helpButton.DisplayStyle = ToolStripItemDisplayStyle.Text;
            helpButton.ToolTipText = L10n.T("ui.help.button") + " (F1)";
            helpButton.Click += delegate (object s, EventArgs e) { OpenHelp(); };
            bar.Items.Add(helpButton);
            return bar;
        }

        private Panel BuildBanner()
        {
            string text = BuildBannerText();
            bool showElevate = !AppInfo.IsAdministrator();
            if (text == null && !showElevate) return null;

            var panel = new Panel();
            panel.Dock = DockStyle.Top;
            panel.Height = 60;
            panel.BackColor = Color.FromArgb(255, 246, 229);
            panel.ForeColor = Color.FromArgb(140, 55, 20);
            panel.Padding = new Padding(10, 6, 10, 6);

            var label = new Label();
            label.Text = text ?? "";
            label.Dock = DockStyle.Fill;
            label.ForeColor = panel.ForeColor;
            label.AutoSize = false;
            label.TextAlign = ContentAlignment.MiddleLeft;
            label.Padding = new Padding(2, 0, 8, 0);

            // 先加 label 再加 button：停靠按「后加入的先贴边」，button 先贴到右侧，label 填充剩余。
            panel.Controls.Add(label);
            if (showElevate)
            {
                var elevate = new Button();
                elevate.Text = L10n.T("ui.banner.elevate");
                elevate.Dock = DockStyle.Right;
                elevate.Width = 130;
                elevate.Click += delegate (object s, EventArgs e) { RestartAsAdministrator(); };
                panel.Controls.Add(elevate);
            }
            return panel;
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            // 关闭时确保钩子被卸载（若用户正在监听/布防）
            try { if (_attribution.IsRunning) _attribution.Stop(); }
            catch (Exception) { }
            try { if (_sniffer.IsRunning) _sniffer.Stop(); }
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
        /// </summary>
        public void RestartAsAdministrator()
        {
            if (AppInfo.IsAdministrator())
            {
                MessageBox.Show(this, L10n.T("cli.elevate.already"), AppInfo.Title,
                                MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            bool ok = Elevation.RequestRestart(null);
            if (ok)
            {
                MessageBox.Show(this, L10n.T("ui.restart.ok"), AppInfo.Title,
                                MessageBoxButtons.OK, MessageBoxIcon.Information);
                Close();
                return;
            }
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
