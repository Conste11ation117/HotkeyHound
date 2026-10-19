using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;

namespace HotkeyHound.Core.Adapters
{
    /// <summary>MSI Afterburner 的 *Hotkey= 行。值为 00000000h 表示未设置。</summary>
    public sealed class MsiAfterburnerAdapter : AdapterBase
    {
        public override string Software { get { return "MSI Afterburner"; } }

        private static readonly Dictionary<string, string> Purposes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            { "OSDToggleHotkey", "adapter.msi.osdToggle" },
            { "OSDOnHotkey", "adapter.msi.osdOn" },
            { "OSDOffHotkey", "adapter.msi.osdOff" },
            { "OSDServerBlockHotkey", "adapter.msi.osdServerBlock" },
            { "LimiterToggleHotkey", "adapter.msi.limiterToggle" },
            { "LimiterOnHotkey", "adapter.msi.limiterOn" },
            { "LimiterOffHotkey", "adapter.msi.limiterOff" },
            { "ScreenCaptureHotkey", "adapter.msi.screenshot" },
            { "VideoCaptureHotkey", "adapter.msi.videoCapture" },
            { "VideoPrerecordHotkey", "adapter.msi.prerecord" },
            { "PTTHotkey", "adapter.msi.ptt" },
            { "PTT2Hotkey", "adapter.msi.ptt2" },
            { "BeginRecordHotkey", "adapter.msi.beginRecord" },
            { "EndRecordHotkey", "adapter.msi.endRecord" }
        };

        public override IEnumerable<DeclaredHotkey> Collect()
        {
            return Safe(this, CollectCore);
        }

        private List<DeclaredHotkey> CollectCore()
        {
            var list = new List<DeclaredHotkey>();
            string file = @"C:\Program Files (x86)\MSI Afterburner\Profiles\MSIAfterburner.cfg";
            if (!File.Exists(file))
            {
                Note(L10n.F("adapter.msi.notFound", file));
                return list;
            }

            foreach (string line in AdapterUtil.ReadLines(file))
            {
                string s = line.Trim();
                int eq = s.IndexOf('=');
                if (eq <= 0) continue;
                string name = s.Substring(0, eq).Trim();
                if (!name.EndsWith("Hotkey", StringComparison.OrdinalIgnoreCase)) continue;

                string raw = s.Substring(eq + 1).Trim();
                if (raw.Length == 0) continue;
                uint value;
                if (!TryParseHex(raw, out value)) continue;
                if (value == 0) continue;

                var combo = Decode(value);
                if (combo == null)
                {
                    Warn(L10n.F("adapter.msi.badEncoding", name, raw));
                    continue;
                }

                string purposeKey;
                string purpose = Purposes.TryGetValue(name, out purposeKey) ? L10n.T(purposeKey) : name;
                if (name.StartsWith("Profile", StringComparison.OrdinalIgnoreCase) && name.EndsWith("Hotkey", StringComparison.OrdinalIgnoreCase))
                {
                    purpose = L10n.F("adapter.msi.profile", name.Substring(7, name.Length - 7 - 6));
                }

                list.Add(Make(Software, "Profiles\\MSIAfterburner.cfg", purpose, combo, name + "=" + raw, file, false));
            }

            Note(L10n.F("adapter.msi.parsed", list.Count));
            return list;
        }

        private static bool TryParseHex(string raw, out uint value)
        {
            value = 0;
            string t = raw.Trim();
            if (t.EndsWith("h", StringComparison.OrdinalIgnoreCase)) t = t.Substring(0, t.Length - 1);
            if (t.StartsWith("0x", StringComparison.OrdinalIgnoreCase)) t = t.Substring(2);
            return uint.TryParse(t, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out value);
        }

        /// <summary>
        /// Afterburner 把热键压成一个 DWORD：低字节是 VK，次高字节是修饰位
        /// （1=Shift、2=Ctrl、4=Alt、8=Win）。本机所有项均为 0，此解码未在实机验证。
        /// </summary>
        private static KeyCombo Decode(uint value)
        {
            uint vk = value & 0xFF;
            if (vk == 0) return null;
            uint mods = (value >> 8) & 0xFF;
            return new KeyCombo((mods & 0x08) != 0, (mods & 0x02) != 0, (mods & 0x04) != 0, (mods & 0x01) != 0, vk);
        }
    }
}
