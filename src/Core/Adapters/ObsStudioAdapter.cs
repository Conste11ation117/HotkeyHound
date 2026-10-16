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
            { "OBSBasic.StartStreaming", "开始推流" },
            { "OBSBasic.StopStreaming", "停止推流" },
            { "OBSBasic.StartRecording", "开始录制" },
            { "OBSBasic.StopRecording", "停止录制" },
            { "OBSBasic.ToggleRecording", "切换录制" },
            { "OBSBasic.ToggleStreaming", "切换推流" },
            { "OBSBasic.StartReplayBuffer", "开启回放缓存" },
            { "OBSBasic.StopReplayBuffer", "关闭回放缓存" },
            { "OBSBasic.SaveReplayBuffer", "保存回放" },
            { "OBSBasic.PauseRecording", "暂停录制" },
            { "OBSBasic.ResumeRecording", "继续录制" },
            { "OBSBasic.StartVirtualCam", "开启虚拟摄像头" },
            { "OBSBasic.StopVirtualCam", "关闭虚拟摄像头" },
            { "OBSBasic.Screenshot", "截图" },
            { "OBSBasic.PushToTalk", "按键通话" },
            { "OBSBasic.PushToMute", "按键静音" },
            { "OBSBasic.TogglePreview", "切换预览" },
            { "OBSBasic.StartReplayBuffer", "开始回放缓存" }
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
                Note("未找到 OBS 配置目录：" + root);
                return list;
            }

            string global = Path.Combine(root, "global.ini");
            if (File.Exists(global)) ReadHotkeys(global, "全局", list);

            string profiles = Path.Combine(root, "basic", "profiles");
            if (Directory.Exists(profiles))
            {
                foreach (string dir in Directory.GetDirectories(profiles))
                {
                    string ini = Path.Combine(dir, "basic.ini");
                    if (File.Exists(ini)) ReadHotkeys(ini, "配置集 " + Path.GetFileName(dir), list);
                }
            }

            Note("解析出 " + list.Count + " 条热键");
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
                    Warn("无法解析 " + kv.Key + " = " + raw + "（" + file + "）");
                    continue;
                }

                string purpose;
                if (!Purposes.TryGetValue(kv.Key, out purpose))
                {
                    purpose = kv.Key.StartsWith("OBSBasic.", StringComparison.OrdinalIgnoreCase)
                        ? kv.Key.Substring(9) : kv.Key;
                }
                outList.Add(Make(Software, module, purpose, combo, kv.Key + "=" + raw, file, false));
            }
        }
    }
}
