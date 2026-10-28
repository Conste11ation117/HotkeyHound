using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Principal;
using System.Text;
using System.Threading;

namespace HotkeyHound.Core
{
    public static class AppInfo
    {
        public const string Title = "HotkeyHound";
        public const string Version = "3.0.0";
        /// <summary>版本代号（写进发布说明与状态栏，不参与比较）。</summary>
        public const string Codename = "Tournament";

        /// <summary>版本说明（随当前语言变化）。</summary>
        public static string VersionNote { get { return L10n.T("app.versionNote"); } }

        /// <summary>一句话描述（随当前语言变化）。</summary>
        public static string Description { get { return L10n.T("app.description"); } }

        public static string MachineName
        {
            get
            {
                try { return Environment.MachineName; }
                catch (Exception) { return L10n.T("common.unknown"); }
            }
        }

        public static string UserName
        {
            get
            {
                try { return Environment.UserName; }
                catch (Exception) { return L10n.T("common.unknown"); }
            }
        }

        /// <summary>以管理员身份运行？</summary>
        public static bool IsAdministrator()
        {
            try
            {
                using (WindowsIdentity id = WindowsIdentity.GetCurrent())
                {
                    var principal = new WindowsPrincipal(id);
                    return principal.IsInRole(WindowsBuiltInRole.Administrator);
                }
            }
            catch (Exception) { return false; }
        }

        public static string Framework
        {
            get
            {
                try { return Environment.Version.ToString(); }
                catch (Exception) { return L10n.T("common.unknown"); }
            }
        }
    }

    /// <summary>线程安全的日志/事件总线，CLI 与界面共用。</summary>
    public sealed class LogSink
    {
        private readonly object _gate = new object();
        private readonly List<string> _lines = new List<string>();

        public event Action<string> LineAdded;

        public void Write(string message)
        {
            string stamped = DateTime.Now.ToString("HH:mm:ss") + "  " + message;
            lock (_gate) _lines.Add(stamped);
            Action<string> handler = LineAdded;
            if (handler != null)
            {
                try { handler(stamped); }
                catch (Exception) { }
            }
        }

        public string[] Snapshot()
        {
            lock (_gate) return _lines.ToArray();
        }

        public void WriteRange(IEnumerable<string> lines)
        {
            if (lines == null) return;
            foreach (string l in lines) Write(l);
        }
    }

    /// <summary>一次完整扫描（引擎 A + 引擎 D + 真相表）的结果。</summary>
    public sealed class ScanSnapshot
    {
        public DateTime GeneratedAt = DateTime.Now;
        public string MachineName = AppInfo.MachineName;
        public bool IsAdministrator;
        public string Version = AppInfo.Version;

        public List<OccupancyResult> Occupancy = new List<OccupancyResult>();
        public int OccupiedCount;
        public int FreeCount;
        public long ScanMilliseconds;

        public ConfigCollection Config = new ConfigCollection();
        public TruthTable Truth = new TruthTable();
        /// <summary>钩子型热键知识库与本次扫描关联出的嫌疑清单（引擎 A/D 之后构建，只读）。</summary>
        public HookCatalog Catalog;
        public List<HookSuspect> HookSuspects = new List<HookSuspect>();
        public string[] Log = new string[0];

        public int DeclaredCount
        {
            get { return Config == null ? 0 : Config.Declared.Count; }
        }
    }

    /// <summary>引擎 A + D + 真相表的完整流程，CLI 与界面共用同一份实现。</summary>
    public static class ScanRunner
    {
        public static ScanSnapshot Run(LogSink log, Action<int, int> progress, CancellationToken cancel)
        {
            var snapshot = new ScanSnapshot();
            snapshot.IsAdministrator = AppInfo.IsAdministrator();

            var scanner = new OccupancyScanner();
            log.Write(L10n.F("scan.engineA.start", OccupancyScanner.TotalCombos));
            var started = DateTime.Now;
            snapshot.Occupancy = scanner.Scan(delegate (int done, int total)
            {
                if (cancel.IsCancellationRequested) throw new OperationCanceledException();
                if (progress != null) progress(done, total);
            });
            snapshot.ScanMilliseconds = (long)(DateTime.Now - started).TotalMilliseconds;

            snapshot.OccupiedCount = OccupancyScanner.Occupied(snapshot.Occupancy).Count;
            snapshot.FreeCount = snapshot.Occupancy.Count - snapshot.OccupiedCount;
            log.Write(L10n.F("scan.engineA.done",
                             snapshot.OccupiedCount, snapshot.FreeCount, snapshot.ScanMilliseconds));

            log.Write(L10n.T("scan.engineD.start"));
            var collector = new ConfigCollector(delegate (string s) { log.Write(s); });
            snapshot.Config = collector.Collect();

            log.Write(L10n.T("scan.truth.building"));
            List<string> candidates = CandidateProcesses.Running();
            log.Write(L10n.F("scan.truth.candidates", candidates.Count));
            snapshot.Truth = Reconciler.Build(snapshot.Occupancy, snapshot.Config.Declared,
                                              snapshot.Config.Reserved, candidates);

            foreach (TruthKind kind in new TruthKind[]
            {
                TruthKind.MatchesDeclared, TruthKind.DeclaredButNotRegistered,
                TruthKind.RegisteredButUnowned, TruthKind.SystemReserved, TruthKind.FreeAndUndeclared
            })
            {
                log.Write(L10n.F("scan.truth.kind", TruthTable.KindName(kind), snapshot.Truth.CountOf(kind)));
            }
            log.Write(L10n.F("scan.truth.done", snapshot.Truth.Rows.Count));

            log.Write(L10n.T("scan.catalog.start"));
            try
            {
                snapshot.Catalog = HookCatalog.Load();
                foreach (string line in snapshot.Catalog.Log) log.Write(line);
                snapshot.HookSuspects = snapshot.Catalog.BuildSuspects(snapshot.Occupancy);
                log.Write(L10n.F("scan.catalog.done", snapshot.Catalog.Entries.Count, snapshot.HookSuspects.Count));
            }
            catch (Exception ex)
            {
                // 知识库坏掉不能拖垮整轮扫描：报告里会写明「本次没有钩子嫌疑数据」。
                log.Write(L10n.F("scan.catalog.failed", ex.Message));
            }

            snapshot.GeneratedAt = DateTime.Now;
            snapshot.Log = log.Snapshot();
            return snapshot;
        }
    }

    public static class Html
    {
        public static string Escape(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            var sb = new StringBuilder(s.Length + 16);
            foreach (char c in s)
            {
                switch (c)
                {
                    case '&': sb.Append("&amp;"); break;
                    case '<': sb.Append("&lt;"); break;
                    case '>': sb.Append("&gt;"); break;
                    case '"': sb.Append("&quot;"); break;
                    case '\'': sb.Append("&#39;"); break;
                    default: sb.Append(c); break;
                }
            }
            return sb.ToString();
        }
    }

    /// <summary>
    /// 找一个当前进程确实能写的临时目录。
    /// 某些工作区目录（例如带 Mandatory Label\Low 完整性标签的目录）会让从里面启动的
    /// 进程以低完整性运行，此时 %TEMP%、%LOCALAPPDATA% 等都会被拒绝写入，
    /// 因此这里逐个候选目录试写，并把失败原因记下来。
    /// </summary>
    public static class TempWorkspace
    {
        private static readonly List<string> _notes = new List<string>();

        public static string[] Notes { get { return _notes.ToArray(); } }

        public static void ClearNotes()
        {
            _notes.Clear();
        }

        public static List<string> Candidates(string prefix)
        {
            var list = new List<string>();
            try
            {
                string sys = Path.GetTempPath();
                if (!string.IsNullOrEmpty(sys)) list.Add(Path.Combine(sys, prefix));
            }
            catch (Exception) { }

            try
            {
                string la = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
                if (!string.IsNullOrEmpty(la)) list.Add(Path.Combine(la, "HotkeyHound", "temp", prefix));
            }
            catch (Exception) { }

            try
            {
                string exeDir = Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location);
                if (!string.IsNullOrEmpty(exeDir)) list.Add(Path.Combine(exeDir, prefix));
            }
            catch (Exception) { }

            try { list.Add(Path.Combine(Environment.CurrentDirectory, prefix)); }
            catch (Exception) { }

            return list;
        }

        public static string Acquire(string prefix)
        {
            string last = null;
            foreach (string candidate in Candidates(prefix))
            {
                try
                {
                    string dir = candidate + "-" + Guid.NewGuid().ToString("N").Substring(0, 8);
                    Directory.CreateDirectory(dir);
                    string probe = Path.Combine(dir, ".write-test");
                    File.WriteAllText(probe, "ok");
                    File.Delete(probe);
                    return dir;
                }
                catch (Exception ex)
                {
                    last = candidate + " → " + ex.GetType().Name + "：" + ex.Message;
                    _notes.Add(L10n.F("shstate.tempDirSkipped", last));
                }
            }
            throw new IOException(L10n.F("shstate.noWritableTemp", last));
        }

        public static bool CanWriteIn(string directory)
        {
            try
            {
                if (string.IsNullOrEmpty(directory)) return false;
                if (!Directory.Exists(directory)) return false;
                string probe = Path.Combine(directory, ".hs-write-test-" + Guid.NewGuid().ToString("N").Substring(0, 6));
                File.WriteAllText(probe, "ok");
                File.Delete(probe);
                return true;
            }
            catch (Exception) { return false; }
        }
    }

    /// <summary>
    /// 引擎 B/C 的会话结果容器（线程安全）。
    /// 界面在监听/布防过程中写入，报告生成时读取；--scan 模式下没有任何写入，
    /// 报告里的 #attribution / #hookcheck 会优雅降级成「本次未采集」。
    /// </summary>
    public static class SessionRecords
    {
        private static readonly object _gate = new object();
        private static readonly List<AttributionRecord> _attribution = new List<AttributionRecord>();
        private static readonly List<HookCheckRecord> _hookRecords = new List<HookCheckRecord>();
        private static readonly List<ComparisonSnapshot> _comparisons = new List<ComparisonSnapshot>();
        private static AutoProbeResult _autoProbe;

        public static void AddAttribution(AttributionRecord record)
        {
            if (record == null) return;
            lock (_gate) _attribution.Add(record);
        }

        public static void AddHookRecord(HookCheckRecord record)
        {
            if (record == null) return;
            lock (_gate) _hookRecords.Add(record);
        }

        public static void AddComparison(ComparisonSnapshot snapshot)
        {
            if (snapshot == null) return;
            lock (_gate) _comparisons.Add(snapshot);
        }

        public static AttributionRecord[] Attribution()
        {
            lock (_gate) return _attribution.ToArray();
        }

        public static HookCheckRecord[] HookRecords()
        {
            lock (_gate) return _hookRecords.ToArray();
        }

        public static ComparisonSnapshot[] Comparisons()
        {
            lock (_gate) return _comparisons.ToArray();
        }

        public static void SetAutoProbe(AutoProbeResult result)
        {
            lock (_gate) _autoProbe = result;
        }

        public static AutoProbeResult AutoProbe()
        {
            lock (_gate) return _autoProbe;
        }

        public static void ClearAutoProbe()
        {
            lock (_gate) _autoProbe = null;
        }

        public static void ClearAttribution()
        {
            lock (_gate) _attribution.Clear();
        }

        public static void ClearHookRecords()
        {
            lock (_gate) _hookRecords.Clear();
        }
    }
}
