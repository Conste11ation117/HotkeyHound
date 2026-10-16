using System;
using System.Collections.Generic;
using System.IO;

namespace HotkeyHound.Core.Adapters
{
    /// <summary>
    /// 递归遍历 %LOCALAPPDATA%\Microsoft\PowerToys\*\settings.json，
    /// 凡出现 {win,ctrl,alt,shift,code} 形状的对象即视为一条声明；
    /// 用途取 JSON 键名，模块启用状态取根 settings.json 的 enabled 字段。
    /// </summary>
    public sealed class PowerToysModulesAdapter : AdapterBase
    {
        public override string Software { get { return "PowerToys"; } }

        private Dictionary<string, bool> _enabled;
        private readonly HashSet<string> _seen = new HashSet<string>(StringComparer.Ordinal);

        public override IEnumerable<DeclaredHotkey> Collect()
        {
            return Safe(this, CollectCore);
        }

        private List<DeclaredHotkey> CollectCore()
        {
            var list = new List<DeclaredHotkey>();
            string root = Path.Combine(AdapterUtil.LocalAppData, "Microsoft", "PowerToys");
            if (!Directory.Exists(root))
            {
                Note("未安装或未找到配置目录：" + root);
                return list;
            }

            string rootSettings = Path.Combine(root, "settings.json");
            _enabled = LoadEnabled(rootSettings);

            if (File.Exists(rootSettings))
            {
                Walk(JsonLite.TryParseFile(rootSettings), "PowerToys 全局", rootSettings, "settings", list);
            }

            string[] dirs = Directory.GetDirectories(root);
            Array.Sort(dirs, StringComparer.OrdinalIgnoreCase);
            foreach (string dir in dirs)
            {
                string file = Path.Combine(dir, "settings.json");
                if (!File.Exists(file)) continue;
                var json = JsonLite.TryParseFile(file);
                if (json == null)
                {
                    Warn("配置解析失败（已跳过）：" + file);
                    continue;
                }
                Walk(json, Path.GetFileName(dir), file, "settings", list);
            }

            Note("共解析出 " + list.Count + " 条热键声明");
            return list;
        }

        private Dictionary<string, bool> LoadEnabled(string path)
        {
            var map = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
            var json = JsonLite.TryParseFile(path);
            if (json == null) return map;
            JsonValue en;
            if (!json.TryGet("enabled", out en) || !en.IsObject) return map;
            foreach (var kv in en.Pairs) map[kv.Key] = kv.Value.AsBool(false);
            return map;
        }

        private void Walk(JsonValue node, string module, string source, string keyName, List<DeclaredHotkey> outList)
        {
            if (node == null) return;
            if (node.IsArray)
            {
                foreach (var item in node.Elements) Walk(item, module, source, keyName, outList);
                return;
            }
            if (!node.IsObject) return;

            if (LooksLikeHotkey(node))
            {
                AddHotkey(node, module, source, keyName, outList);
                return;
            }

            foreach (var kv in node.Pairs)
            {
                Walk(kv.Value, module, source, kv.Key, outList);
            }
        }

        private static bool LooksLikeHotkey(JsonValue o)
        {
            if (!o.IsObject) return false;
            JsonValue ignored;
            if (!o.TryGet("code", out ignored)) return false;
            foreach (string field in new string[] { "win", "ctrl", "alt", "shift" })
            {
                JsonValue v;
                if (!o.TryGet(field, out v) || !v.IsBool) return false;
            }
            return true;
        }

        private void AddHotkey(JsonValue o, string module, string source, string keyName, List<DeclaredHotkey> outList)
        {
            int code = o["code"].AsInt(0);
            if (code <= 0) return;

            var combo = new KeyCombo(
                o["win"].AsBool(false),
                o["ctrl"].AsBool(false),
                o["alt"].AsBool(false),
                o["shift"].AsBool(false),
                (uint)code);

            // 同一模块里 DefaultActivationShortcut 与 ActivationShortcut 常常是同一个键，去重。
            string seenKey = module + "|" + combo.Normalize();
            if (!_seen.Add(seenKey)) return;

            string purpose = string.IsNullOrEmpty(keyName) ? "热键" : keyName;
            bool moduleEnabled = true;
            if (module != null)
            {
                bool on;
                if (_enabled.TryGetValue(module, out on)) moduleEnabled = on;
            }
            if (!moduleEnabled) purpose = "[已禁用] " + purpose;

            outList.Add(Make(Software, module, purpose, combo, keyName == null ? code.ToString() : keyName,
                             source, false));
        }
    }
}
