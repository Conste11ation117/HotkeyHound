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
        public string Note;

        public string Display()
        {
            return string.Format("{0}  {1}  PID {2}  {3}{4}",
                Time.ToString("HH:mm:ss"), Combo == null ? L10n.T("attr.unknownCombo") : Combo.Display(), Pid,
                string.IsNullOrEmpty(ProcessName) ? L10n.T("attr.unknownProc") : ProcessName,
                IsElevated ? L10n.T("attr.elevatedTag") : "");
        }
    }

    /// <summary>
    /// 引擎 B 控制器：把内嵌的原生 hook DLL 释放到磁盘，LoadLibrary 后用
    /// SetWindowsHookEx(WH_GETMESSAGE) 装全局钩子，并在独立线程上跑消息循环。
    /// DLL 在目标进程里读到 WM_HOTKEY 后 PostMessage 回我们的控制窗口，
    /// 这里解析出「哪个进程注册了这个热键」。
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
        /// 原生侧 HksControl 是 pack(1) 的 { DWORD magic; HWND ctl; DWORD arm; }，
        /// x64 上共 16 字节（magic 0..4、ctl 4..12、arm 12..16）。
        /// 映射必须至少这么大，否则 DLL 的 MapViewOfFile(16) 会失败，整条上报链路静默断掉。
        /// 这里留足 64 字节以免将来加字段又踩同一个坑。
        /// </summary>
        private const int ControlMapSize = 64;

        private const uint PROCESS_QUERY_LIMITED_INFORMATION = 0x1000;
        private const uint TOKEN_QUERY = 0x0008;
        private const int TokenElevation = 20;

        private readonly object _gate = new object();
        private readonly List<AttributionRecord> _records = new List<AttributionRecord>();

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
                LastError = "hook DLL 释放失败：" + HookDllLocator.LastError;
                Say(LastError);
                return false;
            }

            _thread = new Thread(HookThreadProc);
            _thread.IsBackground = true;
            _thread.Name = "HotkeyHound-Hook";
            _thread.SetApartmentState(ApartmentState.STA);
            _thread.Start();

            // 等钩子装好（最多 5 秒）
            DateTime deadline = DateTime.Now.AddSeconds(5);
            while (!_ready && DateTime.Now < deadline && _thread.IsAlive)
            {
                Thread.Sleep(20);
            }
            if (!_ready)
            {
                if (LastError == null) LastError = "钩子线程未能就绪（超时）";
                Say(LastError);
                return false;
            }
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
                try { if (!t.Join(4000)) Say("警告：钩子线程在 4 秒内没有退出"); }
                catch (Exception) { }
            }
            _thread = null;
            _ready = false;
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

                // 共享内存：{magic, ctl, arm}
                _mapHandle = NativeMethods.CreateFileMappingW(new IntPtr(-1), IntPtr.Zero,
                    NativeMethods.PAGE_READWRITE, 0, (uint)ControlMapSize, MapName);
                if (_mapHandle == IntPtr.Zero)
                {
                    LastError = "创建共享内存 " + MapName + " 失败，错误码 " + Marshal.GetLastWin32Error();
                    Say(LastError);
                    return;
                }
                _mapView = NativeMethods.MapViewOfFile(_mapHandle, NativeMethods.FILE_MAP_ALL_ACCESS, 0, 0, (uint)ControlMapSize);
                if (_mapView == IntPtr.Zero)
                {
                    LastError = "映射共享内存失败，错误码 " + Marshal.GetLastWin32Error();
                    Say(LastError);
                    return;
                }

                _hMod = NativeMethods.LoadLibraryW(_dllPath);
                if (_hMod == IntPtr.Zero)
                {
                    LastError = "LoadLibrary 失败：" + _dllPath + "，错误码 " + Marshal.GetLastWin32Error();
                    Say(LastError);
                    return;
                }
                IntPtr proc = NativeMethods.GetProcAddress(_hMod, "HotkeyHoundHookProc");
                if (proc == IntPtr.Zero)
                {
                    LastError = "DLL 里找不到导出 HotkeyHoundHookProc";
                    Say(LastError);
                    return;
                }

                Say("LoadLibrary 成功：hMod=0x" + _hMod.ToInt64().ToString("X"));
                Say("导出地址 HotkeyHoundHookProc=0x" + proc.ToInt64().ToString("X"));

                // 全局钩子必须使用原生 DLL 里的导出地址：托管委托的函数指针只在本进程
                // 地址空间里有效，Windows 把 DLL 注入到别的进程后会去调用同一个地址，
                // 那里是一段不存在的代码——结果是每个取消息的进程一起 fail-fast 崩溃。
                _hook = NativeMethods.SetWindowsHookExPtr(WH_GETMESSAGE, proc, _hMod, 0);
                if (_hook == IntPtr.Zero)
                {
                    LastError = "SetWindowsHookEx(WH_GETMESSAGE) 失败，错误码 " + Marshal.GetLastWin32Error();
                    Say(LastError);
                    if (ProcessIntegrity.IsBelowMedium)
                    {
                        Say("提示：当前以" + ProcessIntegrity.Describe(ProcessIntegrity.Current)
                            + "运行，UIPI 会阻止把 DLL 注入到普通进程。请把 exe 复制到普通目录后再运行。");
                    }
                    return;
                }
                Say("SetWindowsHookEx 成功：hook=0x" + _hook.ToInt64().ToString("X") + "，控制窗口=0x" + _hwnd.ToInt64().ToString("X"));

                // 写入共享内存，通知 DLL 可以上报
                Marshal.WriteInt32(_mapView, 0, unchecked((int)HKS_MAGIC));
                Marshal.WriteIntPtr(_mapView, 4, _hwnd);
                Marshal.WriteInt32(_mapView, 4 + IntPtr.Size, 1);
                Say("共享内存 " + MapName + " 已就绪（magic=0x484B5354, arm=1）");

                _ready = true;

                NativeMethods.MSG msg;
                while (!_stopRequested)
                {
                    int r = NativeMethods.GetMessageW(out msg, IntPtr.Zero, 0, 0);
                    if (r <= 0) break;
                    if (msg.message == WM_CLOSE)
                    {
                        Say("收到 WM_CLOSE，钩子线程退出");
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
                LastError = "钩子线程异常：" + ex;
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
                        LastError = "RegisterClassEx 失败，错误码 " + Marshal.GetLastWin32Error();
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
                LastError = "创建控制窗口失败，错误码 " + Marshal.GetLastWin32Error();
                Say(LastError);
                return false;
            }
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

            if (rec.Combo == null) rec.Note = L10n.T("attr.note.badParam");
            else if (rec.ProcessName == null) rec.Note = L10n.T("attr.note.noProc");

            lock (_gate) _records.Add(rec);
            Say("归属报告：" + rec.Display() + (rec.ProcessPath == null ? "" : "  " + rec.ProcessPath));

            Action<AttributionRecord> h = RecordReceived;
            if (h != null)
            {
                try { h(rec); }
                catch (Exception) { }
            }
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
                    Say("已 UnhookWindowsHookEx（旧句柄 0x" + _hook.ToInt64().ToString("X") + "）");
                    _hook = IntPtr.Zero;
                }
            }
            catch (Exception) { }

            try
            {
                if (_mapView != IntPtr.Zero)
                {
                    Marshal.WriteInt32(_mapView, 4 + IntPtr.Size, 0);   // arm=0，DLL 立刻停止上报
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
