using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;

namespace HotkeyHound.Core
{
    /// <summary>
    /// 释放内嵌的 32 位产物：
    ///   - 32 位 hook DLL → %LOCALAPPDATA%\HotkeyHound\hooks\x86\HotkeyHound.Hook32.dll
    ///   - 32 位宿主 exe → %LOCALAPPDATA%\HotkeyHound\HotkeyHound.Helper32.exe
    /// 每个产物独立按可写性回退（exe 同目录、%TEMP%），与 64 位 hook DLL 的策略一致。
    /// </summary>
    public static class Helper32Locator
    {
        public const string DllResourceName = "HotkeyHound.Hook32.dll";
        public const string ExeResourceName = "HotkeyHound.Helper32.exe";
        public const string DllFileName = "HotkeyHound.Hook32.dll";
        public const string ExeFileName = "HotkeyHound.Helper32.exe";

        private const int ERROR_ACCESS_DENIED = 5;
        private const int ERROR_SHARING_VIOLATION = 32;

        private static string _cachedDll;
        private static string _cachedExe;
        private static readonly List<string> _notes = new List<string>();

        public static string[] Notes { get { return _notes.ToArray(); } }
        public static string LastError { get; private set; }

        public static string ExtractDll()
        {
            return Extract(DllResourceName, DllFileName, Path.Combine("hooks", "x86"), ref _cachedDll);
        }

        public static string ExtractExe()
        {
            return Extract(ExeResourceName, ExeFileName, null, ref _cachedExe);
        }

        private static List<string> CandidateDirectories(string subDir)
        {
            var list = new List<string>();
            try
            {
                string la = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
                if (!string.IsNullOrEmpty(la))
                {
                    string baseDir = Path.Combine(la, "HotkeyHound");
                    list.Add(string.IsNullOrEmpty(subDir) ? baseDir : Path.Combine(baseDir, subDir));
                }
            }
            catch (Exception) { }

            try
            {
                string exeDir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
                if (!string.IsNullOrEmpty(exeDir))
                {
                    list.Add(string.IsNullOrEmpty(subDir) ? exeDir : Path.Combine(exeDir, subDir));
                }
            }
            catch (Exception) { }

            try
            {
                string tmp = Path.GetTempPath();
                if (!string.IsNullOrEmpty(tmp))
                {
                    string baseDir = Path.Combine(tmp, "HotkeyHound");
                    list.Add(string.IsNullOrEmpty(subDir) ? baseDir : Path.Combine(baseDir, subDir));
                }
            }
            catch (Exception) { }

            return list;
        }

        private static byte[] ReadResource(string name)
        {
            try
            {
                Assembly asm = Assembly.GetExecutingAssembly();
                using (Stream s = asm.GetManifestResourceStream(name))
                {
                    if (s == null) return null;
                    var buffer = new byte[s.Length];
                    int read = 0;
                    while (read < buffer.Length)
                    {
                        int n = s.Read(buffer, read, buffer.Length - read);
                        if (n <= 0) break;
                        read += n;
                    }
                    if (read != buffer.Length) return null;
                    return buffer;
                }
            }
            catch (Exception ex)
            {
                LastError = L10n.F("helper32.note.resourceFailed", name, ex.Message);
                return null;
            }
        }

        private static string Extract(string resourceName, string fileName, string subDir, ref string cache)
        {
            if (cache != null && File.Exists(cache))
            {
                // 命中缓存也要清一次：否则上一次释放失败留下的 notes/LastError 会继续显示。
                _notes.Clear();
                LastError = null;
                return cache;
            }
            _notes.Clear();
            LastError = null;

            byte[] bytes = ReadResource(resourceName);
            if (bytes == null || bytes.Length == 0)
            {
                if (LastError == null) LastError = L10n.F("helper32.note.noResource", resourceName);
                return null;
            }

            foreach (string dir in CandidateDirectories(subDir))
            {
                try
                {
                    Directory.CreateDirectory(dir);
                    string target = Path.Combine(dir, fileName);

                    bool needWrite = true;
                    if (File.Exists(target))
                    {
                        // 与 64 位 DLL 同理：只比长度会把「同长度的旧版本」当成最新，必须比内容。
                        try
                        {
                            byte[] existing = File.ReadAllBytes(target);
                            if (existing.Length == bytes.Length && SameBytes(existing, bytes)) needWrite = false;
                        }
                        catch (Exception) { }
                    }
                    if (needWrite)
                    {
                        // 覆盖前先确保没有别的进程正加载着它（否则会抛 IOException）。
                        if (File.Exists(target))
                        {
                            try
                            {
                                File.Delete(target);
                            }
                            catch (Exception ex)
                            {
                                int code = ex.HResult & 0xFFFF;
                                if (code == ERROR_SHARING_VIOLATION || code == ERROR_ACCESS_DENIED)
                                {
                                    // 文件被占用时换目录会留下两份不同版本的产物，下一次运行就更难解释；
                                    // 这里必须明确失败，让调用方把「谁还加载着旧 DLL」报给用户。
                                    LastError = L10n.F("helper32.note.dirFailed", target,
                                        "Win32=" + code + " " + ex.GetType().Name + "：" + ex.Message);
                                    _notes.Add(LastError);
                                    return null;
                                }
                            }
                        }
                        File.WriteAllBytes(target, bytes);
                    }

                    string probe = Path.Combine(dir, ".hs32-probe");
                    File.WriteAllText(probe, "ok");
                    File.Delete(probe);

                    cache = target;
                    _notes.Add(L10n.F("helper32.note.extracted", target, bytes.Length));
                    return target;
                }
                catch (Exception ex)
                {
                    int code = ex.HResult & 0xFFFF;
                    string detail = "Win32=" + code + " " + ex.GetType().Name + "：" + ex.Message;
                    if (code == ERROR_SHARING_VIOLATION || code == ERROR_ACCESS_DENIED)
                    {
                        LastError = L10n.F("helper32.note.dirFailed", dir, detail);
                        _notes.Add(LastError);
                        return null;
                    }
                    _notes.Add(L10n.F("helper32.note.dirFailed", dir, detail));
                }
            }

            LastError = L10n.F("helper32.note.allFailed",
                _notes.Count > 0 ? _notes[_notes.Count - 1] : L10n.T("common.none"));
            return null;
        }

        private static bool SameBytes(byte[] a, byte[] b)
        {
            if (a == null || b == null || a.Length != b.Length) return false;
            for (int i = 0; i < a.Length; i++)
            {
                if (a[i] != b[i]) return false;
            }
            return true;
        }
    }

    /// <summary>
    /// 32 位伴随宿主的生命周期管理器。
    ///
    /// Windows 只允许钩子注入与安装者同位数的进程，因此 32 位进程的归属必须由另一个
    /// 32 位进程装 32 位 hook DLL 来覆盖。这个类负责释放并启动 HotkeyHound.Helper32.exe、
    /// 收集它的就绪/错误输出，并在 Stop 时确保它退出（stdin 送 q → 等 2 秒 → Kill）。
    /// 它是本程序自己拉起的子进程，结束它不违反「绝不结束别人的进程」。
    ///
    /// 另有一层强制回收：父进程把宿主放进 KILL_ON_JOB_CLOSE 的 Job Object，父进程被强杀时
    /// 系统会替我们结束宿主；宿主自己等待父进程句柄的逻辑保留为第二道防线。
    /// </summary>
    public sealed class Helper32Runtime : IDisposable
    {
        private const int JobObjectExtendedLimitInformation = 9;
        private const uint JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE = 0x00002000;

        private static readonly object _jobGate = new object();
        private static IntPtr _jobHandle = IntPtr.Zero;
        private static bool _jobFailed;
        private static string _jobFailure;

        private readonly object _gate = new object();
        private readonly object _lifecycle = new object();
        private ManualResetEventSlim _readyEvent = new ManualResetEventSlim(false);

        private Process _process;
        private Thread _reader;
        private Thread _errReader;
        private volatile Action<string> _log;

        private bool _ready;
        private bool _hookInstalled;
        private int _pid;
        private long _hwnd;
        private string _lastLine;
        private string _readyLine;
        private string _error;
        private volatile string _exePath;
        private volatile string _dllPath;
        private string _ctlLine;

        public bool IsRunning
        {
            get
            {
                Process p;
                lock (_gate) p = _process;
                if (p == null) return false;
                try { return !p.HasExited; }
                catch (Exception) { return false; }
            }
        }

        public bool Ready { get { lock (_gate) return _ready; } }
        public bool HookInstalled { get { lock (_gate) return _hookInstalled; } }
        public int Pid { get { lock (_gate) return _pid; } }
        public long WindowHandle { get { lock (_gate) return _hwnd; } }
        public string Error { get { lock (_gate) return _error; } }
        public string LastError { get { return Error; } }
        public string LastLine { get { lock (_gate) return _lastLine; } }
        public string ReadyLine { get { lock (_gate) return _readyLine; } }
        public string ExePath { get { lock (_gate) return _exePath; } }
        public string DllPath { get { lock (_gate) return _dllPath; } }

        /// <summary>界面用的短状态文本；详细原因在日志里。</summary>
        public string StatusText
        {
            get
            {
                bool running;
                bool hook;
                int pid;
                bool failed;
                lock (_gate)
                {
                    running = false;
                    if (_process != null)
                    {
                        try { running = !_process.HasExited; }
                        catch (Exception) { running = false; }
                    }
                    hook = _hookInstalled;
                    pid = _pid;
                    failed = _error != null;
                }
                if (running)
                {
                    return hook
                        ? L10n.F("helper32.ui.running", pid)
                        : L10n.T("helper32.ui.noHook");
                }
                return failed ? L10n.T("helper32.ui.failed") : L10n.T("helper32.ui.off");
            }
        }

        public bool Start(int parentPid, string extraArgs, Action<string> log)
        {
            lock (_lifecycle)
            {
                // 任何一次新的 Start 都先彻底结束旧宿主：否则旧进程成孤儿，旧 reader 也不再
                // 抽干它的 stdout，管道写满后旧宿主会卡死在 Print 里。
                StopInternal(log);

                lock (_gate)
                {
                    _log = log;
                    _error = null;
                    _lastLine = null;
                    _readyLine = null;
                    _ctlLine = null;
                    _ready = false;
                    _hookInstalled = false;
                    _pid = 0;
                    _hwnd = 0;
                    // 每次 Start 都换一个新事件实例：Stop 在旧实例上 Set 不会让下一次 WaitReady 假返回。
                    _readyEvent = new ManualResetEventSlim(false);
                }

                _exePath = Helper32Locator.ExtractExe();
                foreach (string note in Helper32Locator.Notes) Emit(note);
                if (_exePath == null)
                {
                    return Fail(L10n.F("helper32.log.extractFailed", Helper32Locator.LastError));
                }

                _dllPath = Helper32Locator.ExtractDll();
                foreach (string note in Helper32Locator.Notes) Emit(note);
                if (_dllPath == null)
                {
                    return Fail(L10n.F("helper32.log.extractFailed", Helper32Locator.LastError));
                }

                var psi = new ProcessStartInfo();
                psi.FileName = _exePath;
                psi.Arguments = "--dll \"" + _dllPath + "\" --parent " + parentPid
                                + (string.IsNullOrEmpty(extraArgs) ? "" : " " + extraArgs);
                psi.UseShellExecute = false;
                psi.RedirectStandardOutput = true;
                psi.RedirectStandardError = true;
                psi.RedirectStandardInput = true;
                psi.CreateNoWindow = true;
                psi.WorkingDirectory = Path.GetDirectoryName(_exePath);
                try
                {
                    // 宿主自己会把 stdout 设成 UTF-8；两边统一，避免中文路径/异常信息变成乱码。
                    psi.StandardOutputEncoding = new UTF8Encoding(false);
                    psi.StandardErrorEncoding = new UTF8Encoding(false);
                }
                catch (Exception) { }

                Process started;
                try
                {
                    started = Process.Start(psi);
                }
                catch (Exception ex)
                {
                    return Fail(L10n.F("helper32.log.startFailed", ex.GetType().Name + "：" + ex.Message));
                }
                if (started == null)
                {
                    return Fail(L10n.F("helper32.log.startFailed", L10n.T("helper32.processStartNull")));
                }

                int pid = 0;
                try { pid = started.Id; } catch (Exception) { }
                lock (_gate)
                {
                    _process = started;
                    _pid = pid;
                }
                Emit(L10n.F("helper32.log.started", pid, _exePath));

                // 系统级兜底：父进程被强杀时 Job 句柄关闭，宿主由内核回收。
                AssignToJob(started);

                try
                {
                    lock (_gate)
                    {
                        _reader = new Thread(ReadLoop);
                        _reader.IsBackground = true;
                        _reader.Name = "HotkeyHound-Helper32-Reader";
                        _reader.Start(started);

                        _errReader = new Thread(StdErrLoop);
                        _errReader.IsBackground = true;
                        _errReader.Name = "HotkeyHound-Helper32-StdErr";
                        _errReader.Start(started);
                    }
                }
                catch (Exception ex)
                {
                    StopInternal(null);
                    return Fail(L10n.F("helper32.log.startFailed", ex.GetType().Name + "：" + ex.Message));
                }
                return true;
            }
        }

        /// <summary>等宿主报出 HELPER32_READY；超时或钩子失败都只返回 false，不抛异常。</summary>
        public bool WaitReady(int timeoutMs, Action<string> log)
        {
            if (log != null) _log = log;

            ManualResetEventSlim ev;
            lock (_gate) ev = _readyEvent;
            bool signaled = false;
            try { signaled = ev.Wait(timeoutMs); }
            catch (Exception) { }

            string error;
            string lastLine;
            string ctlLine;
            int pid;
            long hwnd;
            bool hook;
            lock (_gate)
            {
                error = _error;
                lastLine = _lastLine;
                ctlLine = _ctlLine;
                pid = _pid;
                hwnd = _hwnd;
                hook = _hookInstalled;
            }

            if (error != null)
            {
                Emit(L10n.F("helper32.log.failed", error));
                return false;
            }
            if (!signaled)
            {
                Emit(L10n.F("helper32.log.timeout", timeoutMs, lastLine ?? L10n.T("common.none")));
                return false;
            }
            if (!hook)
            {
                Emit(L10n.F("helper32.log.readyNoHook", lastLine ?? L10n.T("common.none")));
                return false;
            }
            Emit(L10n.F("helper32.log.ready", pid, hwnd, ctlLine ?? L10n.T("common.none")));
            return true;
        }

        public void Stop(Action<string> log)
        {
            lock (_lifecycle)
            {
                StopInternal(log);
            }
        }

        public void Dispose()
        {
            Stop(null);
        }

        private bool Fail(string message)
        {
            lock (_gate)
            {
                _error = message;
                _ready = false;
                _hookInstalled = false;
                // 失败也是「等待结束」：WaitReady 必须立刻返回，而不是白等满超时。
                _readyEvent.Set();
            }
            Emit(message);
            return false;
        }

        private void StopInternal(Action<string> log)
        {
            if (log != null) _log = log;

            Process p;
            Thread reader;
            Thread errReader;
            lock (_gate)
            {
                p = _process;
                reader = _reader;
                errReader = _errReader;
                _process = null;
                _reader = null;
                _errReader = null;

                _ready = false;
                _hookInstalled = false;
                _pid = 0;
                _hwnd = 0;
                _lastLine = null;
                _readyLine = null;
                _ctlLine = null;
                _error = null;
                // Set 与 Reset 都只在 _gate 里做，并且只作用于本次 Start 的事件实例。
                _readyEvent.Set();
            }

            if (p == null)
            {
                JoinQuietly(reader, 600);
                JoinQuietly(errReader, 600);
                return;
            }

            int pid = 0;
            try { pid = p.Id; } catch (Exception) { }
            bool graceful = false;
            try
            {
                try
                {
                    if (!p.HasExited)
                    {
                        // 约定：stdin 收 q 就退出；关掉 stdin（父进程死亡）时宿主也会自己退出。
                        p.StandardInput.WriteLine("q");
                        p.StandardInput.Flush();
                        p.StandardInput.Close();
                    }
                }
                catch (Exception) { }

                try { graceful = p.WaitForExit(2000); }
                catch (Exception) { }

                if (!graceful)
                {
                    try { p.Kill(); }
                    catch (Exception) { }
                    try { p.WaitForExit(1500); }
                    catch (Exception) { }
                }
            }
            catch (Exception ex)
            {
                Emit(L10n.F("helper32.log.stopFailed", ex.Message));
            }
            finally
            {
                JoinQuietly(reader, 800);
                JoinQuietly(errReader, 800);
                try { p.Dispose(); }
                catch (Exception) { }
            }

            if (graceful) Emit(L10n.F("helper32.log.exited", pid));
            else Emit(L10n.F("helper32.log.killed", pid));
        }

        private static void JoinQuietly(Thread t, int milliseconds)
        {
            if (t == null) return;
            try { if (t.IsAlive) t.Join(milliseconds); }
            catch (Exception) { }
        }

        private void Emit(string message)
        {
            if (string.IsNullOrEmpty(message)) return;
            Action<string> handler = _log;
            if (handler != null)
            {
                try { handler(message); }
                catch (Exception) { }
            }
        }

        private void EmitDiagnostic(string message)
        {
            // 诊断文本本身没有对应的 L10n 键（本次改动不允许改 L10n.cs），
            // 沿用现有 helper32.log.line 前缀，且只写英文技术信息，不散落中文字面量。
            Emit(L10n.F("helper32.log.line", message));
        }

        private bool IsCurrent(Process p)
        {
            lock (_gate) return object.ReferenceEquals(_process, p);
        }

        private void ReadLoop(object state)
        {
            var p = state as Process;
            if (p == null) return;
            try
            {
                string line;
                while ((line = p.StandardOutput.ReadLine()) != null)
                {
                    bool current;
                    lock (_gate) current = object.ReferenceEquals(_process, p);
                    if (!current)
                    {
                        // 旧会话的管道也要抽干，只是不再写入当前状态/日志。
                        continue;
                    }
                    HandleLine(p, line);
                }
            }
            catch (Exception ex)
            {
                bool current;
                string message = L10n.F("helper32.log.readFailed", ex.GetType().Name + "：" + ex.Message);
                lock (_gate)
                {
                    current = object.ReferenceEquals(_process, p);
                    if (current && _error == null) _error = message;
                }
                if (current) Emit(message);
            }
            finally
            {
                bool current;
                lock (_gate)
                {
                    current = object.ReferenceEquals(_process, p);
                    // 进程退出（正常或崩溃）也要放行 WaitReady，不能让它一直等到超时。
                    if (current) _readyEvent.Set();
                }
                if (current) Emit(L10n.T("helper32.log.stdoutEnded"));
            }
        }

        /// <summary>状态写入全部在 _gate 内完成，并在锁内复核「这条输出属于当前会话」。</summary>
        private void HandleLine(Process p, string line)
        {
            lock (_gate)
            {
                if (!object.ReferenceEquals(_process, p)) return;
                _lastLine = line;
                if (line.StartsWith("HELPER32_READY", StringComparison.Ordinal))
                {
                    _pid = ExtractInt(line, "pid=", _pid);
                    _hwnd = ExtractHwnd(line, "hwnd=", 0);
                    _hookInstalled = ExtractInt(line, "hook=", 0) != 0;
                    _readyLine = line;
                    _ready = true;
                    _readyEvent.Set();
                }
                else if (line.StartsWith("HELPER32_CTL", StringComparison.Ordinal))
                {
                    _ctlLine = line;
                }
                else if (line.StartsWith("HELPER32_ERROR", StringComparison.Ordinal))
                {
                    _error = line;
                }
            }
            Emit(L10n.F("helper32.log.line", line));
        }

        private void StdErrLoop(object state)
        {
            var p = state as Process;
            if (p == null) return;
            try
            {
                string line;
                while ((line = p.StandardError.ReadLine()) != null)
                {
                    if (!IsCurrent(p)) continue;
                    Emit("[helper32:err] " + line);
                }
            }
            catch (Exception) { }
        }

        // ------------------------------------------------------------ Job Object

        private void AssignToJob(Process process)
        {
            string failure;
            IntPtr job = GetOrCreateJob(out failure);
            if (job == IntPtr.Zero)
            {
                EmitDiagnostic("job object unavailable (" + failure + "); falling back to parent PID watch");
                return;
            }
            try
            {
                if (!AssignProcessToJobObject(job, process.Handle))
                {
                    int code = Marshal.GetLastWin32Error();
                    EmitDiagnostic("AssignProcessToJobObject failed, Win32=" + code
                                   + "; falling back to parent PID watch");
                }
            }
            catch (Exception ex)
            {
                EmitDiagnostic("AssignProcessToJobObject threw " + ex.GetType().Name + ": " + ex.Message
                               + "; falling back to parent PID watch");
            }
        }

        /// <summary>Job 句柄在父进程存活期间一直持有；句柄关闭（父进程死亡）时系统结束宿主。</summary>
        private static IntPtr GetOrCreateJob(out string failure)
        {
            failure = null;
            lock (_jobGate)
            {
                if (_jobHandle != IntPtr.Zero) return _jobHandle;
                if (_jobFailed)
                {
                    failure = _jobFailure;
                    return IntPtr.Zero;
                }

                IntPtr job = CreateJobObjectW(IntPtr.Zero, null);
                if (job == IntPtr.Zero)
                {
                    failure = "CreateJobObject Win32=" + Marshal.GetLastWin32Error();
                    _jobFailed = true;
                    _jobFailure = failure;
                    return IntPtr.Zero;
                }

                var info = new JOBOBJECT_EXTENDED_LIMIT_INFORMATION();
                info.BasicLimitInformation.LimitFlags = JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE;
                int size = Marshal.SizeOf(typeof(JOBOBJECT_EXTENDED_LIMIT_INFORMATION));
                if (!SetInformationJobObject(job, JobObjectExtendedLimitInformation, ref info, size))
                {
                    failure = "SetInformationJobObject Win32=" + Marshal.GetLastWin32Error();
                    try { CloseHandle(job); }
                    catch (Exception) { }
                    _jobFailed = true;
                    _jobFailure = failure;
                    return IntPtr.Zero;
                }

                _jobHandle = job;
                return job;
            }
        }

        private static int ExtractInt(string line, string key, int fallback)
        {
            long v = ExtractLong(line, key, long.MinValue);
            return v == long.MinValue ? fallback : (int)v;
        }

        private static long ExtractLong(string line, string key, long fallback)
        {
            int i = line.IndexOf(key, StringComparison.Ordinal);
            if (i < 0) return fallback;
            i += key.Length;
            int sign = 1;
            if (i < line.Length && line[i] == '-')
            {
                sign = -1;
                i++;
            }
            int j = i;
            while (j < line.Length && char.IsDigit(line[j])) j++;
            if (j == i) return fallback;
            long v;
            if (!long.TryParse(line.Substring(i, j - i), out v)) return fallback;
            return sign * v;
        }

        /// <summary>
        /// 32 位窗口句柄按无符号十六进制输出（0x + 8 位）。旧版本打印十进制（bit31 为 1 时
        /// 会符号扩展成负数），这里兼容两种格式，避免升级期间把合法句柄读成 0。
        /// </summary>
        private static long ExtractHwnd(string line, string key, long fallback)
        {
            int i = line.IndexOf(key, StringComparison.Ordinal);
            if (i < 0) return fallback;
            i += key.Length;

            if (i + 1 < line.Length && line[i] == '0' && (line[i + 1] == 'x' || line[i + 1] == 'X'))
            {
                int start = i + 2;
                int j = start;
                while (j < line.Length && Uri.IsHexDigit(line[j])) j++;
                if (j == start) return fallback;
                ulong v;
                if (!ulong.TryParse(line.Substring(start, j - start), NumberStyles.HexNumber,
                                    System.Globalization.CultureInfo.InvariantCulture, out v))
                {
                    return fallback;
                }
                return unchecked((long)v);
            }

            return ExtractLong(line, key, fallback);
        }

        // ------------------------------------------------------------ Win32

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern IntPtr CreateJobObjectW(IntPtr jobAttributes, string name);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool SetInformationJobObject(IntPtr job, int infoClass,
            ref JOBOBJECT_EXTENDED_LIMIT_INFORMATION info, int infoLength);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool AssignProcessToJobObject(IntPtr job, IntPtr process);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool CloseHandle(IntPtr handle);

        [StructLayout(LayoutKind.Sequential)]
        private struct JOBOBJECT_BASIC_LIMIT_INFORMATION
        {
            public long PerProcessUserTimeLimit;
            public long PerJobUserTimeLimit;
            public uint LimitFlags;
            public UIntPtr MinimumWorkingSetSize;
            public UIntPtr MaximumWorkingSetSize;
            public uint ActiveProcessLimit;
            public UIntPtr Affinity;
            public uint PriorityClass;
            public uint SchedulingClass;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct IO_COUNTERS
        {
            public ulong ReadOperationCount;
            public ulong WriteOperationCount;
            public ulong OtherOperationCount;
            public ulong ReadTransferCount;
            public ulong WriteTransferCount;
            public ulong OtherTransferCount;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct JOBOBJECT_EXTENDED_LIMIT_INFORMATION
        {
            public JOBOBJECT_BASIC_LIMIT_INFORMATION BasicLimitInformation;
            public IO_COUNTERS IoInfo;
            public UIntPtr ProcessMemoryLimit;
            public UIntPtr JobMemoryLimit;
            public UIntPtr PeakProcessMemoryUsed;
            public UIntPtr PeakJobMemoryUsed;
        }
    }
}
