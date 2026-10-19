using System;
using System.Collections.Generic;
using System.IO;

namespace HotkeyHound.Core.Adapters
{
    /// <summary>PowerToys 键盘管理器的 remapShortcuts.global[]。本机最高优先级的冲突源。</summary>
    public sealed class PowerToysKeyboardManagerAdapter : AdapterBase
    {
        public override string Software { get { return "PowerToys"; } }

        public override IEnumerable<DeclaredHotkey> Collect()
        {
            return Safe(this, CollectCore);
        }

        public string ConfigPath
        {
            get
            {
                return Path.Combine(AdapterUtil.LocalAppData, "Microsoft", "PowerToys",
                                    "Keyboard Manager", "default.json");
            }
        }

        private List<DeclaredHotkey> CollectCore()
        {
            string file = ConfigPath;
            if (!File.Exists(file))
            {
                Note(L10n.F("adapter.ptkey.configNotFound", file));
                return new List<DeclaredHotkey>();
            }

            var json = JsonLite.TryParseFile(file);
            if (json == null)
            {
                Warn(L10n.F("adapter.ptkey.parseFailed", file));
                return new List<DeclaredHotkey>();
            }

            List<DeclaredHotkey> list = ParseDocument(json, file);
            Note(L10n.F("adapter.ptkey.parsed", list.Count));
            return list;
        }

        /// <summary>
        /// 把已解析的 JSON 文档转成声明列表。抽出来是为了让自测能用合成样本验证解析器本身，
        /// 而不是依赖「本机恰好装了 PowerToys」。
        /// </summary>
        internal static List<DeclaredHotkey> ParseDocument(JsonValue json, string file)
        {
            var list = new List<DeclaredHotkey>();
            if (json == null) return list;

            JsonValue shortcuts;
            if (!json.TryGet("remapShortcuts", out shortcuts) || !shortcuts.IsObject) return list;

            JsonValue global;
            if (!shortcuts.TryGet("global", out global) || !global.IsArray) return list;

            foreach (var item in global.Elements)
            {
                if (item == null || !item.IsObject) continue;
                string original = item["originalKeys"] == null ? null : item["originalKeys"].AsString(null);
                if (string.IsNullOrEmpty(original)) continue;

                var combo = KeyCombo.FromVkList(original);
                if (combo == null) continue;

                int op = item["operationType"] == null ? 0 : item["operationType"].AsInt(0);
                string purpose;
                if (op == 1)
                {
                    string exe = item["runProgramFilePath"] == null ? "" : item["runProgramFilePath"].AsString("");
                    purpose = L10n.F("adapter.ptkey.run", AdapterUtil.ExeName(AdapterUtil.Expand(exe)));
                }
                else
                {
                    string newKeys = item["newRemapKeys"] == null ? null : item["newRemapKeys"].AsString(null);
                    var target = KeyCombo.FromVkList(newKeys);
                    purpose = L10n.F("adapter.ptkey.remap",
                                     target != null ? target.Display()
                                                    : (newKeys == null ? L10n.T("adapter.ptkey.unknownTarget") : newKeys));
                }

                list.Add(Make("PowerToys", "Keyboard Manager", purpose, combo, original, file, false));
            }
            return list;
        }
    }
}
