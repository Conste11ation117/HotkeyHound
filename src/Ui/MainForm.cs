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
        private readonly List<Button> _navButtons = new List<Button>();
        private readonly List<Control> _pages = new List<Control>();
        private Panel _content;
        private TextBox _logBox;
        private StatusStrip _status;
        private ToolStripStatusLabel _statusLabel;
        private Control _toolbar;

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
                                        _pages == null ? -1 : _pages.Count,
                                        ClientSize.Width, ClientSize.Height));
            string[] names = new string[]
            {
                "01-overview", "02-attribution", "03-hookcheck", "04-keymonitor",
                "05-truth", "06-hooksuspects", "07-report"
            };
            for (int i = 0; i < _pages.Count && i < names.Length; i++)
            {
                SelectPage(i);
                Application.DoEvents();
                System.Threading.Thread.Sleep(400);
                Application.DoEvents();
                // DrawToBitmap 会连标题栏一起画进结果；用 ClientSize 作位图会把底部状态栏
                // 裁掉约一个标题栏的高度。这里按窗口外框取尺寸，截图才和屏幕上看到的一致。
                int w = Width;
                int h = Height;
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
            ClientSize = UiTheme.ClampToWorkingArea(new Size(1240, 820), 60);
            MinimumSize = new Size(940, 640);
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
            _content = new Panel();
            _content.Dock = DockStyle.Fill;
            _content.Padding = new Padding(UiTheme.SpaceM, UiTheme.SpaceS, UiTheme.SpaceM, UiTheme.SpaceM);

            _overviewTab = new OverviewTab(this);
            _truthTab = new TruthTab(this);
            _attributionTab = new AttributionTab(this);
            _hookCheckTab = new HookCheckTab(this);
            _keyMonitorTab = new KeyMonitorTab(this);
            _hookSuspectsTab = new HookSuspectsTab(this);
            _reportTab = new ReportTab(this);

            string[] titles = new string[]
            {
                L10n.T("ui.tab.overview"), L10n.T("ui.tab.attribution"), L10n.T("ui.tab.hookcheck"),
                L10n.T("ui.tab.keymonitor"), L10n.T("ui.nav.truth"), L10n.T("ui.tab.hooksuspects"),
                L10n.T("ui.tab.report")
            };
            Control[] contents = new Control[]
            {
                _overviewTab, _attributionTab, _hookCheckTab, _keyMonitorTab,
                _truthTab, _hookSuspectsTab, _reportTab
            };
            for (int i = 0; i < contents.Length; i++) AddPage(contents[i]);

            var body = new Panel();
            body.Dock = DockStyle.Fill;
            body.Controls.Add(_content);
            body.Controls.Add(BuildSidebar(titles));

            // 日志默认折叠成一条可点击的栏：把垂直空间留给主内容，需要排错时再展开。
            var logPanel = new Panel();
            logPanel.Dock = DockStyle.Bottom;
            logPanel.Height = 36;
            logPanel.Padding = new Padding(10, 0, 10, 0);
            logPanel.Tag = "surface";

            _logBox = new TextBox();
            _logBox.Multiline = true;
            _logBox.ReadOnly = true;
            _logBox.ScrollBars = ScrollBars.Vertical;
            _logBox.Dock = DockStyle.Fill;
            _logBox.Visible = false;
            try { _logBox.Font = UiTheme.MonoFont(8.5F); }
            catch (Exception) { }

            var logToggle = new Button();
            logToggle.Text = "▸ " + L10n.T("ui.log.group");
            logToggle.Dock = DockStyle.Top;
            logToggle.Height = 32;
            logToggle.TextAlign = ContentAlignment.MiddleLeft;
            logToggle.Padding = new Padding(4, 0, 4, 0);
            logToggle.Click += delegate (object s, EventArgs e)
            {
                bool show = !_logBox.Visible;
                _logBox.Visible = show;
                logPanel.Height = show ? 190 : 36;
                logToggle.Text = (show ? "▾ " : "▸ ") + L10n.T("ui.log.group");
            };

            logPanel.Controls.Add(_logBox);
            logPanel.Controls.Add(logToggle);

            var shell = new Panel();
            shell.Dock = DockStyle.Fill;
            shell.Controls.Add(body);
            shell.Controls.Add(logPanel);

            _status = new StatusStrip();
            _statusLabel = new ToolStripStatusLabel(L10n.T("ui.status.ready"));
            var statusSpring = new ToolStripStatusLabel("");
            statusSpring.Spring = true;
            var versionLabel = new ToolStripStatusLabel(
                AppInfo.Title + " v" + AppInfo.Version + " " + AppInfo.Codename
                + " · " + L10n.LangDisplay(L10n.Current));
            versionLabel.ForeColor = UiTheme.Muted;
            _status.Items.Add(_statusLabel);
            _status.Items.Add(statusSpring);
            _status.Items.Add(versionLabel);

            _banner = BuildBanner();

            _toolbar = BuildToolbar();

            // 停靠顺序按「后加入的先贴边」：页眉在最上，提示条在其下，状态栏在底部，主区填充剩余。
            Controls.Add(shell);
            Controls.Add(_status);
            if (_banner != null) Controls.Add(_banner);
            Controls.Add(_toolbar);

            UiTheme.ApplyTree(this);
            SelectPage(0);
        }

        private void AddPage(Control content)
        {
            content.Dock = DockStyle.Fill;
            _pages.Add(content);
            _content.Controls.Add(content);
            content.Visible = false;
        }

        private Control BuildSidebar(string[] titles)
        {
            var sidebar = new Panel();
            sidebar.Dock = DockStyle.Left;
            // 英文导航词更长：196px 只够显示单词的第一个词（Key attribution → Key），
            // 会让人误以为版本缺页。英文界面单独加宽，中文保持紧凑。
            sidebar.Width = L10n.IsEnglish ? 238 : 196;
            sidebar.Padding = new Padding(10, 12, 10, 12);
            sidebar.Tag = "sidebar";
            sidebar.BackColor = UiTheme.SurfaceAlt;

            var nav = new TableLayoutPanel();
            nav.Dock = DockStyle.Fill;
            nav.ColumnCount = 1;
            nav.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            nav.RowCount = titles.Length + 1;
            for (int i = 0; i < titles.Length; i++)
            {
                nav.RowStyles.Add(new RowStyle(SizeType.AutoSize));
                var button = BuildNavButton(titles[i], i);
                _navButtons.Add(button);
                nav.Controls.Add(button, 0, i);
            }
            // 末尾占满剩余高度：导航项永远从顶部开始排，不会被拉长平分。
            nav.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            nav.Controls.Add(new Label(), 0, titles.Length);

            sidebar.Controls.Add(nav);
            return sidebar;
        }

        private Button BuildNavButton(string title, int index)
        {
            var button = new Button();
            button.Text = title;
            button.Dock = DockStyle.Fill;
            button.Height = 46;
            button.Margin = new Padding(0, 0, 0, 6);
            button.Padding = new Padding(14, 0, 8, 0);
            button.TextAlign = ContentAlignment.MiddleLeft;
            UiTheme.MarkNavigation(button, false);
            button.Click += delegate (object s, EventArgs e) { SelectPage(index); };
            return button;
        }

        private void SelectPage(int index)
        {
            if (index < 0 || index >= _pages.Count) return;
            for (int i = 0; i < _pages.Count; i++)
            {
                _pages[i].Visible = i == index;
                if (i < _navButtons.Count) StyleNavButton(_navButtons[i], i == index);
            }
            try { _content.Controls.SetChildIndex(_pages[index], 0); }
            catch (Exception) { }
        }

        private void StyleNavButton(Button button, bool selected)
        {
            if (button == null) return;
            UiTheme.MarkNavigation(button, selected);
            button.BackColor = selected ? UiTheme.Surface : Color.Transparent;
            button.ForeColor = selected ? UiTheme.Accent : UiTheme.Text;
            // ButtonBase 不允许把 BorderColor 设为 Transparent（会抛 NotSupportedException）；
            // 未选中项靠 BorderSize=0 隐藏边框，颜色给一个合法值即可。
            button.FlatAppearance.BorderColor = selected ? UiTheme.Accent : UiTheme.Border;
            button.FlatAppearance.BorderSize = selected ? 1 : 0;
            button.FlatAppearance.MouseOverBackColor = UiTheme.Surface;
            button.FlatAppearance.MouseDownBackColor = UiTheme.Surface;
        }

        /// <summary>页眉：图标 + 产品名 + 一句话说明靠左，帮助靠右。</summary>
        private Control BuildToolbar()
        {
            var header = new Panel();
            header.Dock = DockStyle.Top;
            // 高 DPI 下 9pt 中文字体实际行高接近 30px：页眉必须留足两行，
            // 否则「全局快捷键冲突排查工具」的下缘会被下面的提示条盖住。
            header.Height = 96;
            header.Padding = new Padding(16, 8, 16, 8);
            // Tag 让 ApplyTree 保留页眉的 Surface 底色，而不是把它刷成页面底色。
            header.Tag = "header";
            header.BackColor = UiTheme.Surface;

            var icon = new PictureBox();
            icon.Dock = DockStyle.Left;
            icon.Width = 44;
            icon.Margin = new Padding(0, 0, 12, 0);
            icon.SizeMode = PictureBoxSizeMode.Zoom;
            try
            {
                Icon appIcon = UiTheme.LoadAppIcon();
                if (appIcon != null) icon.Image = appIcon.ToBitmap();
            }
            catch (Exception) { }

            // 标题和副标题各占一行：不再用绝对坐标，任何 DPI / 字体下都不会叠在一起。
            var textPanel = new TableLayoutPanel();
            textPanel.Dock = DockStyle.Fill;
            textPanel.ColumnCount = 1;
            textPanel.RowCount = 2;
            textPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            textPanel.RowStyles.Add(new RowStyle(SizeType.Percent, 50F));
            textPanel.RowStyles.Add(new RowStyle(SizeType.Percent, 50F));
            textPanel.Margin = new Padding(0);
            try { textPanel.BackColor = Color.Transparent; }
            catch (Exception) { }

            var title = new Label();
            title.Text = AppInfo.Title;
            title.Font = UiTheme.TitleFont(11F);
            title.ForeColor = UiTheme.Accent;
            title.AutoSize = true;
            title.Dock = DockStyle.Fill;
            title.TextAlign = ContentAlignment.BottomLeft;
            title.Margin = new Padding(0);

            var subtitle = new Label();
            subtitle.Text = AppInfo.Description;
            subtitle.Font = UiTheme.BaseFont();
            subtitle.ForeColor = UiTheme.Muted;
            subtitle.AutoSize = true;
            subtitle.Dock = DockStyle.Fill;
            subtitle.TextAlign = ContentAlignment.TopLeft;
            subtitle.Margin = new Padding(0);

            textPanel.Controls.Add(title, 0, 0);
            textPanel.Controls.Add(subtitle, 0, 1);

            var helpButton = new Button();
            helpButton.Text = L10n.T("ui.help.button");
            helpButton.AutoSize = true;
            helpButton.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            helpButton.MinimumSize = new Size(96, 34);
            helpButton.Padding = new Padding(12, 0, 12, 0);
            helpButton.Dock = DockStyle.Right;
            helpButton.Margin = new Padding(0, 0, 0, 0);
            helpButton.Click += delegate (object s, EventArgs e) { OpenHelp(); };
            // 页眉上的帮助按钮用主色（蓝底白字）：普通灰底按钮在浅色页眉里几乎看不见。
            UiTheme.MarkPrimary(helpButton);

            // 停靠顺序：先加 Fill 文本区，再加左右贴边的图标和帮助按钮。
            header.Controls.Add(textPanel);
            header.Controls.Add(icon);
            header.Controls.Add(helpButton);
            return header;
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
            // Tag 让 ApplyTree 保留提示条底色（它承载「非管理员/低完整性」的语义色）。
            banner.Tag = "banner";

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
            try { if (_hookCheckTab != null) _hookCheckTab.StopCaptureIfActive(); }
            catch (Exception) { }
            base.OnFormClosing(e);
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
