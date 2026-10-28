using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace HotkeyHound.Core
{
    /// <summary>组合键模型。修饰键顺序固定为 Win+Ctrl+Alt+Shift。</summary>
    public sealed class KeyCombo
    {
        public bool Win;
        public bool Ctrl;
        public bool Alt;
        public bool Shift;
        public uint Vk;

        public KeyCombo() { }

        public KeyCombo(bool win, bool ctrl, bool alt, bool shift, uint vk)
        {
            Win = win; Ctrl = ctrl; Alt = alt; Shift = shift; Vk = vk;
        }

        public KeyCombo Clone()
        {
            return new KeyCombo(Win, Ctrl, Alt, Shift, Vk);
        }

        public bool HasModifier
        {
            get { return Win || Ctrl || Alt || Shift; }
        }

        public string Display()
        {
            var sb = new StringBuilder();
            if (Win) sb.Append("Win+");
            if (Ctrl) sb.Append("Ctrl+");
            if (Alt) sb.Append("Alt+");
            if (Shift) sb.Append("Shift+");
            sb.Append(VkNames.Name(Vk));
            return sb.ToString();
        }

        public string Normalize()
        {
            return Display();
        }

        /// <summary>忽略 Shift 的规范形，用于模糊匹配（"可能有 Shift 变体"）。</summary>
        public string NormalizeIgnoreShift()
        {
            var c = Clone();
            c.Shift = false;
            return c.Display();
        }

        public override string ToString()
        {
            return Display();
        }

        public override int GetHashCode()
        {
            return (int)(Vk * 31 + (Win ? 1 : 0) + (Ctrl ? 2 : 0) + (Alt ? 4 : 0) + (Shift ? 8 : 0));
        }

        public override bool Equals(object obj)
        {
            var o = obj as KeyCombo;
            if (o == null) return false;
            return o.Vk == Vk && o.Win == Win && o.Ctrl == Ctrl && o.Alt == Alt && o.Shift == Shift;
        }

        public static KeyCombo Parse(string s)
        {
            if (s == null) return null;
            string text = s.Trim();
            if (text.Length == 0) return null;

            string[] parts = text.Split('+');
            // 形如 "=+" 的按键名自身含有 '+'，切分后末尾会留下空串；
            // 因此从后往前尝试 1/2/3 个 token 的合并结果能否解析成按键名。
            for (int take = 1; take <= 3; take++)
            {
                int modCount = parts.Length - take;
                if (modCount < 0) break;
                var nameParts = new string[take];
                Array.Copy(parts, modCount, nameParts, 0, take);
                string keyName = string.Join("+", nameParts);
                uint vk;
                if (!VkNames.TryGetVk(keyName, out vk)) continue;

                bool win = false, ctrl = false, alt = false, shift = false;
                bool ok = true;
                for (int i = 0; i < modCount; i++)
                {
                    string m = parts[i].Trim();
                    if (m.Length == 0) { ok = false; break; }
                    switch (m.ToLowerInvariant())
                    {
                        case "win": case "windows": win = true; break;
                        case "ctrl": case "control": ctrl = true; break;
                        case "alt": alt = true; break;
                        case "shift": shift = true; break;
                        default: ok = false; break;
                    }
                    if (!ok) break;
                }
                if (!ok) continue;
                return new KeyCombo(win, ctrl, alt, shift, vk);
            }
            return null;
        }

        /// <summary>PowerToys 的 "162;164;66" 形式（十进制 VK 列表）。</summary>
        public static KeyCombo FromVkList(string s)
        {
            if (string.IsNullOrEmpty(s)) return null;
            string[] parts = s.Split(new char[] { ';', ',', ' ' }, StringSplitOptions.RemoveEmptyEntries);
            bool win = false, ctrl = false, alt = false, shift = false;
            uint vk = 0;
            bool any = false;
            foreach (string p in parts)
            {
                uint v;
                if (!TryParseVk(p, out v)) continue;
                any = true;
                switch (v)
                {
                    case 0x10: case 0xA0: case 0xA1: shift = true; break;
                    case 0x11: case 0xA2: case 0xA3: ctrl = true; break;
                    case 0x12: case 0xA4: case 0xA5: alt = true; break;
                    case 0x5B: case 0x5C: win = true; break;
                    default: vk = v; break;
                }
            }
            if (!any || vk == 0) return null;
            return new KeyCombo(win, ctrl, alt, shift, vk);
        }

        /// <summary>
        /// 解析 WM_HOTKEY 的 lParam：LOWORD 是修饰键位（MOD_ALT/CONTROL/SHIFT/WIN），HIWORD 是 VK。
        /// </summary>
        public static KeyCombo FromHotkeyLParam(long lParam)
        {
            uint mods = (uint)(lParam & 0xFFFF);
            uint vk = (uint)((lParam >> 16) & 0xFFFF);
            if (vk == 0) return null;
            // MOD_NOREPEAT(0x4000) 不属于归属信息
            return new KeyCombo((mods & 0x0008) != 0, (mods & 0x0002) != 0, (mods & 0x0001) != 0, (mods & 0x0004) != 0, vk);
        }

        /// <summary>组合键 → WM_HOTKEY 的 lParam，供自测合成消息。</summary>
        public long ToHotkeyLParam()
        {
            uint mods = 0;
            if (Alt) mods |= 0x0001;
            if (Ctrl) mods |= 0x0002;
            if (Shift) mods |= 0x0004;
            if (Win) mods |= 0x0008;
            return (long)(mods | (Vk << 16));
        }

        public static bool TryParseVk(string token, out uint vk)
        {
            vk = 0;
            if (string.IsNullOrEmpty(token)) return false;
            string t = token.Trim();
            if (t.Length == 0) return false;
            try
            {
                if (t.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
                {
                    vk = uint.Parse(t.Substring(2), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
                }
                else if (t.EndsWith("h", StringComparison.OrdinalIgnoreCase))
                {
                    vk = uint.Parse(t.Substring(0, t.Length - 1), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
                }
                else
                {
                    vk = uint.Parse(t, CultureInfo.InvariantCulture);
                }
            }
            catch (Exception) { return false; }
            return vk > 0;
        }

        /// <summary>VK → 十进制分号串（PowerToys originalKeys 格式），用于自测与报告。</summary>
        public string ToVkList()
        {
            var list = new List<string>();
            if (Shift) list.Add("160");
            if (Ctrl) list.Add("162");
            if (Alt) list.Add("164");
            if (Win) list.Add("91");
            list.Add(Vk.ToString(CultureInfo.InvariantCulture));
            return string.Join(";", list.ToArray());
        }
    }

    /// <summary>虚拟键名表（VK ↔ 显示名）。</summary>
    public static class VkNames
    {
        private static readonly Dictionary<uint, string> ToName = new Dictionary<uint, string>();
        private static readonly Dictionary<string, uint> ToVk = new Dictionary<string, uint>(StringComparer.OrdinalIgnoreCase);

        static VkNames()
        {
            Add(0x08, "Backspace");
            Add(0x09, "Tab");
            Add(0x0D, "Enter");
            Add(0x13, "Pause");
            Add(0x14, "CapsLock");
            Add(0x1B, "Esc");
            Add(0x20, "Space");
            Add(0x21, "PgUp");
            Add(0x22, "PgDn");
            Add(0x23, "End");
            Add(0x24, "Home");
            Add(0x25, "Left");
            Add(0x26, "Up");
            Add(0x27, "Right");
            Add(0x28, "Down");
            Add(0x2C, "PrintScreen");
            Add(0x2D, "Ins");
            Add(0x2E, "Del");
            Add(0x2F, "Help");
            Add(0x5B, "LWin");
            Add(0x5C, "RWin");
            Add(0x5D, "Apps");
            Add(0x90, "NumLock");
            Add(0x91, "ScrollLock");

            Add(0x6A, "Num*");
            Add(0x6B, "Num+");
            Add(0x6C, "NumSeparator");
            Add(0x6D, "Num-");
            Add(0x6E, "Num.");
            Add(0x6F, "Num/");

            Add(0xBA, ";:");
            Add(0xBB, "=+");
            Add(0xBC, ",<");
            Add(0xBD, "-_");
            Add(0xBE, ".>");
            Add(0xBF, "/?");
            Add(0xC0, "`~");
            Add(0xDB, "[{");
            Add(0xDC, "\\|");
            Add(0xDD, "]}");
            Add(0xDE, "'\"");

            for (uint i = 0; i < 10; i++)
            {
                Add(0x30 + i, ((char)('0' + i)).ToString());
                Add(0x60 + i, "Num" + ((char)('0' + i)));
            }
            for (uint i = 0; i < 26; i++)
            {
                Add(0x41 + i, ((char)('A' + i)).ToString());
            }
            for (uint i = 1; i <= 24; i++)
            {
                Add(0x6F + i, "F" + i.ToString(CultureInfo.InvariantCulture));
            }

            Alias("Win", 0x5B);
            Alias("Return", 0x0D);
            Alias("Escape", 0x1B);
            Alias("Delete", 0x2E);
            Alias("Insert", 0x2D);
            Alias("PageUp", 0x21);
            Alias("PageDown", 0x22);
            Alias("ArrowUp", 0x26);
            Alias("ArrowDown", 0x28);
            Alias("ArrowLeft", 0x25);
            Alias("ArrowRight", 0x27);
            Alias("Back", 0x08);
            Alias("Space", 0x20);
            Alias("Break", 0x13);
            Alias("PrtScn", 0x2C);
            Alias("PrintScrn", 0x2C);
            Alias("Menu", 0x5D);
            Alias("Multiply", 0x6A);
            Alias("Add", 0x6B);
            Alias("Separator", 0x6C);
            Alias("Subtract", 0x6D);
            Alias("Decimal", 0x6E);
            Alias("Divide", 0x6F);
        }

        private static void Add(uint vk, string name)
        {
            ToName[vk] = name;
            if (!ToVk.ContainsKey(name)) ToVk[name] = vk;
        }

        private static void Alias(string name, uint vk)
        {
            if (!ToVk.ContainsKey(name)) ToVk[name] = vk;
        }

        public static string Name(uint vk)
        {
            string n;
            if (ToName.TryGetValue(vk, out n)) return n;
            if (vk >= 0x30 && vk <= 0x5A) return "VK_" + vk.ToString("X2", CultureInfo.InvariantCulture);
            return "VK" + vk.ToString(CultureInfo.InvariantCulture);
        }

        public static bool TryGetVk(string name, out uint vk)
        {
            vk = 0;
            if (string.IsNullOrEmpty(name)) return false;
            string n = name.Trim();
            if (ToVk.TryGetValue(n, out vk)) return true;
            // OBS 的 OBS_KEY_XXX 形式
            if (n.StartsWith("OBS_KEY_", StringComparison.OrdinalIgnoreCase))
            {
                string rest = n.Substring(8);
                if (ToVk.TryGetValue(rest, out vk)) return true;
            }
            if (n.StartsWith("VK", StringComparison.OrdinalIgnoreCase) && n.Length > 2)
            {
                uint parsed;
                if (KeyCombo.TryParseVk(n.Substring(2), out parsed)) { vk = parsed; return true; }
            }
            return false;
        }

        public static string ModifierName(uint modVk)
        {
            switch (modVk)
            {
                case 0x10: case 0xA0: case 0xA1: return "Shift";
                case 0x11: case 0xA2: case 0xA3: return "Ctrl";
                case 0x12: case 0xA4: case 0xA5: return "Alt";
                case 0x5B: case 0x5C: return "Win";
                default: return null;
            }
        }

        /// <summary>显示名 → VK，供解析器与报告使用。</summary>
        public static bool IsKnownName(string name)
        {
            uint vk;
            return TryGetVk(name, out vk);
        }
    }
}
