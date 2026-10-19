using System;
using System.Collections.Generic;
using System.IO;
using Microsoft.Win32;

namespace HotkeyHound.Core.Adapters
{
    /// <summary>Wallpaper Engine 的热键（config.json + HKCU\Software\WallpaperEngine）。找不到就返回 0 条，不报错。</summary>
    public sealed class WallpaperEngineAdapter : AdapterBase
    {
        public override string Software { get { return "Wallpaper Engine"; } }

        public override IEnumerable<DeclaredHotkey> Collect()
        {
            return Safe(this, CollectCore);
        }

        private List<DeclaredHotkey> CollectCore()
        {
            var list = new List<DeclaredHotkey>();
            string configPath = FindConfig();
            if (configPath != null)
            {
                var json = JsonLite.TryParseFile(configPath);
                if (json == null) Warn(L10n.F("adapter.wallpaper.parseFailed", configPath));
                else Walk(json, L10n.T("adapter.common.config"), configPath, list);
            }
            else
            {
                Note(L10n.T("adapter.wallpaper.notFound"));
            }

            try
            {
                using (RegistryKey key = Registry.CurrentUser.OpenSubKey(@"Software\WallpaperEngine", false))
                {
                    if (key != null)
                    {
                        foreach (string name in key.GetValueNames())
                        {
                            if (name.IndexOf("hotkey", StringComparison.OrdinalIgnoreCase) < 0) continue;
                            string s = key.GetValue(name) as string;
                            if (string.IsNullOrEmpty(s)) continue;
                            var combo = KeyCombo.Parse(s);
                            if (combo == null) continue;
                            list.Add(Make(Software, L10n.T("adapter.common.registry"),
                                          L10n.F("adapter.wallpaper.hotkey", name), combo, s,
                                          "HKCU\\Software\\WallpaperEngine\\" + name, false));
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Warn(L10n.F("adapter.wallpaper.registryFailed", ex.Message));
            }

            Note(L10n.F("adapter.wallpaper.parsed", list.Count));
            return list;
        }

        private string FindConfig()
        {
            var candidates = new List<string>();
            candidates.Add(Path.Combine(AdapterUtil.RoamingAppData, "WallpaperEngine", "config.json"));
            candidates.Add(Path.Combine(AdapterUtil.LocalAppData, "WallpaperEngine", "config.json"));

            try
            {
                using (RegistryKey key = Registry.CurrentUser.OpenSubKey(@"Software\WallpaperEngine", false))
                {
                    if (key != null)
                    {
                        string install = key.GetValue("installPath") as string;
                        if (!string.IsNullOrEmpty(install))
                        {
                            try
                            {
                                candidates.Add(Path.Combine(Path.GetDirectoryName(install), "config.json"));
                            }
                            catch (Exception) { }
                        }
                    }
                }
            }
            catch (Exception) { }

            foreach (string drive in new string[] { "D:", "C:", "E:", "F:" })
            {
                candidates.Add(drive + "\\steam\\steamapps\\common\\wallpaper_engine\\config.json");
            }

            foreach (string c in candidates)
            {
                try { if (File.Exists(c)) return c; }
                catch (Exception) { }
            }
            return null;
        }

        private void Walk(JsonValue node, string module, string source, List<DeclaredHotkey> outList)
        {
            if (node == null) return;
            if (node.IsArray)
            {
                foreach (var item in node.Elements) Walk(item, module, source, outList);
                return;
            }
            if (!node.IsObject) return;

            foreach (var kv in node.Pairs)
            {
                if (kv.Value == null) continue;
                if (kv.Key.IndexOf("hotkey", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    var combo = ComboFromJson(kv.Value);
                    if (combo != null)
                    {
                        outList.Add(Make(Software, module, L10n.F("adapter.wallpaper.hotkey", kv.Key), combo,
                                         RawOf(kv.Value), source, false));
                    }
                }
                Walk(kv.Value, module, source, outList);
            }
        }

        private static KeyCombo ComboFromJson(JsonValue v)
        {
            if (v.IsString)
            {
                var c = KeyCombo.Parse(v.Str);
                if (c != null) return c;
                var c2 = KeyCombo.FromVkList(v.Str);
                if (c2 != null && v.Str.IndexOf(';') >= 0) return c2;
                return null;
            }
            if (v.IsNumber)
            {
                int n = (int)Math.Round(v.Num);
                if (n <= 0) return null;
                return new KeyCombo(false, false, false, false, (uint)n);
            }
            return null;
        }

        private static string RawOf(JsonValue v)
        {
            if (v.IsString) return v.Str;
            if (v.IsNumber) return v.Num.ToString();
            return v.Kind.ToString();
        }
    }
}
