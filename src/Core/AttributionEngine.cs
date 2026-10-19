using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;

namespace HotkeyHound.Core
{
    public sealed class AttributionRecord
    {
        public DateTime Time;
        public KeyCombo Combo;
        public int Pid;
        public string ProcessName;
        public string ProcessPath;
        public bool IsElevated;
        /// <summary>
        /// 目标进程的位数（"x64" / "x86" / "?"）。x64 钩子只能注入 x64 进程、x86 钩子只能注入
        /// x86 进程，所以这个字段同时也说明了这条记录是哪一条注入链路抓到的。
        /// </summary>
        public string Bitness;
        public string Note;

        public string Display()
        {
            return string.Format("{0}  {1}  PID {2}  {3}{4}{5}",
                Time.ToString("HH:mm:ss"), Combo == null ? L10n.T("attr.unknownCombo") : Combo.Display(), Pid,
                string.IsNullOrEmpty(ProcessName) ? L10n.T("attr.unknownProc") : ProcessName,
                IsElevated ? L10n.T("attr.elevatedTag") : "",
                string.IsNullOrEmpty(Bitness) ? "" : "  " + Bitness);
        }
    }

    /// <summary>
    /// 引擎 B 控制器：把内嵌的原生 hook DLL 释放到磁盘，LoadLibrary 后用
    /// SetWindowsHookEx(WH_GETMESSAGE) 装全局钩子，并在独立线程上跑消息循环。
    /// DLL 在目标进程里读到 WM_HOTKEY 后 PostMessage 回我们的控制窗口，
    /// 这里解析出「哪个进程注册了这个热键」。
    ///
    /// 32 位进程需要另一条链路：Windows 只允许钩子注入与安装者同位数的进程，
    /// 所以这里还会同时拉起一个 32 位宿主进程（HotkeyHound.Helper32.exe）装 32 位钩子。
    /// 两条链路报进同一张表，只用 Bitness 区分；32 位那一侧失败只降级，不影响 64 位路径。
    /// </summary>
    public sealed class AttributionEngine : IDisposable
    {
        private const int WH_GETMESSAGE = 3;
        private const uint WM_APP = 0x8000;
        public const int WM_HOTKEY_REPORT = (int)(WM_APP + 1);
        private const int WM_CLOSE = 0x0010;

        private const uint HKS_MAGIC = 0x484B5354;
        private const string MapName = "Local\\HotkeyHound.Ctl";
        /// <summary>
        /// 原生侧 HksControl 是 pack(1) 的 { DWORD magic; DWORD arm; DWORD ctlLo; DWORD ctlHi; }，
        /// 固定 16 字节，x86 与 x64 完全一致（这正是改布局的原因：旧的 HWND 字段跨位数会错位）。
        /// 映射留足 64 字节以免将来加字段又踩同一个坑。
        /// </summary>
        private const int ControlMapSize = 64;

        private const uint PROCESS_QUERY_LIMITED_INFORMATION = 0x1000;
        private const uint TOKEN_QUERY = 0x0008;
        private const int TokenElevation = 20;

        private readonly object _gate = new object();
        private readonly List<AttributionRecord> _records = new List<AttributionRecord>();
        private readonly Helper32Runtime _helper32 = new Helper32Runtime();

        // 一次会话最多保留多少条归属记录。见 HandleReport 里为什么必须有上限。
        private const int MaxRecords = 20000;
        private bool _truncated;

        private Thread _thread;
        private volatile bool _stopRequested;
        private volatile bool _ready;
        private string _dllPath;

        private IntPtr _hwnd = IntPtr.Zero;
        private IntPtr _hook = IntPtr.Zero;
        private IntPtr _mapHandle = IntPtr.Zero;
        private IntPtr _mapView = IntPtr.Zero;
        private IntPtr _hMod = IntPtr.Zero;

        private static WndProcDelegate _wndProcKeepAlive;
        private static readonly object ClassGate = new object();
        private static bool _classRegistered;
        private static string _className;

        public event Action<AttributionRecord> RecordReceived;
        public event Action<string> Logged;

        public bool IsRunning { get { return _thread != null && _thread.IsAlive; } }
        public bool IsArmed { get { return _ready; } }
        public IntPtr ControlWindow { get { return _hwnd; } }
        public IntPtr HookHandle { get { return _hook; } }
        public string DllPath { get { return _dllPath; } }
        public string LastError { get; private set; }

        /// <summary>32 位伴随宿主的状态（未启动时 IsRunning=false，Error 里有原因）。</summary>
        public Helper32Runtime Helper32 { get { return _helper32; } }

        public AttributionRecord[] Records
        {
            get { lock (_gate) return _records.ToArray(); }
        }

        public void Clear()
        {
            lock (_gate) _records.Clear();
        }

        private void Say(string message)
        {
            Action<string> h = Logged;
            if (h != null)
            {
                try { h(message); }
                catch (Exception) { }
            }
        }

        // ------------------------------------------------------------ 启动 / 停止

        public bool Start()
        {
            if (IsRunning) return true;
            LastError = null;
            _stopRequested = false;
            _ready = false;

            _dllPath = HookDllLocator.Extract();
            foreach (string note in HookDllLocator.Notes) Say(note);
            if (_dllPath == null)
            {
                LastError = L10n.F("attrib.dllReleaseFailed", HookDllLocator.LastError);
                Say(LastError);
                return false;
            }

            _thread = new Thread(HookThreadProc);
            _thread.IsBackground = true;
            _thread.Name = "HotkeyHound-Hook";
            _thread.SetApartmentState(ApartmentState.STA);
            _thread.Start();

            // 32 位宿主与 x64 钩子并行启动：两条链路互不依赖，任何一边失败都只影响自己那一半。
            _helper32.Start(Process.GetCurrentProcess().Id, null, Say);

            // 等钩子装好（最多 5 秒）
            DateTime deadline = DateTime.Now.AddSeconds(5);
            while (!_ready && DateTime.Now < deadline && _thread.IsAlive)
            {
                Thread.Sleep(20);
            }
            if (!_ready)
            {
                if (LastError == null) LastError = L10n.T("attrib.threadTimeout");
                Say(LastError);
                try { _helper32.Stop(Say); }
                catch (Exception) { }
                return false;
            }

            // x64 路径已就绪；32 位宿主最多再等 4 秒，超时也只是缺少 32 位覆盖。
            _helper32.WaitReady(4000, Say);
            return true;
        }

        public void Stop()
        {
            _stopRequested = true;
            IntPtr hwnd = _hwnd;
            if (hwnd != IntPtr.Zero)
            {
                try { NativeMethods.PostMessageW(hwnd, WM_CLOSE, IntPtr.Zero, IntPtr.Zero); }
                catch (Exception) { }
            }
            Thread t = _thread;
            if (t != null)
            {
                try { if (!t.Join(4000)) Say(L10n.T("attrib.threadJoinTimeout")); }
                catch (Exception) { }
            }
            _thread = null;
            _ready = false;

            // 32 位宿主是本程序自己拉起的子进程：必须在这里结束它，绝不留僵尸。
            try { _helper32.Stop(Say); }
            catch (Exception) { }
        }

        public void Dispose()
        {
            Stop();
        }

        // ------------------------------------------------------------ 钩子线程

        private void HookThreadProc()
        {
            try
            {
                if (!CreateControlWindow()) return;

                // 共享内存：{magic, arm, ctlLo, ctlHi}
                _mapHandle = NativeMethods.CreateFileMappingW(new IntPtr(-1), IntPtr.Zero,
                    NativeMethods.PAGE_READWRITE, 0, (uint)ControlMapSize, MapName);
                if (_mapHandle == IntPtr.Zero)
                {
                    LastError = L10n.F("attrib.mapCreateFailed", MapName, Marshal.GetLastWin32Error());
                    Say(LastError);
                    return;
                }
                _mapView = NativeMethods.MapViewOfFile(_mapHandle, NativeMethods.FILE_MAP_ALL_ACCESS, 0, 0, (uint)ControlMapSize);
                if (_mapView == IntPtr.Zero)
                {
                    LastError = L10n.F("attrib.mapViewFailed", Marshal.GetLastWin32Error());
                    Say(LastError);
                    return;
                }

                _hMod = NativeMethods.LoadLibraryW(_dllPath);
                if (_hMod == IntPtr.Zero)
                {
                    LastError = L10n.F("attrib.loadLibraryFailed", _dllPath, Marshal.GetLastWin32Error());
                    Say(LastError);
                    return;
                }
                IntPtr proc = NativeMethods.GetProcAddress(_hMod, "HotkeyHoundHookProc");
                if (proc == IntPtr.Zero)
                {
                    LastError = L10n.T("attrib.exportMissing");
                    Say(LastError);
                    return;
                }

                Say(L10n.F("attrib.loadLibraryOk", _hMod.ToInt64().ToString("X")));
                Say(L10n.F("attrib.exportAddress", proc.ToInt64().ToString("X")));

                // 全局钩子必须使用原生 DLL 里的导出地址：托管委托的函数指针只在本进程
                // 地址空间里有效，Windows 把 DLL 注入到别的进程后会去调用同一个地址，
                // 那里是一段不存在的代码——结果是每个取消息的进程一起 fail-fast 崩溃。
                _hook = NativeMethods.SetWindowsHookExPtr(WH_GETMESSAGE, proc, _hMod, 0);
                if (_hook == IntPtr.Zero)
                {
                    LastError = L10n.F("attrib.hookInstallFailed", Marshal.GetLastWin32Error());
                    Say(LastError);
                    if (ProcessIntegrity.IsBelowMedium)
                    {
                        Say(L10n.F("attrib.uipiHint", ProcessIntegrity.Describe(ProcessIntegrity.Current)));
                    }
                    return;
                }
                Say(L10n.F("attrib.hookInstallOk", _hook.ToInt64().ToString("X"), _hwnd.ToInt64().ToString("X")));

                // 写入共享内存，通知 DLL 可以上报。布局必须与位数无关：全是 4 字节字段。
                // 写序必须与 HookDll.cpp 的读序（arm → magic → 句柄）配套：先写句柄与 magic，
                // 加一道内存屏障，最后才把 arm 置 1。arm 是「可以用了」的唯一开关，必须最后写；
                // 否则注入的 DLL 可能看到 arm=1 而句柄还没落地，那条报告就静默丢了。
                long hwndValue = _hwnd.ToInt64();
                Marshal.WriteInt32(_mapView, 8, unchecked((int)(hwndValue & 0xFFFFFFFFL)));          // ctlLo
                Marshal.WriteInt32(_mapView, 12, unchecked((int)((hwndValue >> 32) & 0xFFFFFFFFL))); // ctlHi
                Marshal.WriteInt32(_mapView, 0, unchecked((int)HKS_MAGIC));                           // magic
                System.Threading.Thread.MemoryBarrier();
                Marshal.WriteInt32(_mapView, 4, 1);                                                  // arm=1
                Say(L10n.F("attrib.mapReady", MapName, _hwnd.ToInt64().ToString("X")));

                _ready = true;

                NativeMethods.MSG msg;
                while (!_stopRequested)
                {
                    int r = NativeMethods.GetMessageW(out msg, IntPtr.Zero, 0, 0);
                    if (r <= 0) break;
                    if (msg.message == WM_CLOSE)
                    {
                        Say(L10n.T("attrib.wmClose"));
                        break;
                    }
                    if (msg.message == WM_HOTKEY_REPORT)
                    {
                        HandleReport(msg.wParam.ToInt32(), msg.lParam.ToInt64());
                        continue;
                    }
                    NativeMethods.TranslateMessage(ref msg);
                    NativeMethods.DispatchMessageW(ref msg);
                }
            }
            catch (Exception ex)
            {
                LastError = L10n.F("attrib.threadException", ex);
                Say(LastError);
            }
            finally
            {
                Cleanup();
            }
        }

        private bool CreateControlWindow()
        {
            string cls;
            lock (ClassGate)
            {
                if (!_classRegistered)
                {
                    _className = "HotkeyHoundCtl_" + Process.GetCurrentProcess().Id.ToString() + "_" + Guid.NewGuid().ToString("N").Substring(0, 6);
                    _wndProcKeepAlive = new WndProcDelegate(StaticWndProc);
                    var wc = new NativeMethods.WNDCLASSEX();
                    wc.cbSize = Marshal.SizeOf(typeof(NativeMethods.WNDCLASSEX));
                    wc.style = 0;
                    wc.lpfnWndProc = Marshal.GetFunctionPointerForDelegate(_wndProcKeepAlive);
                    wc.hInstance = NativeMethods.GetModuleHandleW(null);
                    wc.lpszClassName = _className;
                    ushort atom = NativeMethods.RegisterClassExW(ref wc);
                    if (atom == 0)
                    {
                        LastError = L10n.F("attrib.registerClassFailed", Marshal.GetLastWin32Error());
                        Say(LastError);
                        return false;
                    }
                    _classRegistered = true;
                }
                cls = _className;
            }

            _hwnd = NativeMethods.CreateWindowExW(0, cls, "HotkeyHoundControl", 0, 0, 0, 0, 0,
                                                  new IntPtr(-3) /* HWND_MESSAGE */, IntPtr.Zero,
                                                  NativeMethods.GetModuleHandleW(null), IntPtr.Zero);
            if (_hwnd == IntPtr.Zero)
            {
                LastError = L10n.F("attrib.createWindowFailed", Marshal.GetLastWin32Error());
                Say(LastError);
                return false;
            }

            // 提权运行时是「高权限控制器 + 低权限目标进程」这个方向：那些进程里注入的 DLL
            // 投给我们的 WM_APP+1 会被 UIPI 直接拦掉，结果是提权后归属彻底失灵，而且
            // 一个错都不报——看起来就像根本没有程序占用热键。只放行这一条上报消息。
            try
            {
                if (!NativeMethods.ChangeWindowMessageFilterEx(
                        _hwnd, (uint)WM_HOTKEY_REPORT, NativeMethods.MSGFLT_ALLOW, IntPtr.Zero))
                {
                    Say(L10n.F("attrib.msgFilterFailed", Marshal.GetLastWin32Error()));
                }
            }
            catch (Exception) { }

            return true;
        }

        private static IntPtr StaticWndProc(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam)
        {
            return NativeMethods.DefWindowProcW(hWnd, msg, wParam, lParam);
        }

        /// <summary>
        /// 全局钩子走的是原生 DLL 里的 HotkeyHoundHookProc（见 SetWindowsHookExPtr），
        /// 本进程不再需要任何替身过程——历史上这里放过一个托管替身，
        /// 那正是把全机进程一起打崩的原因，不要再加回来。
        /// </summary>
        // ------------------------------------------------------------ 上报解析

        private void HandleReport(int pid, long hotkeyLParam)
        {
            var rec = new AttributionRecord();
            rec.Time = DateTime.Now;
            rec.Pid = pid;
            rec.Combo = KeyCombo.FromHotkeyLParam(hotkeyLParam);

            string path;
            bool elevated = IsProcessElevated(pid, out path);
            rec.ProcessPath = path;
            rec.IsElevated = elevated;
            rec.ProcessName = DescribeProcess(pid, path);
            rec.Bitness = BitnessOf(pid);

            if (rec.Combo == null) rec.Note = L10n.T("attr.note.badParam");
            else if (rec.ProcessName == null) rec.Note = L10n.T("attr.note.noProc");

            // 上限：别人注册的热键如果没带 MOD_NOREPEAT，按住不放每秒能产生几十条 WM_HOTKEY，
            // 开一晚上就是十几万条——内存一直涨、界面列表也会卡死。两万条已经远超人类按键
            // 速度，超了就丢最早的，并在日志里说一次（不静默丢数据）。
            bool justTruncated = false;
            lock (_gate)
            {
                _records.Add(rec);
                if (_records.Count > MaxRecords)
                {
                    _records.RemoveRange(0, _records.Count - MaxRecords);
                    if (!_truncated) { _truncated = true; justTruncated = true; }
                }
            }
            if (justTruncated)
            {
                Say(L10n.F("attrib.truncated", MaxRecords));
            }
            Say(L10n.F("attrib.report", rec.Display(),
                       rec.ProcessPath == null ? "" : "  " + rec.ProcessPath));

            Action<AttributionRecord> h = RecordReceived;
            if (h != null)
            {
                try { h(rec); }
                catch (Exception) { }
            }
        }

        /// <summary>
        /// 从目标进程本身判断位数：x64 钩子只可能注入 x64 进程，x86 钩子只可能注入 x86 进程，
        /// 因此这个字段就是「哪条链路抓到的」的可靠证据。拿不到进程（已退出/权限不足）时写 "?"。
        /// </summary>
        public static string BitnessOf(int pid)
        {
            IntPtr h = IntPtr.Zero;
            try
            {
                h = NativeMethods.OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION, false, pid);
                if (h == IntPtr.Zero) return "?";
                bool wow64;
                if (!NativeMethods.IsWow64Process(h, out wow64)) return "?";
                return wow64 ? "x86" : "x64";
            }
            catch (Exception) { return "?"; }
            finally { if (h != IntPtr.Zero) NativeMethods.CloseHandle(h); }
        }

        public static string DescribeProcess(int pid, string path)
        {
            try
            {
                using (Process p = Process.GetProcessById(pid))
                {
                    string name = p.ProcessName;
                    if (string.IsNullOrEmpty(path) && name != null) return name;
                    if (!string.IsNullOrEmpty(path))
                    {
                        try
                        {
                            string file = Path.GetFileName(path);
                            return string.IsNullOrEmpty(file) ? name : file;
                        }
                        catch (Exception) { return name; }
                    }
                }
            }
            catch (Exception) { }

            if (!string.IsNullOrEmpty(path))
            {
                try { return Path.GetFileName(path); }
                catch (Exception) { }
            }
            return null;
        }

        public static bool IsProcessElevated(int pid, out string imagePath)
        {
            imagePath = null;
            IntPtr h = IntPtr.Zero;
            try
            {
                h = NativeMethods.OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION, false, pid);
                if (h == IntPtr.Zero) return false;

                var sb = new StringBuilder(1024);
                int size = sb.Capacity;
                if (NativeMethods.QueryFullProcessImageNameW(h, 0, sb, ref size)) imagePath = sb.ToString();

                IntPtr token;
                if (!NativeMethods.OpenProcessToken(h, TOKEN_QUERY, out token)) return false;
                try
                {
                    int len = 4;
                    IntPtr buf = Marshal.AllocHGlobal(len);
                    try
                    {
                        int ret;
                        if (NativeMethods.GetTokenInformation(token, TokenElevation, buf, len, out ret))
                        {
                            return Marshal.ReadInt32(buf) != 0;
                        }
                    }
                    finally { Marshal.FreeHGlobal(buf); }
                }
                finally { NativeMethods.CloseHandle(token); }
                return false;
            }
            catch (Exception) { return false; }
            finally { if (h != IntPtr.Zero) NativeMethods.CloseHandle(h); }
        }

        private void Cleanup()
        {
            _ready = false;
            try
            {
                if (_hook != IntPtr.Zero)
                {
                    NativeMethods.UnhookWindowsHookEx(_hook);
                    Say(L10n.F("attrib.unhooked", _hook.ToInt64().ToString("X")));
                    _hook = IntPtr.Zero;
                }
            }
            catch (Exception) { }

            try
            {
                if (_mapView != IntPtr.Zero)
                {
                    Marshal.WriteInt32(_mapView, 4, 0);   // arm=0，DLL 立刻停止上报
                    NativeMethods.UnmapViewOfFile(_mapView);
                    _mapView = IntPtr.Zero;
                }
            }
            catch (Exception) { }

            try
            {
                if (_mapHandle != IntPtr.Zero) { NativeMethods.CloseHandle(_mapHandle); _mapHandle = IntPtr.Zero; }
            }
            catch (Exception) { }

            try
            {
                if (_hwnd != IntPtr.Zero) { NativeMethods.DestroyWindow(_hwnd); _hwnd = IntPtr.Zero; }
            }
            catch (Exception) { }

            // 不 FreeLibrary：卸载时机不安全，进程退出时自然释放
        }
    }

    internal delegate IntPtr WndProcDelegate(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);

    internal static class NativeMethods
    {
        public const uint PAGE_READWRITE = 0x04;
        public const uint FILE_MAP_ALL_ACCESS = 0x000F001F;
        public const uint PROCESS_QUERY_LIMITED_INFORMATION = 0x1000;
        public const uint TOKEN_QUERY = 0x0008;

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        public static extern IntPtr CreateFileMappingW(IntPtr hFile, IntPtr attrs, uint protect, uint maxHigh, uint maxLow, string name);

        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern IntPtr MapViewOfFile(IntPtr hMap, uint access, uint offsetHigh, uint offsetLow, uint bytes);

        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern bool UnmapViewOfFile(IntPtr addr);

        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern bool CloseHandle(IntPtr h);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        public static extern IntPtr LoadLibraryW(string path);

        [DllImport("kernel32.dll", CharSet = CharSet.Ansi, SetLastError = true)]
        public static extern IntPtr GetProcAddress(IntPtr hMod, string proc);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        public static extern IntPtr GetModuleHandleW(string name);

        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern bool IsWow64Process(IntPtr process, out bool wow64);

        [DllImport("user32.dll", SetLastError = true, EntryPoint = "SetWindowsHookExW")]
        public static extern IntPtr SetWindowsHookExPtr(int idHook, IntPtr proc, IntPtr hMod, uint threadId);

        [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true, EntryPoint = "SetWindowsHookExW")]
        public static extern IntPtr SetWindowsHookExLL(int idHook, LowLevelKeyboardProc proc, IntPtr hMod, uint threadId);

        [DllImport("kernel32.dll")]
        public static extern uint GetCurrentThreadId();

        [DllImport("user32.dll", SetLastError = true)]
        public static extern bool PostThreadMessageW(uint threadId, int msg, IntPtr wParam, IntPtr lParam);

        [StructLayout(LayoutKind.Sequential)]
        public struct KBDLLHOOKSTRUCT
        {
            public uint vkCode;
            public uint scanCode;
            public uint flags;
            public uint time;
            public IntPtr dwExtraInfo;
        }

        [DllImport("user32.dll", SetLastError = true)]
        public static extern bool UnhookWindowsHookEx(IntPtr hook);

        [DllImport("user32.dll")]
        public static extern IntPtr CallNextHookEx(IntPtr hook, int nCode, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        public static extern ushort RegisterClassExW(ref WNDCLASSEX wc);

        [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        public static extern IntPtr CreateWindowExW(int exStyle, string className, string windowName, int style,
                                                    int x, int y, int w, int h,
                                                    IntPtr parent, IntPtr menu, IntPtr instance, IntPtr param);

        [DllImport("user32.dll", SetLastError = true)]
        public static extern bool DestroyWindow(IntPtr hWnd);

        // UIPI 例外：Win7+ 才有这个导出（Vista 上是 ChangeWindowMessageFilter）。
        // 参数是 (hwnd, message, MSGFLT_* 动作, CHANGEFILTERSTRUCT*)，最后一个传 NULL 即可。
        [DllImport("user32.dll", SetLastError = true)]
        public static extern bool ChangeWindowMessageFilterEx(IntPtr hwnd, uint message, uint action, IntPtr changeInfo);

        public const uint MSGFLT_ALLOW = 1;

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        public static extern IntPtr DefWindowProcW(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll", SetLastError = true)]
        public static extern bool PostMessageW(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        public static extern int GetMessageW(out MSG msg, IntPtr hWnd, uint min, uint max);

        [DllImport("user32.dll")]
        public static extern bool TranslateMessage(ref MSG msg);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        public static extern IntPtr DispatchMessageW(ref MSG msg);

        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern IntPtr OpenProcess(uint access, bool inherit, int pid);

        [DllImport("user32.dll", SetLastError = true)]
        public static extern bool RegisterHotKey(IntPtr hWnd, int id, uint mods, uint vk);

        [DllImport("user32.dll", SetLastError = true)]
        public static extern bool UnregisterHotKey(IntPtr hWnd, int id);

        [DllImport("user32.dll", SetLastError = true)]
        public static extern IntPtr SetTimer(IntPtr hWnd, int id, uint elapse, IntPtr proc);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        public static extern bool QueryFullProcessImageNameW(IntPtr hProcess, int flags, StringBuilder name, ref int size);

        [DllImport("advapi32.dll", SetLastError = true)]
        public static extern bool OpenProcessToken(IntPtr hProcess, uint access, out IntPtr token);

        [DllImport("advapi32.dll", SetLastError = true)]
        public static extern bool GetTokenInformation(IntPtr token, int cls, IntPtr info, int len, out int ret);

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        public struct WNDCLASSEX
        {
            public int cbSize;
            public uint style;
            public IntPtr lpfnWndProc;
            public int cbClsExtra;
            public int cbWndExtra;
            public IntPtr hInstance;
            public IntPtr hIcon;
            public IntPtr hCursor;
            public IntPtr hbrBackground;
            public string lpszMenuName;
            public string lpszClassName;
            public IntPtr hIconSm;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct POINT
        {
            public int x;
            public int y;
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        public struct MSG
        {
            public IntPtr hwnd;
            public int message;
            public IntPtr wParam;
            public IntPtr lParam;
            public int time;
            public POINT pt;
        }
    }
}
