using System;
using System.Collections.Generic;
using System.IO;

namespace HotkeyHound.Core.Adapters
{
    public sealed class EverythingAdapter : AdapterBase
    {
        public override string Software { get { return "Everything"; } }

        private static readonly string[] Keys = new string[]
        {
            "new_window_key", "show_window_key", "toggle_window_key"
        };

        private static readonly Dictionary<string, string> Purposes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            { "new_window_key", "打开新搜索窗口" },
            { "show_window_key", "显示主窗口" },
            { "toggle_window_key", "显示/隐藏主窗口" }
        };

        public override IEnumerable<DeclaredHotkey> Collect()
        {
            return Safe(this, CollectCore);
        }

        private List<DeclaredHotkey> CollectCore()
        {
            var list = new List<DeclaredHotkey>();
            string file = Path.Combine(AdapterUtil.RoamingAppData, "Everything", "Everything.ini");
            if (!File.Exists(file))
            {
                Note("未找到配置文件：" + file);
                return list;
            }

            var values = IniRead.ReadSectionlessKeys(file);
            foreach (string key in Keys)
            {
                string raw = IniRead.Get(values, key);
                if (raw == null) continue;
                raw = raw.Trim();
                if (raw.Length == 0 || raw == "0") continue;

                var combo = ParseEverythingValue(raw);
                if (combo == null)
                {
                    Warn("无法解析 " + key + " = " + raw);
                    continue;
                }
                string purpose;
                if (!Purposes.TryGetValue(key, out purpose)) purpose = key;
                list.Add(Make(Software, "全局热键", purpose, combo, raw, file, false));
            }

            Note("解析出 " + list.Count + " 条热键");
            return list;
        }

        /// <summary>Everything 的键值是 "修饰符:VK" 十进制，如 "2:65" 表示 Ctrl+A。</summary>
        internal static KeyCombo ParseEverythingValue(string raw)
        {
            string modPart = null;
            string vkPart = raw;
            int colon = raw.LastIndexOf(':');
            if (colon >= 0)
            {
                modPart = raw.Substring(0, colon);
                vkPart = raw.Substring(colon + 1);
            }

            uint vk;
            if (!KeyCombo.TryParseVk(vkPart, out vk)) return null;

            // 兜底：Everything 也可能写 "Ctrl+Alt+X"
            var viaParse = KeyCombo.Parse(raw);
            if (viaParse != null) return viaParse;

            uint mods = 0;
            if (!string.IsNullOrEmpty(modPart))
            {
                if (!KeyCombo.TryParseVk(modPart, out mods)) return null;
            }
            return new KeyCombo((mods & 0x08) != 0, (mods & 0x02) != 0, (mods & 0x01) != 0, (mods & 0x04) != 0, vk);
        }
    }

    internal static class IniRead
    {
        /// <summary>读取无 [section] 的 key=value（Everything.ini 的全局设置都在文件头）。</summary>
        public static Dictionary<string, string> ReadSectionlessKeys(string path)
        {
            var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (string line in AdapterUtil.ReadLines(path))
            {
                string s = line.Trim();
                if (s.Length == 0 || s[0] == ';' || s[0] == '#') continue;
                if (s[0] == '[') break;
                int eq = s.IndexOf('=');
                if (eq <= 0) continue;
                map[s.Substring(0, eq).Trim()] = s.Substring(eq + 1);
            }
            return map;
        }

        public static string Get(Dictionary<string, string> map, string key)
        {
            string v;
            return map.TryGetValue(key, out v) ? v : null;
        }

        /// <summary>读取指定 [section] 下的全部 key=value。找不到 section 返回空表。</summary>
        public static Dictionary<string, string> ReadSection(string path, string section)
        {
            var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            bool inSection = false;
            foreach (string line in AdapterUtil.ReadLines(path))
            {
                string s = line.Trim();
                if (s.Length == 0) continue;
                if (s[0] == ';' || s[0] == '#') continue;
                if (s[0] == '[')
                {
                    int end = s.IndexOf(']');
                    string name = end > 1 ? s.Substring(1, end - 1) : s.Substring(1);
                    inSection = string.Equals(name.Trim(), section, StringComparison.OrdinalIgnoreCase);
                    continue;
                }
                if (!inSection) continue;
                int eq = s.IndexOf('=');
                if (eq <= 0) continue;
                map[s.Substring(0, eq).Trim()] = s.Substring(eq + 1);
            }
            return map;
        }
    }
}
