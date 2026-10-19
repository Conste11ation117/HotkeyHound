using System;
using System.Collections.Generic;
using System.Globalization;
using Microsoft.Win32;

namespace HotkeyHound.Core.Adapters
{
    /// <summary>NVIDIA ShadowPlay / GeForce Experience 的注册表热键（只读）。</summary>
    public sealed class NvidiaShadowPlayAdapter : AdapterBase
    {
        public override string Software { get { return "NVIDIA ShadowPlay"; } }

        private const string RegPath = @"Software\NVIDIA Corporation\Global\ShadowPlay\NVSPCAPS";

        private static readonly Dictionary<string, string> Purposes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            { "GFEOverlayHKeyV2", "adapter.nvidia.overlayToggle" },
            { "ScreenshotHKey", "adapter.nvidia.screenshot" },
            { "ManualHKey", "adapter.nvidia.manualRecord" },
            { "DVRHKey", "adapter.nvidia.replayToggle" },
            { "IRToggleHKey", "adapter.nvidia.replayToggle" },
            { "FSToggleHKey", "adapter.nvidia.replayToggle" },
            { "FSPresetHKey", "adapter.nvidia.replayPreset" },
            { "FSPresetStyle1HKey", "adapter.nvidia.replayStyle1" },
            { "FSPresetStyle2HKey", "adapter.nvidia.replayStyle2" },
            { "FSPresetStyle3HKey", "adapter.nvidia.replayStyle3" },
            { "FSUIHKeyV1", "adapter.nvidia.replayUi" },
            { "AnselUIHKey", "adapter.nvidia.anselUi" },
            { "PMOCOverlayHKey", "adapter.nvidia.perfOverlay" },
            { "PMOCOverlayCycleHKey", "adapter.nvidia.perfCycle" },
            { "PMOCResetAverageMetricsHKey", "adapter.nvidia.perfResetAverage" },
            { "PMOCLoggingToggleHKey", "adapter.nvidia.perfLogging" },
            { "PMOCFontSizeCycleHKey", "adapter.nvidia.perfFont" },
            { "MicToggleHKey", "adapter.nvidia.micToggle" },
            { "MicPTTHKey", "adapter.nvidia.micPtt" },
            { "ChatUIToggleHKey", "adapter.nvidia.chatToggle" },
            { "ChatPTTHKey", "adapter.nvidia.chatPtt" },
            { "NativeKeyboardInputDialogHKey", "adapter.nvidia.keyboardDialog" },
            { "CustomOverlayBHKey", "adapter.nvidia.overlayB" },
            { "CustomOverlayCHKey", "adapter.nvidia.overlayC" }
        };

        public override IEnumerable<DeclaredHotkey> Collect()
        {
            return Safe(this, CollectCore);
        }

        private List<DeclaredHotkey> CollectCore()
        {
            var list = new List<DeclaredHotkey>();
            string source = "HKCU\\" + RegPath;

            using (RegistryKey key = Registry.CurrentUser.OpenSubKey(RegPath, false))
            {
                if (key == null)
                {
                    Note(L10n.F("adapter.nvidia.keyMissing", source));
                    return list;
                }

                string[] names = key.GetValueNames();
                var counts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
                var vkByName = new Dictionary<string, uint>(StringComparer.OrdinalIgnoreCase);

                foreach (string name in names)
                {
                    object value = key.GetValue(name);
                    if (value == null) continue;

                    if (name.EndsWith("Count", StringComparison.OrdinalIgnoreCase))
                    {
                        uint c;
                        if (ToUint(value, out c)) counts[name.Substring(0, name.Length - 5)] = (int)c;
                        continue;
                    }

                    uint vk;
                    if (ToUint(value, out vk)) vkByName[name] = vk;
                }

                var emitted = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (var kv in counts)
                {
                    if (kv.Value <= 0) continue;
                    string baseName = kv.Key;
                    string sourcePath = source + "\\" + baseName + "Count";

                    var members = new List<KeyValuePair<int, uint>>();
                    foreach (var v in vkByName)
                    {
                        if (!v.Key.StartsWith(baseName, StringComparison.OrdinalIgnoreCase)) continue;
                        if (v.Key.Length <= baseName.Length) continue;
                        int index;
                        if (!int.TryParse(v.Key.Substring(baseName.Length), NumberStyles.Integer,
                                          CultureInfo.InvariantCulture, out index)) continue;
                        members.Add(new KeyValuePair<int, uint>(index, v.Value));
                    }
                    members.Sort(delegate (KeyValuePair<int, uint> a, KeyValuePair<int, uint> b) { return a.Key.CompareTo(b.Key); });

                    var combo = ComposeFromVks(members);
                    if (combo == null)
                    {
                        Warn(L10n.F("adapter.nvidia.badCombo", baseName));
                        continue;
                    }
                    string purposeKey;
                    string purpose;
                    if (!Purposes.TryGetValue(baseName, out purposeKey)) purpose = baseName;
                    else purpose = L10n.T(purposeKey);
                    string raw = baseName + "Count=" + kv.Value;
                    list.Add(Make(Software, "ShadowPlay", purpose, combo, raw, sourcePath, false));
                    emitted.Add(baseName);
                }

                // 另一种编码：值本身是 "0x12;0x5A" 这样的字符串列表
                foreach (string name in names)
                {
                    object value = key.GetValue(name);
                    string s = value as string;
                    if (s == null || s.IndexOf(';') < 0) continue;
                    var combo = KeyCombo.FromVkList(s);
                    if (combo == null) continue;
                    string purposeKey;
                    string purpose;
                    if (!Purposes.TryGetValue(name, out purposeKey)) purpose = name;
                    else purpose = L10n.T(purposeKey);
                    list.Add(Make(Software, "ShadowPlay", purpose, combo, s, source + "\\" + name, false));
                }
            }

            Note(L10n.F("adapter.nvidia.parsed", list.Count));
            return list;
        }

        private static KeyCombo ComposeFromVks(List<KeyValuePair<int, uint>> members)
        {
            if (members.Count == 0) return null;
            bool win = false, ctrl = false, alt = false, shift = false;
            uint vk = 0;
            foreach (var kv in members)
            {
                uint v = kv.Value;
                switch (v)
                {
                    case 0x10: case 0xA0: case 0xA1: shift = true; break;
                    case 0x11: case 0xA2: case 0xA3: ctrl = true; break;
                    case 0x12: case 0xA4: case 0xA5: alt = true; break;
                    case 0x5B: case 0x5C: win = true; break;
                    default: vk = v; break;
                }
            }
            if (vk == 0 || vk > 0xFE) return null;
            return new KeyCombo(win, ctrl, alt, shift, vk);
        }

        private static bool ToUint(object value, out uint result)
        {
            result = 0;
            if (value is byte[])
            {
                byte[] b = (byte[])value;
                if (b.Length < 4) return false;
                result = (uint)(b[0] | (b[1] << 8) | (b[2] << 16) | (b[3] << 24));
                return true;
            }
            if (value is int) { result = (uint)(int)value; return true; }
            if (value is long) { result = (uint)(long)value; return true; }
            string s = value as string;
            if (s != null)
            {
                s = s.Trim();
                if (s.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
                {
                    uint parsed;
                    if (uint.TryParse(s.Substring(2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out parsed))
                    {
                        result = parsed;
                        return true;
                    }
                }
                uint d;
                if (uint.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out d)) { result = d; return true; }
            }
            return false;
        }
    }
}
