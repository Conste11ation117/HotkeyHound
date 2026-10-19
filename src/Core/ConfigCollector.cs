using System;
using System.Collections.Generic;
using HotkeyHound.Core.Adapters;

namespace HotkeyHound.Core
{
    public sealed class DeclaredHotkey
    {
        public string Software;
        public string Module;
        public string Purpose;
        public KeyCombo Combo;
        public string RawValue;
        public string SourcePath;
        public bool IsDefaultValue;
    }

    public sealed class ConfigCollection
    {
        public List<DeclaredHotkey> Declared = new List<DeclaredHotkey>();
        public List<DeclaredHotkey> Reserved = new List<DeclaredHotkey>();
        public List<string> Log = new List<string>();
        public List<string> UnparsedComboValues = new List<string>();
    }

    /// <summary>引擎 D 调度：跑完所有适配器，任何单个适配器出错都不影响其它适配器。</summary>
    public sealed class ConfigCollector
    {
        private readonly Action<string> _log;

        public ConfigCollector(Action<string> log)
        {
            _log = log;
        }

        private void Say(string message)
        {
            if (_log != null) _log(message);
        }

        public ConfigCollection Collect()
        {
            var result = new ConfigCollection();
            var adapters = new AdapterBase[]
            {
                new PowerToysModulesAdapter(),
                new PowerToysKeyboardManagerAdapter(),
                new EverythingAdapter(),
                new ObsStudioAdapter(),
                new NvidiaShadowPlayAdapter(),
                new WallpaperEngineAdapter(),
                new MsiAfterburnerAdapter()
            };

            foreach (AdapterBase adapter in adapters)
            {
                List<DeclaredHotkey> items;
                try
                {
                    items = new List<DeclaredHotkey>(adapter.Collect());
                }
                catch (Exception ex)
                {
                    string crashed = L10n.F("cfg.adapterCrashed", adapter.Software, ex.Message);
                    result.Log.Add(crashed);
                    Say(crashed);
                    continue;
                }

                int before = result.Declared.Count;
                foreach (DeclaredHotkey d in items)
                {
                    if (d == null || d.Combo == null) continue;
                    result.Declared.Add(d);
                }
                foreach (string line in adapter.Log)
                {
                    result.Log.Add(line);
                    Say(line);
                }
                Say(L10n.F("cfg.adapterContributed", adapter.Software, result.Declared.Count - before));
            }

            var reservedAdapter = new SystemReservedTable();
            try
            {
                result.Reserved.AddRange(SystemReservedTable.Reserved());
            }
            catch (Exception ex)
            {
                result.Log.Add(L10n.F("cfg.reservedFailed", ex.Message));
            }
            Say(L10n.F("cfg.done", result.Declared.Count, result.Reserved.Count));
            return result;
        }
    }

    /// <summary>静态候选进程表：用于「已占用但无声明」时提示可能是谁。</summary>
    public static class CandidateProcesses
    {
        public static readonly string[] Names = new string[]
        {
            "QQ", "QQNT", "Weixin", "WeChat", "WeChatAppEx", "WeChatOCR",
            "wetype_core", "wetype_renderer", "wetype_update",
            "Doubao", "cloudmusic", "mailmaster", "ToDesk", "ldplayer",
            "uu", "UUGameAssistant", "xunfei", "zhipu", "iFlyInput",
            "SogouInput", "SGTool", "QQPinyinTSF", "BaiduPinyin",
            "steamwebhelper", "Discord", "Telegram", "Slack", "Teams",
            "nvcontainer", "NVIDIA Overlay", "NVIDIA Share", "nvsphelper64",
            "PowerToys.KeyboardManagerEngine", "PowerToys.PowerLauncher",
            "PowerToys.Peek.UI", "PowerToys.PowerToys", "PowerToys.FancyZones",
            "obs64", "Everything", "MSIAfterburner", "wallpaper64", "wallpaper32",
            "ToDesk_Session", "SunloginClient", "AnyDesk", "TeamViewer",
            "Snipaste", "PicPick", "Ditto", "AutoHotkey", "AutoHotkey64",
            "TranslucentTB", "StartAllBack", "ExplorerPatcher", "TrafficMonitor",
            "LenovoUtility", "DellQuickSet", "HPHotkey", "RealtekAudioConsole"
        };

        /// <summary>返回当前正在运行的候选项（进程名，不含路径）。</summary>
        public static List<string> Running()
        {
            var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            try
            {
                foreach (System.Diagnostics.Process p in System.Diagnostics.Process.GetProcesses())
                {
                    try { names.Add(p.ProcessName); }
                    catch (Exception) { }
                    finally { try { p.Dispose(); } catch (Exception) { } }
                }
            }
            catch (Exception) { }

            var hit = new List<string>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (string candidate in Names)
            {
                // 候选名可能是带前缀的内部名（如 PowerToys.KeyboardManagerEngine），取最后一段做宽松匹配
                foreach (string actual in names)
                {
                    if (seen.Contains(actual)) continue;
                    bool match = string.Equals(actual, candidate, StringComparison.OrdinalIgnoreCase)
                              || actual.StartsWith(candidate, StringComparison.OrdinalIgnoreCase)
                              || (candidate.IndexOf('.') > 0 &&
                                  actual.StartsWith(candidate.Substring(candidate.LastIndexOf('.') + 1), StringComparison.OrdinalIgnoreCase));
                    if (match)
                    {
                        hit.Add(actual);
                        seen.Add(actual);
                    }
                }
            }
            hit.Sort(StringComparer.OrdinalIgnoreCase);
            return hit;
        }
    }
}
