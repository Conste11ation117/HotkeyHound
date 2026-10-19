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
            var list = new List<DeclaredHotkey>();
            string file = ConfigPath;
            if (!File.Exists(file))
            {
                Note(L10n.F("adapter.ptkey.configNotFound", file));
                return list;
            }

            var json = JsonLite.TryParseFile(file);
            if (json == null)
            {
                Warn(L10n.F("adapter.ptkey.parseFailed", file));
                return list;
            }

            JsonValue shortcuts;
            if (!json.TryGet("remapShortcuts", out shortcuts) || !shortcuts.IsObject)
            {
                Note(L10n.T("adapter.ptkey.noRemapSection"));
                return list;
            }

            JsonValue global;
            if (!shortcuts.TryGet("global", out global) || !global.IsArray)
            {
                Note(L10n.T("adapter.ptkey.noGlobalArray"));
                return list;
            }

            foreach (var item in global.Elements)
            {
                if (item == null || !item.IsObject) continue;
                string original = item["originalKeys"] == null ? null : item["originalKeys"].AsString(null);
                if (string.IsNullOrEmpty(original)) continue;

                var combo = KeyCombo.FromVkList(original);
                if (combo == null)
                {
                    Warn(L10n.F("adapter.ptkey.badOriginal", original));
                    continue;
                }

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

                list.Add(Make(Software, "Keyboard Manager", purpose, combo, original, file, false));
            }

            Note(L10n.F("adapter.ptkey.parsed", list.Count));
            return list;
        }
    }
}
