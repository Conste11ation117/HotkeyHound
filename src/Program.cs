using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
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
        public bool Helper;
        public bool NoHooks;
        public bool AttributeAll;
        public bool Yes;
        public bool DryRun;
        public int Max = AutoProbe.DefaultMax;
        /// <summary>文档截图模式：给出目录即启用（渲染五个页面后退出，不安装任何钩子）。</summary>
        public string ScreenshotDir;
        public bool Elevate;
        public string Lang;
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

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool AttachConsole(int dwProcessId);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr GetStdHandle(int nStdHandle);

        [STAThread]
        public static int Main(string[] args)
        {
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
                return 1;
            }
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

            if (opts.Helper)
            {
                PrepareConsole();
                return HelperHost.Run(args);
            }

            // 无参数：启动图形界面
            try
            {
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                Application.Run(new HotkeyHound.Ui.MainForm(opts.ScreenshotDir));
                return 0;
            }
            catch (Exception ex)
            {
                try
                {
                    MessageBox.Show(L10n.F("cli.except", ex), AppInfo.Title,
                                    MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
                catch (Exception) { }
                return 1;
            }
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
                Console.WriteLine(string.Format("{0}  {1,-22} PID {2,-7} {3}{4}",
                    r.Time.ToString("HH:mm:ss"),
                    r.Combo == null ? L10n.T("attr.unknownCombo") : r.Combo.Display(),
                    r.Pid,
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
            Console.WriteLine(L10n.T("cli.help.attributeAll"));
            Console.WriteLine(L10n.T("cli.help.attributeAllDesc"));
            Console.WriteLine(L10n.T("cli.help.selftest"));
            Console.WriteLine(L10n.T("cli.help.selftestDesc"));
            Console.WriteLine(L10n.T("cli.help.elevate"));
            Console.WriteLine(L10n.T("cli.help.elevateDesc"));
            Console.WriteLine(L10n.T("cli.help.lang"));
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
                if (r.Kind == TruthKind.MatchesDeclared || r.Kind == TruthKind.RegisteredButUnowned) occupied.Add(r);
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
