// HotkeyHound 32 位伴随宿主（x86，控制台，target:exe）
//
// 为什么需要它：Windows 只允许钩子注入到与安装者同位数的进程。主程序是 x64，
// 它装的 x64 钩子永远进不了 32 位进程，所以必须再有一个 32 位进程装 32 位钩子。
// 本进程只做三件事：LoadLibrary 32 位 hook DLL → SetWindowsHookEx(WH_GETMESSAGE)
// → 跑消息循环。钩子过程用 GetProcAddress 拿到的原生导出地址，绝不用托管委托
// （托管委托的地址只在本进程有效，注入出去就是野指针，会把整机进程打崩）。
//
// 生命周期：父进程退出必须自杀。这里用 OpenProcess(SYNCHRONIZE) + 等待句柄，
// 父进程一死立刻收到信号退出；stdin 关闭或收到 q 也退出（父进程被强杀时管道会关）。
// 这是「绝不留僵尸进程」的兜底。
//
// 本文件被 build.ps1 单独编成 x86 的 HotkeyHound.Helper32.exe，不参与主程序编译，
// 因此不引用工程内任何其它源码（不依赖 L10n/SharedState 等）。

using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;

internal static class Helper32Main
{
    private const int WH_GETMESSAGE = 3;
    private const int WM_QUIT = 0x0012;
    private const int WM_HOTKEY = 0x0312;
    private const int WM_CLOSE = 0x0010;
    private const int WM_TIMER = 0x0113;
    private const int TIMER_ID = 1;
    private const int VK_F13 = 0x7C;
    private const uint MOD_ALT = 0x0001;
    private const uint MOD_CONTROL = 0x0002;
    private const uint SYNCHRONIZE = 0x00100000;
    private const uint FILE_MAP_READ = 0x0004;
    private const uint HKS_MAGIC = 0x484B5354;
    private const uint LOAD_WITH_ALTERED_SEARCH_PATH = 0x00000008;
    private const long MAX_TIMER_MILLISECONDS = 24L * 60L * 60L * 1000L;
    private const string MapName = "Local\\HotkeyHound.Ctl";

    private static readonly object OutputGate = new object();
    private static readonly uint MainThreadId = GetCurrentThreadId();

    private static WndProcDelegate _wndProcKeepAlive;
    private static IntPtr _hwnd = IntPtr.Zero;
    private static IntPtr _hook = IntPtr.Zero;
    private static IntPtr _hMod = IntPtr.Zero;
    private static int _registered;
    private static int _quitting;

    private static int Main(string[] args)
    {
        try { Console.OutputEncoding = new UTF8Encoding(false); }
        catch (Exception) { }

        var options = new Options();
        if (!options.Parse(args)) return 2;

        try
        {
            if (!CreateHostWindow())
            {
                Print("HELPER32_ERROR create_window err=" + Marshal.GetLastWin32Error().ToString(CultureInfo.InvariantCulture));
                return 3;
            }

            int regError = 0;
            if (options.SelfTestTarget)
            {
                // 自测目标：注册 Ctrl+Alt+F13 并泵消息。注册失败也不致命——上层是直接
                // PostMessage(WM_HOTKEY) 到窗口，钩子链照样会被调用，只是少一条真实注册记录。
                bool ok = RegisterHotKey(_hwnd, 1, MOD_CONTROL | MOD_ALT, VK_F13);
                _registered = ok ? 1 : 0;
                if (!ok) regError = Marshal.GetLastWin32Error();
            }

            string hookError;
            int hookOk = InstallHook(options.DllPath, out hookError);

            // 先读一次控制块：控制器通常已经写好（引擎先建共享内存再启动宿主），
            // 这样 HELPER32_READY 里就能带上句柄，父进程的日志更有用。
            StartControlBlockProbe();

            Print(string.Format(CultureInfo.InvariantCulture,
                "HELPER32_READY pid={0} hwnd=0x{1:X8} hook={2} registered={3} regError={4} dllLoaded={5}",
                Process.GetCurrentProcess().Id, unchecked((uint)_hwnd.ToInt64()), hookOk, _registered, regError,
                _hMod != IntPtr.Zero ? 1 : 0));
            if (hookError != null) Print("HELPER32_ERROR hook " + hookError);

            StartParentWatch(options.ParentPid);
            StartStdinWatch();
            if (options.Seconds > 0)
            {
                // 用 long 计算再夹取，避免秒数很大时 (uint)(Seconds * 1000) 溢出成几毫秒。
                long timerMs = (long)options.Seconds * 1000L;
                if (timerMs > MAX_TIMER_MILLISECONDS) timerMs = MAX_TIMER_MILLISECONDS;
                SetTimer(_hwnd, TIMER_ID, (uint)timerMs, IntPtr.Zero);
            }

            // 默认模式装不上钩子就没人需要这个进程：退出，让主程序明确记录降级原因。
            if (hookOk == 0 && !options.SelfTestTarget)
            {
                Print("HELPER32_EXIT reason=hook_failed");
                Cleanup();
                return 5;
            }

            MSG msg;
            while (true)
            {
                int r = GetMessageW(out msg, IntPtr.Zero, 0, 0);
                if (r == -1)
                {
                    Print("HELPER32_ERROR get_message err="
                          + Marshal.GetLastWin32Error().ToString(CultureInfo.InvariantCulture));
                    Cleanup();
                    return 6;
                }
                if (r == 0)
                {
                    Print("HELPER32_EXIT reason=quit_message");
                    break;
                }
                if (msg.message == WM_CLOSE)
                {
                    Print("HELPER32_EXIT reason=wm_close");
                    break;
                }
                if (msg.message == WM_TIMER)
                {
                    Print("HELPER32_EXIT reason=timeout");
                    break;
                }
                // WM_HOTKEY 统一由 WndProc 处理：消息循环里也打印一次会造成重复输出，
                // 而且自测目标需要在第一次收到后立刻 UnregisterHotKey。
                TranslateMessage(ref msg);
                DispatchMessageW(ref msg);
            }

            Cleanup();
            return 0;
        }
        catch (Exception ex)
        {
            Print("HELPER32_ERROR exception=" + ex.GetType().Name + ": " + ex.Message);
            try { Cleanup(); }
            catch (Exception) { }
            return 1;
        }
    }

    // ------------------------------------------------------------------ 窗口 / 钩子

    private static bool CreateHostWindow()
    {
        string className = "HotkeyHoundHelper32_" + Guid.NewGuid().ToString("N").Substring(0, 8);
        _wndProcKeepAlive = new WndProcDelegate(WndProc);
        var wc = new WNDCLASSEX();
        wc.cbSize = Marshal.SizeOf(typeof(WNDCLASSEX));
        wc.lpfnWndProc = Marshal.GetFunctionPointerForDelegate(_wndProcKeepAlive);
        wc.hInstance = GetModuleHandleW(null);
        wc.lpszClassName = className;
        if (RegisterClassExW(ref wc) == 0) return false;

        _hwnd = CreateWindowExW(0, className, "HotkeyHoundHelper32", 0,
                                0, 0, 0, 0, IntPtr.Zero, IntPtr.Zero,
                                GetModuleHandleW(null), IntPtr.Zero);
        return _hwnd != IntPtr.Zero;
    }

    private static int InstallHook(string dllPath, out string error)
    {
        error = null;
        if (string.IsNullOrEmpty(dllPath))
        {
            error = "no_dll_argument";
            return 0;
        }
        try { dllPath = Path.GetFullPath(dllPath); }
        catch (Exception) { }

        _hMod = LoadLibraryExW(dllPath, IntPtr.Zero, LOAD_WITH_ALTERED_SEARCH_PATH);
        if (_hMod == IntPtr.Zero)
        {
            error = "load_library err=" + Marshal.GetLastWin32Error().ToString(CultureInfo.InvariantCulture) + " path=" + dllPath;
            return 0;
        }

        // 铁律：全局钩子必须用原生导出地址。托管委托只在本进程地址空间有效。
        IntPtr proc = GetProcAddress(_hMod, "HotkeyHoundHookProc");
        if (proc == IntPtr.Zero)
        {
            error = "get_proc_address";
            return 0;
        }

        _hook = SetWindowsHookExW(WH_GETMESSAGE, proc, _hMod, 0);
        if (_hook == IntPtr.Zero)
        {
            error = "set_windows_hook_ex err=" + Marshal.GetLastWin32Error().ToString(CultureInfo.InvariantCulture);
            return 0;
        }
        return 1;
    }

    private static IntPtr WndProc(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam)
    {
        if (msg == WM_HOTKEY) ReportHotkey(wParam.ToInt64());
        return DefWindowProcW(hWnd, msg, wParam, lParam);
    }

    /// <summary>
    /// 自测目标只借这个热键确认「钩子链确实能收到真实注册的热键」；收到第一次就注销，
    /// 不留任何注册痕迹（AGENTS.md：RegisterHotKey 成功必须立刻 UnregisterHotKey）。
    /// </summary>
    private static void ReportHotkey(long id)
    {
        Print("HELPER32_GOT_HOTKEY id=" + id.ToString(CultureInfo.InvariantCulture));
        if (_registered != 0)
        {
            if (UnregisterHotKey(_hwnd, 1)) _registered = 0;
        }
    }

    private static void Cleanup()
    {
        try
        {
            if (_registered != 0) UnregisterHotKey(_hwnd, 1);
        }
        catch (Exception) { }

        try
        {
            if (_hook != IntPtr.Zero) UnhookWindowsHookEx(_hook);
        }
        catch (Exception) { }

        try
        {
            if (_hwnd != IntPtr.Zero) DestroyWindow(_hwnd);
        }
        catch (Exception) { }

        _hook = IntPtr.Zero;
        _hwnd = IntPtr.Zero;
        _registered = 0;
    }

    // ------------------------------------------------------------------ 共享内存（只读探测）

    /// <summary>
    /// 读控制器写的控制块，确认两边理解的是同一套布局。
    /// 真正的上报由注入到其它进程的 hook DLL 完成；这里读出来只用于日志/自检，
    /// 读不到也不算失败（控制器可能比宿主晚一点写）。
    /// </summary>
    private static void StartControlBlockProbe()
    {
        if (TryProbeControlBlock()) return;
        var t = new Thread(delegate ()
        {
            for (int i = 0; i < 15; i++)
            {
                Thread.Sleep(200);
                if (TryProbeControlBlock()) return;
            }
            Print("HELPER32_CTL pending (control block not readable)");
        });
        t.IsBackground = true;
        t.Name = "hh32-ctl-probe";
        t.Start();
    }

    private static bool TryProbeControlBlock()
    {
        IntPtr h = IntPtr.Zero;
        IntPtr view = IntPtr.Zero;
        try
        {
            h = OpenFileMappingW(FILE_MAP_READ, false, MapName);
            if (h == IntPtr.Zero) return false;
            view = MapViewOfFile(h, FILE_MAP_READ, 0, 0, 16);
            if (view == IntPtr.Zero) return false;

            uint magic = unchecked((uint)Marshal.ReadInt32(view, 0));
            uint arm = unchecked((uint)Marshal.ReadInt32(view, 4));
            uint lo = unchecked((uint)Marshal.ReadInt32(view, 8));
            uint hi = unchecked((uint)Marshal.ReadInt32(view, 12));
            if (magic != HKS_MAGIC) return false;

            long hwnd = ((long)hi << 32) | lo;
            Print(string.Format(CultureInfo.InvariantCulture,
                "HELPER32_CTL magic=0x{0:X8} arm={1} hwnd=0x{2:X}", magic, arm, hwnd));
            return true;
        }
        catch (Exception)
        {
            return false;
        }
        finally
        {
            try { if (view != IntPtr.Zero) UnmapViewOfFile(view); }
            catch (Exception) { }
            try { if (h != IntPtr.Zero) CloseHandle(h); }
            catch (Exception) { }
        }
    }

    // ------------------------------------------------------------------ 父进程 / stdin 守护

    private static void StartParentWatch(int parentPid)
    {
        if (parentPid <= 0) return;
        var t = new Thread(delegate ()
        {
            IntPtr h = OpenProcess(SYNCHRONIZE, false, parentPid);
            if (h != IntPtr.Zero)
            {
                try
                {
                    WaitForSingleObject(h, 0xFFFFFFFF);
                    RequestQuit("parent_exited");
                }
                finally { CloseHandle(h); }
                return;
            }

            // 拿不到同步句柄（权限不足等）时退回轮询：宁可多等一秒，也不能留僵尸。
            while (true)
            {
                if (!IsProcessAlive(parentPid))
                {
                    RequestQuit("parent_gone");
                    return;
                }
                Thread.Sleep(500);
            }
        });
        t.IsBackground = true;
        t.Name = "hh32-parent-watch";
        t.Start();
    }

    private static bool IsProcessAlive(int pid)
    {
        try
        {
            using (Process p = Process.GetProcessById(pid))
            {
                return !p.HasExited;
            }
        }
        catch (Exception)
        {
            return false;
        }
    }

    private static void StartStdinWatch()
    {
        var t = new Thread(delegate ()
        {
            try
            {
                while (true)
                {
                    string line = Console.In.ReadLine();
                    if (line == null)
                    {
                        RequestQuit("stdin_eof");
                        return;
                    }
                    if (line.Trim().Equals("q", StringComparison.OrdinalIgnoreCase))
                    {
                        RequestQuit("stdin_q");
                        return;
                    }
                }
            }
            catch (Exception)
            {
                RequestQuit("stdin_error");
            }
        });
        t.IsBackground = true;
        t.Name = "hh32-stdin-watch";
        t.Start();
    }

    /// <summary>
    /// 从任意线程请求退出。顺序很重要：先无锁抢占退出标志、立刻启动强制退出线程，
    /// 最后才尝试 Print。Print 会抢 OutputGate，而主线程可能正卡在同一个锁里（stdout
    /// 管道满、父进程不读）；如果先 Print，就轮到 RequestQuit 阻塞，兜底线程永远不会启动。
    /// </summary>
    private static void RequestQuit(string reason)
    {
        if (Interlocked.Exchange(ref _quitting, 1) != 0) return;

        var t = new Thread(delegate ()
        {
            // 800 毫秒：正常路径靠 WM_QUIT 瞬间退出，这个只在主线程卡死时兜底；
            // 父进程被强杀后宿主必须在 1 秒内消失，不能再等 1.5 秒。
            Thread.Sleep(800);
            Environment.Exit(0);
        });
        t.IsBackground = true;
        t.Name = "hh32-forced-exit";
        t.Start();

        try { PostThreadMessageW(MainThreadId, WM_QUIT, IntPtr.Zero, IntPtr.Zero); }
        catch (Exception) { }

        // 尽力而为：Print 失败或阻塞都不影响上面的强制退出。
        Print("HELPER32_EXIT reason=" + reason);
    }

    // ------------------------------------------------------------------ 输出

    private static void Print(string line)
    {
        lock (OutputGate)
        {
            try
            {
                Console.Out.Write(line);
                Console.Out.Write("\r\n");
                Console.Out.Flush();
            }
            catch (Exception) { }
        }
    }

    // ------------------------------------------------------------------ 命令行

    private static void PrintUsage()
    {
        Print("HELPER32_USAGE --dll <path> [--parent <pid>] [--seconds <n>] [--selftest-target]");
    }

    private sealed class Options
    {
        public string DllPath;
        public int ParentPid;
        public bool SelfTestTarget;
        public int Seconds;

        public bool Parse(string[] args)
        {
            for (int i = 0; i < args.Length; i++)
            {
                string a = args[i];
                if (string.IsNullOrEmpty(a)) continue;
                if (a == "--dll")
                {
                    if (i + 1 >= args.Length) return Bad("--dll needs a value");
                    DllPath = args[++i];
                }
                else if (a == "--parent")
                {
                    if (i + 1 >= args.Length) return Bad("--parent needs a value");
                    if (!TryInt(args[++i], out ParentPid)) return Bad("--parent is not an integer");
                }
                else if (a == "--seconds")
                {
                    if (i + 1 >= args.Length) return Bad("--seconds needs a value");
                    if (!TryInt(args[++i], out Seconds)) return Bad("--seconds is not an integer");
                }
                else if (a == "--selftest-target")
                {
                    SelfTestTarget = true;
                }
                else if (a.StartsWith("--dll=", StringComparison.Ordinal))
                {
                    DllPath = a.Substring(6);
                    if (DllPath.Length == 0) return Bad("--dll needs a value");
                }
                else if (a.StartsWith("--parent=", StringComparison.Ordinal))
                {
                    if (!TryInt(a.Substring(9), out ParentPid)) return Bad("--parent is not an integer");
                }
                else if (a.StartsWith("--seconds=", StringComparison.Ordinal))
                {
                    if (!TryInt(a.Substring(10), out Seconds)) return Bad("--seconds is not an integer");
                }
                else
                {
                    // 未知开关必须显式失败：静默忽略会让上层以为参数生效了。
                    return Bad("unknown option " + a);
                }
            }
            return true;
        }

        private static bool Bad(string why)
        {
            Print("HELPER32_ERROR bad_arguments " + why);
            PrintUsage();
            return false;
        }

        private static bool TryInt(string text, out int value)
        {
            return int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out value);
        }
    }

    // ------------------------------------------------------------------ Win32

    private delegate IntPtr WndProcDelegate(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);

    [DllImport("kernel32.dll")]
    private static extern uint GetCurrentThreadId();

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr GetModuleHandleW(string name);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr LoadLibraryExW(string path, IntPtr file, uint flags);

    [DllImport("kernel32.dll", CharSet = CharSet.Ansi, SetLastError = true)]
    private static extern IntPtr GetProcAddress(IntPtr hMod, string proc);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr OpenProcess(uint access, bool inherit, int pid);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern int WaitForSingleObject(IntPtr handle, uint milliseconds);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool CloseHandle(IntPtr handle);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr OpenFileMappingW(uint access, bool inherit, string name);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr MapViewOfFile(IntPtr map, uint access, uint offsetHigh, uint offsetLow, uint bytes);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool UnmapViewOfFile(IntPtr address);

    [DllImport("user32.dll", SetLastError = true, EntryPoint = "SetWindowsHookExW")]
    private static extern IntPtr SetWindowsHookExW(int idHook, IntPtr proc, IntPtr hMod, uint threadId);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool UnhookWindowsHookEx(IntPtr hook);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern ushort RegisterClassExW(ref WNDCLASSEX wc);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr CreateWindowExW(int exStyle, string className, string windowName, int style,
                                                 int x, int y, int w, int h,
                                                 IntPtr parent, IntPtr menu, IntPtr instance, IntPtr param);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool DestroyWindow(IntPtr hWnd);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr DefWindowProcW(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint mods, uint vk);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SetTimer(IntPtr hWnd, int id, uint elapse, IntPtr proc);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern int GetMessageW(out MSG msg, IntPtr hWnd, uint min, uint max);

    [DllImport("user32.dll")]
    private static extern bool TranslateMessage(ref MSG msg);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr DispatchMessageW(ref MSG msg);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool PostThreadMessageW(uint threadId, int msg, IntPtr wParam, IntPtr lParam);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct WNDCLASSEX
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
    private struct POINT
    {
        public int x;
        public int y;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct MSG
    {
        public IntPtr hwnd;
        public int message;
        public IntPtr wParam;
        public IntPtr lParam;
        public int time;
        public POINT pt;
    }
}
