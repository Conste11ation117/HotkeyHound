using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace HotkeyHound.Core
{
    public sealed class OccupancyResult
    {
        public KeyCombo Combo;
        public bool Occupied;
        public uint Win32Error;
    }

    public static class NativeHotkey
    {
        public const uint MOD_ALT = 0x0001;
        public const uint MOD_CONTROL = 0x0002;
        public const uint MOD_SHIFT = 0x0004;
        public const uint MOD_WIN = 0x0008;
        public const uint MOD_NOREPEAT = 0x4000;
        public const uint ERROR_HOTKEY_ALREADY_REGISTERED = 1409;

        [DllImport("user32.dll", SetLastError = true)]
        public static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

        [DllImport("user32.dll", SetLastError = true)]
        public static extern bool UnregisterHotKey(IntPtr hWnd, int id);
    }

    /// <summary>引擎 A：把「修饰键组合 × 按键」全部试探一遍，得到占用/空闲地图。</summary>
    public sealed class OccupancyScanner
    {
        private const int HotkeyId = 0x4853;

        private static readonly uint[][] ModifierSets = new uint[][]
        {
            new uint[] { NativeHotkey.MOD_ALT },                                         // Alt
            new uint[] { NativeHotkey.MOD_CONTROL },                                     // Ctrl
            new uint[] { NativeHotkey.MOD_CONTROL, NativeHotkey.MOD_ALT },               // Ctrl+Alt
            new uint[] { NativeHotkey.MOD_SHIFT },                                       // Shift
            new uint[] { NativeHotkey.MOD_SHIFT, NativeHotkey.MOD_ALT },                 // Shift+Alt
            new uint[] { NativeHotkey.MOD_SHIFT, NativeHotkey.MOD_CONTROL },             // Shift+Ctrl
            new uint[] { NativeHotkey.MOD_SHIFT, NativeHotkey.MOD_CONTROL, NativeHotkey.MOD_ALT },
            new uint[] { NativeHotkey.MOD_WIN },                                         // Win
            new uint[] { NativeHotkey.MOD_WIN, NativeHotkey.MOD_ALT },
            new uint[] { NativeHotkey.MOD_WIN, NativeHotkey.MOD_CONTROL },
            new uint[] { NativeHotkey.MOD_WIN, NativeHotkey.MOD_CONTROL, NativeHotkey.MOD_ALT },
            new uint[] { NativeHotkey.MOD_WIN, NativeHotkey.MOD_SHIFT },
            new uint[] { NativeHotkey.MOD_WIN, NativeHotkey.MOD_SHIFT, NativeHotkey.MOD_ALT },
            new uint[] { NativeHotkey.MOD_WIN, NativeHotkey.MOD_SHIFT, NativeHotkey.MOD_CONTROL },
            new uint[] { NativeHotkey.MOD_WIN, NativeHotkey.MOD_SHIFT, NativeHotkey.MOD_CONTROL, NativeHotkey.MOD_ALT }
        };

        /// <summary>探测用按键集合（字母/数字/F1-F24/编辑键/标点/小键盘）。</summary>
        public static readonly uint[] Keys = BuildKeys();

        private static uint[] BuildKeys()
        {
            var list = new List<uint>();
            for (uint vk = 0x41; vk <= 0x5A; vk++) list.Add(vk);           // A-Z
            for (uint i = 0; i < 10; i++) list.Add(0x30 + i);              // 0-9
            for (uint i = 1; i <= 24; i++) list.Add(0x6F + i);             // F1-F24
            list.Add(0x08); // Backspace
            list.Add(0x09); // Tab
            list.Add(0x0D); // Enter
            list.Add(0x1B); // Esc
            list.Add(0x20); // Space
            list.Add(0x24); // Home
            list.Add(0x23); // End
            list.Add(0x21); // PgUp
            list.Add(0x22); // PgDn
            list.Add(0x25); // Left
            list.Add(0x26); // Up
            list.Add(0x27); // Right
            list.Add(0x28); // Down
            list.Add(0x2D); // Ins
            list.Add(0x2E); // Del
            list.Add(0x2C); // PrintScreen
            list.Add(0x13); // Pause
            list.Add(0x2F); // Help
            list.Add(0x5B); // LWin
            list.Add(0x5C); // RWin
            list.Add(0x5D); // Apps
            list.Add(0xBC); // , <
            list.Add(0xBE); // . >
            list.Add(0xBF); // / ?
            list.Add(0xBA); // ; :
            list.Add(0xDE); // ' "
            list.Add(0xC0); // ` ~
            list.Add(0xDB); // [ {
            list.Add(0xDD); // ] }
            list.Add(0xDC); // \ |
            list.Add(0xBD); // - _
            list.Add(0xBB); // = +
            list.Add(0x6A); // Num *
            list.Add(0x6B); // Num +
            list.Add(0x6C); // Num -
            list.Add(0x6E); // Num /
            list.Add(0x6D); // Num .
            for (uint i = 0; i < 10; i++) list.Add(0x60 + i);              // Num0-9
            list.Add(0x90); // NumLock
            list.Add(0x91); // ScrollLock
            list.Add(0x14); // CapsLock
            return list.ToArray();
        }

        public static int TotalCombos
        {
            get { return ModifierSets.Length * Keys.Length; }
        }

        /// <summary>单个组合键试探。注册成功立即注销，不留任何副作用。</summary>
        public static bool Probe(uint[] mods, uint vk, out uint err)
        {
            uint flags = 0;
            foreach (uint m in mods) flags |= m;
            bool ok = NativeHotkey.RegisterHotKey(IntPtr.Zero, HotkeyId, flags | NativeHotkey.MOD_NOREPEAT, vk);
            if (ok)
            {
                NativeHotkey.UnregisterHotKey(IntPtr.Zero, HotkeyId);
                err = 0;
                return false;
            }
            // 必须用 Marshal.GetLastWin32Error()：再 P/Invoke 一次 GetLastError 会先被
            // 编组器自身的调用覆盖掉。（DllImport 已声明 SetLastError=true）
            err = (uint)Marshal.GetLastWin32Error();
            return true;
        }

        public List<OccupancyResult> Scan(Action<int, int> progress)
        {
            var results = new List<OccupancyResult>(TotalCombos);
            int total = TotalCombos;
            int done = 0;
            foreach (uint[] mods in ModifierSets)
            {
                foreach (uint vk in Keys)
                {
                    uint err;
                    bool occupied = Probe(mods, vk, out err);
                    var r = new OccupancyResult();
                    r.Combo = new KeyCombo(
                        (Has(mods, NativeHotkey.MOD_WIN)),
                        (Has(mods, NativeHotkey.MOD_CONTROL)),
                        (Has(mods, NativeHotkey.MOD_ALT)),
                        (Has(mods, NativeHotkey.MOD_SHIFT)),
                        vk);
                    r.Occupied = occupied;
                    r.Win32Error = err;
                    results.Add(r);

                    done++;
                    if (progress != null && (done % 25 == 0 || done == total)) progress(done, total);
                }
            }
            if (progress != null) progress(total, total);
            return results;
        }

        private static bool Has(uint[] mods, uint flag)
        {
            foreach (uint m in mods) if (m == flag) return true;
            return false;
        }

        public static List<OccupancyResult> Occupied(List<OccupancyResult> all)
        {
            var list = new List<OccupancyResult>();
            foreach (var r in all) if (r.Occupied) list.Add(r);
            return list;
        }

        public static List<OccupancyResult> Free(List<OccupancyResult> all)
        {
            var list = new List<OccupancyResult>();
            foreach (var r in all) if (!r.Occupied) list.Add(r);
            return list;
        }

        /// <summary>两次扫描结果的组合键重合度（Jaccard 系数），供 T2 使用。</summary>
        public static double Overlap(List<OccupancyResult> a, List<OccupancyResult> b)
        {
            var sa = new HashSet<string>();
            var sb = new HashSet<string>();
            foreach (var r in a) if (r.Occupied) sa.Add(r.Combo.Normalize());
            foreach (var r in b) if (r.Occupied) sb.Add(r.Combo.Normalize());
            if (sa.Count == 0 && sb.Count == 0) return 1.0;
            int inter = 0;
            foreach (string s in sa) if (sb.Contains(s)) inter++;
            int union = sa.Count + sb.Count - inter;
            return union == 0 ? 1.0 : (double)inter / union;
        }
    }
}
