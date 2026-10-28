using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Windows.Forms;
using HotkeyHound.Core;

namespace HotkeyHound
{
    public sealed class CliOptions
    {
        public bool Help;
        public bool Scan;
        public bool SelfTest;
        public bool Listen;
        public bool Monitor;
        public bool Helper;
        public bool NoHooks;
        public bool AttributeAll;
        public bool Yes;
        public bool DryRun;
        public int Max = AutoProbe.DefaultMax;
        /// <summary>文档截图模式：给出目录即启用（渲染七个页面后退出，不安装任何钩子）。</summary>
        public string ScreenshotDir;
        public bool Elevate;
        public string Lang;
        public string Theme;
        public string ReportPath;
        public string JsonPath;
        public int Seconds = 30;

        public static CliOptions Parse(string[] args)
        {
            var o = new CliOptions();
            for (int i = 0; i < args.Length; i++)
            {
                string a = args[i];
                if (a == null) continue;
                switch (a.ToLowerInvariant())
                {
                    case "--help":
                    case "-h":
                    case "-?":
                    case "/?":
                        o.Help = true;
                        break;
                    case "--scan":
                        o.Scan = true;
                        break;
                    case "--selftest":
                        o.SelfTest = true;
                        break;
                    case "--no-hooks":
                        o.NoHooks = true;
                        break;
                    case "--screenshot":
                        if (i + 1 < args.Length) { o.ScreenshotDir = args[++i]; }
                        break;
                    case "--listen":
                        o.Listen = true;
                        break;
                    case "--monitor":
                        o.Monitor = true;
                        break;
                    case "--attribute-all":
                        o.AttributeAll = true;
                        break;
                    case "--yes":
                        o.Yes = true;
                        break;
                    case "--dry-run":
                        o.DryRun = true;
                        break;
                    case "--max":
                        {
                            int n;
                            if (i + 1 < args.Length && int.TryParse(args[i + 1], out n)) { o.Max = n; i++; }
                        }
                        break;
                    case "--helper":
                        o.Helper = true;
                        break;
                    case "--elevate":
                        o.Elevate = true;
                        break;
                    case "--elevated":
                        // 内部标记：本进程已经由提权重启产生，忽略（避免无限重启循环）。
                        break;
                    case "--report":
                        if (i + 1 < args.Length) { o.ReportPath = args[++i]; o.Scan = true; }
                        break;
                    case "--json":
                        if (i + 1 < args.Length) { o.JsonPath = args[++i]; o.Scan = true; }
                        break;
                    case "--lang":
                        if (i + 1 < args.Length) { o.Lang = args[++i]; }
                        break;
                    case "--theme":
                        if (i + 1 < args.Length) { o.Theme = args[++i]; }
                        break;
                    case "--seconds":
                        {
                            int n;
                            if (i + 1 < args.Length && int.TryParse(args[i + 1], out n)) { o.Seconds = n; i++; }
                        }
                        break;
                    default:
                        string[] kv = a.Split(new char[] { '=' }, 2);
                        if (kv.Length == 2 && kv[0].ToLowerInvariant() == "--report") { o.ReportPath = kv[1]; o.Scan = true; }
                        else if (kv.Length == 2 && kv[0].ToLowerInvariant() == "--json") { o.JsonPath = kv[1]; o.Scan = true; }
                        else if (kv.Length == 2 && kv[0].ToLowerInvariant() == "--lang") { o.Lang = kv[1]; }
                        else if (kv.Length == 2 && kv[0].ToLowerInvariant() == "--theme") { o.Theme = kv[1]; }
                        else if (kv.Length == 2 && kv[0].ToLowerInvariant() == "--max")
                        {
                            int n;
                            if (int.TryParse(kv[1], out n)) o.Max = n;
                        }
                        break;
                }
            }
            return o;
        }
    }

    public static class Program
    {
        private const int STD_OUTPUT_HANDLE = -11;
        private const int SW_RESTORE = 9;

        // 注入到别的进程里的 hook DLL 是通过固定名的共享内存（Local\HotkeyHound.Ctl）来找
        // 控制窗口的，DLL 那边没有别的办法知道"该跟哪个实例说话"。所以两个实例同时跑会互相
        // 覆盖 arm/ctl：归属记录串台，而且任意一方退出都会把对方的上报一起关掉。
        // 与其把共享内存改成每实例唯一（DLL 无法安全地发现随机名），不如禁止第二个实例。
        private static System.Threading.Mutex _instanceMutex;

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool AttachConsole(int dwProcessId);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr GetStdHandle(int nStdHandle);

        private delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

        [DllImport("user32.dll")]
        private static extern bool EnumWindows(EnumWindowsProc cb, IntPtr lParam);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern int GetWindowTextW(IntPtr hWnd, StringBuilder text, int maxCount);

        [DllImport("user32.dll")]
        private static extern bool IsWindowVisible(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint pid);

        [DllImport("user32.dll")]
        private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

        [DllImport("user32.dll")]
        private static extern bool SetForegroundWindow(IntPtr hWnd);

        private static int _unhandledDialogShown;

        [STAThread]
        public static int Main(string[] args)
        {
            // UI 线程的异常交给下面的处理器：不要让 CLR 弹出原始「未处理的异常」对话框。
            // 后台线程的异常无法恢复，但至少把完整堆栈写进 error.log，用户能把它发回来。
            Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
            Application.ThreadException += delegate (object s, ThreadExceptionEventArgs e)
            {
                ReportUnhandled(e.Exception, false);
            };
            AppDomain.CurrentDomain.UnhandledException += delegate (object s, UnhandledExceptionEventArgs e)
            {
                ReportUnhandled(e.ExceptionObject as Exception, true);
            };

            // 任何未捕获的异常都不能让进程挂起在 Windows 错误报告对话框上，
            // 必须转成明确的退出码并打印原因。
            try
            {
                return RunCore(args);
            }
            catch (Exception ex)
            {
                try { Console.Error.WriteLine(L10n.F("cli.except", ex)); }
                catch (Exception) { }
                ReportUnhandled(ex, true);
                return 1;
            }
        }

        /// <summary>
        /// 统一兜底：写一份可定位的错误日志，只弹一次简明提示，不把一整页堆栈甩给用户。
        /// 返回日志路径（写不进去时为空）。
        /// </summary>
        private static string WriteCrashLog(Exception ex)
        {
            return WriteCrashLog(ex, null);
        }

        private static string WriteCrashLog(Exception ex, string source)
        {
            try
            {
                string dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                                          "HotkeyHound");
                if (string.IsNullOrEmpty(dir)) dir = Path.Combine(Path.GetTempPath(), "HotkeyHound");
                Directory.CreateDirectory(dir);
                string path = Path.Combine(dir, "error.log");
                string body = "==== " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + "  v" + AppInfo.Version
                              + (string.IsNullOrEmpty(source) ? "" : "  [" + source + "]")
                              + " ====\r\n" + (ex == null ? "(no exception object)" : ex.ToString()) + "\r\n\r\n";
                File.AppendAllText(path, body, new UTF8Encoding(true));
                return path;
            }
            catch (Exception)
            {
                return null;
            }
        }

        /// <summary>
        /// UI 线程回调里的失败（来自后台线程的投递）。只写日志、不弹框：
        /// 这类失败通常是某条事件的处理出错，弹窗会把整台机器的工作节奏打断，而日志里
        /// 带着来源方法名，足够定位。
        /// </summary>
        public static void ReportUiFailure(Exception ex, string source)
        {
            try { WriteCrashLog(ex, "ui:" + (source ?? "?")); }
            catch (Exception) { }
        }

        private static void ReportUnhandled(Exception ex, bool fatal)
        {
            string logPath = WriteCrashLog(ex);
            if (Interlocked.Exchange(ref _unhandledDialogShown, 1) != 0) return;
            string message = ex == null ? "" : ex.Message;
            string body;
            try
            {
                body = L10n.F("ui.crash.body", message, logPath ?? L10n.T("common.none"));
            }
            catch (Exception)
            {
                body = "HotkeyHound hit an unexpected error. / 程序遇到未处理的错误。\r\n\r\n"
                       + message + "\r\n\r\nLog / 日志：" + (logPath ?? "(unavailable)");
            }
            try
            {
                MessageBox.Show(body, AppInfo.Title, MessageBoxButtons.OK,
                                fatal ? MessageBoxIcon.Error : MessageBoxIcon.Warning);
            }
            catch (Exception) { }
        }

        private static int RunCore(string[] args)
        {
            CliOptions opts;
            try
            {
                opts = CliOptions.Parse(args);
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine(L10n.F("cli.parseFailed", ex.Message));
                return 2;
            }

            // 先确定语言，之后的一切输出都用对应语言。
            L10n.Initialize(opts.Lang);

            // --elevate：以管理员身份重启并继续（保留原命令行参数）。已经带 --elevated 标记
            // 说明本进程就是提权重启后的进程，不能再重启，否则会无限循环。
            if (opts.Elevate && !Elevation.IsAdministrator() && !Elevation.WasRelaunched(args))
            {
                PrepareConsole();
                Console.WriteLine(L10n.T("cli.elevate.requesting"));
                if (Elevation.RequestRestart(null))
                {
                    Console.WriteLine(L10n.T("cli.elevate.ok"));
                    return 0;
                }
                if (Elevation.LastWasCancelled)
                {
                    Console.WriteLine(L10n.T("cli.elevate.cancelled"));
                    return 1;
                }
                Console.Error.WriteLine(L10n.F("cli.elevate.failed", Elevation.LastError));
                return 1;
            }

            if (opts.Help)
            {
                PrepareConsole();
                PrintHelp();
                return 0;
            }

            if (opts.SelfTest)
            {
                PrepareConsole();
                try
                {
                    return SelfTest.Run(opts.NoHooks);
                }
                catch (Exception ex)
                {
                    Console.Error.WriteLine(L10n.F("cli.selftest.failed", ex));
                    return 1;
                }
            }

            if (opts.AttributeAll)
            {
                PrepareConsole();
                try
                {
                    return RunAttributeAll(opts);
                }
                catch (Exception ex)
                {
                    Console.Error.WriteLine(L10n.F("cli.attr.failed", ex));
                    return 1;
                }
            }

            if (opts.Scan)
            {
                PrepareConsole();
                try
                {
                    return RunScan(opts);
                }
                catch (Exception ex)
                {
                    Console.Error.WriteLine(L10n.F("cli.scan.failed", ex));
                    return 1;
                }
            }

            if (opts.Listen)
            {
                PrepareConsole();
                try
                {
                    return RunListen(opts);
                }
                catch (Exception ex)
                {
                    Console.Error.WriteLine(L10n.F("cli.listen.title", AppInfo.Title, AppInfo.Version) + "： " + ex);
                    return 1;
                }
            }

            if (opts.Monitor)
            {
                PrepareConsole();
                try
                {
                    return RunMonitor(opts);
                }
                catch (Exception ex)
                {
                    Console.Error.WriteLine(L10n.F("cli.monitor.title", AppInfo.Title, AppInfo.Version) + "： " + ex);
                    return 1;
                }
            }

            if (opts.Helper)
            {
                PrepareConsole();
                return HelperHost.Run(args);
            }

            // 无参数：启动图形界面
            if (!EnsureSingleInstance())
            {
                Console.Error.WriteLine(L10n.T("cli.alreadyRunning"));
                return 0;
            }
            try
            {
                if (!string.IsNullOrEmpty(opts.Theme)) HotkeyHound.Ui.UiTheme.Initialize(opts.Theme);
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                // 首次启动先选语言（默认英文）：主窗口的全部文字在构造时按语言生成，
                // 所以必须赶在 MainForm 之前。截图模式与显式 --lang 不弹。
                if (opts.ScreenshotDir == null && string.IsNullOrEmpty(opts.Lang) && !L10n.HasConfiguredLanguage())
                {
                    HotkeyHound.Ui.FirstRunLanguage.Ask();
                }
                Application.Run(new HotkeyHound.Ui.MainForm(opts.ScreenshotDir));
                return 0;
            }
            catch (Exception ex)
            {
                ReportUnhandled(ex, true);
                return 1;
            }
        }

        /// <summary>
        /// 保证只有一个图形实例在跑（见 _instanceMutex 的注释）。
        /// 已有实例时先短暂等它释放互斥体（提权交接时旧实例正在退出），
        /// 等不到再把它切到前台并返回 false，调用方应当安静退出。
        /// </summary>
        private static bool EnsureSingleInstance()
        {
            try
            {
                bool created;
                _instanceMutex = new System.Threading.Mutex(true, @"Local\HotkeyHound.SingleInstance", out created);
                if (created) return true;
                try
                {
                    // 提权重启的交接窗口：旧实例已经/即将释放互斥体，给它 1.5 秒。
                    if (_instanceMutex.WaitOne(1500)) return true;
                }
                catch (System.Threading.AbandonedMutexException)
                {
                    // 旧实例异常退出，互斥体归本进程接管。
                    return true;
                }
                catch (Exception) { }
                BringExistingToFront();
                return false;
            }
            catch (Exception)
            {
                // 拿不到互斥体（极少见）时宁可放行，也不要挡住用户使用。
                return true;
            }
        }

        /// <summary>
        /// 提权重启前释放单实例互斥体。
        /// v1.3.1「点了提权重启没反应」的根因：新的提权实例启动时，旧实例还占着互斥体
        /// （正弹着模态提示框），新实例判定「已有实例在跑」就安静退出了；随后旧实例关闭，
        /// 结果桌面上什么都没有。所以必须先把互斥体放掉，再拉起新实例。
        /// </summary>
        public static void ReleaseSingleInstance()
        {
            try
            {
                System.Threading.Mutex m = _instanceMutex;
                _instanceMutex = null;
                if (m != null)
                {
                    try { m.ReleaseMutex(); }
                    catch (Exception) { }
                    m.Dispose();
                }
            }
            catch (Exception) { }
        }

        /// <summary>提权失败/被取消后把互斥体拿回来，避免两个图形实例共用 Local\HotkeyHound.Ctl。</summary>
        public static void ReacquireSingleInstance()
        {
            try
            {
                if (_instanceMutex != null) return;
                bool created;
                _instanceMutex = new System.Threading.Mutex(true, @"Local\HotkeyHound.SingleInstance", out created);
            }
            catch (Exception) { }
        }

        private static void BringExistingToFront()
        {
            try
            {
                // 窗口标题是「HotkeyHound —— 描述」（ui.title 格式化结果），
                // FindWindowW 全字匹配永远找不到——改用 EnumWindows 做前缀匹配。
                IntPtr found = IntPtr.Zero;
                uint self = (uint)System.Diagnostics.Process.GetCurrentProcess().Id;
                EnumWindows(delegate (IntPtr hWnd, IntPtr lParam)
                {
                    if (!IsWindowVisible(hWnd)) return true;
                    uint pid;
                    GetWindowThreadProcessId(hWnd, out pid);
                    if (pid == self) return true;
                    var sb = new StringBuilder(512);
                    GetWindowTextW(hWnd, sb, sb.Capacity);
                    if (sb.ToString().StartsWith(AppInfo.Title, StringComparison.Ordinal))
                    {
                        found = hWnd;
                        return false;
                    }
                    return true;
                }, IntPtr.Zero);
                if (found == IntPtr.Zero) return;
                ShowWindow(found, SW_RESTORE);
                SetForegroundWindow(found);
            }
            catch (Exception) { }
        }

        /// <summary>
        /// --attribute-all：先跑引擎 A，再把已占用组合逐个模拟按下（默认只做 dry-run）。
        /// 只有 --yes 才会安装归属钩子并真的按键，且黑名单永远生效。
        /// </summary>
        private static int RunAttributeAll(CliOptions opts)
        {
            var log = new LogSink();
            log.LineAdded += delegate (string line) { Console.WriteLine(line); };

            ScanSnapshot snapshot;
            try
            {
                snapshot = ScanRunner.Run(log, null, System.Threading.CancellationToken.None);
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine(L10n.F("cli.scan.failed", ex));
                return 1;
            }

            bool willPress = opts.Yes && !opts.DryRun;
            Console.WriteLine(L10n.F("cli.attr.title", AppInfo.Title, AppInfo.Version));
            Console.WriteLine(L10n.F("cli.attr.meta", snapshot.OccupiedCount, AutoProbe.ClampMax(opts.Max),
                              willPress ? L10n.T("cli.attr.mode.real") : L10n.T("cli.attr.mode.dryRun")));
            Console.WriteLine(new string('-', 72));

            AutoProbeResult result;
            if (!willPress)
            {
                result = AutoProbe.DryRun(snapshot.Occupancy, opts.Max, log);
                Console.WriteLine(new string('-', 72));
                Console.WriteLine(L10n.F("cli.attr.dryRunDone", result.PlannedCount, SkippedCount(result)));
            }
            else
            {
                PrintPlan(AutoProbe.Plan(snapshot.Occupancy, opts.Max));
                Console.WriteLine(L10n.T("cli.attr.starting"));

                var engine = new AttributionEngine();
                engine.Logged += delegate (string line) { Console.WriteLine("  " + line); };
                if (!engine.Start())
                {
                    Console.Error.WriteLine(L10n.F("cli.attr.engineFailed", engine.LastError));
                    return 1;
                }
                try
                {
                    result = AutoProbe.Run(engine, snapshot.Occupancy, opts.Max, log, null, null);
                }
                finally
                {
                    try { engine.Stop(); } catch (Exception) { }
                    try { engine.Dispose(); } catch (Exception) { }
                }
                Console.WriteLine(new string('-', 72));
                Console.WriteLine(L10n.F("cli.attr.done", result.CountOf(AutoProbeOutcome.Attributed),
                                  result.CountOf(AutoProbeOutcome.NoResponse), SkippedCount(result)));
                if (result.Aborted) Console.WriteLine(L10n.T("cli.attr.aborted"));
            }

            if (!string.IsNullOrEmpty(opts.ReportPath))
            {
                try
                {
                    ReportBuilder.Write(snapshot, opts.ReportPath);
                    Console.WriteLine(L10n.F("cli.scan.reportWritten", Path.GetFullPath(opts.ReportPath)));
                }
                catch (Exception ex)
                {
                    Console.Error.WriteLine(L10n.F("cli.scan.reportWriteFailed", ex.Message));
                    Console.Error.WriteLine(WritabilityHint(Path.GetDirectoryName(Path.GetFullPath(opts.ReportPath))));
                    return 1;
                }
            }
            return 0;
        }

        private static int SkippedCount(AutoProbeResult result)
        {
            return result.SkippedTotal;
        }

        private static void PrintPlan(AutoProbeResult result)
        {
            Console.WriteLine(L10n.F("cli.attr.plan", result.PlannedCount, SkippedCount(result)));
            foreach (AutoProbePlanItem item in result.Plan)
            {
                if (item.WillPress)
                {
                    Console.WriteLine(L10n.F("cli.attr.willPress", item.Combo.Display()));
                }
                else if (item.Combo == null)
                {
                    Console.WriteLine(L10n.F("auto.log.skipSummary", item.SkipReason));
                }
                else
                {
                    Console.WriteLine(L10n.F("cli.attr.skip", item.Combo.Display(), item.SkipReason));
                }
            }
        }

        /// <summary>--listen：控制台模式下跑引擎 B，按秒数输出归属记录表格。</summary>
        private static int RunListen(CliOptions opts)
        {
            int seconds = opts.Seconds <= 0 ? 30 : opts.Seconds;
            Console.WriteLine(L10n.F("cli.listen.title", AppInfo.Title, AppInfo.Version));
            Console.WriteLine(L10n.F("cli.listen.meta", seconds,
                              ProcessIntegrity.Describe(ProcessIntegrity.Current),
                              AppInfo.IsAdministrator() ? L10n.T("common.yes") : L10n.T("common.no")));
            string warn = ProcessIntegrity.WarningText();
            if (warn != null) Console.WriteLine(L10n.F("cli.listen.note", warn));
            if (!AppInfo.IsAdministrator()) Console.WriteLine(L10n.T("cli.listen.nonAdminHint"));
            Console.WriteLine(new string('-', 72));

            var engine = new AttributionEngine();
            engine.Logged += delegate (string line) { Console.WriteLine("  " + line); };
            engine.RecordReceived += delegate (AttributionRecord r)
            {
                Console.WriteLine(string.Format("{0}  {1,-22} PID {2,-7} {3,-3} {4}{5}",
                    r.Time.ToString("HH:mm:ss"),
                    r.Combo == null ? L10n.T("attr.unknownCombo") : r.Combo.Display(),
                    r.Pid,
                    string.IsNullOrEmpty(r.Bitness) ? "?" : r.Bitness,
                    r.ProcessName == null ? L10n.T("attr.unknownProc") : r.ProcessName,
                    r.IsElevated ? L10n.T("attr.elevatedTag") : ""));
                if (!string.IsNullOrEmpty(r.ProcessPath)) Console.WriteLine(L10n.F("cli.listen.pathPrefix", r.ProcessPath));
            };

            if (!engine.Start())
            {
                Console.Error.WriteLine(L10n.F("cli.listen.startFailed", engine.LastError));
                return 1;
            }

            Console.WriteLine(L10n.F("cli.listen.installed", engine.HookHandle.ToInt64().ToString("X")));
            Console.WriteLine(L10n.T("cli.listen.hint"));

            DateTime deadline = DateTime.Now.AddSeconds(seconds);
            while (DateTime.Now < deadline)
            {
                System.Threading.Thread.Sleep(200);
            }

            engine.Stop();
            AttributionRecord[] records = engine.Records;
            Console.WriteLine(new string('-', 72));
            Console.WriteLine(L10n.F("cli.listen.total", records.Length));
            if (records.Length == 0)
            {
                Console.WriteLine(L10n.T("cli.listen.empty1"));
                Console.WriteLine(L10n.T("cli.listen.empty2"));
            }
            Console.WriteLine(L10n.T("cli.listen.unhooked"));
            return 0;
        }

        /// <summary>
        /// --monitor：控制台模式的按键监视。会安装 WH_KEYBOARD_LL（显式命令，允许），
        /// 只观察、不吞键；不安装引擎 B 的全局消息钩子，所以全局热键结论会诚实地标为「无法确定」。
        /// </summary>
        private static int RunMonitor(CliOptions opts)
        {
            int seconds = opts.Seconds <= 0 ? 15 : opts.Seconds;
            Console.WriteLine(L10n.F("cli.monitor.title", AppInfo.Title, AppInfo.Version));
            Console.WriteLine(L10n.F("cli.monitor.meta", seconds,
                              ProcessIntegrity.Describe(ProcessIntegrity.Current),
                              AppInfo.IsAdministrator() ? L10n.T("common.yes") : L10n.T("common.no")));
            string warn = ProcessIntegrity.WarningText();
            if (warn != null) Console.WriteLine(L10n.F("cli.listen.note", warn));
            Console.WriteLine(new string('-', 72));

            var monitor = new KeyMonitor();
            monitor.Logged += delegate (string line) { Console.WriteLine("  " + line); };
            monitor.KeyEvent += delegate (KeyLayerEvent e)
            {
                Console.WriteLine(L10n.F("cli.monitor.event",
                    e.Time.ToString("HH:mm:ss.fff"), e.LayerName, e.KeyName, e.Vk, e.ScanCode,
                    e.FlagsText, e.Description));
            };
            monitor.VerdictReady += delegate (KeyMonitorVerdictRecord r)
            {
                Console.WriteLine(L10n.F("cli.monitor.verdict",
                    r.Time.ToString("HH:mm:ss"),
                    r.VerdictName,
                    r.Combo == null ? VkNames.Name(r.Vk) : r.Combo.Display(),
                    r.LayersText,
                    r.Note));
            };
            monitor.ModifiersChanged += delegate (ModifierSnapshot[] states)
            {
                var stuck = new System.Collections.Generic.List<string>();
                foreach (ModifierSnapshot s in states)
                    if (s != null && s.Stuck) stuck.Add(s.Name + " " + s.StateText);
                if (stuck.Count > 0)
                    Console.WriteLine(L10n.F("cli.monitor.stuckSome", string.Join("；", stuck.ToArray())));
            };
            monitor.IsGlobalHotkey = delegate (KeyCombo combo)
            {
                if (!KeyMonitorLogic.LooksLikeGlobalHotkey(combo)) return false;
                var mods = new System.Collections.Generic.List<uint>();
                if (combo.Alt) mods.Add(NativeHotkey.MOD_ALT);
                if (combo.Ctrl) mods.Add(NativeHotkey.MOD_CONTROL);
                if (combo.Shift) mods.Add(NativeHotkey.MOD_SHIFT);
                if (combo.Win) mods.Add(NativeHotkey.MOD_WIN);
                uint err;
                return OccupancyScanner.Probe(mods.ToArray(), combo.Vk, out err);
            };

            if (!monitor.Start())
            {
                Console.Error.WriteLine(L10n.F("cli.monitor.startFailed",
                    monitor.LastError ?? L10n.T("common.unknown")));
                return 1;
            }

            try
            {
                if (monitor.RawReady)
                    Console.WriteLine(L10n.F("cli.monitor.rawReady", monitor.RawWindow.ToInt64().ToString("X")));
                else
                    Console.WriteLine(L10n.F("cli.monitor.rawFailed", monitor.LastError ?? L10n.T("common.unknown")));
                if (monitor.HookReady)
                    Console.WriteLine(L10n.F("cli.monitor.hookReady", monitor.HookHandle.ToInt64().ToString("X")));
                else
                    Console.WriteLine(L10n.F("cli.monitor.hookFailed", monitor.LastError ?? L10n.T("common.unknown")));
                Console.WriteLine(L10n.T("cli.monitor.windowNote"));
                Console.WriteLine(L10n.F("cli.monitor.waiting", seconds));
                Console.WriteLine(new string('-', 72));

                DateTime deadline = DateTime.Now.AddSeconds(seconds);
                while (DateTime.Now < deadline)
                {
                    System.Threading.Thread.Sleep(150);
                }

                monitor.Stop();

                KeyLayerEvent[] events = monitor.Events;
                int raw = 0, hook = 0, window = 0;
                foreach (KeyLayerEvent e in events)
                {
                    if (e.Layer == KeyMonitorLayer.RawInput) raw++;
                    else if (e.Layer == KeyMonitorLayer.LowLevelHook) hook++;
                    else window++;
                }
                Console.WriteLine(new string('-', 72));
                Console.WriteLine(L10n.F("cli.monitor.summary", raw, hook, window, monitor.Verdicts.Length));
                if (events.Length == 0) Console.WriteLine(L10n.T("cli.monitor.noEvents"));

                PrintMonitorReadOnlyInfo();

                ModifierSnapshot[] modifiers = monitor.CheckModifiers();
                var stuckFinal = new System.Collections.Generic.List<string>();
                foreach (ModifierSnapshot s in modifiers)
                    if (s != null && s.Stuck) stuckFinal.Add(s.Name + " " + s.StateText);
                Console.WriteLine(stuckFinal.Count == 0
                    ? L10n.T("cli.monitor.stuckNone")
                    : L10n.F("cli.monitor.stuckSome", string.Join("；", stuckFinal.ToArray())));
                Console.WriteLine(L10n.T("cli.monitor.unhooked"));
                return 0;
            }
            finally
            {
                try { monitor.Stop(); }
                catch (Exception) { }
                try { monitor.Dispose(); }
                catch (Exception) { }
            }
        }

        private static void PrintMonitorReadOnlyInfo()
        {
            try
            {
                var map = KeyMonitor.ReadScancodeMap();
                if (map.Count == 0) Console.WriteLine(L10n.T("cli.monitor.scancodeMapEmpty"));
                else
                {
                    Console.WriteLine(L10n.F("cli.monitor.scancodeMap", map.Count));
                    foreach (ScanCodeMapping m in map)
                    {
                        Console.WriteLine(L10n.F("cli.monitor.scancodeLine",
                            m.InputName, m.OutputName, m.Input, m.Output));
                    }
                }
            }
            catch (Exception) { }

            try
            {
                var drivers = KeyMonitor.ReadKeyboardDrivers();
                Console.WriteLine(L10n.F("cli.monitor.drivers", drivers.Count));
                foreach (KeyboardFilterDriver d in drivers)
                {
                    Console.WriteLine(L10n.F("cli.monitor.driverLine",
                        d.Name, string.IsNullOrEmpty(d.Group) ? L10n.T("common.none") : d.Group,
                        d.StartName, d.ImagePath));
                }
            }
            catch (Exception) { }
        }

        /// <summary>
        /// winexe 程序本身没有控制台。若从命令行启动，附加到父进程的控制台，
        /// 使 --scan / --selftest 的输出可见。
        ///
        /// 关键顺序：先判断标准输出是否已经有效（例如被 cmd 用 "> file" 重定向）。
        /// 如果已经被重定向，就绝不能再 AttachConsole —— 那会把标准句柄换成控制台的，
        /// 结果输出会跑进一个黑窗口，而调用方（批处理、CI）拿到的文件是空的。
        /// </summary>
        public static void PrepareConsole()
        {
            try
            {
                bool already = Console.IsOutputRedirected;
                if (!already)
                {
                    IntPtr probe = GetStdHandle(STD_OUTPUT_HANDLE);
                    already = probe != IntPtr.Zero && probe != new IntPtr(-1);
                }
                if (!already)
                {
                    try { AttachConsole(-1); }
                    catch (Exception) { }
                }
            }
            catch (Exception) { }

            try
            {
                IntPtr handle = GetStdHandle(STD_OUTPUT_HANDLE);
                bool valid = handle != IntPtr.Zero && handle != new IntPtr(-1);
                if (!valid)
                {
                    Console.SetOut(TextWriter.Null);
                    Console.SetError(TextWriter.Null);
                    return;
                }
                var writer = new StreamWriter(Console.OpenStandardOutput(), new UTF8Encoding(false));
                writer.AutoFlush = true;
                Console.SetOut(writer);
                var errWriter = new StreamWriter(Console.OpenStandardError(), new UTF8Encoding(false));
                errWriter.AutoFlush = true;
                Console.SetError(errWriter);
            }
            catch (Exception)
            {
                try { Console.SetOut(TextWriter.Null); }
                catch (Exception) { }
            }
        }

        private static void PrintHelp()
        {
            Console.WriteLine(AppInfo.Title + " " + AppInfo.Version + " —— " + AppInfo.Description);
            Console.WriteLine(AppInfo.VersionNote);
            Console.WriteLine();
            Console.WriteLine(L10n.T("cli.usage.title"));
            Console.WriteLine(L10n.T("cli.help.gui"));
            Console.WriteLine(L10n.T("cli.help.scan"));
            Console.WriteLine(L10n.T("cli.help.scanDesc"));
            Console.WriteLine(L10n.T("cli.help.listen"));
            Console.WriteLine(L10n.T("cli.help.listenDesc"));
            Console.WriteLine(L10n.T("cli.help.monitor"));
            Console.WriteLine(L10n.T("cli.help.monitorDesc"));
            Console.WriteLine(L10n.T("cli.help.attributeAll"));
            Console.WriteLine(L10n.T("cli.help.attributeAllDesc"));
            Console.WriteLine(L10n.T("cli.help.selftest"));
            Console.WriteLine(L10n.T("cli.help.selftestDesc"));
            Console.WriteLine(L10n.T("cli.help.elevate"));
            Console.WriteLine(L10n.T("cli.help.elevateDesc"));
            Console.WriteLine(L10n.T("cli.help.lang"));
            Console.WriteLine(L10n.T("cli.help.theme"));
            Console.WriteLine(L10n.T("cli.help.helper"));
            Console.WriteLine(L10n.T("cli.help.screenshot"));
            Console.WriteLine(L10n.T("cli.help.help"));
            Console.WriteLine();
            Console.WriteLine(L10n.T("cli.help.notesTitle"));
            Console.WriteLine(L10n.T("cli.help.note.report"));
            Console.WriteLine(L10n.T("cli.help.note.readonly"));
            Console.WriteLine(L10n.T("cli.help.note.hook"));
        }

        private static int RunScan(CliOptions opts)
        {
            var log = new LogSink();
            log.LineAdded += delegate (string line) { Console.WriteLine(line); };

            ScanSnapshot snapshot;
            try
            {
                snapshot = ScanRunner.Run(log, null, System.Threading.CancellationToken.None);
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine(L10n.F("cli.scan.failed", ex));
                return 1;
            }

            string reportPath = opts.ReportPath;
            bool explicitPath = !string.IsNullOrEmpty(reportPath);
            if (!explicitPath)
            {
                reportPath = Path.Combine(Environment.CurrentDirectory, ReportBuilder.DefaultFileName());
            }

            try
            {
                ReportBuilder.Write(snapshot, reportPath);
                Console.WriteLine(L10n.F("cli.scan.reportWritten", Path.GetFullPath(reportPath)));
            }
            catch (Exception ex)
            {
                if (explicitPath)
                {
                    Console.Error.WriteLine(L10n.F("cli.scan.reportWriteFailed", ex.Message));
                    Console.Error.WriteLine(WritabilityHint(Path.GetDirectoryName(Path.GetFullPath(reportPath))));
                    return 1;
                }
                // 默认路径写不进去时，退回到能写的临时目录
                Console.Error.WriteLine(L10n.F("cli.scan.defaultDirFailed", ex.Message));
                try
                {
                    string fallbackDir = TempWorkspace.Acquire("HotkeyHound");
                    reportPath = Path.Combine(fallbackDir, ReportBuilder.DefaultFileName());
                    ReportBuilder.Write(snapshot, reportPath);
                    Console.WriteLine(L10n.F("cli.scan.reportWritten", Path.GetFullPath(reportPath)));
                }
                catch (Exception ex2)
                {
                    Console.Error.WriteLine(L10n.F("cli.scan.reportWriteFailed", ex2.Message));
                    return 1;
                }
            }

            if (!string.IsNullOrEmpty(opts.JsonPath))
            {
                try
                {
                    JsonDump.Write(snapshot, opts.JsonPath);
                    Console.WriteLine(L10n.F("cli.scan.jsonWritten", Path.GetFullPath(opts.JsonPath)));
                }
                catch (Exception ex)
                {
                    Console.Error.WriteLine(L10n.F("cli.scan.jsonWriteFailed", ex.Message));
                    Console.Error.WriteLine(WritabilityHint(Path.GetDirectoryName(Path.GetFullPath(opts.JsonPath))));
                    return 1;
                }
            }

            Console.WriteLine(L10n.F("cli.scan.summary",
                snapshot.OccupiedCount, snapshot.FreeCount, snapshot.DeclaredCount, snapshot.Truth.Rows.Count));
            return 0;
        }

        /// <summary>
        /// 本机的工作区目录带 Low 完整性标签，从里面启动的进程会以低完整性运行，
        /// 想写 %TEMP% 之类的中完整性目录会被拒绝——给出可执行的提示。
        /// </summary>
        private static string WritabilityHint(string directory)
        {
            string target = string.IsNullOrEmpty(directory) ? "" : L10n.F("cli.writable.target", directory);
            return L10n.F("cli.writable.hint", target);
        }
    }

    /// <summary>--json 的极简输出（手写序列化，避免引入依赖）。</summary>
    public static class JsonDump
    {
        public static void Write(ScanSnapshot s, string path)
        {
            var sb = new StringBuilder();
            sb.Append("{\n");
            sb.Append("  \"generatedAt\": \"").Append(Esc(s.GeneratedAt.ToString("s"))).Append("\",\n");
            sb.Append("  \"machine\": \"").Append(Esc(s.MachineName)).Append("\",\n");
            sb.Append("  \"isAdministrator\": ").Append(s.IsAdministrator ? "true" : "false").Append(",\n");
            sb.Append("  \"version\": \"").Append(Esc(s.Version)).Append("\",\n");
            sb.Append("  \"lang\": \"").Append(Esc(L10n.Current)).Append("\",\n");
            sb.Append("  \"counts\": { \"occupied\": ").Append(s.OccupiedCount)
              .Append(", \"free\": ").Append(s.FreeCount)
              .Append(", \"declared\": ").Append(s.DeclaredCount)
              .Append(", \"truthRows\": ").Append(s.Truth.Rows.Count).Append(" },\n");

            sb.Append("  \"declared\": [\n");
            for (int i = 0; i < s.Config.Declared.Count; i++)
            {
                DeclaredHotkey d = s.Config.Declared[i];
                sb.Append("    { \"combo\": \"").Append(Esc(d.Combo.Display())).Append("\", \"software\": \"").Append(Esc(d.Software))
                  .Append("\", \"module\": \"").Append(Esc(d.Module)).Append("\", \"purpose\": \"").Append(Esc(d.Purpose))
                  .Append("\", \"raw\": \"").Append(Esc(d.RawValue)).Append("\", \"source\": \"").Append(Esc(d.SourcePath))
                  .Append("\" }").Append(i + 1 < s.Config.Declared.Count ? "," : "").Append("\n");
            }
            sb.Append("  ],\n");

            sb.Append("  \"occupied\": [\n");
            var occupied = new List<TruthRow>();
            foreach (TruthRow r in s.Truth.Rows)
            {
                if (r.Kind == TruthKind.MatchesDeclared || r.Kind == TruthKind.RegisteredButUnowned
                    || r.Kind == TruthKind.SystemReserved) occupied.Add(r);
            }
            for (int i = 0; i < occupied.Count; i++)
            {
                TruthRow r = occupied[i];
                sb.Append("    { \"combo\": \"").Append(Esc(r.Combo.Display()))
                  .Append("\", \"kind\": \"").Append(r.Kind.ToString())
                  .Append("\", \"declaredBy\": \"").Append(Esc(r.DeclaredBy))
                  .Append("\", \"ownerHint\": \"").Append(Esc(r.OwnerHint))
                  .Append("\" }").Append(i + 1 < occupied.Count ? "," : "").Append("\n");
            }
            sb.Append("  ]\n}\n");

            string dir = Path.GetDirectoryName(Path.GetFullPath(path));
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir)) Directory.CreateDirectory(dir);
            File.WriteAllText(path, sb.ToString(), new UTF8Encoding(true));
        }

        private static string Esc(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            var sb = new StringBuilder(s.Length + 8);
            foreach (char c in s)
            {
                switch (c)
                {
                    case '"': sb.Append("\\\""); break;
                    case '\\': sb.Append("\\\\"); break;
                    case '\r': sb.Append("\\r"); break;
                    case '\n': sb.Append("\\n"); break;
                    case '\t': sb.Append("\\t"); break;
                    default:
                        if (c < 32) sb.Append("\\u").Append(((int)c).ToString("x4", CultureInfo.InvariantCulture));
                        else sb.Append(c);
                        break;
                }
            }
            return sb.ToString();
        }
    }
}
