using System;
using System.Collections.Generic;

namespace HotkeyHound.Core.Adapters
{
    /// <summary>
    /// Windows 自身占用的常用组合（内置静态表）。这些不算「某个软件声明」，
    /// 只作为真相表的「已知归属提示」；不参与冲突判定，也不计入声明数。
    /// </summary>
    public sealed class SystemReservedTable : AdapterBase
    {
        public override string Software { get { return L10n.T("reserved.software"); } }

        // 第二列是 L10n 键名而不是文案：语言在 Initialize() 之后才确定，
        // 静态字段初始化时取词会锁死语言。
        private static readonly string[][] Entries = new string[][]
        {
            new[] { "Win+L", "reserved.lockComputer" },
            new[] { "Win+D", "reserved.showDesktop" },
            new[] { "Win+M", "reserved.minimizeAll" },
            new[] { "Win+Shift+M", "reserved.restoreMinimized" },
            new[] { "Win+E", "reserved.fileExplorer" },
            new[] { "Win+R", "reserved.runDialog" },
            new[] { "Win+I", "reserved.settings" },
            new[] { "Win+S", "reserved.search" },
            new[] { "Win+A", "reserved.quickSettings" },
            new[] { "Win+N", "reserved.notificationCenter" },
            new[] { "Win+W", "reserved.widgets" },
            new[] { "Win+V", "reserved.clipboardHistory" },
            new[] { "Win+X", "reserved.powerUserMenu" },
            new[] { "Win+P", "reserved.projectDisplay" },
            new[] { "Win+K", "reserved.wirelessDisplay" },
            new[] { "Win+O", "reserved.lockOrientation" },
            new[] { "Win+U", "reserved.accessibility" },
            new[] { "Win+F", "reserved.feedbackHub" },
            new[] { "Win+H", "reserved.voiceTyping" },
            new[] { "Win+J", "reserved.focusTip" },
            new[] { "Win+Tab", "reserved.taskView" },
            new[] { "Win+Enter", "reserved.narrator" },
            new[] { "Win+Space", "reserved.inputMethod" },
            new[] { "Win+Pause", "reserved.systemProperties" },
            new[] { "Win+Break", "reserved.systemProperties" },
            new[] { "Win+PrtScn", "reserved.screenshotPictures" },
            new[] { "Win+Shift+S", "reserved.snipRegion" },
            new[] { "Win+Ctrl+D", "reserved.newDesktop" },
            new[] { "Win+Ctrl+Left", "reserved.desktopLeft" },
            new[] { "Win+Ctrl+Right", "reserved.desktopRight" },
            new[] { "Win+Ctrl+F4", "reserved.closeDesktop" },
            new[] { "Win+Ctrl+Shift+B", "reserved.resetGraphics" },
            new[] { "Win+Ctrl+Enter", "reserved.narratorAlt" },
            new[] { "Win+Up", "reserved.maximize" },
            new[] { "Win+Down", "reserved.restoreMinimize" },
            new[] { "Win+Left", "reserved.snapLeft" },
            new[] { "Win+Right", "reserved.snapRight" },
            new[] { "Win+Home", "reserved.minimizeOthers" },
            new[] { "Win+Z", "reserved.snapLayouts" },
            new[] { "Win+G", "Xbox Game Bar" },
            new[] { "Win+Alt+R", "reserved.gameRecording" },
            new[] { "Win+Alt+G", "reserved.recordLast30" },
            new[] { "Win+Alt+PrtScn", "reserved.gameScreenshot" },
            new[] { "Win+Shift+Enter", "reserved.fullscreenSomeApps" },
            new[] { "Alt+Tab", "reserved.switchWindows" },
            new[] { "Alt+Shift+Tab", "reserved.switchWindowsReverse" },
            new[] { "Alt+F4", "reserved.closeWindow" },
            new[] { "Alt+Esc", "reserved.cycleWindows" },
            new[] { "Alt+Space", "reserved.windowSystemMenu" },
            new[] { "Alt+PrtScn", "reserved.copyWindow" },
            new[] { "Ctrl+Shift+Esc", "reserved.taskManager" },
            new[] { "Ctrl+Alt+Del", "reserved.secureAttention" },
            new[] { "Ctrl+Shift+Enter", "reserved.runAsAdmin" },
            new[] { "Ctrl+Esc", "reserved.startMenu" },
            new[] { "Ctrl+Shift+N", "reserved.newFolder" },
            new[] { "PrtScn", "reserved.copyScreen" },
            new[] { "NumLock", "reserved.toggleNumLock" },
            new[] { "ScrollLock", "reserved.toggleScrollLock" },
            new[] { "CapsLock", "reserved.toggleCapsLock" }
        };

        /// <summary>已知归属提示：组合键 → 用途说明。</summary>
        public static Dictionary<string, string> Hints()
        {
            var map = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (string[] e in Entries)
            {
                var combo = KeyCombo.Parse(e[0]);
                if (combo == null) continue;
                map[combo.Normalize()] = L10n.T(e[1]);
            }
            for (int i = 0; i <= 9; i++)
            {
                map["Win+" + i] = L10n.F("reserved.taskbar.app", i + 1);
                map["Win+Shift+" + i] = L10n.F("reserved.taskbar.appAdmin", i + 1);
                map["Win+Ctrl+" + i] = L10n.F("reserved.taskbar.newInstance", i + 1);
                map["Win+Alt+" + i] = L10n.F("reserved.taskbar.jumpList", i + 1);
            }
            return map;
        }

        public static List<DeclaredHotkey> Reserved()
        {
            var list = new List<DeclaredHotkey>();
            foreach (string[] e in Entries)
            {
                var combo = KeyCombo.Parse(e[0]);
                if (combo == null) continue;
                list.Add(Make(L10n.T("reserved.software"), L10n.T("reserved.module"), L10n.T(e[1]), combo,
                              e[0], L10n.T("reserved.source"), false));
            }
            for (int i = 0; i <= 9; i++)
            {
                Add(list, "Win+" + i, L10n.F("reserved.taskbar.app", i + 1));
                Add(list, "Win+Shift+" + i, L10n.F("reserved.taskbar.appAdmin", i + 1));
                Add(list, "Win+Ctrl+" + i, L10n.F("reserved.taskbar.newInstance", i + 1));
                Add(list, "Win+Alt+" + i, L10n.F("reserved.taskbar.jumpList", i + 1));
            }
            return list;
        }

        private static void Add(List<DeclaredHotkey> list, string text, string purpose)
        {
            var combo = KeyCombo.Parse(text);
            if (combo == null) return;
            list.Add(Make(L10n.T("reserved.software"), L10n.T("reserved.module"), purpose, combo, text,
                          L10n.T("reserved.source"), false));
        }

        public override IEnumerable<DeclaredHotkey> Collect()
        {
            return Reserved();
        }
    }
}
