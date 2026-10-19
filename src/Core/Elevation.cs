using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace HotkeyHound.Core
{
    /// <summary>
    /// 提权接口：用 ShellExecuteEx + "runas" 以管理员身份重启自身，保留原有命令行参数，
    /// 并追加内部标记 --elevated 以避免无限重启循环。
    ///
    /// 用户在 UAC 提示里点「否」时 ShellExecuteEx 返回 false 且 GetLastError 为
    /// ERROR_CANCELLED(1223)——这属于正常结果，必须识别为「用户取消」而不是抛出异常。
    /// </summary>
    public static class Elevation
    {
        public const int ERROR_CANCELLED = 1223;

        /// <summary>用户显式要求提权。</summary>
        public const string ElevateFlag = "--elevate";
        /// <summary>内部标记：本进程已经是提权重启后的进程，不要再重启。</summary>
        public const string ElevatedFlag = "--elevated";

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct SHELLEXECUTEINFO
        {
            public int cbSize;
            public uint fMask;
            public IntPtr hwnd;
            public string lpVerb;
            public string lpFile;
            public string lpParameters;
            public string lpDirectory;
            public int nShow;
            public IntPtr hInstApp;
            public IntPtr lpIDList;
            public string lpClass;
            public IntPtr hkeyClass;
            public uint dwHotKey;
            public IntPtr hIcon;
            public IntPtr hProcess;
        }

        [DllImport("shell32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern bool ShellExecuteExW(ref SHELLEXECUTEINFO info);

        private const uint SEE_MASK_NOCLOSEPROCESS = 0x00000040;
        private const int SW_SHOWNORMAL = 1;

        /// <summary>最近一次请求的结果说明；成功时为 null。</summary>
        public static string LastError { get; private set; }

        /// <summary>上次请求是否被用户取消。</summary>
        public static bool LastWasCancelled { get; private set; }

        public static bool IsAdministrator()
        {
            return AppInfo.IsAdministrator();
        }

        /// <summary>命令行里是否带有内部提权标记（说明本进程由提权重启产生）。</summary>
        public static bool WasRelaunched(string[] args)
        {
            if (args == null) return false;
            foreach (string a in args)
            {
                if (string.Equals(a, ElevatedFlag, StringComparison.OrdinalIgnoreCase)) return true;
            }
            return false;
        }

        /// <summary>
        /// 以管理员身份重启自身。成功返回 true（本进程应随即退出）；
        /// 用户取消返回 false 且 LastWasCancelled=true；其它失败返回 false 并写 LastError。
        /// </summary>
        public static bool RequestRestart(string extraArgs)
        {
            LastError = null;
            LastWasCancelled = false;
            try
            {
                string exe = CurrentExecutablePath();
                if (string.IsNullOrEmpty(exe))
                {
                    LastError = L10n.T("elev.noExePath");
                    return false;
                }

                string parameters = BuildRelaunchArguments(extraArgs);

                var info = new SHELLEXECUTEINFO();
                info.cbSize = Marshal.SizeOf(typeof(SHELLEXECUTEINFO));
                info.fMask = SEE_MASK_NOCLOSEPROCESS;
                info.lpVerb = "runas";
                info.lpFile = exe;
                info.lpParameters = parameters;
                info.lpDirectory = SafeDirectory(exe);
                info.nShow = SW_SHOWNORMAL;

                if (!ShellExecuteExW(ref info))
                {
                    int err = Marshal.GetLastWin32Error();
                    if (err == ERROR_CANCELLED)
                    {
                        LastWasCancelled = true;
                        LastError = L10n.T("elev.cancelled");
                        return false;
                    }
                    LastError = L10n.F("elev.shellExecuteFailed", err);
                    return false;
                }

                try { if (info.hProcess != IntPtr.Zero) CloseHandle(info.hProcess); }
                catch (Exception) { }
                return true;
            }
            catch (Exception ex)
            {
                LastError = ex.GetType().Name + "：" + ex.Message;
                return false;
            }
        }

        /// <summary>构造重启用的参数串：去掉提权/已提权标记，保留其它原参数，追加 extraArgs 与内部标记。</summary>
        public static string BuildRelaunchArguments(string extraArgs)
        {
            var sb = new StringBuilder();
            string[] args = null;
            try { args = Environment.GetCommandLineArgs(); }
            catch (Exception) { }

            if (args != null && args.Length > 1)
            {
                for (int i = 1; i < args.Length; i++)
                {
                    string a = args[i];
                    if (a == null) continue;
                    if (string.Equals(a, ElevateFlag, StringComparison.OrdinalIgnoreCase)) continue;
                    if (string.Equals(a, ElevatedFlag, StringComparison.OrdinalIgnoreCase)) continue;
                    AppendQuoted(sb, a);
                }
            }
            if (!string.IsNullOrEmpty(extraArgs)) sb.Append(extraArgs).Append(' ');
            sb.Append(ElevatedFlag);
            return sb.ToString().Trim();
        }

        private static void AppendQuoted(StringBuilder sb, string arg)
        {
            if (arg.IndexOf(' ') >= 0 && arg.IndexOf('"') < 0)
                sb.Append('"').Append(arg).Append('"').Append(' ');
            else
                sb.Append(arg).Append(' ');
        }

        private static string CurrentExecutablePath()
        {
            try
            {
                using (Process p = Process.GetCurrentProcess())
                {
                    if (p.MainModule != null && !string.IsNullOrEmpty(p.MainModule.FileName))
                        return p.MainModule.FileName;
                }
            }
            catch (Exception) { }
            try { return System.Reflection.Assembly.GetExecutingAssembly().Location; }
            catch (Exception) { return null; }
        }

        private static string SafeDirectory(string exe)
        {
            try
            {
                string dir = System.IO.Path.GetDirectoryName(exe);
                if (!string.IsNullOrEmpty(dir) && System.IO.Directory.Exists(dir)) return dir;
            }
            catch (Exception) { }
            return null;
        }

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool CloseHandle(IntPtr h);
    }
}
