using System;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;

namespace HotkeyHound.Core
{
    /// <summary>
    /// 自测用的辅助进程宿主（`--helper`）。
    /// 它注册一个热键后泵消息；父进程往它的窗口 PostMessage(WM_HOTKEY) 就能
    /// 确定性地触发「注入进来的 hook DLL → 拦截 → 回调」，不依赖真实按键输入，
    /// 所以桌面锁定也能跑完整条归属链路。
    /// </summary>
    public static class HelperHost
    {
        private const int WM_HOTKEY = 0x0312;
        private const int WM_CLOSE = 0x0010;
        private const int WM_TIMER = 0x0113;
        private const int TIMER_ID = 1;

        private static WndProcDelegate _wndProcKeepAlive;
        private static readonly StringBuilder Output = new StringBuilder();

        public static int Run(string[] args)
        {
            int seconds = 60;
            string keyText = "Ctrl+Alt+F13";
            for (int i = 0; i < args.Length; i++)
            {
                if (args[i] == "--seconds" && i + 1 < args.Length)
                {
                    int n;
                    if (int.TryParse(args[i + 1], NumberStyles.Integer, CultureInfo.InvariantCulture, out n)) seconds = n;
                    i++;
                }
                else if (args[i] == "--key" && i + 1 < args.Length)
                {
                    keyText = args[i + 1];
                    i++;
                }
            }

            KeyCombo combo = KeyCombo.Parse(keyText);
            if (combo == null)
            {
                Say("HELPER_ERROR 无法解析热键：" + keyText);
                Flush();
                return 4;
            }

            string className = "HotkeyHoundHelper_" + Guid.NewGuid().ToString("N").Substring(0, 8);
            _wndProcKeepAlive = new WndProcDelegate(WndProc);

            var wc = new NativeMethods.WNDCLASSEX();
            wc.cbSize = Marshal.SizeOf(typeof(NativeMethods.WNDCLASSEX));
            wc.lpfnWndProc = Marshal.GetFunctionPointerForDelegate(_wndProcKeepAlive);
            wc.hInstance = NativeMethods.GetModuleHandleW(null);
            wc.lpszClassName = className;
            if (NativeMethods.RegisterClassExW(ref wc) == 0)
            {
                Say("HELPER_ERROR RegisterClassEx 失败，错误码 " + Marshal.GetLastWin32Error());
                Flush();
                return 4;
            }

            IntPtr hwnd = NativeMethods.CreateWindowExW(0, className, "HotkeyHoundHelper", 0,
                                                        0, 0, 0, 0, IntPtr.Zero, IntPtr.Zero,
                                                        NativeMethods.GetModuleHandleW(null), IntPtr.Zero);
            if (hwnd == IntPtr.Zero)
            {
                Say("HELPER_ERROR CreateWindowEx 失败，错误码 " + Marshal.GetLastWin32Error());
                Flush();
                return 4;
            }

            uint mods = 0;
            if (combo.Alt) mods |= 0x0001;
            if (combo.Ctrl) mods |= 0x0002;
            if (combo.Shift) mods |= 0x0004;
            if (combo.Win) mods |= 0x0008;

            bool registered = NativeMethods.RegisterHotKey(hwnd, 1, mods, combo.Vk);
            int regError = registered ? 0 : Marshal.GetLastWin32Error();

            Say(string.Format("HELPER_READY pid={0} hwnd={1} key={2} registered={3} regError={4}",
                              System.Diagnostics.Process.GetCurrentProcess().Id,
                              hwnd.ToInt64(), combo.Display(), registered ? 1 : 0, regError));
            Flush();

            if (seconds > 0) NativeMethods.SetTimer(hwnd, TIMER_ID, (uint)(seconds * 1000), IntPtr.Zero);

            NativeMethods.MSG msg;
            while (true)
            {
                int r = NativeMethods.GetMessageW(out msg, IntPtr.Zero, 0, 0);
                if (r <= 0) break;
                if (msg.message == WM_CLOSE || msg.message == WM_TIMER)
                {
                    Say("HELPER_EXIT reason=" + (msg.message == WM_CLOSE ? "wm_close" : "timeout"));
                    break;
                }
                if (msg.message == WM_HOTKEY)
                {
                    Say("HELPER_GOT_HOTKEY id=" + msg.wParam.ToInt64() + " lParam=" + msg.lParam.ToInt64());
                    Flush();
                }
                NativeMethods.TranslateMessage(ref msg);
                NativeMethods.DispatchMessageW(ref msg);
            }

            if (registered) NativeMethods.UnregisterHotKey(hwnd, 1);
            NativeMethods.DestroyWindow(hwnd);
            Flush();
            return 0;
        }

        private static IntPtr WndProc(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam)
        {
            return NativeMethods.DefWindowProcW(hWnd, msg, wParam, lParam);
        }

        private static void Say(string line)
        {
            Output.Append(line).Append("\r\n");
        }

        private static void Flush()
        {
            try
            {
                Console.Out.Write(Output.ToString());
                Console.Out.Flush();
            }
            catch (Exception) { }
            Output.Length = 0;
        }
    }
}
