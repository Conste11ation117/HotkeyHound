using System;
using System.Collections.Generic;
using System.IO;

namespace HotkeyHound.Core.Adapters
{
    /// <summary>OBS Studio 的 [Hotkeys] 段（global.ini 与各 profile 的 basic.ini）。</summary>
    public sealed class ObsStudioAdapter : AdapterBase
    {
        public override string Software { get { return "OBS Studio"; } }

        private static readonly Dictionary<string, string> Purposes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            { "OBSBasic.StartStreaming", "adapter.obs.startStreaming" },
            { "OBSBasic.StopStreaming", "adapter.obs.stopStreaming" },
            { "OBSBasic.StartRecording", "adapter.obs.startRecording" },
            { "OBSBasic.StopRecording", "adapter.obs.stopRecording" },
            { "OBSBasic.ToggleRecording", "adapter.obs.toggleRecording" },
            { "OBSBasic.ToggleStreaming", "adapter.obs.toggleStreaming" },
            { "OBSBasic.StartReplayBuffer", "adapter.obs.startReplayBuffer" },
            { "OBSBasic.StopReplayBuffer", "adapter.obs.stopReplayBuffer" },
            { "OBSBasic.SaveReplayBuffer", "adapter.obs.saveReplay" },
            { "OBSBasic.PauseRecording", "adapter.obs.pauseRecording" },
            { "OBSBasic.ResumeRecording", "adapter.obs.resumeRecording" },
            { "OBSBasic.StartVirtualCam", "adapter.obs.startVirtualCam" },
            { "OBSBasic.StopVirtualCam", "adapter.obs.stopVirtualCam" },
            { "OBSBasic.Screenshot", "adapter.obs.screenshot" },
            { "OBSBasic.PushToTalk", "adapter.obs.pushToTalk" },
            { "OBSBasic.PushToMute", "adapter.obs.pushToMute" },
            { "OBSBasic.TogglePreview", "adapter.obs.togglePreview" },
            { "OBSBasic.StartReplayBuffer", "adapter.obs.startReplayBuffer2" }
        };

        public override IEnumerable<DeclaredHotkey> Collect()
        {
            return Safe(this, CollectCore);
        }

        private List<DeclaredHotkey> CollectCore()
        {
            var list = new List<DeclaredHotkey>();
            string root = Path.Combine(AdapterUtil.RoamingAppData, "obs-studio");
            if (!Directory.Exists(root))
            {
                Note(L10n.F("adapter.obs.notFound", root));
                return list;
            }

            string global = Path.Combine(root, "global.ini");
            if (File.Exists(global)) ReadHotkeys(global, L10n.T("adapter.obs.globalModule"), list);

            string profiles = Path.Combine(root, "basic", "profiles");
            if (Directory.Exists(profiles))
            {
                foreach (string dir in Directory.GetDirectories(profiles))
                {
                    string ini = Path.Combine(dir, "basic.ini");
                    if (File.Exists(ini)) ReadHotkeys(ini, L10n.F("adapter.obs.profileSet", Path.GetFileName(dir)), list);
                }
            }

            Note(L10n.F("adapter.obs.parsed", list.Count));
            return list;
        }

        private void ReadHotkeys(string file, string module, List<DeclaredHotkey> outList)
        {
            var values = IniRead.ReadSection(file, "Hotkeys");
            foreach (var kv in values)
            {
                string raw = kv.Value == null ? "" : kv.Value.Trim();
                if (raw.Length == 0) continue;
                if (string.Equals(raw, "None", StringComparison.OrdinalIgnoreCase)) continue;

                var combo = KeyCombo.Parse(raw);
                if (combo == null)
                {
                    Warn(L10n.F("adapter.obs.badValue", kv.Key, raw, file));
                    continue;
                }

                string purposeKey;
                string purpose;
                if (!Purposes.TryGetValue(kv.Key, out purposeKey))
                {
                    purpose = kv.Key.StartsWith("OBSBasic.", StringComparison.OrdinalIgnoreCase)
                        ? kv.Key.Substring(9) : kv.Key;
                }
                else
                {
                    purpose = L10n.T(purposeKey);
                }
                outList.Add(Make(Software, module, purpose, combo, kv.Key + "=" + raw, file, false));
            }
        }
    }
}
