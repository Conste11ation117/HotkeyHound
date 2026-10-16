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
        public override string Software { get { return "Windows 系统保留"; } }

        private static readonly string[][] Entries = new string[][]
        {
            new[] { "Win+L", "锁定计算机" },
            new[] { "Win+D", "显示/隐藏桌面" },
            new[] { "Win+M", "最小化所有窗口" },
            new[] { "Win+Shift+M", "还原最小化的窗口" },
            new[] { "Win+E", "打开文件资源管理器" },
            new[] { "Win+R", "打开「运行」对话框" },
            new[] { "Win+I", "打开「设置」" },
            new[] { "Win+S", "打开搜索" },
            new[] { "Win+A", "打开快速设置" },
            new[] { "Win+N", "打开通知中心" },
            new[] { "Win+W", "打开小组件" },
            new[] { "Win+V", "剪贴板历史" },
            new[] { "Win+X", "打开高级用户菜单" },
            new[] { "Win+P", "投影/外接显示" },
            new[] { "Win+K", "连接无线显示/音频" },
            new[] { "Win+O", "锁定设备方向" },
            new[] { "Win+U", "打开「辅助功能」" },
            new[] { "Win+F", "反馈中心" },
            new[] { "Win+H", "语音输入" },
            new[] { "Win+J", "聚焦提示" },
            new[] { "Win+Tab", "任务视图" },
            new[] { "Win+Enter", "启动讲述人" },
            new[] { "Win+Space", "切换输入法" },
            new[] { "Win+Pause", "系统属性" },
            new[] { "Win+Break", "系统属性" },
            new[] { "Win+PrtScn", "截屏并保存到图片文件夹" },
            new[] { "Win+Shift+S", "截图工具（区域截图）" },
            new[] { "Win+Ctrl+D", "新建虚拟桌面" },
            new[] { "Win+Ctrl+Left", "切换到左侧虚拟桌面" },
            new[] { "Win+Ctrl+Right", "切换到右侧虚拟桌面" },
            new[] { "Win+Ctrl+F4", "关闭当前虚拟桌面" },
            new[] { "Win+Ctrl+Shift+B", "重置显卡驱动" },
            new[] { "Win+Ctrl+Enter", "启动讲述人（替代入口）" },
            new[] { "Win+Up", "窗口最大化" },
            new[] { "Win+Down", "窗口还原/最小化" },
            new[] { "Win+Left", "窗口贴靠到左侧" },
            new[] { "Win+Right", "窗口贴靠到右侧" },
            new[] { "Win+Home", "最小化除当前窗口外的所有窗口" },
            new[] { "Win+Z", "打开贴靠布局" },
            new[] { "Win+G", "Xbox Game Bar" },
            new[] { "Win+Alt+R", "开始/停止游戏录制" },
            new[] { "Win+Alt+G", "录制最近 30 秒" },
            new[] { "Win+Alt+PrtScn", "保存游戏截图" },
            new[] { "Win+Shift+Enter", "在部分应用里全屏" },
            new[] { "Alt+Tab", "切换窗口" },
            new[] { "Alt+Shift+Tab", "反向切换窗口" },
            new[] { "Alt+F4", "关闭当前窗口" },
            new[] { "Alt+Esc", "按顺序切换窗口" },
            new[] { "Alt+Space", "打开窗口系统菜单" },
            new[] { "Alt+PrtScn", "截取当前窗口到剪贴板" },
            new[] { "Ctrl+Shift+Esc", "任务管理器" },
            new[] { "Ctrl+Alt+Del", "安全注意序列" },
            new[] { "Ctrl+Shift+Enter", "以管理员身份运行" },
            new[] { "Ctrl+Esc", "打开开始菜单" },
            new[] { "Ctrl+Shift+N", "新建文件夹" },
            new[] { "PrtScn", "截取全屏到剪贴板" },
            new[] { "NumLock", "切换数字键盘锁定" },
            new[] { "ScrollLock", "切换滚动锁定" },
            new[] { "CapsLock", "切换大写锁定" }
        };

        /// <summary>已知归属提示：组合键 → 用途说明。</summary>
        public static Dictionary<string, string> Hints()
        {
            var map = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (string[] e in Entries)
            {
                var combo = KeyCombo.Parse(e[0]);
                if (combo == null) continue;
                map[combo.Normalize()] = e[1];
            }
            for (int i = 0; i <= 9; i++)
            {
                map["Win+" + i] = "启动/切换到任务栏第 " + (i + 1) + " 个应用";
                map["Win+Shift+" + i] = "以管理员身份启动任务栏第 " + (i + 1) + " 个应用";
                map["Win+Ctrl+" + i] = "打开任务栏第 " + (i + 1) + " 个应用的新实例";
                map["Win+Alt+" + i] = "打开任务栏第 " + (i + 1) + " 个应用的跳转列表";
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
                list.Add(Make("Windows 系统保留", "内置表", e[1], combo, e[0], "(内置静态表)", false));
            }
            for (int i = 0; i <= 9; i++)
            {
                Add(list, "Win+" + i, "启动/切换到任务栏第 " + (i + 1) + " 个应用");
                Add(list, "Win+Shift+" + i, "以管理员身份启动任务栏第 " + (i + 1) + " 个应用");
                Add(list, "Win+Ctrl+" + i, "打开任务栏第 " + (i + 1) + " 个应用的新实例");
                Add(list, "Win+Alt+" + i, "打开任务栏第 " + (i + 1) + " 个应用的跳转列表");
            }
            return list;
        }

        private static void Add(List<DeclaredHotkey> list, string text, string purpose)
        {
            var combo = KeyCombo.Parse(text);
            if (combo == null) return;
            list.Add(Make("Windows 系统保留", "内置表", purpose, combo, text, "(内置静态表)", false));
        }

        public override IEnumerable<DeclaredHotkey> Collect()
        {
            return Reserved();
        }
    }
}
