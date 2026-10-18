using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Text;
using Microsoft.Win32;
using HotkeyHound.Core.Adapters;

namespace HotkeyHound.Core
{
    /// <summary>证据等级，定义见 docs/hook-catalog.md。宁可直接降级，也不要写成事实。</summary>
    public enum HookEvidence
    {
        Confirmed,
        HighlySuspected,
        KnownDefault,
        ReferenceOnly
    }

    public sealed class CatalogHotkey
    {
        public KeyCombo Combo;
        public string RawKeys;
        public string PurposeZh;
        public string PurposeEn;
        /// <summary>config（本机读到）、default（官方默认）、docs（文档记载）。</summary>
        public string Source;

        public string Purpose
        {
            get { return L10n.IsEnglish && !string.IsNullOrEmpty(PurposeEn) ? PurposeEn : PurposeZh; }
        }

        public bool FromConfig { get { return string.Equals(Source, "config", StringComparison.OrdinalIgnoreCase); } }
    }

    public sealed class CatalogEntry
    {
        public string Id;
        public string SoftwareZh;
        public string SoftwareEn;
        public string ModuleZh;
        public string ModuleEn;
        public string NotesZh;
        public string NotesEn;
        public string DocsUrl;
        /// <summary>hook / registered / mixed。</summary>
        public string Kind = "hook";

        public List<string> ProcessNames = new List<string>();
        public List<string> ConfigPaths = new List<string>();
        public List<string> RegistryKeys = new List<string>();
        public List<CatalogHotkey> KnownHotkeys = new List<CatalogHotkey>();
        /// <summary>JSON 里人工标注的配置可读性（read / unreadable / missing），覆盖自动探测。</summary>
        public string ConfigStateHint;

        // ---- 运行时检测结果 ----
        public bool ProcessRunning;
        public List<string> RunningProcesses = new List<string>();
        /// <summary>read / unreadable / missing / none。</summary>
        public string ConfigState = "none";
        public string ConfigPathResolved;
        public List<string> ExistingPaths = new List<string>();
        public bool ModuleDisabled;
        public bool EnrichedFromEngineD;

        public string Software
        {
            get { return L10n.IsEnglish && !string.IsNullOrEmpty(SoftwareEn) ? SoftwareEn : SoftwareZh; }
        }

        public string Module
        {
            get { return L10n.IsEnglish && !string.IsNullOrEmpty(ModuleEn) ? ModuleEn : ModuleZh; }
        }

        public string Notes
        {
            get { return L10n.IsEnglish && !string.IsNullOrEmpty(NotesEn) ? NotesEn : NotesZh; }
        }

        public bool IsHook
        {
            get
            {
                return string.Equals(Kind, "hook", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(Kind, "mixed", StringComparison.OrdinalIgnoreCase);
            }
        }

        public string FirstConfigDisplay
        {
            get { return ConfigPaths.Count == 0 ? null : ConfigPaths[0]; }
        }
    }

    public sealed class HookSuspect
    {
        public KeyCombo Combo;
        public string EntryId;
        public string Software;
        public string Module;
        public string Purpose;
        public string Note;
        public string SourcePath;
        public string SourceLabel;
        public HookEvidence Evidence;
        public bool ProcessRunning;
        public string ConfigState;
        /// <summary>null = 该组合不在引擎 A 的探测网格内。</summary>
        public bool? Occupied;
    }

    /// <summary>
    /// 钩子型热键知识库：内嵌 data/hook-catalog.json + 可选的用户覆盖
    /// （%LOCALAPPDATA%\HotkeyHound\catalog.json）。只读、不联网、不写任何配置。
    /// </summary>
    public sealed class HookCatalog
    {
        public const string EmbeddedResource = "hook-catalog.json";

        public List<CatalogEntry> Entries = new List<CatalogEntry>();
        public int EmbeddedCount;
        public int UserCount;
        public List<string> Log = new List<string>();
        public string UserPath;

        public static string UserCatalogPath()
        {
            try
            {
                string la = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
                if (!string.IsNullOrEmpty(la)) return Path.Combine(la, "HotkeyHound", "catalog.json");
            }
            catch (Exception) { }
            return null;
        }

        public static HookCatalog Load()
        {
            var catalog = new HookCatalog();
            catalog.UserPath = UserCatalogPath();

            int embedded = 0;
            JsonValue root = ReadEmbedded(out embedded);
            if (root == null)
            {
                catalog.Log.Add(L10n.T("cat.embeddedMissing"));
            }
            else
            {
                ParseEntries(root, catalog, false, out embedded);
                catalog.EmbeddedCount = embedded;
            }

            if (!string.IsNullOrEmpty(catalog.UserPath) && File.Exists(catalog.UserPath))
            {
                JsonValue user = JsonLite.TryParseFile(catalog.UserPath);
                if (user == null)
                {
                    catalog.Log.Add(L10n.F("cat.userFailed", catalog.UserPath));
                }
                else
                {
                    int merged;
                    ParseEntries(user, catalog, true, out merged);
                    catalog.UserCount = merged;
                }
            }

            catalog.ExpandAndProbe();
            catalog.EnrichFromEngineD();
            catalog.Log.Add(L10n.F("cat.loaded", catalog.Entries.Count, catalog.EmbeddedCount, catalog.UserCount));
            return catalog;
        }

        private static JsonValue ReadEmbedded(out int count)
        {
            count = 0;
            try
            {
                Assembly asm = Assembly.GetExecutingAssembly();
                using (Stream s = asm.GetManifestResourceStream(EmbeddedResource))
                {
                    if (s == null) return null;
                    using (var reader = new StreamReader(s, Encoding.UTF8))
                    {
                        return JsonLite.TryParse(reader.ReadToEnd());
                    }
                }
            }
            catch (Exception) { return null; }
        }

        /// <summary>
        /// 同一个 id 覆盖，新的 id 追加。用户文件里的条目即使 id 相同也可以只改一部分字段吗？
        /// 不允许——整条覆盖，语义更清楚，也不会出现「改了一半」的配置。
        /// </summary>
        private static void ParseEntries(JsonValue root, HookCatalog catalog, bool user, out int count)
        {
            count = 0;
            JsonValue entries = root;
            if (root != null && root.IsObject)
            {
                JsonValue list;
                if (!root.TryGet("entries", out list)) return;
                entries = list;
            }
            if (entries == null || !entries.IsArray) return;

            foreach (JsonValue item in entries.Elements)
            {
                if (item == null || !item.IsObject) continue;
                var entry = ParseEntry(item);
                if (entry == null || string.IsNullOrEmpty(entry.Id)) continue;

                int existing = -1;
                for (int i = 0; i < catalog.Entries.Count; i++)
                {
                    if (string.Equals(catalog.Entries[i].Id, entry.Id, StringComparison.OrdinalIgnoreCase))
                    {
                        existing = i;
                        break;
                    }
                }
                if (existing >= 0) catalog.Entries[existing] = entry;
                else catalog.Entries.Add(entry);
                count++;
            }
        }

        private static CatalogEntry ParseEntry(JsonValue o)
        {
            var entry = new CatalogEntry();
            entry.Id = Str(o, "id");
            entry.SoftwareZh = Str(o, "software");
            entry.SoftwareEn = Str(o, "softwareEn");
            entry.ModuleZh = Str(o, "module");
            entry.ModuleEn = Str(o, "moduleEn");
            entry.NotesZh = Str(o, "notes");
            entry.NotesEn = Str(o, "notesEn");
            entry.DocsUrl = Str(o, "docs");
            entry.ConfigStateHint = Str(o, "configState");
            string kind = Str(o, "kind");
            if (!string.IsNullOrEmpty(kind)) entry.Kind = kind;

            ReadStrings(o["processNames"], entry.ProcessNames);
            ReadStrings(o["configPaths"], entry.ConfigPaths);
            ReadStrings(o["registryKeys"], entry.RegistryKeys);

            JsonValue hotkeys = o["knownHotkeys"];
            if (hotkeys != null && hotkeys.IsArray)
            {
                foreach (JsonValue hk in hotkeys.Elements)
                {
                    if (hk == null || !hk.IsObject) continue;
                    string keys = Str(hk, "keys");
                    KeyCombo combo = KeyCombo.Parse(keys);
                    if (combo == null) continue;
                    var item = new CatalogHotkey();
                    item.Combo = combo;
                    item.RawKeys = keys;
                    item.PurposeZh = Str(hk, "purpose");
                    item.PurposeEn = Str(hk, "purposeEn");
                    item.Source = Str(hk, "source");
                    if (string.IsNullOrEmpty(item.Source)) item.Source = "docs";
                    entry.KnownHotkeys.Add(item);
                }
            }
            return entry;
        }

        private static string Str(JsonValue o, string key)
        {
            JsonValue v;
            if (o != null && o.TryGet(key, out v) && v != null && v.IsString) return v.Str;
            return null;
        }

        private static void ReadStrings(JsonValue array, List<string> target)
        {
            if (array == null || !array.IsArray) return;
            foreach (JsonValue v in array.Elements)
            {
                if (v != null && v.IsString && !string.IsNullOrEmpty(v.Str)) target.Add(v.Str);
            }
        }

        // ------------------------------------------------------------ 环境变量 / 配置可读性 / 进程

        private void ExpandAndProbe()
        {
            var running = RunningProcessNames();
            foreach (CatalogEntry entry in Entries)
            {
                if (entry.ProcessNames.Count > 0)
                {
                    foreach (string raw in entry.ProcessNames)
                    {
                        string name = ProcessNameOnly(raw);
                        if (name.Length == 0) continue;
                        if (running.Contains(name))
                        {
                            entry.ProcessRunning = true;
                            if (!entry.RunningProcesses.Contains(name)) entry.RunningProcesses.Add(name);
                        }
                    }
                }
                ProbeConfig(entry);
                if (!string.IsNullOrEmpty(entry.ConfigStateHint)
                    && !string.Equals(entry.ConfigStateHint, "auto", StringComparison.OrdinalIgnoreCase))
                {
                    entry.ConfigState = entry.ConfigStateHint;
                }
            }
        }

        private static HashSet<string> RunningProcessNames()
        {
            var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            try
            {
                foreach (Process p in Process.GetProcesses())
                {
                    try { set.Add(p.ProcessName); }
                    catch (Exception) { }
                    finally { try { p.Dispose(); } catch (Exception) { } }
                }
            }
            catch (Exception) { }
            return set;
        }

        private static string ProcessNameOnly(string raw)
        {
            if (string.IsNullOrEmpty(raw)) return "";
            string name = raw.Trim();
            try
            {
                string file = Path.GetFileName(name);
                if (!string.IsNullOrEmpty(file)) name = file;
            }
            catch (Exception) { }
            if (name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) name = name.Substring(0, name.Length - 4);
            return name;
        }

        private void ProbeConfig(CatalogEntry entry)
        {
            bool any = false;
            bool readable = false;
            bool unreadable = false;

            foreach (string raw in entry.ConfigPaths)
            {
                string path = Expand(raw);
                if (string.IsNullOrEmpty(path)) continue;
                any = true;
                string state = ProbePath(path);
                if (state == "missing") continue;
                if (entry.ConfigPathResolved == null) entry.ConfigPathResolved = path;
                if (!entry.ExistingPaths.Contains(path)) entry.ExistingPaths.Add(path);
                if (state == "read") readable = true;
                else unreadable = true;
            }
            foreach (string raw in entry.RegistryKeys)
            {
                string key = Expand(raw);
                if (string.IsNullOrEmpty(key)) continue;
                any = true;
                string state = ProbeRegistry(key);
                if (state == "missing") continue;
                if (entry.ConfigPathResolved == null) entry.ConfigPathResolved = key;
                if (!entry.ExistingPaths.Contains(key)) entry.ExistingPaths.Add(key);
                if (state == "read") readable = true;
                else unreadable = true;
            }

            if (!any) entry.ConfigState = "none";
            else if (readable) entry.ConfigState = "read";
            else if (unreadable) entry.ConfigState = "unreadable";
            else entry.ConfigState = "missing";
        }

        /// <summary>返回 read / unreadable / missing。</summary>
        private static string ProbePath(string path)
        {
            try
            {
                if (Directory.Exists(path))
                {
                    try { Directory.GetFileSystemEntries(path); return "read"; }
                    catch (Exception) { return "unreadable"; }
                }
                if (!File.Exists(path)) return "missing";
                try
                {
                    using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                    {
                        if (fs.Length > 0)
                        {
                            var buf = new byte[1];
                            fs.Read(buf, 0, 1);
                        }
                    }
                    return "read";
                }
                catch (Exception) { return "unreadable"; }
            }
            catch (Exception) { return "missing"; }
        }

        private static string ProbeRegistry(string key)
        {
            try
            {
                string hive;
                string rest;
                SplitRegistry(key, out hive, out rest);
                if (hive == null) return "missing";
                RegistryKey root = Registry.CurrentUser;
                if (hive == "HKLM" || hive == "HKEY_LOCAL_MACHINE") root = Registry.LocalMachine;
                else if (hive == "HKCR") root = Registry.ClassesRoot;
                else if (hive == "HKU") root = Registry.Users;
                using (RegistryKey sub = root.OpenSubKey(rest))
                {
                    return sub == null ? "missing" : "read";
                }
            }
            catch (Exception) { return "unreadable"; }
        }

        private static void SplitRegistry(string key, out string hive, out string rest)
        {
            hive = null;
            rest = null;
            if (string.IsNullOrEmpty(key)) return;
            int i = key.IndexOf('\\');
            if (i <= 0) return;
            hive = key.Substring(0, i).ToUpperInvariant();
            rest = key.Substring(i + 1);
        }

        private static string Expand(string value)
        {
            if (string.IsNullOrEmpty(value)) return null;
            try { return Environment.ExpandEnvironmentVariables(value); }
            catch (Exception) { return value; }
        }

        /// <summary>
        /// 能用引擎 D 解析的配置就不要写第二份解析器：PowerToys 键盘管理器与 Peek 直接复用
        /// 现有适配器的结果，JSON 里的 knownHotkeys 只是给读不到配置的软件兜底。
        /// </summary>
        private void EnrichFromEngineD()
        {
            foreach (CatalogEntry entry in Entries)
            {
                try
                {
                    if (string.Equals(entry.Id, "powertoys.keyboard-manager", StringComparison.OrdinalIgnoreCase))
                    {
                        var adapter = new PowerToysKeyboardManagerAdapter();
                        var items = new List<DeclaredHotkey>(adapter.Collect());
                        string path = adapter.ConfigPath;
                        if (File.Exists(path))
                        {
                            entry.ConfigState = "read";
                            entry.ConfigPathResolved = path;
                            if (!entry.ExistingPaths.Contains(path)) entry.ExistingPaths.Add(path);
                        }
                        if (items.Count > 0)
                        {
                            entry.KnownHotkeys.Clear();
                            foreach (DeclaredHotkey d in items)
                            {
                                entry.KnownHotkeys.Add(new CatalogHotkey
                                {
                                    Combo = d.Combo,
                                    RawKeys = d.RawValue,
                                    PurposeZh = d.Purpose,
                                    Source = "config"
                                });
                            }
                            entry.EnrichedFromEngineD = true;
                        }
                    }
                    else if (string.Equals(entry.Id, "powertoys.peek", StringComparison.OrdinalIgnoreCase))
                    {
                        var adapter = new PowerToysModulesAdapter();
                        var items = new List<DeclaredHotkey>(adapter.Collect());
                        var fromConfig = new List<CatalogHotkey>();
                        bool disabled = false;
                        foreach (DeclaredHotkey d in items)
                        {
                            if (d == null || d.Combo == null) continue;
                            if (!string.Equals(d.Module, "Peek", StringComparison.OrdinalIgnoreCase)) continue;
                            if (d.Purpose != null && d.Purpose.StartsWith("[已禁用]", StringComparison.Ordinal))
                            {
                                disabled = true;
                                continue;
                            }
                            fromConfig.Add(new CatalogHotkey
                            {
                                Combo = d.Combo,
                                RawKeys = d.RawValue,
                                PurposeZh = d.Purpose,
                                Source = "config"
                            });
                        }
                        if (fromConfig.Count > 0)
                        {
                            entry.KnownHotkeys.Clear();
                            entry.KnownHotkeys.AddRange(fromConfig);
                            entry.EnrichedFromEngineD = true;
                            entry.ModuleDisabled = disabled;
                        }
                    }
                }
                catch (Exception ex)
                {
                    Log.Add(L10n.F("cat.enrichFailed", entry.Id, ex.Message));
                }
            }
        }

        // ------------------------------------------------------------ 与扫描结果关联

        public List<HookSuspect> BuildSuspects(List<OccupancyResult> occupancy)
        {
            var occ = new Dictionary<string, bool>(StringComparer.Ordinal);
            if (occupancy != null)
            {
                foreach (OccupancyResult r in occupancy)
                {
                    if (r == null || r.Combo == null) continue;
                    occ[r.Combo.Normalize()] = r.Occupied;
                }
            }

            var list = new List<HookSuspect>();
            foreach (CatalogEntry entry in Entries)
            {
                if (!entry.IsHook) continue;

                if (entry.KnownHotkeys.Count == 0)
                {
                    HookSuspect reference = MakeSuspect(entry, null, null);
                    reference.Evidence = HookEvidence.ReferenceOnly;
                    list.Add(reference);
                    continue;
                }

                foreach (CatalogHotkey hk in entry.KnownHotkeys)
                {
                    if (hk.Combo == null) continue;
                    bool occupied;
                    if (!occ.TryGetValue(hk.Combo.Normalize(), out occupied)) continue;   // 不在探测网格内
                    list.Add(MakeSuspect(entry, hk, (bool?)occupied));
                }
            }

            list.Sort(delegate (HookSuspect a, HookSuspect b)
            {
                int ea = (int)a.Evidence, eb = (int)b.Evidence;
                if (ea != eb) return ea.CompareTo(eb);
                int s = string.Compare(a.Software, b.Software, StringComparison.OrdinalIgnoreCase);
                if (s != 0) return s;
                string ca = a.Combo == null ? "" : a.Combo.Display();
                string cb = b.Combo == null ? "" : b.Combo.Display();
                return string.Compare(ca, cb, StringComparison.Ordinal);
            });
            return list;
        }

        private HookSuspect MakeSuspect(CatalogEntry entry, CatalogHotkey hk, bool? occupied)
        {
            var suspect = new HookSuspect();
            suspect.EntryId = entry.Id;
            suspect.Combo = hk == null ? null : hk.Combo;
            suspect.Software = entry.Software;
            suspect.Module = entry.Module;
            suspect.Purpose = hk == null ? null : hk.Purpose;
            suspect.ProcessRunning = entry.ProcessRunning;
            suspect.ConfigState = entry.ConfigState;
            suspect.Occupied = occupied;
            suspect.Evidence = DecideEvidence(entry, hk);

            if (hk != null && hk.FromConfig && entry.ConfigPathResolved != null)
            {
                suspect.SourcePath = entry.ConfigPathResolved;
                suspect.SourceLabel = L10n.T("cat.source.config");
            }
            else if (hk != null && !hk.FromConfig)
            {
                suspect.SourcePath = entry.DocsUrl;
                suspect.SourceLabel = string.Equals(hk.Source, "default", StringComparison.OrdinalIgnoreCase)
                    ? L10n.T("cat.source.default")
                    : L10n.T("cat.source.docs");
            }
            else
            {
                suspect.SourcePath = entry.ConfigPathResolved ?? entry.FirstConfigDisplay;
                suspect.SourceLabel = entry.ConfigPathResolved == null ? L10n.T("cat.source.none") : L10n.T("cat.source.config");
            }

            suspect.Note = ComposeNote(entry, hk, occupied);
            return suspect;
        }

        private static HookEvidence DecideEvidence(CatalogEntry entry, CatalogHotkey hk)
        {
            bool configDerived = hk != null && hk.FromConfig;
            if (configDerived)
            {
                if (entry.ModuleDisabled) return HookEvidence.KnownDefault;
                return entry.ProcessRunning ? HookEvidence.Confirmed : HookEvidence.HighlySuspected;
            }
            // 键位来自官方默认/文档。配置读不出来但进程在跑 → 只有一侧证据，标「高度怀疑」；
            // 否则按「已知默认」标注，绝不冒充本机实测。
            if (hk != null && string.Equals(entry.ConfigState, "unreadable", StringComparison.OrdinalIgnoreCase)
                && entry.ProcessRunning)
            {
                return HookEvidence.HighlySuspected;
            }
            return HookEvidence.KnownDefault;
        }

        private string ComposeNote(CatalogEntry entry, CatalogHotkey hk, bool? occupied)
        {
            var sb = new StringBuilder();
            if (!string.IsNullOrEmpty(entry.Notes)) sb.Append(entry.Notes);

            if (hk != null)
            {
                if (hk.FromConfig) sb.Append(L10n.T("cat.note.configDerived"));
                else if (string.Equals(hk.Source, "default", StringComparison.OrdinalIgnoreCase))
                    sb.Append(L10n.T("cat.note.defaultSource"));
                else sb.Append(L10n.T("cat.note.docsSource"));
            }

            sb.Append(entry.ProcessRunning
                ? L10n.F("cat.note.processRunning", string.Join(", ", entry.RunningProcesses.ToArray()))
                : L10n.T("cat.note.processNotRunning"));

            switch (entry.ConfigState)
            {
                case "read":
                    sb.Append(L10n.F("cat.note.configRead", entry.ConfigPathResolved ?? ""));
                    break;
                case "unreadable":
                    sb.Append(L10n.F("cat.note.configUnreadable", entry.ConfigPathResolved ?? entry.FirstConfigDisplay ?? ""));
                    break;
                case "missing":
                    sb.Append(L10n.F("cat.note.configMissing", entry.FirstConfigDisplay ?? ""));
                    break;
            }

            if (occupied.HasValue)
            {
                sb.Append(occupied.Value ? L10n.T("cat.note.scanOccupied") : L10n.T("cat.note.scanFree"));
            }
            else
            {
                sb.Append(L10n.T("cat.note.keyUnknown"));
            }
            return sb.ToString();
        }

        public static string EvidenceName(HookEvidence e)
        {
            switch (e)
            {
                case HookEvidence.Confirmed: return L10n.T("ev.confirmed");
                case HookEvidence.HighlySuspected: return L10n.T("ev.highlySuspected");
                case HookEvidence.KnownDefault: return L10n.T("ev.knownDefault");
                default: return L10n.T("ev.reference");
            }
        }

        public static string EvidenceExplain(HookEvidence e)
        {
            switch (e)
            {
                case HookEvidence.Confirmed: return L10n.T("ev.explain.confirmed");
                case HookEvidence.HighlySuspected: return L10n.T("ev.explain.highlySuspected");
                case HookEvidence.KnownDefault: return L10n.T("ev.explain.knownDefault");
                default: return L10n.T("ev.explain.reference");
            }
        }

        public static string ConfigStateName(string state)
        {
            switch (state)
            {
                case "read": return L10n.T("cat.config.read");
                case "unreadable": return L10n.T("cat.config.unreadable");
                case "missing": return L10n.T("cat.config.missing");
                default: return L10n.T("cat.config.none");
            }
        }
    }
}
