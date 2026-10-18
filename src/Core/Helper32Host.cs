using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Reflection;
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
            if (cache != null && File.Exists(cache)) return cache;
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
                        // 覆盖前先确保没有别的进程正加载着它（否则会抛 IOException）
                        try { File.Delete(target); }
                        catch (Exception) { }
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
                    _notes.Add(L10n.F("helper32.note.dirFailed", dir, ex.GetType().Name + "：" + ex.Message));
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
    /// </summary>
    public sealed class Helper32Runtime : IDisposable
    {
        private readonly object _gate = new object();
        private readonly ManualResetEventSlim _readyEvent = new ManualResetEventSlim(false);

        private Process _process;
        private Thread _reader;
        private Action<string> _log;

        private volatile bool _ready;
        private volatile bool _hookInstalled;
        private volatile int _pid;
        private long _hwnd;
        private string _lastLine;
        private string _error;
        private string _exePath;
        private string _dllPath;
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

        public bool Ready { get { return _ready; } }
        public bool HookInstalled { get { return _hookInstalled; } }
        public int Pid { get { return _pid; } }
        public long WindowHandle { get { return _hwnd; } }
        public string Error { get { return _error; } }
        public string LastLine { get { return _lastLine; } }
        public string ExePath { get { return _exePath; } }
        public string DllPath { get { return _dllPath; } }

        /// <summary>界面用的短状态文本；详细原因在日志里。</summary>
        public string StatusText
        {
            get
            {
                if (IsRunning)
                {
                    return _hookInstalled
                        ? L10n.F("helper32.ui.running", _pid)
                        : L10n.T("helper32.ui.noHook");
                }
                return _error == null ? L10n.T("helper32.ui.off") : L10n.T("helper32.ui.failed");
            }
        }

        public bool Start(int parentPid, string extraArgs, Action<string> log)
        {
            lock (_gate)
            {
                _log = log;
                _error = null;
                _lastLine = null;
                _ready = false;
                _hookInstalled = false;
                _pid = 0;
                _hwnd = 0;
                _ctlLine = null;
                _readyEvent.Reset();

                _exePath = Helper32Locator.ExtractExe();
                foreach (string note in Helper32Locator.Notes) Emit(note);
                if (_exePath == null)
                {
                    _error = L10n.F("helper32.log.extractFailed", Helper32Locator.LastError);
                    Emit(_error);
                    return false;
                }

                _dllPath = Helper32Locator.ExtractDll();
                foreach (string note in Helper32Locator.Notes) Emit(note);
                if (_dllPath == null)
                {
                    _error = L10n.F("helper32.log.extractFailed", Helper32Locator.LastError);
                    Emit(_error);
                    return false;
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

                try
                {
                    _process = Process.Start(psi);
                }
                catch (Exception ex)
                {
                    _error = L10n.F("helper32.log.startFailed", ex.GetType().Name + "：" + ex.Message);
                    Emit(_error);
                    return false;
                }
                if (_process == null)
                {
                    _error = L10n.F("helper32.log.startFailed", "Process.Start 返回 null");
                    Emit(_error);
                    return false;
                }

                try { _pid = _process.Id; } catch (Exception) { }
                Emit(L10n.F("helper32.log.started", _pid, _exePath));

                _reader = new Thread(ReadLoop);
                _reader.IsBackground = true;
                _reader.Name = "HotkeyHound-Helper32-Reader";
                _reader.Start(_process);
                return true;
            }
        }

        /// <summary>等宿主报出 HELPER32_READY；超时或钩子失败都只返回 false，不抛异常。</summary>
        public bool WaitReady(int timeoutMs, Action<string> log)
        {
            _log = log;
            bool signaled = false;
            try { signaled = _readyEvent.Wait(timeoutMs); }
            catch (Exception) { }

            if (_error != null)
            {
                Emit(L10n.F("helper32.log.failed", _error));
                return false;
            }
            if (!signaled)
            {
                Emit(L10n.F("helper32.log.timeout", timeoutMs, _lastLine ?? L10n.T("common.none")));
                return false;
            }
            if (!_hookInstalled)
            {
                Emit(L10n.F("helper32.log.readyNoHook", _lastLine ?? L10n.T("common.none")));
                return false;
            }
            Emit(L10n.F("helper32.log.ready", _pid, _hwnd, _ctlLine ?? L10n.T("common.none")));
            return true;
        }

        public void Stop(Action<string> log)
        {
            Process p;
            lock (_gate)
            {
                p = _process;
                _process = null;
            }
            if (p == null) return;
            _log = log;

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
                Thread reader = _reader;
                _reader = null;
                if (reader != null)
                {
                    try { if (reader.IsAlive) reader.Join(600); }
                    catch (Exception) { }
                }
                try { p.Dispose(); }
                catch (Exception) { }

                _ready = false;
                _hookInstalled = false;
                _pid = 0;
                _hwnd = 0;
                _readyEvent.Set();
            }

            if (graceful) Emit(L10n.F("helper32.log.exited", pid));
            else Emit(L10n.F("helper32.log.killed", pid));
        }

        public void Dispose()
        {
            Stop(null);
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
                    // Stop() 之后旧进程的输出不能再影响新一次 Start() 的状态。
                    if (!IsCurrent(p)) return;
                    _lastLine = line;
                    if (line.StartsWith("HELPER32_READY", StringComparison.Ordinal))
                    {
                        _pid = ExtractInt(line, "pid=", _pid);
                        _hwnd = ExtractLong(line, "hwnd=", 0);
                        _hookInstalled = ExtractInt(line, "hook=", 0) != 0;
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
                    Emit(L10n.F("helper32.log.line", line));
                }
            }
            catch (Exception ex)
            {
                if (IsCurrent(p)) _error = L10n.F("helper32.log.readFailed", ex.GetType().Name + "：" + ex.Message);
            }
            finally
            {
                // 进程退出（正常或崩溃）也要放行 WaitReady，不能让它一直等到超时。
                // 只有「还是当前这个进程」时才这样做：Stop() 会自己 Set，避免串到下一次 Start。
                if (IsCurrent(p))
                {
                    _readyEvent.Set();
                    Emit(L10n.T("helper32.log.stdoutEnded"));
                }
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
            int j = i;
            while (j < line.Length && char.IsDigit(line[j])) j++;
            if (j == i) return fallback;
            long v;
            if (!long.TryParse(line.Substring(i, j - i), out v)) return fallback;
            return v;
        }
    }
}
