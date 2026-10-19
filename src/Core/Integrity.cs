using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;

namespace HotkeyHound.Core
{
    public enum IntegrityLevel
    {
        Unknown = -1,
        Untrusted = 0x0000,
        Low = 0x1000,
        Medium = 0x2000,
        High = 0x3000,
        System = 0x4000
    }

    /// <summary>
    /// 进程完整性级别探测。
    /// 工作区目录带 Mandatory Label\Low 时，从那里启动的程序会以低完整性运行，
    /// UIPI 会拦住对普通进程的钩子注入——必须提前告诉用户，而不是让功能静默失效。
    /// </summary>
    public static class ProcessIntegrity
    {
        [DllImport("advapi32.dll", SetLastError = true)]
        private static extern bool OpenProcessToken(IntPtr h, uint access, out IntPtr token);

        [DllImport("advapi32.dll", SetLastError = true)]
        private static extern bool GetTokenInformation(IntPtr token, int cls, IntPtr info, int len, out int ret);

        [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern bool ConvertSidToStringSidW(IntPtr sid, out IntPtr str);

        [DllImport("kernel32.dll")]
        private static extern IntPtr GetCurrentProcess();

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool CloseHandle(IntPtr h);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr LocalFree(IntPtr h);

        private const uint TOKEN_QUERY = 0x0008;
        private const int TokenIntegrityLevel = 25;

        private static bool _cached;
        private static IntegrityLevel _level = IntegrityLevel.Unknown;

        public static IntegrityLevel Current
        {
            get
            {
                if (!_cached)
                {
                    _level = Probe();
                    _cached = true;
                }
                return _level;
            }
        }

        public static int CurrentRid
        {
            get { return (int)Current; }
        }

        public static bool IsBelowMedium
        {
            get { return Current != IntegrityLevel.Unknown && (int)Current < 0x2000; }
        }

        private static IntegrityLevel Probe()
        {
            IntPtr token = IntPtr.Zero;
            try
            {
                if (!OpenProcessToken(GetCurrentProcess(), TOKEN_QUERY, out token)) return IntegrityLevel.Unknown;
                int size;
                GetTokenInformation(token, TokenIntegrityLevel, IntPtr.Zero, 0, out size);
                if (size <= 0) return IntegrityLevel.Unknown;

                IntPtr buf = Marshal.AllocHGlobal(size);
                try
                {
                    int ret;
                    if (!GetTokenInformation(token, TokenIntegrityLevel, buf, size, out ret)) return IntegrityLevel.Unknown;
                    IntPtr sid = Marshal.ReadIntPtr(buf);   // TOKEN_MANDATORY_LABEL.Label.Sid
                    if (sid == IntPtr.Zero) return IntegrityLevel.Unknown;

                    // 直接拿 SID 字符串再取最后一段：比手算子权威偏移稳妥得多
                    // （GetSidSubAuthorityCount 返回的是 PUCHAR，按字节马歇尔化会读到指针高位）。
                    uint rid;
                    if (!TryGetRidFromSid(sid, out rid)) return IntegrityLevel.Unknown;

                    switch (rid)
                    {
                        case 0x0000: return IntegrityLevel.Untrusted;
                        case 0x1000: return IntegrityLevel.Low;
                        case 0x2000: return IntegrityLevel.Medium;
                        case 0x3000: return IntegrityLevel.High;
                        case 0x4000: return IntegrityLevel.System;
                        default: return IntegrityLevel.Unknown;
                    }
                }
                finally { Marshal.FreeHGlobal(buf); }
            }
            catch (Exception) { return IntegrityLevel.Unknown; }
            finally { if (token != IntPtr.Zero) CloseHandle(token); }
        }

        /// <summary>把完整性 SID（形如 S-1-16-8192）的最后一段解析成 RID。</summary>
        private static bool TryGetRidFromSid(IntPtr sid, out uint rid)
        {
            rid = 0;
            IntPtr strPtr = IntPtr.Zero;
            try
            {
                if (!ConvertSidToStringSidW(sid, out strPtr)) return false;
                string sidText = Marshal.PtrToStringUni(strPtr);
                if (string.IsNullOrEmpty(sidText)) return false;
                int dash = sidText.LastIndexOf('-');
                if (dash < 0 || dash + 1 >= sidText.Length) return false;
                return uint.TryParse(sidText.Substring(dash + 1),
                                     System.Globalization.NumberStyles.Integer,
                                     System.Globalization.CultureInfo.InvariantCulture, out rid);
            }
            catch (Exception) { return false; }
            finally { if (strPtr != IntPtr.Zero) LocalFree(strPtr); }
        }

        /// <summary>诊断用：当前令牌的完整性 SID 原文。</summary>
        public static string CurrentSidText()
        {
            IntPtr token = IntPtr.Zero;
            IntPtr buf = IntPtr.Zero;
            try
            {
                if (!OpenProcessToken(GetCurrentProcess(), TOKEN_QUERY, out token)) return L10n.T("il.sidReadFailed");
                int size;
                GetTokenInformation(token, TokenIntegrityLevel, IntPtr.Zero, 0, out size);
                if (size <= 0) return L10n.T("il.sidReadFailed");
                buf = Marshal.AllocHGlobal(size);
                int ret;
                if (!GetTokenInformation(token, TokenIntegrityLevel, buf, size, out ret)) return L10n.T("il.sidReadFailed");
                IntPtr sid = Marshal.ReadIntPtr(buf);
                IntPtr strPtr = IntPtr.Zero;
                try
                {
                    if (!ConvertSidToStringSidW(sid, out strPtr)) return L10n.T("il.sidReadFailed");
                    return Marshal.PtrToStringUni(strPtr);
                }
                finally { if (strPtr != IntPtr.Zero) LocalFree(strPtr); }
            }
            catch (Exception) { return L10n.T("il.sidReadFailed"); }
            finally
            {
                if (buf != IntPtr.Zero) Marshal.FreeHGlobal(buf);
                if (token != IntPtr.Zero) CloseHandle(token);
            }
        }

        public static string Describe(IntegrityLevel level)
        {
            switch (level)
            {
                case IntegrityLevel.Untrusted: return L10n.T("il.untrusted");
                case IntegrityLevel.Low: return L10n.T("il.low");
                case IntegrityLevel.Medium: return L10n.T("il.medium");
                case IntegrityLevel.High: return L10n.T("il.high");
                case IntegrityLevel.System: return L10n.T("il.system");
                default: return L10n.T("il.unknown");
            }
        }

        /// <summary>给用户看的完整提示；没有问题时返回 null。</summary>
        public static string WarningText()
        {
            var parts = new List<string>();
            if (IsBelowMedium)
            {
                parts.Add(L10n.F("il.warn.low", Describe(Current)));
            }
            if (!AppInfo.IsAdministrator())
            {
                parts.Add(L10n.T("il.warn.notAdmin"));
            }
            if (parts.Count == 0) return null;
            return string.Join("　", parts.ToArray());
        }
    }

    /// <summary>
    /// 把内嵌的原生 hook DLL 释放到磁盘。
    /// 顺序按可写性尝试：%LOCALAPPDATA%\HotkeyHound\hooks\x64\ → exe 同目录 hooks\x64\ → %TEMP%\HotkeyHound\hooks\x64\。
    /// </summary>
    public static class HookDllLocator
    {
        public const string ResourceName = "HotkeyHound.Hook.dll";
        public const string FileName = "HotkeyHound.Hook.dll";

        private static string _cachedPath;
        private static readonly List<string> _notes = new List<string>();

        public static string[] Notes { get { return _notes.ToArray(); } }
        public static string LastError { get; private set; }

        public static List<string> CandidateDirectories()
        {
            var list = new List<string>();
            try
            {
                string la = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
                if (!string.IsNullOrEmpty(la)) list.Add(Path.Combine(la, "HotkeyHound", "hooks", "x64"));
            }
            catch (Exception) { }

            try
            {
                string exeDir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
                if (!string.IsNullOrEmpty(exeDir)) list.Add(Path.Combine(exeDir, "hooks", "x64"));
            }
            catch (Exception) { }

            try
            {
                string tmp = Path.GetTempPath();
                if (!string.IsNullOrEmpty(tmp)) list.Add(Path.Combine(tmp, "HotkeyHound", "hooks", "x64"));
            }
            catch (Exception) { }

            return list;
        }

        public static byte[] EmbeddedBytes()
        {
            try
            {
                Assembly asm = Assembly.GetExecutingAssembly();
                using (Stream s = asm.GetManifestResourceStream(ResourceName))
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
                LastError = L10n.F("il.embeddedReadFailed", ex.Message);
                return null;
            }
        }

        /// <summary>释放并返回 DLL 的绝对路径；失败返回 null（LastError 里有原因）。</summary>
        public static string Extract()
        {
            if (_cachedPath != null && File.Exists(_cachedPath)) return _cachedPath;
            _notes.Clear();
            LastError = null;

            byte[] bytes = EmbeddedBytes();
            if (bytes == null || bytes.Length == 0)
            {
                if (LastError == null) LastError = L10n.F("il.resourceMissing", ResourceName);
                return null;
            }

            foreach (string dir in CandidateDirectories())
            {
                try
                {
                    Directory.CreateDirectory(dir);
                    string target = Path.Combine(dir, FileName);

                    bool needWrite = true;
                    if (File.Exists(target))
                    {
                        // 必须比内容，不能只比长度：共享内存布局改过之后，新旧 DLL 的长度可能一样，
                        // 只比长度会把旧 DLL 当成「已是最新」而不覆盖，结果是 x64 链路静默失效。
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

                    // 复验一遍确实可读可写
                    string probe = Path.Combine(dir, ".hs-probe");
                    File.WriteAllText(probe, "ok");
                    File.Delete(probe);

                    _cachedPath = target;
                    _notes.Add(L10n.F("il.dllExtracted", target, bytes.Length));
                    return target;
                }
                catch (Exception ex)
                {
                    _notes.Add(L10n.F("il.dllDirSkipped", dir, ex.GetType().Name, ex.Message));
                }
            }

            LastError = L10n.F("il.noWritableDir",
                               _notes.Count > 0 ? _notes[_notes.Count - 1] : L10n.T("il.noCandidate"));
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
}
