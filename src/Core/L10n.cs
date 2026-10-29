using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

namespace HotkeyHound.Core
{
    /// <summary>
    /// 极简国际化（i18n）。两套字符串表：zh-CN 与 en-US，全部面向用户的文案都集中在这里，
    /// 代码里不再散落中文字面量。
    ///
    /// 语言选择优先级：命令行 --lang > 配置（%LOCALAPPDATA%\HotkeyHound\settings.ini）
    /// > CultureInfo.CurrentUICulture（zh* → 中文，其余 → English）。
    /// </summary>
    public static class L10n
    {
        public const string Chinese = "zh-CN";
        public const string English = "en-US";

        private static readonly Dictionary<string, string> Zh = new Dictionary<string, string>(StringComparer.Ordinal);
        private static readonly Dictionary<string, string> En = new Dictionary<string, string>(StringComparer.Ordinal);

        private static string _current = Chinese;

        public static string Current { get { return _current; } }
        public static bool IsEnglish { get { return _current != Chinese; } }

        /// <summary>语言显示名（用于界面语言下拉）。</summary>
        public static string LangDisplay(string lang)
        {
            return lang == Chinese ? "中文（简体）" : "English (en-US)";
        }

        public static string CurrentDisplay { get { return LangDisplay(_current); } }

        /// <summary>当前语言下取字符串；找不到时依次回退到中文、英文、键名本身。</summary>
        public static string T(string key)
        {
            if (key == null) return "";
            string v;
            if (IsEnglish && En.TryGetValue(key, out v)) return v;
            if (Zh.TryGetValue(key, out v)) return v;
            if (En.TryGetValue(key, out v)) return v;
            return key;
        }

        /// <summary>T(key) 后做 string.Format。</summary>
        public static string F(string key, params object[] args)
        {
            string fmt = T(key);
            try { return string.Format(fmt, args); }
            catch (Exception) { return fmt; }
        }

        // ------------------------------------------------------------------ 语言选择

        /// <summary>按优先级决定当前语言。langArg 来自命令行 <c>--lang</c>（可为 null）。</summary>
        public static void Initialize(string langArg)
        {
            string lang = Normalize(langArg);
            if (lang == null) lang = ReadConfiguredLanguage();
            if (lang == null) lang = FromCulture(CultureInfo.CurrentUICulture);
            _current = lang;
        }

        /// <summary>切换语言并持久化（界面语言下拉用）。返回是否成功写入配置。</summary>
        public static bool SetLanguage(string lang)
        {
            string normalized = Normalize(lang);
            if (normalized == null) return false;
            bool changed = normalized != _current;
            _current = normalized;
            WriteConfiguredLanguage(normalized);
            return changed;
        }

        public static string Normalize(string lang)
        {
            if (string.IsNullOrEmpty(lang)) return null;
            string l = lang.Trim().ToLowerInvariant();
            if (l.Length == 0) return null;
            if (l == "zh" || l.StartsWith("zh-")) return Chinese;
            if (l == "en" || l.StartsWith("en-")) return English;
            return null;
        }

        private static string FromCulture(CultureInfo culture)
        {
            try
            {
                string name = culture == null ? null : culture.Name;
                if (!string.IsNullOrEmpty(name) && name.ToLowerInvariant().StartsWith("zh")) return Chinese;
            }
            catch (Exception) { }
            return English;
        }

        // ------------------------------------------------------------------ 配置读写

        public static string SettingsPath()
        {
            try
            {
                string la = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
                if (!string.IsNullOrEmpty(la)) return Path.Combine(la, "HotkeyHound", "settings.ini");
            }
            catch (Exception) { }
            return null;
        }

        private static string ReadConfiguredLanguage()
        {
            string lang = ReadSetting("lang");
            if (lang == null) lang = ReadSetting("language");
            return Normalize(lang);
        }

        /// <summary>用户是否已经选过语言（决定首次启动要不要弹语言选择框）。</summary>
        public static bool HasConfiguredLanguage()
        {
            return ReadConfiguredLanguage() != null;
        }

        private static void WriteConfiguredLanguage(string lang)
        {
            WriteSetting("lang", lang);
        }

        /// <summary>
        /// 读取 settings.ini 里的一个键（不区分大小写）。语言与主题共用这一套读写，
        /// 避免出现两套互相覆盖的配置文件逻辑。
        /// </summary>
        public static string ReadSetting(string key)
        {
            if (string.IsNullOrEmpty(key)) return null;
            try
            {
                string path = SettingsPath();
                if (path == null || !File.Exists(path)) return null;
                foreach (string raw in File.ReadAllLines(path))
                {
                    string line = raw == null ? "" : raw.Trim();
                    if (line.Length == 0 || line.StartsWith("#") || line.StartsWith(";")) continue;
                    int eq = line.IndexOf('=');
                    if (eq <= 0) continue;
                    string k = line.Substring(0, eq).Trim();
                    if (!string.Equals(k, key, StringComparison.OrdinalIgnoreCase)) continue;
                    return line.Substring(eq + 1).Trim();
                }
            }
            catch (Exception) { }
            return null;
        }

        /// <summary>写入 settings.ini 的一个键；保留其它键（例如切换主题不能清掉语言）。</summary>
        public static bool WriteSetting(string key, string value)
        {
            if (string.IsNullOrEmpty(key)) return false;
            try
            {
                string path = SettingsPath();
                if (path == null) return false;
                string dir = Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir)) Directory.CreateDirectory(dir);

                var lines = new List<string>();
                bool replaced = false;
                if (File.Exists(path))
                {
                    foreach (string raw in File.ReadAllLines(path))
                    {
                        string line = raw == null ? "" : raw;
                        string trimmed = line.Trim();
                        int eq = trimmed.IndexOf('=');
                        if (eq > 0 && !trimmed.StartsWith("#") && !trimmed.StartsWith(";"))
                        {
                            string k = trimmed.Substring(0, eq).Trim();
                            if (string.Equals(k, key, StringComparison.OrdinalIgnoreCase))
                            {
                                if (!replaced) lines.Add(key + "=" + (value ?? ""));
                                replaced = true;
                                continue;
                            }
                        }
                        lines.Add(line);
                    }
                }
                if (!replaced) lines.Add(key + "=" + (value ?? ""));

                File.WriteAllText(path, string.Join("\r\n", lines.ToArray()) + "\r\n", new UTF8Encoding(true));
                return true;
            }
            catch (Exception) { return false; }
        }

        // ------------------------------------------------------------------ 字符串表

        static L10n()
        {
            BuildApp();
            BuildIntegrity();
            BuildCli();
            BuildTruth();
            BuildVerdict();
            BuildScanLog();
            BuildReport();
            BuildUi();
            BuildKeyMonitor();
            BuildHelp();
            BuildTerms();
            BuildProbeCatalog();
            BuildAdapters();
            BuildRuntime();
        }

        private static void A(string key, string zh, string en)
        {
            Zh[key] = zh;
            En[key] = en;
        }

        private static void BuildApp()
        {
            A("app.title", "HotkeyHound", "HotkeyHound");
            A("app.description", "全局快捷键冲突排查工具", "Global hotkey conflict troubleshooting tool");
            A("app.versionNote",
              "四个引擎全部可用：A 占用扫描、B 按键归属、C 钩子窃取检测、D 配置聚合。"
              + "A/D 只读、不需要管理员；B/C 需要安装钩子（仅在显式点「开始监听」/「布防」时），"
              + "管理员权限下覆盖最全。x64 与 32 位进程的归属都已覆盖（后者由自动拉起的 32 位宿主提供），"
              + "并已在真机验证。引擎 C 在前提不足（引擎 B 未监听 / 引擎 A 未扫描）时只给「无法判定」，"
              + "不会误报截胡。",
              "All four engines are available: A occupancy scan, B key attribution, "
              + "C hook interception detection, D config aggregation. "
              + "A/D are read-only and need no administrator rights; B/C install hooks "
              + "(only when you explicitly click Listen / Arm), cover the most as administrator. "
              + "Attribution now covers both x64 and 32-bit processes (the latter through a 32-bit "
              + "companion host started automatically), and has been verified on a real machine. "
              + "Engine C reports Inconclusive instead of guessing when its prerequisites are missing "
              + "(B not listening / A not scanned).");
            A("common.unknown", "未知", "Unknown");
            A("common.yes", "是", "Yes");
            A("common.no", "否", "No");
            A("common.none", "（无）", "(none)");
        }

        private static void BuildIntegrity()
        {
            A("il.untrusted", "低完整性（Untrusted）", "Low integrity (Untrusted)");
            A("il.low", "低完整性（Low）", "Low integrity (Low)");
            A("il.medium", "中完整性（Medium，正常）", "Medium integrity (Medium, normal)");
            A("il.high", "高完整性（High，已提权）", "High integrity (High, elevated)");
            A("il.system", "系统完整性（System）", "System integrity (System)");
            A("il.unknown", "完整性未知", "Integrity unknown");
            A("il.warn.low",
              "当前程序以{0}运行（exe 位于带 Low 标签的目录）。UIPI 会拦住对普通完整性进程的钩子注入，"
              + "引擎 B/C 基本失效，写 %TEMP%、%LOCALAPPDATA% 也会被拒绝。"
              + "请把 HotkeyHound.exe 复制到普通目录（例如 C:\\Tools\\）后再运行。",
              "This program is running at {0} (the exe lives in a directory carrying the Low label). "
              + "UIPI blocks hook injection into normal-integrity processes, so engines B/C are largely "
              + "useless, and writes to %TEMP% / %LOCALAPPDATA% are denied. "
              + "Copy HotkeyHound.exe to an ordinary directory (e.g. C:\\Tools\\) and run it from there.");
            A("il.warn.notAdmin",
              "当前不是管理员。以管理员身份运行才能看到 PowerToys 这类提权进程的归属。",
              "Not running as administrator. Run as administrator to see attribution for elevated "
              + "processes such as PowerToys.");
        }

        private static void BuildCli()
        {
            A("cli.usage.title", "用法：", "Usage:");
            A("cli.except", "未处理的异常：{0}", "Unhandled exception: {0}");
            A("cli.parseFailed", "命令行解析失败：{0}", "Command-line parsing failed: {0}");

            A("cli.scan.start", "正在运行引擎 A（占用扫描）+ 引擎 D（配置聚合）…",
              "Running engine A (occupancy scan) + engine D (config aggregation)…");
            A("cli.alreadyRunning", "HotkeyHound 已在运行，已把现有窗口切到前台。",
              "HotkeyHound is already running; brought its window to the front.");
            A("cli.scan.failed", "扫描失败：{0}", "Scan failed: {0}");
            A("cli.scan.reportWritten", "报告已写入：{0}", "Report written: {0}");
            A("cli.scan.reportWriteFailed", "报告写入失败：{0}", "Failed to write report: {0}");
            A("cli.scan.defaultDirFailed", "默认目录写入失败：{0}", "Default directory write failed: {0}");
            A("cli.scan.jsonWritten", "JSON 已写入：{0}", "JSON written: {0}");
            A("cli.scan.jsonWriteFailed", "JSON 写入失败：{0}", "Failed to write JSON: {0}");
            A("cli.scan.summary", "占用 {0} / 空闲 {1} / 声明 {2} / 真相表 {3} 行",
              "Occupied {0} / free {1} / declared {2} / truth rows {3}");
            A("cli.writable.hint",
              "提示：请把输出路径改到可写目录（例如程序所在目录）。"
              + "若从带「Mandatory Label\\Low 完整性」标签的目录（如 iCloud Drive 下的工作区）启动本程序，"
              + "进程会以低完整性运行，%TEMP%、%LOCALAPPDATA%、%USERPROFILE% 等目录都会写入失败。{0}",
              "Hint: choose a writable output directory (e.g. the program's own folder). "
              + "If this program was launched from a directory carrying the "
              + "\"Mandatory Label\\Low Integrity\" label (such as the workspace under iCloud Drive), "
              + "the process runs at low integrity and writes to %TEMP%, %LOCALAPPDATA%, "
              + "%USERPROFILE% and similar will fail.{0}");
            A("cli.writable.target", "本次目标目录：{0}", " Target directory: {0}");

            A("cli.listen.title", "{0} {1} —— 按键归属监听（引擎 B）",
              "{0} {1} — key attribution listener (engine B)");
            A("cli.listen.meta", "时长：{0} 秒　完整性：{1}　管理员：{2}",
              "Duration: {0} s  Integrity: {1}  Admin: {2}");
            A("cli.listen.note", "注意：{0}", "Note: {0}");
            A("cli.listen.startFailed", "启动失败：{0}", "Start failed: {0}");
            A("cli.listen.installed",
              "钩子已安装（句柄 0x{0}），请按下热键…",
              "Hook installed (handle 0x{0}); press your hotkey now…");
            A("cli.listen.hint",
              "提示：用键盘钩子实现的热键（键盘管理器、输入法、AHK）不会经过 WM_HOTKEY，看不到。",
              "Hint: hotkeys implemented with keyboard hooks (Keyboard Manager, IME, AHK) do not go "
              + "through WM_HOTKEY and cannot be seen here.");
            A("cli.listen.nonAdminHint",
              "注意：当前不是管理员，看不到提权进程的归属。加 --elevate 可提权重启。",
              "Note: not running as administrator, so elevated processes are invisible. "
              + "Add --elevate to relaunch elevated.");
            A("cli.listen.total", "共收到 {0} 条归属记录。", "Received {0} attribution record(s).");
            A("cli.listen.empty1",
              "一条都没有。可能原因：没有按下任何全局热键；或当前不是管理员，看不到提权进程；",
              "None at all. Possible reasons: no global hotkey was pressed; or you are not an "
              + "administrator so elevated processes are invisible;");
            A("cli.listen.empty2",
              "或程序以低完整性运行，钩子注入被 UIPI 拦住了。",
              "or the program runs at low integrity and UIPI blocked hook injection.");
            A("cli.listen.unhooked",
              "钩子已卸载（UnhookWindowsHookEx 已在引擎停止时调用）。",
              "Hook unloaded (UnhookWindowsHookEx was called when the engine stopped).");

            A("cli.monitor.title", "{0} {1} —— 按键监视（Raw Input + 低层钩子 + 窗口消息）",
              "{0} {1} — key monitor (raw input + low-level hook + window messages)");
            A("cli.monitor.meta", "时长：{0} 秒　完整性：{1}　管理员：{2}",
              "Duration: {0} s  Integrity: {1}  Admin: {2}");
            A("cli.monitor.startFailed", "按键监视启动失败：{0}", "Failed to start the key monitor: {0}");
            A("cli.monitor.rawReady", "Raw Input：已注册（窗口 0x{0}，RIDEV_INPUTSINK）",
              "Raw input: registered (window 0x{0}, RIDEV_INPUTSINK)");
            A("cli.monitor.rawFailed", "Raw Input：不可用（{0}）", "Raw input: unavailable ({0})");
            A("cli.monitor.hookReady", "低层键盘钩子：已安装（WH_KEYBOARD_LL，句柄 0x{0}）",
              "Low-level keyboard hook: installed (WH_KEYBOARD_LL, handle 0x{0})");
            A("cli.monitor.hookFailed", "低层键盘钩子：安装失败（{0}）", "Low-level keyboard hook: installation failed ({0})");
            A("cli.monitor.windowNote",
              "窗口消息层：控制台模式没有本程序窗口，只有图形界面聚焦时才可能收到 WM_KEYDOWN。",
              "Window-message layer: in console mode there is no HotkeyHound window; WM_KEYDOWN is possible only "
              + "when the GUI window has focus.");
            A("cli.monitor.waiting", "请现在按键…（{0} 秒后自动停止；进程不写配置、不改注册表）",
              "Press keys now… (stops automatically after {0} s; no config or registry is written)");
            A("cli.monitor.event", "{0}  [{1}]  {2,-14} vk=0x{3:X2} scan=0x{4:X2} flags={5}  {6}",
              "{0}  [{1}]  {2,-14} vk=0x{3:X2} scan=0x{4:X2} flags={5}  {6}");
            A("cli.monitor.verdict", "{0}  [判定] {1}：{2}　{3}　{4}",
              "{0}  [verdict] {1}: {2}  {3}  {4}");
            A("cli.monitor.summary", "三层事件数：Raw Input {0}　低层钩子 {1}　窗口消息 {2}；判定 {3} 条。",
              "Layer events: raw input {0}, low-level hook {1}, window messages {2}; verdicts {3}.");
            A("cli.monitor.noEvents",
              "本次没有捕获到任何按键。请确认键盘可用，并在监视期间按下要排查的键。",
              "No key was captured. Make sure the keyboard works and press the key under investigation while "
              + "monitoring.");
            A("cli.monitor.unhooked",
              "按键监视已停止：低层钩子已卸载，Raw Input 已注销。",
              "Key monitor stopped: the low-level hook was unloaded and raw input was unregistered.");
            A("cli.monitor.scancodeMap", "Scancode Map（重映射）：{0} 条",
              "Scancode Map (remaps): {0}");
            A("cli.monitor.scancodeMapEmpty", "Scancode Map（重映射）：无（注册表中没有该值）",
              "Scancode Map (remaps): none (the registry value does not exist)");
            A("cli.monitor.scancodeLine", "  {0} → {1}（0x{2:X2} → 0x{3:X2}）",
              "  {0} -> {1} (0x{2:X2} -> 0x{3:X2})");
            A("cli.monitor.drivers", "键盘类过滤驱动（仅供参考，不判定、不卸载）：{0} 个",
              "Keyboard-class filter drivers (reference only, never judged or unloaded): {0}");
            A("cli.monitor.driverLine", "  {0}　组={1}　Start={2}　{3}",
              "  {0}  group={1}  Start={2}  {3}");
            A("cli.monitor.stuckNone", "卡住的修饰键：无。", "Stuck modifiers: none.");
            A("cli.monitor.stuckSome", "卡住的修饰键：{0}", "Stuck modifiers: {0}");

            A("cli.elevate.already", "当前已经是管理员，无需提权。",
              "Already running as administrator; no elevation needed.");
            A("cli.elevate.requesting", "正在请求以管理员身份重启…", "Requesting restart as administrator…");
            A("cli.elevate.ok",
              "已请求提权，新的管理员进程即将启动，本进程退出。",
              "Elevation requested; a new administrator process will start and this one will exit.");
            A("cli.elevate.cancelled",
              "提权被取消（用户点了「否」），未做任何改动。",
              "Elevation cancelled (the user chose No); nothing was changed.");
            A("cli.elevate.failed", "提权失败：{0}", "Elevation failed: {0}");

            A("cli.lang.bad", "无法识别的语言：{0}（可用：zh、en）", "Unrecognized language: {0} (use: zh, en)");
            A("cli.selftest.failed", "自测执行失败：{0}", "Self-test execution failed: {0}");
            A("cli.listen.pathPrefix", "      路径：{0}", "      Path: {0}");

            A("cli.help.gui", "  HotkeyHound.exe                                启动图形界面",
              "  HotkeyHound.exe                                Launch the GUI");
            A("cli.help.scan", "  HotkeyHound.exe --scan [--report <路径>] [--json <路径>]",
              "  HotkeyHound.exe --scan [--report <path>] [--json <path>]");
            A("cli.help.scanDesc", "                                                跑引擎 A + D，生成 HTML 报告",
              "                                                Run engines A + D, generate an HTML report");
            A("cli.help.listen", "  HotkeyHound.exe --listen [--seconds N]         按键归属监听（引擎 B），N 默认 30 秒",
              "  HotkeyHound.exe --listen [--seconds N]         Key attribution listener (engine B), N defaults to 30");
            A("cli.help.listenDesc", "                                                 需要管理员才有完整覆盖；用 --elevate 可提权重启；32 位进程由自动拉起的 32 位宿主覆盖",
              "                                                 Administrator gives full coverage; --elevate relaunches elevated; 32-bit processes are covered by an automatically started 32-bit host");
            A("cli.help.monitor", "  HotkeyHound.exe --monitor [--seconds N]        按键监视（Raw Input + 低层钩子），N 默认 15 秒",
              "  HotkeyHound.exe --monitor [--seconds N]        Key monitor (raw input + low-level hook), N defaults to 15");
            A("cli.help.monitorDesc", "                                                 会安装 WH_KEYBOARD_LL；只观察、不吞键，不结束任何进程",
              "                                                 Installs WH_KEYBOARD_LL; observes only, never swallows keys or ends processes");
            A("cli.help.selftest", "  HotkeyHound.exe --selftest [--no-hooks]        自测，全部通过退出码 0",
              "  HotkeyHound.exe --selftest [--no-hooks]        Self-test; exit code 0 when all pass");
            A("cli.help.selftestDesc", "                                                 --no-hooks 跳过会安装钩子的 T6/T7",
              "                                                 --no-hooks skips T6/T7, which install hooks");
            A("cli.help.elevate", "  HotkeyHound.exe --elevate                      以管理员身份重启并继续（保留原参数）",
              "  HotkeyHound.exe --elevate                      Relaunch as administrator and continue (keeps args)");
            A("cli.help.elevateDesc", "                                                 在 UAC 提示里点「否」会安全取消，不会崩溃",
              "                                                 Choosing No in the UAC prompt cancels safely, no crash");
            A("cli.help.lang", "  HotkeyHound.exe --lang zh|en                   指定界面/报告语言（默认跟随系统区域）",
              "  HotkeyHound.exe --lang zh|en                   Set UI/report language (default follows the system locale)");
            A("cli.help.theme", "  HotkeyHound.exe --theme system|light|dark     本次运行强制主题（不写入设置；--screenshot 深色验收用）",
              "  HotkeyHound.exe --theme system|light|dark     Force the theme for this run (not persisted; for dark screenshots)");
            A("cli.help.helper", "  HotkeyHound.exe --helper                       （内部用）辅助进程",
              "  HotkeyHound.exe --helper                       (internal) helper process");
            A("cli.help.screenshot", "  HotkeyHound.exe --screenshot <目录>            （文档用）渲染八个页面为 PNG 后退出，不装钩子",
              "  HotkeyHound.exe --screenshot <dir>             (docs) render the eight pages to PNG, then exit; installs no hooks");
            A("cli.help.help", "  HotkeyHound.exe --help                         显示本帮助",
              "  HotkeyHound.exe --help                         Show this help");
            A("cli.help.notesTitle", "说明：", "Notes:");
            A("cli.help.note.report", "  --report 缺省时在当前目录生成 report-yyyyMMdd-HHmmss.html",
              "  Without --report, report-yyyyMMdd-HHmmss.html is written to the current directory");
            A("cli.help.note.readonly", "  报告与扫描过程全部只读，不会修改任何配置或注册表",
              "  The report and the scan are fully read-only; no config or registry is modified");
            A("cli.help.note.hook", "  钩子类热键（键盘管理器、输入法、AutoHotkey）不经过 RegisterHotKey，扫描看不到；"
              + "要看清归属请用 --listen 或图形界面的「按键归属」页，并尽量以管理员身份运行。",
              "  Hook-based hotkeys (Keyboard Manager, IME, AutoHotkey) do not use RegisterHotKey and are invisible "
              + "to the scan; to see attribution use --listen or the Attribution page, ideally as administrator.");
        }

        private static void BuildTruth()
        {
            A("truth.kind.matched", "声明与实测一致", "Declared and measured match");
            A("truth.kind.declaredOnly", "有声明但未注册成功", "Declared but not registered");
            A("truth.kind.unowned", "已占用但无声明", "Occupied but undeclared");
            A("truth.kind.reserved", "Windows 内置快捷键", "Windows built-in shortcut");
            A("truth.kind.free", "空闲且无声明", "Free and undeclared");

            A("truth.explain.matched",
              "配置文件里有声明，且系统里确实注册了这个热键——归属明确，冲突风险最低。",
              "There is a declaration in a config file and the hotkey is actually registered in the "
              + "system — attribution is clear and conflict risk is lowest.");
            A("truth.explain.declaredOnly",
              "配置文件里有声明，但系统里注册不上（说明当前是空闲的）。常见原因：该软件用键盘钩子而不是 "
              + "RegisterHotKey 实现，或者它当前没有运行。",
              "There is a declaration in a config file but it cannot be registered in the system "
              + "(so it is currently free). Common reasons: the software implements it with a keyboard "
              + "hook instead of RegisterHotKey, or it is not running now.");
            A("truth.explain.unowned",
              "系统里已被占用，但所有可读的配置文件里都没有对应声明。可能是配置不可读的软件（IM、输入法、"
              + "驱动工具），也可能是别的用户会话注册的。",
              "It is occupied in the system but no readable config file declares it. It may come from "
              + "software whose config is unreadable (IM, IME, driver tools), or from another user session.");
            A("truth.explain.reserved",
              "这是 Windows 内置键盘行为，不表示第三方软件发生了冲突。具体作用会随活动窗口和系统版本变化。",
              "This is built-in Windows keyboard behavior, not a third-party software conflict. The exact action "
              + "depends on the active window and Windows version.");
            A("truth.explain.free",
              "系统里既没注册、也没有任何软件声明。这些键位是自由的，可用来挑一个不冲突的热键。",
              "Neither registered in the system nor declared by any software. These keys are free and can "
              + "be used to pick a conflict-free hotkey.");

            A("truth.evidence.both", "两者", "Both");
            A("truth.evidence.config", "配置文件", "Config file");
            A("truth.evidence.measured", "实测注册", "Measured");

            A("truth.note.matched",
              "配置文件声明与系统注册都存在，归属明确。",
              "Both the config declaration and the system registration exist; attribution is clear.");
            A("truth.note.declaredOnly",
              "声明存在但系统未注册：该软件可能用键盘钩子实现该热键（不经过 RegisterHotKey），"
              + "或当前没有运行。",
              "Declared but not registered in the system: the software may implement this hotkey with a "
              + "keyboard hook (not via RegisterHotKey), or it is not running now.");
            A("truth.note.declaredOnly.defaultSuffix", "（该项可能是默认值）", " (this may be a default value)");
            A("truth.note.unowned",
              "没有找到任何可读配置的声明；可能是 IM/输入法等配置不可读的软件注册的。",
              "No declaration was found in any readable config; it may have been registered by software "
              + "whose config is unreadable (IM/IME).");
            A("truth.note.reserved",
              "Windows 内置行为；这不是未知软件占用，也不需要按第三方冲突处理。",
              "Built-in Windows behavior; this is not an unknown software owner and does not need to be treated "
              + "as a third-party conflict.");
            A("truth.note.unowned.errSuffix",
              "注册失败错误码 {0}（不是 1409，属于系统保留或特殊限制，不一定是软件占用）。",
              "RegisterHotKey failed with error {0} (not 1409; this is a system reservation or special "
              + "limit, not necessarily software occupancy).");
            A("truth.note.unowned.candidates", "当前运行的候选进程：", "Candidate processes currently running: ");
            A("truth.note.unowned.more", " 等{0}个", " and {0} in total");
            A("truth.note.free", "空闲，可以直接拿来当作新热键。", "Free; can be used directly as a new hotkey.");
            A("truth.note.offGrid",
              "该组合不在本次探测网格内（引擎 A 只覆盖修饰键×常用按键），因此没有实测结果。",
              "This combination is outside the probe grid (engine A covers only modifiers x common keys), "
              + "so there is no measured result.");
            A("truth.note.shiftVariant",
              "可能有 Shift 变体：声明为 {0}（{1}/{2}）",
              "Possible Shift variant: declared as {0} ({1}/{2})");
            A("truth.note.moreDeclared", " 等{0}条", " and {0} in total");
        }

        private static void BuildVerdict()
        {
            A("verdict.swallowedUpstream", "被上层钩子吞掉", "Swallowed by an upstream hook");
            A("verdict.swallowedBeforeHotkey", "疑似被钩子截胡", "Likely intercepted by a hook");
            A("verdict.delivered", "正常送达", "Delivered normally");
            A("verdict.notGlobal", "不是全局热键", "Not a global hotkey");
            A("verdict.unknown", "等待结论", "Awaiting verdict");
            A("verdict.inconclusive", "无法判定", "Inconclusive");

            A("verdictLogic.pending.evidence", "还在布防时间窗内", "Still inside the arming window");
            A("verdictLogic.pending.note",
              "在时间窗内按这个组合键，等窗口结束出结论。",
              "Press this combination inside the window and wait for the verdict.");

            A("verdictLogic.delivered.evidence",
              "本程序的 LL 钩子看到了这个按键，且引擎 B 收到了归属报告",
              "This program's LL hook saw the key and engine B received an attribution report");
            A("verdictLogic.delivered.note",
              "按键正常送达，持有者明确。",
              "The key was delivered normally and its owner is clear.");

            A("verdictLogic.noInput.evidence", "时间窗内未检测到该组合键。",
              "The combination was not detected during the time window.");
            A("verdictLogic.noInput.note",
              "没有按键证据。请布防后按一次该组合键；未收到事件本身不能证明被钩子拦截。",
              "No keypress was observed. Press the combination after arming; a lack of events alone does not prove interception.");

            A("verdictLogic.swallowedUpstream.evidence",
              "布防后的时间窗内，本程序的 WH_KEYBOARD_LL 钩子根本没看到这个组合键",
              "Within the window after arming, this program's WH_KEYBOARD_LL hook never saw this combination");
            A("verdictLogic.swallowedUpstream.note",
              "按键被你机器上某个更上层的钩子吞掉了，没能到达本程序。"
              + "常见嫌疑：键盘管理器、输入法、远程控制软件、游戏平台的叠加层。",
              "The key was swallowed by an upstream hook on your machine and never reached this program. "
              + "Common suspects: Keyboard Manager, IME, remote-control software, game-platform overlays.");

            A("verdictLogic.notGlobal.evidence",
              "本程序的 LL 钩子看到了这个按键，但引擎 A 的占用表里没有它",
              "This program's LL hook saw the key, but engine A's occupancy table does not contain it");
            A("verdictLogic.notGlobal.note",
              "这不是全局热键，按键由当前窗口自己处理。",
              "This is not a global hotkey; the key is handled by the current window.");

            A("verdictLogic.beforeHotkey.evidence",
              "本程序的 LL 钩子看到了这个按键，引擎 A 也把它记为已占用，但在阈值时间内没有收到归属报告",
              "This program's LL hook saw the key and engine A marked it occupied, but no attribution "
              + "report arrived within the threshold");
            A("verdictLogic.beforeHotkey.note",
              "按键到达了系统，但持有者没收到命令。可用对照排除逐个退出可疑软件复测。",
              "The key reached the system but the owner did not get the command. Use the comparison workflow to "
              + "exit suspect software one at a time and re-test.");

            A("verdictLogic.inconclusiveNoScan.evidence",
              "本次结算时还没有可用的占用扫描结果（引擎 A 的占用表是空的）",
              "No occupancy scan result was available when this probe settled (engine A's table is empty)");
            A("verdictLogic.inconclusiveNoScan.note",
              "缺少占用表，无法区分“不是全局热键”和“被截胡”。先扫描，再布防。",
              "Without the occupancy table it cannot tell \"not a global hotkey\" from \"intercepted\". Scan first, "
              + "then arm again.");

            A("verdictLogic.inconclusiveNoB.evidence",
              "本程序的 LL 钩子看到了这个按键，引擎 A 也把它记为已占用，但结算时引擎 B（按键归属）没有在监听",
              "This program's LL hook saw the key and engine A marked it occupied, but engine B (attribution) "
              + "was not listening when the probe settled");
            A("verdictLogic.inconclusiveNoB.note",
              "没有归属报告可能只是引擎 B 没开，不能据此断定被截胡。布防时一并启动引擎 B，或先开始监听再布防。",
              "A missing attribution report may simply mean engine B was off; that is not evidence of interception. "
              + "Start engine B together when arming, or start listening first and then arm.");

            A("verdictLogic.late",
              "（注：{0} 的归属报告在按键之后超过 {1:0.0} 秒才到，判为截胡）",
              " (note: the attribution report from {0} arrived more than {1:0.0} s after the keypress, "
              + "so it is judged as intercepted)");

            A("sniffer.arm.log", "已布防 {0}：判定窗口 {1:0.0} 秒；引擎 A 把它记为{2}",
              "Armed {0}: window {1:0.0} s; engine A records it as {2}");
            A("sniffer.arm.occupied", "已占用", "occupied");
            A("sniffer.arm.free", "空闲", "free");
            A("sniffer.verdict.log", "钩子检测结论：{0} → {1}", "Hook-check verdict: {0} -> {1}");
            A("sniffer.compare.unnamed", "未命名对照", "Unnamed comparison");
            A("sniffer.compare.none", "结论：本次还没有任何钩子检测结论", "Verdict: no hook-check verdict yet");
            A("sniffer.compare.candNone", "候选进程：一个都没在运行", "Candidate processes: none running");
            A("sniffer.compare.candList", "候选进程（{0} 个）：{1}", "Candidate processes ({0}): {1}");

            A("attr.unknownCombo", "(未知)", "(unknown)");
            A("attr.unknownProc", "(未知进程)", "(unknown process)");
            A("attr.elevatedTag", "  [已提权]", "  [elevated]");
            A("attr.note.badParam", "无法解析热键参数", "Could not parse the hotkey parameter");
            A("attr.note.noProc", "拿不到进程信息（可能已退出，或权限不足）",
              "Could not get process info (it may have exited, or permissions are insufficient)");

            // 32 位伴随宿主：钩子只能注入同位数的进程，32 位归属由另一个 32 位进程提供。
            A("ui.at.col.bitness", "位数", "Bitness");
            A("ui.at.helper32", "　32 位宿主：{0}", "  32-bit host: {0}");

            A("helper32.note.extracted", "32 位产物已释放：{0}（{1:N0} 字节）",
              "32-bit artifact extracted: {0} ({1:N0} bytes)");
            A("helper32.note.resourceFailed", "读取内嵌资源 {0} 失败：{1}",
              "Failed to read embedded resource {0}: {1}");
            A("helper32.note.noResource",
              "程序里没有内嵌 {0}（构建时 32 位产物没编出来？）",
              "{0} is not embedded in the program (was the 32-bit build skipped?)");
            A("helper32.note.dirFailed", "32 位产物释放目录不可用，已跳过：{0} → {1}",
              "Directory for the 32-bit artifact is unusable, skipped: {0} → {1}");
            A("helper32.note.allFailed", "所有候选目录都写不进去。最后一次：{0}",
              "No candidate directory is writable. Last failure: {0}");
            A("helper32.log.extractFailed", "32 位产物释放失败：{0}",
              "Failed to extract the 32-bit artifacts: {0}");
            A("helper32.log.startFailed", "32 位宿主启动失败：{0}",
              "Failed to start the 32-bit companion: {0}");
            A("helper32.log.started", "32 位宿主已启动：pid={0}，exe={1}",
              "32-bit companion started: pid={0}, exe={1}");
            A("helper32.log.failed", "32 位宿主不可用（32 位进程的归属将缺失）：{0}",
              "The 32-bit companion is unavailable (attribution for 32-bit processes will be missing): {0}");
            A("helper32.log.timeout",
              "32 位宿主在 {0} 毫秒内没有就绪（32 位进程的归属缺失）；最后一行：{1}",
              "The 32-bit companion did not become ready within {0} ms (attribution for 32-bit processes "
              + "is missing); last line: {1}");
            A("helper32.log.readyNoHook",
              "32 位宿主已就绪，但 32 位钩子没有装上（32 位进程的归属缺失）：{0}",
              "The 32-bit companion is ready but its hook is not installed (attribution for 32-bit "
              + "processes is missing): {0}");
            A("helper32.log.ready",
              "32 位宿主就绪：pid={0}，窗口=0x{1:X}，32 位钩子已安装；控制块：{2}",
              "32-bit companion ready: pid={0}, window=0x{1:X}, 32-bit hook installed; control block: {2}");
            A("helper32.log.line", "  [helper32] {0}", "  [helper32] {0}");
            A("helper32.log.stdoutEnded", "32 位宿主的输出流已结束（进程已退出）。",
              "The 32-bit companion's output stream ended (the process exited).");
            A("helper32.log.readFailed", "读取 32 位宿主输出失败：{0}",
              "Failed to read the 32-bit companion's output: {0}");
            A("helper32.log.exited", "32 位宿主已退出（pid={0}）。",
              "32-bit companion exited (pid={0}).");
            A("helper32.log.killed", "32 位宿主没有响应退出请求，已强制结束（pid={0}）。",
              "The 32-bit companion did not respond to the exit request and was terminated (pid={0}).");
            A("helper32.log.stopFailed", "结束 32 位宿主时出错：{0}",
              "Error while stopping the 32-bit companion: {0}");
            A("helper32.ui.running", "32 位宿主运行中（PID {0}）",
              "32-bit host running (PID {0})");
            A("helper32.ui.noHook", "32 位宿主在跑，但 32 位钩子没装上",
              "32-bit host is running but its hook is not installed");
            A("helper32.ui.off", "32 位宿主未启动", "32-bit host not started");
            A("helper32.ui.failed", "32 位宿主不可用（32 位进程覆盖缺失）",
              "32-bit host unavailable (no 32-bit coverage)");
        }

        private static void BuildScanLog()
        {
            A("scan.engineA.start", "引擎 A：开始全量组合键试探，共 {0} 个组合",
              "Engine A: probing all key combinations, {0} in total");
            A("scan.engineA.done", "引擎 A：占用 {0}，空闲 {1}，耗时 {2} 毫秒",
              "Engine A: occupied {0}, free {1}, took {2} ms");
            A("scan.engineD.start", "引擎 D：开始采集已知软件的配置声明",
              "Engine D: collecting config declarations from known software");
            A("scan.truth.building", "正在生成真相表（A × D）", "Building the truth table (A x D)");
            A("scan.truth.candidates", "检测到 {0} 个候选进程正在运行",
              "Detected {0} candidate processes running");
            A("scan.truth.kind", "真相表：{0} = {1} 行", "Truth table: {0} = {1} row(s)");
            A("scan.truth.done", "扫描完成，共 {0} 行真相表", "Scan complete, {0} truth rows in total");
        }

        private static void BuildReport()
        {
            A("rep.title", "{0} 报告 {1}", "{0} report {1}");
            A("rep.h1", "HotkeyHound 全局快捷键冲突报告", "HotkeyHound — Global Hotkey Conflict Report");
            A("rep.meta.generated", "生成时间：", "Generated: ");
            A("rep.meta.machine", "机器名：", "Machine: ");
            A("rep.meta.admin", "管理员权限：", "Administrator: ");
            A("rep.meta.adminYes", "是", "yes");
            A("rep.meta.adminNo", "否（部分提权进程看不到，建议以管理员身份重跑）",
              "no (some elevated processes are invisible; re-run as administrator)");
            A("rep.meta.version", "版本：", "Version: ");
            A("rep.meta.lang", "语言：", "Language: ");
            A("rep.meta.sep", "　|　", "  |  ");

            A("rep.card.occupied", "已占用组合", "Occupied combos");
            A("rep.card.free", "空闲组合", "Free combos");
            A("rep.card.declared", "配置声明数", "Config declarations");
            A("rep.card.truthRows", "真相表行数", "Truth table rows");
            A("rep.card.matched", "声明×实测一致", "Declared = measured");
            A("rep.card.declaredOnly", "有声明未注册", "Declared, not registered");
            A("rep.card.unowned", "占用无声明", "Occupied, undeclared");
            A("rep.card.freeOnly", "空闲无声明", "Free, undeclared");

            A("rep.engines.title", "四个引擎的本次结果", "This run's four engines");
            A("rep.engines.a",
              "<li><b>引擎 A（占用扫描）</b>：已执行，探测 {0} 个组合，占用 {1} 个，耗时 {2} 毫秒。</li>",
              "<li><b>Engine A (occupancy scan)</b>: executed, probed {0} combinations, {1} occupied, "
              + "took {2} ms.</li>");
            A("rep.engines.b",
              "<li><b>引擎 B（按键归属）</b>：已实现，但按设计只在图形界面「按键归属」页显式点「开始监听」时安装钩子。"
              + "本次结果见下方 <a href=\"#attribution\">按键归属</a> 区块。</li>",
              "<li><b>Engine B (key attribution)</b>: implemented, but by design it installs a hook only when "
              + "you explicitly click Listen on the Attribution page. See the "
              + "<a href=\"#attribution\">Key attribution</a> section below for this run's result.</li>");
            A("rep.engines.c",
              "<li><b>引擎 C（钩子窃取检测）</b>：已实现，只在「钩子检测」页显式点「布防」时安装 "
              + "WH_KEYBOARD_LL。本次结果见下方 <a href=\"#hookcheck\">钩子检测结论</a> 区块。</li>",
              "<li><b>Engine C (hook interception detection)</b>: implemented; installs WH_KEYBOARD_LL only "
              + "when you explicitly click Arm on the Hook check page. See the "
              + "<a href=\"#hookcheck\">Hook-check verdict</a> section below.</li>");
            A("rep.engines.d",
              "<li><b>引擎 D（配置聚合）</b>：已执行，从 {0} 个配置文件里解析出 {1} 条声明，"
              + "另有 {2} 条 Windows 系统保留提示。</li>",
              "<li><b>Engine D (config aggregation)</b>: executed, parsed {1} declaration(s) from {0} config "
              + "file(s), plus {2} Windows system-reserved hint(s).</li>");

            A("rep.occupancy.h2", "占用地图（引擎 A 实测）", "Occupancy map (engine A, measured)");
            A("rep.occupancy.hint",
              "探测方法：对每个「修饰键组合 × 按键」调用 RegisterHotKey，成功立即注销（记为空闲），"
              + "失败且错误码为 1409（ERROR_HOTKEY_ALREADY_REGISTERED）记为已占用。",
              "Method: for each \"modifier x key\" call RegisterHotKey; on success unregister immediately "
              + "(record as free); on failure with error 1409 (ERROR_HOTKEY_ALREADY_REGISTERED) record as occupied.");
            A("rep.occupancy.search", "搜索：", "Search: ");
            A("rep.occupancy.searchPh", "例如 Ctrl+Alt 或 Ctrl+Alt+M", "e.g. Ctrl+Alt or Ctrl+Alt+M");
            A("rep.occupancy.onlyOccupied", "只看已占用", "Occupied only");
            A("rep.state.occupied", "已占用", "Occupied");
            A("rep.state.free", "空闲", "Free");
            A("rep.occupancy.systemReserved", "系统保留：", "System reserved: ");
            A("rep.occupancy.noneOwner", "（无）", "(none)");

            A("rep.truth.h2", "配置真相表（A × D）", "Config truth table (A x D)");
            A("rep.truth.hint",
              "底表是引擎 A 实测的全部组合，逐条对引擎 D 的声明做精确匹配（额外做一次忽略 Shift 的模糊匹配，"
              + "结论写在备注里）。",
              "The base table is every combination measured by engine A, matched item-by-item against "
              + "engine D's declarations (an extra Shift-insensitive fuzzy match is noted in the remarks).");
            A("rep.truth.emptyKind", "这一类没有条目。", "No entries in this category.");
            A("rep.truth.sampled", "共 {0} 条，下面按顺序抽样展示前 {1} 条（报告体积考虑）。",
              "{0} in total; the first {1} are sampled below in order (to keep the report small).");
            A("rep.truth.count", "（{0} 条）", " ({0})");

            A("rep.attribution.h2", "按键归属（引擎 B，本次会话）", "Key attribution (engine B, this session)");
            A("rep.attribution.empty",
              "本次未采集按键归属。归属需要在图形界面的「按键归属」页点「开始监听」后，按下想排查的热键才会记录；"
              + "或使用命令行 <code>HotkeyHound.exe --listen [--seconds N]</code>。"
              + "按设计，监听只在用户显式点击时安装全局钩子；本报告若来自 <code>--scan</code>，该区块为空属于正常现象。",
              "No key attribution was collected this run. Attribution is recorded only after you click "
              + "Listen on the Attribution page and press the hotkey you want to investigate, or via "
              + "<code>HotkeyHound.exe --listen [--seconds N]</code>. By design the listener installs a "
              + "global hook only on an explicit click; if this report came from <code>--scan</code>, an "
              + "empty section is expected.");
            A("rep.attribution.hint",
              "下面每一行表示「这个组合键被哪个进程持有」，由注入的 hook DLL 拦截 WM_HOTKEY 消息得到。"
              + "记录只在开启监听后的本次会话内有效。",
              "Each row below means \"this combination is held by which process\", captured by the injected "
              + "hook DLL intercepting WM_HOTKEY messages. Records are valid only within this listening session.");
            A("rep.attribution.pathMissing", "（拿不到路径，可能权限不足或进程已退出）",
              "(path unavailable; permissions may be insufficient or the process exited)");

            A("rep.hookcheck.h2", "钩子检测结论（引擎 C，本次会话）", "Hook-check verdict (engine C, this session)");
            A("rep.hookcheck.empty",
              "本次未采集钩子检测结论。该结论来自图形界面「钩子检测」页：点「布防」选定一个组合键后，"
              + "在判定窗口内按下它，才能判断按键是否被更上层的钩子吞掉。"
              + "该页只做记录与判定，绝不结束或暂停任何进程。若本报告来自 <code>--scan</code>，该区块为空属于正常现象。",
              "No hook-check verdict was collected this run. The verdict comes from the Hook check page: "
              + "click Arm for a combination, then press it within the window to judge whether the key was "
              + "swallowed by an upstream hook. That page only records and judges; it never ends or pauses "
              + "any process. If this report came from <code>--scan</code>, an empty section is expected.");
            A("rep.hookcheck.hint",
              "判定依赖本程序安装的 <code>WH_KEYBOARD_LL</code> 低层键盘钩子；它只观察按键，不吞键、不改键。"
              + "结论：被上层钩子吞掉 / 疑似被钩子截胡 / 正常送达 / 不是全局热键；前提不足（引擎 B 未监听、未扫描、时间窗内没按）时给「无法判定」。",
              "The verdict relies on this program's <code>WH_KEYBOARD_LL</code> low-level keyboard hook; "
              + "it only observes keys, never swallows or alters them. Verdicts: swallowed upstream / "
              + "likely intercepted / delivered normally / not a global hotkey; when a prerequisite is missing "
              + "(engine B not listening, no scan, or the key was not pressed in the window) it reports Inconclusive.");
            A("rep.hookcheck.compareH3", "对照排除记录", "Comparison records");
            A("rep.hookcheck.compareHint",
              "这些快照是用户在界面上点「记录本次对照」时留下的结论与候选进程，用于「退出可疑软件前后」逐条对比。"
              + "工具只记录，不会结束任何进程。",
              "These snapshots are verdicts and candidate processes saved when the user clicked Record "
              + "comparison in the UI, for item-by-item comparison before/after exiting suspect software. "
              + "The tool only records; it never ends any process.");

            A("rep.declared.h2", "配置声明清单（引擎 D）", "Config declarations (engine D)");
            A("rep.declared.empty", "本次没有从任何已知软件里解析出热键声明。",
              "No hotkey declarations were parsed from any known software this run.");
            A("rep.declared.count", "（{0} 条）", " ({0})");
            A("rep.declared.module", "模块", "Module");
            A("rep.declared.purpose", "用途", "Purpose");
            A("rep.declared.raw", "原始值", "Raw value");
            A("rep.declared.source", "来源文件", "Source file");

            A("rep.advice.h2", "建议", "Advice");
            A("rep.advice.km",
              "<li><b>PowerToys 键盘管理器</b>截走了 <b>{0}</b> 个组合：{1}。"
              + "键盘管理器用的是底层键盘钩子，它会在按键到达其它程序之前改键或启动程序；"
              + "如果别处也想用这些组合，几乎必然是这里先赢。确认不需要就删掉对应的那条重映射。</li>",
              "<li><b>PowerToys Keyboard Manager</b> has grabbed <b>{0}</b> combination(s): {1}. "
              + "Keyboard Manager uses a low-level keyboard hook and remaps keys or launches programs before "
              + "the key reaches other applications; if something else also wants these combinations, this "
              + "almost certainly wins first. If you do not need them, delete the corresponding remapping.</li>");
            A("rep.advice.peek",
              "<li><b>PowerToys Peek</b> uses <b>{0}</b> combination(s): {1}. "
              + "This key often conflicts with the IME's language switch and IDE completion; {2}</li>",
              "<li><b>PowerToys Peek</b> uses <b>{0}</b> combination(s): {1}. "
              + "This key often conflicts with the IME language switch and IDE completion; {2}</li>");
            A("rep.advice.peekOccupied",
              "实测确实被占用。", "it is indeed occupied in the measurement.");
            A("rep.advice.peekNotReg",
              "当前没有注册成功，说明 Peek 未运行或改用钩子实现，风险仍在。",
              "it did not register successfully now, so Peek is either not running or uses a hook; the risk remains.");
            A("rep.advice.unowned",
              "<div class=\"warn\">有 <b>{0}</b> 个组合被占用但找不到任何声明。"
              + "这些多半来自配置不可读的程序（输入法、IM、驱动工具，例如 NVIDIA 叠加层自带的热键），"
              + "也可能是被系统保留。想知道「按下去到底是谁响应」，可用「按键归属」页（引擎 B）监听，"
              + "或用「钩子检测」页（引擎 C）判断按键是否被更上层的钩子吞掉。</div>",
              "<div class=\"warn\">{0} combination(s) are occupied but have no declaration. "
              + "Most come from software whose config is unreadable (IME, IM, driver tools, e.g. NVIDIA overlay "
              + "hotkeys), or may be system-reserved. To find out \"who actually responds when pressed\", use the "
              + "Attribution page (engine B) or the Hook check page (engine C) to see if a hook swallows the key.</div>");
            A("rep.advice.declaredNotReg",
              "<div class=\"warn\">有 <b>{0}</b> 个组合在配置里有声明，但没有注册到系统里。"
              + "典型原因：该软件当前没运行，或它用键盘钩子实现热键（键盘钩子不占 RegisterHotKey）。"
              + "所以「扫描说空闲」并不等于「按下去没人响应」。</div>",
              "<div class=\"warn\">{0} combination(s) are declared in config but not registered in the system. "
              + "Typical reasons: the software is not running, or it implements the hotkey with a keyboard hook "
              + "(hooks do not use RegisterHotKey). So \"the scan says free\" does not mean \"nothing responds "
              + "when pressed\".</div>");
            A("rep.advice.freeOk1",
              "<div class=\"ok\">有 <b>{0}</b> 个组合既没被占用也没人声明，可以直接拿来用。"
              + "<code>Ctrl+Alt+F1..F24</code>、<code>Win+Ctrl+Alt+F1..F24</code> 这类组合几乎总是安全的",
              "<div class=\"ok\">{0} combination(s) are neither occupied nor declared and can be used directly. "
              + "Combinations like <code>Ctrl+Alt+F1..F24</code> and <code>Win+Ctrl+Alt+F1..F24</code> are "
              + "almost always safe");
            A("rep.advice.freeOk2", "，本次实测空闲的例如 {0}。", "; free ones measured this run include {0}.");
            A("rep.advice.freeOkEnd", "。", ".");
            A("rep.advice.none", "<div class=\"hint\">本次没有发现值得特别提示的冲突。</div>",
              "<div class=\"hint\">No conflicts worth special mention were found this run.</div>");

            A("rep.method.h2", "方法与局限", "Method and limitations");
            A("rep.method.howA", "引擎 A 怎么判断「占用」", "How engine A decides \"occupied\"");
            A("rep.method.howA.body",
              "<p>对每一个「修饰键组合 × 按键」调用 Win32 的 <code>RegisterHotKey</code>："
              + "如果返回成功，说明这个组合没人占用，我们立刻 <code>UnregisterHotKey</code> 注销并记为「空闲」；"
              + "如果失败且错误码是 1409，说明系统里已经有人注册了同一个组合，记为「已占用」。</p>"
              + "<p>本工具不写任何配置、不改注册表，注册动作全部在同一个瞬间被撤销，因此扫描本身没有副作用。</p>",
              "<p>For each \"modifier combination x key\" it calls the Win32 <code>RegisterHotKey</code>: on "
              + "success, the combination is unowned, so it immediately calls <code>UnregisterHotKey</code> and "
              + "records it as free; on failure with error 1409, someone has already registered the same "
              + "combination, so it is recorded as occupied.</p>"
              + "<p>The tool writes no config and does not touch the registry; every registration is undone at "
              + "the same instant, so the scan itself has no side effects.</p>");
            A("rep.method.why.h", "为什么「扫描说空闲」不代表按下去没人管",
              "Why \"the scan says free\" does not mean nobody handles it when pressed");
            A("rep.method.why.b1",
              "<li><b>键盘钩子型热键看不到。</b>PowerToys 键盘管理器、AutoHotkey、输入法、部分游戏工具用底层键盘钩子实现热键，"
              + "它们不调用 RegisterHotKey，所以引擎 A 会把键位报成「空闲」，实际上按下后会被它们抢先处理。</li>",
              "<li><b>Keyboard-hook hotkeys are invisible.</b> PowerToys Keyboard Manager, AutoHotkey, IMEs and some "
              + "game tools implement hotkeys with low-level keyboard hooks; they do not call RegisterHotKey, so "
              + "engine A reports the key as \"free\" even though they grab it first when pressed.</li>");
            A("rep.method.why.b2",
              "<li><b>只有本会话可见。</b>RegisterHotKey 是会话级/窗口站级的，别的登录会话注册的热键这里探测不到。</li>",
              "<li><b>Only this session is visible.</b> RegisterHotKey is session/window-station scoped; hotkeys "
              + "registered in other login sessions cannot be probe here.</li>");
            A("rep.method.why.b3",
              "<li><b>只是「注册失败」不等于「软件占用」。</b>少数组合（如部分面板功能键）会返回 1409 之外的错误码，"
              + "报告里会把错误码一并列出，请按备注分辨。</li>",
              "<li><b>\"Registration failed\" is not the same as \"software occupancy\".</b> A few combinations "
              + "(e.g. some panel function keys) return errors other than 1409; the report lists the error code, "
              + "so read the remarks carefully.</li>");
            A("rep.method.miss.h", "为什么可能漏掉某些进程",
              "Why some processes may be missed");
            A("rep.method.miss.b1",
              "<li><b>32 位进程需要额外的 32 位宿主。</b>Windows 只允许钩子注入与安装者同位数的进程，"
              + "因此 32 位进程的归属由本程序自动拉起的 32 位伴随宿主覆盖；如果它启动失败"
              + "（日志里会写明原因），这部分归属就会缺失。归属表里每行都标了目标进程的位数。</li>",
              "<li><b>32-bit processes need an extra 32-bit host.</b> Windows only lets a hook inject into "
              + "processes of the same bitness, so attribution for 32-bit processes comes from a 32-bit "
              + "companion process this program starts automatically; if it fails to start (the log says "
              + "why), that coverage is missing. Every attribution row labels the target process's bitness.</li>");
            A("rep.method.miss.b2",
              "<li>非管理员运行时，针对提权进程（例如以管理员身份运行的 PowerToys）拿不到进程信息。",
              "<li>When not run as administrator, process info for elevated processes (e.g. PowerToys running as "
              + "administrator) cannot be obtained.");
            A("rep.method.miss.b2this", "<b>本次就是以非管理员身份运行的。</b>",
              "<b>This run was executed as a non-administrator.</b>");
            A("rep.method.miss.b3",
              "<li><b>本次以{0}运行</b>（exe 位于带 Low 标签的目录）。UIPI 会拦住钩子注入，引擎 B/C 基本失效，"
              + "请把 exe 复制到普通目录（例如 <code>C:\\Tools\\</code>）后再运行。</li>",
              "<li><b>This run was executed at {0}</b> (the exe is in a directory carrying the Low label). UIPI "
              + "blocks hook injection, so engines B/C are largely useless; copy the exe to an ordinary directory "
              + "(e.g. <code>C:\\Tools\\</code>) and run it from there.</li>");
            A("rep.method.decl.h", "配置声明的解读注意", "Reading config declarations");
            A("rep.method.decl.b1",
              "<li>声明来自配置文件，反映的是「软件打算用这个键」，不等于「此刻真的生效」——软件没运行、模块被禁用、"
              + "或者同一软件内部的其它设置覆盖了它，都可能让声明落空。</li>",
              "<li>Declarations come from config files and reflect \"the software intends to use this key\", not "
              + "\"it takes effect right now\" — the software being stopped, the module being disabled, or another "
              + "setting in the same software overriding it can all invalidate a declaration.</li>");
            A("rep.method.decl.b2",
              "<li>标着 <code>[已禁用]</code> 的条目属于当前被关掉的模块，通常不会真的占用键位。</li>",
              "<li>Entries marked <code>[disabled]</code> belong to currently disabled modules and usually do not "
              + "occupy the key.</li>");
            A("rep.method.decl.b3",
              "<li>MSI Afterburner 的热键编码格式未在本机验证（本机所有项都是「未设置」），Everything 的"
              + "「修饰符:VK」十进制编码同理——本机三项都写着 0，两种口径都需要人工核对。</li>",
              "<li>MSI Afterburner's hotkey encoding was not verified on this machine (all entries are \"not set\"), "
              + "and so is Everything's \"modifier:VK\" decimal encoding — all three entries here are 0, so both "
              + "formats need a manual check.</li>");
            A("rep.method.log.h", "本次运行日志", "Run log");
            A("rep.footer", "{0} {1} · 报告语言：{2} · 由 HotkeyHound 生成",
              "{0} {1} · Report language: {2} · Generated by HotkeyHound");

            A("rep.js.shown", "显示 {0} / {1} 条", "Showing {0} / {1}");
            A("rep.list.more", " 等", " and more");
        }

        private static void BuildUi()
        {
            A("ui.title", "{0} —— {1}", "{0} — {1}");
            A("ui.log.group", "日志", "Log");
            A("ui.status.ready", "就绪", "Ready");
            A("ui.tab.overview", "扫描总览", "Scan overview");
            A("ui.tab.attribution", "按键归属", "Key attribution");
            A("ui.tab.hookcheck", "钩子检测", "Hook detection");
            A("ui.tab.truth", "配置真相表", "Config truth table");
            A("ui.nav.truth", "配置真相表", "Truth table");
            A("ui.tab.report", "报告", "Report");
            A("ui.tab.about", "关于", "About");
            A("ui.ab.heading", "关于 HotkeyHound", "About HotkeyHound");
            A("ui.ab.license", "许可证：MIT © 2026 Conste11ation117　·　免安装、无网络、无遥测。",
              "License: MIT (c) 2026 Conste11ation117 - no installer, no network, no telemetry.");
            A("ui.ab.hint", "完整帮助：按 F1 或点右上角「帮助」按钮。",
              "Full help: press F1 or click the Help button at the top right.");
            A("ui.ab.intro",
              "这是一个 Windows 全局快捷键冲突排查器。它回答三个 Windows 自己不肯回答的问题："
              + "哪些组合键已经被占用、是被谁占用的、以及你按下去为什么毫无反应。",
              "A global hotkey conflict troubleshooter for Windows. It answers three questions Windows "
              + "itself won't: which combinations are already taken, who took them, and why a key press "
              + "does nothing at all.");
            A("ui.ab.f1", "四个引擎：A 占用扫描、B 按键归属、C 钩子窃取检测、D 配置聚合。",
              "Four engines: A occupancy scan, B press attribution, C hook-swallow detection, D config aggregation.");
            A("ui.ab.f2", "A 和 D 只读，不需要管理员，也从不写任何配置或注册表。",
              "A and D are read-only, need no administrator, and never write any config or registry.");
            A("ui.ab.f3", "B 和 C 会安装键盘钩子，但只在你点「开始监听」/「布防」/「开始监视」之后；钩子只观察，绝不吞键、不改键。",
              "B and C install keyboard hooks only after you click Start listening / Arm / Start monitoring; the hooks observe and never swallow or alter keys.");
            A("ui.ab.f4", "x64 与 32 位进程的归属都已覆盖，已在真机验证；提权进程需以管理员身份运行才看得到。",
              "Attribution covers both x64 and 32-bit processes, verified on real machines; elevated processes need running as administrator.");
            A("ui.ab.f5", "全程无网络、无遥测、不上传任何东西。",
              "No network, no telemetry, nothing is ever uploaded.");
            A("ui.ab.env", "运行框架：{0}　·　机器：{1}　·　用户：{2}　·　管理员：{3}",
              "Runtime: {0}  ·  Machine: {1}  ·  User: {2}  ·  Administrator: {3}");
            A("ui.ab.settings", "语言与主题", "Language & theme");
            A("ui.help.button", "帮助", "Help");
            A("ui.crash.body",
              "程序遇到未处理的错误。\r\n\r\n{0}\r\n\r\n完整日志已写入：\r\n{1}\r\n\r\n"
              + "你可以把该日志附在问题反馈里；程序会尽量保持运行，必要时请重新启动。",
              "HotkeyHound hit an unexpected error.\r\n\r\n{0}\r\n\r\nFull details were written to:\r\n{1}\r\n\r\n"
              + "You can attach that log to a bug report. The app will keep running when possible; restart it if needed.");
            A("ui.log.helpFailed", "打开帮助窗口失败：{0}", "Failed to open the help window: {0}");
            A("ui.banner.prefix", "⚠ ", "⚠ ");
            A("ui.banner.low",
              "低完整性运行（{0}）：UIPI 会拦住钩子注入，引擎 B/C 基本失效，写 %TEMP%/%LOCALAPPDATA% 也会被拒。"
              + "请把 exe 复制到普通目录（如 C:\\Tools\\）后再运行。",
              "Running at low integrity ({0}): UIPI blocks hook injection, engines B/C are largely useless, and "
              + "writes to %TEMP%/%LOCALAPPDATA% are denied. Copy the exe to an ordinary directory "
              + "(e.g. C:\\Tools\\) and run it from there.");
            A("ui.banner.notAdmin",
              "非管理员：看不到提权进程（例如 PowerToys）。",
              "Not an administrator: elevated processes (e.g. PowerToys) are invisible.");
            A("ui.banner.elevate", "提权重启", "Relaunch elevated");
            A("ui.log.started", "HotkeyHound 已启动，版本 {0}", "HotkeyHound started, version {0}");
            A("ui.log.admin", "管理员权限：{0}", "Administrator: {0}");
            A("ui.log.adminYes", "有", "yes");
            A("ui.log.adminNo", "无（部分提权进程不可见）", "no (some elevated processes are invisible)");
            A("ui.log.howto",
              "点击“开始扫描”开始。",
              "Click Start scan to begin.");
            A("ui.restart.title", "{0} —— 提权重启", "{0} — relaunch elevated");
            A("ui.restart.failed", "以管理员身份重启失败：{0}", "Relaunch as administrator failed: {0}");
            A("ui.restart.cancelled", "已取消提权（你在 UAC 提示里点了「否」）。",
              "Elevation cancelled (you chose No in the UAC prompt).");
            A("ui.restart.ok", "已请求提权重启，本窗口即将关闭。", "Elevation requested; this window will now close.");
            A("ui.restart.handoff", "已启动提权实例，本窗口正在关闭并完成交接…",
              "Elevated instance launched; this window is closing to hand over…");

            // Overview
            A("ui.ov.scan", "开始扫描", "Start scan");
            A("ui.ov.report", "生成 HTML 报告", "Generate HTML report");
            A("ui.ov.elevate", "以管理员身份重启", "Relaunch as administrator");
            A("ui.ov.notScanned", "尚未扫描", "Not scanned yet");
            A("ui.ov.card.occupied", "占用组合", "Occupied");
            A("ui.ov.card.free", "空闲组合", "Free");
            A("ui.ov.card.declared", "配置声明数", "Declarations");
            A("ui.ov.card.truth", "真相表", "Truth rows");
            A("ui.ov.hint",
              "逐个试探组合键，成功即注销，不修改任何设置。",
              "Probes each combination and unregisters on success; nothing is modified.");
            A("ui.ov.missHint",
              "扫描扫不干净：只覆盖探测网格内的组合，没列出的组合不代表一定空闲。",
              "The scan is not exhaustive: it only covers combinations in the probe grid, so unlisted ones are not guaranteed free.");
            A("ui.col.combo", "组合键", "Combination");
            A("ui.col.state", "状态", "State");
            A("ui.col.owner", "归属 / 声明来源", "Owner / declaration");
            A("ui.col.evidence", "证据", "Evidence");
            A("ui.col.note", "备注", "Remark");
            A("ui.col.path", "进程路径", "Process path");
            A("ui.ov.owner.none", "（未找到声明）", "(no declaration found)");
            A("ui.ov.owner.reserved", "系统保留：{0}", "System reserved: {0}");
            A("ui.ov.scanning", "正在扫描…", "Scanning…");
            A("ui.ov.progress", "正在扫描：{0} / {1}", "Scanning: {0} / {1}");
            A("ui.ov.scanFailed", "扫描失败：{0}", "Scan failed: {0}");
            A("ui.ov.scanFailedDetail", "扫描失败：{0}\r\n\r\n详细信息见底部日志区。",
              "Scan failed: {0}\r\n\r\nSee the log panel at the bottom for details.");
            A("ui.ov.scanDone", "扫描完成，耗时 {0} 毫秒", "Scan complete, took {0} ms");
            A("ui.ov.saveTitle", "保存 HTML 报告", "Save HTML report");
            A("ui.ov.saveFilter", "HTML 文件 (*.html)|*.html", "HTML files (*.html)|*.html");
            A("ui.ov.reportSaved", "报告已生成：\r\n{0}\r\n\r\n现在打开吗？",
              "Report generated:\r\n{0}\r\n\r\nOpen it now?");
            A("ui.ov.reportWriteFailed", "报告写入失败：{0}", "Failed to write report: {0}");
            A("ui.ov.openFailed", "无法打开：{0}\r\n{1}", "Cannot open: {0}\r\n{1}");
            A("ui.ov.ownerTitle", "归属 / 声明来源", "Owner / declaration");

            // Truth
            A("ui.truth.filter.all", "全部（有声明或有占用）", "All (declared or occupied)");
            A("ui.truth.category", "类别：", "Category: ");
            A("ui.truth.explain",
              "双击一行可打开它的来源配置文件。",
              "Double-click a row to open its source config file.");
            A("ui.col.type", "类别", "Category");
            A("ui.col.declaredBy", "声明来源", "Declared by");
            A("ui.col.ownerHint", "已知归属提示", "Known owner hint");
            A("ui.truth.notScanned", "尚未扫描", "Not scanned yet");
            A("ui.truth.sample", "显示 {0} / {1} 条（抽样上限 300）", "Showing {0} / {1} (sampling cap 300)");
            A("ui.truth.count", "共 {0} 条", "{0} in total");
            A("ui.truth.noSource", "这一条没有对应的来源配置文件。\r\n\r\n{0}",
              "This row has no corresponding source config file.\r\n\r\n{0}");
            A("ui.truth.dlgTitle", "配置真相表", "Config truth table");

            // Attribution
            A("ui.at.title", "按键归属", "Key attribution");
            A("ui.at.hint",
              "点“开始监听”后安装全局消息钩子，点“停止监听”立即卸载。按热键后，注入的 DLL 回报“持有者 PID + 组合键”。",
              "Start listening installs a global message hook; Stop listening unloads it. After a key press, "
              + "the injected DLL reports \"owner PID + combination\".");
            A("ui.at.missHint",
              "监听不到时，可能是被低层钩子截走了；用“钩子检测”复测。",
              "If nothing is reported, the key may be swallowed by a low-level hook; re-test it with Hook detection.");
            A("ui.at.start", "开始监听（安装钩子）", "Start listening (installs hook)");
            A("ui.at.stop", "停止监听（卸载钩子）", "Stop listening (unload hook)");
            A("ui.at.clear", "清空", "Clear");
            A("ui.at.copy", "复制到剪贴板", "Copy to clipboard");
            A("ui.at.notStarted", "未开始监听。", "Not listening.");
            A("ui.at.warn.low",
              "⚠ 当前以{0}运行，UIPI 会拦住钩子注入，看不到任何进程。请把 exe 复制到普通目录再运行。",
              "⚠ Running at {0}; UIPI blocks hook injection, so no process is visible. Copy the exe to an "
              + "ordinary directory and run it from there.");
            A("ui.at.warn.notAdmin",
              "⚠ 非管理员：看不到提权进程（如以管理员运行的 PowerToys）。建议「以管理员身份重启」。",
              "⚠ Not an administrator: elevated processes (e.g. PowerToys run as admin) are invisible. Relaunch "
              + "as administrator.");
            A("ui.at.warn.ok",
              "运行环境正常：管理员 + 中/高完整性，提权进程也应可见。",
              "Environment is normal: administrator + medium/high integrity; elevated processes should be visible.");
            A("ui.at.col.time", "时间", "Time");
            A("ui.at.col.process", "进程名", "Process");
            A("ui.at.col.pid", "PID", "PID");
            A("ui.at.col.elevated", "是否提权", "Elevated");
            A("ui.at.unknownPath", "(拿不到路径)", "(no path)");
            A("ui.at.captured", "已捕获 {0} 条归属记录。", "Captured {0} attribution record(s).");
            A("ui.at.installing", "正在安装钩子…", "Installing hook…");
            A("ui.at.stopped", "已停止监听，钩子已卸载。", "Stopped listening; hook unloaded.");
            A("ui.at.listening", "正在监听…请按下要排查的全局热键。",
              "Listening… press the global hotkey you want to investigate.");
            A("ui.at.startFailed", "启动失败：{0}", "Start failed: {0}");
            A("ui.at.startFailedDetail",
              "无法安装全局钩子：\r\n{0}\r\n\r\n常见原因：程序以低完整性运行（exe 在带 Low 标签的目录），"
              + "UIPI 拦住了注入；或当前不是管理员，看不到提权进程。\r\n详见底部日志。",
              "Cannot install the global hook:\r\n{0}\r\n\r\nCommon reasons: the program runs at low integrity "
              + "(exe in a directory carrying the Low label) and UIPI blocked injection; or you are not an "
              + "administrator and elevated processes are invisible.\r\nSee the log at the bottom.");
            A("ui.at.cleared", "已清空本页结果与本次会话归属记录。",
              "Cleared this page's results and this session's attribution records.");
            A("ui.at.copyHeader", "时间\t进程名\tPID\t组合键\t进程路径\t是否提权",
              "Time\tProcess\tPID\tCombination\tPath\tElevated");
            A("ui.at.noRecords", "(无记录)", "(no records)");
            A("ui.at.copied", "已复制 {0} 条记录到剪贴板。", "Copied {0} record(s) to the clipboard.");
            A("ui.at.copyFailed", "复制失败：{0}", "Copy failed: {0}");

            // Hook check
            A("ui.hk.title", "钩子检测", "Hook detection");
            A("ui.hk.guide",
              "填好组合键，点“布防”，然后在时间窗内按一次该键。时间窗内没按键，结论是“无法判定”，不会判成被吞。",
              "Enter a combination, click Arm, then press it once inside the time window. If nothing is pressed, "
              + "the verdict is Inconclusive — not \"swallowed\".");
            A("ui.hk.capture", "捕获组合键", "Capture combination");
            A("ui.hk.capturing", "等待按键：按一次要检测的组合键。", "Waiting for a key: press the combination to test.");
            A("ui.hk.captured", "已捕获：{0}", "Captured: {0}");
            A("ui.hk.captureFailed", "无法开始组合键捕获：{0}", "Cannot start combination capture: {0}");
            A("ui.hk.arm", "布防（安装钩子）", "Arm (install hook)");
            A("ui.hk.stop", "停止（卸载钩子）", "Stop (unload hook)");
            A("ui.hk.notArmed", "低层键盘钩子未安装。", "Low-level keyboard hook not installed.");
            A("ui.hk.warn",
              "只观察按键，不拦截按键，也不结束或暂停任何进程。",
              "It only observes keys; it never intercepts them and never ends or pauses any process.");
            A("ui.hk.col.verdict", "判定", "Verdict");
            A("ui.hk.col.note", "说明", "Explanation");
            A("ui.hk.candTitle", "候选进程（运行中）", "Candidate processes");
            A("ui.hk.refresh", "刷新候选进程", "Refresh candidates");
            A("ui.hk.steps",
              "对照排除：\r\n"
              + "1. 记录当前结论；\r\n"
              + "2. 手动退出一个可疑软件；\r\n"
              + "3. 用同一组合键重测并记录；\r\n"
              + "4. 对比两次结论。",
              "Comparison workflow:\r\n"
              + "1. Record the current verdict;\r\n"
              + "2. Manually exit one suspect process;\r\n"
              + "3. Re-test the same combination and record again;\r\n"
              + "4. Compare the two verdicts.");
            A("ui.hk.record", "记录本次对照", "Record comparison");
            A("ui.hk.parseFail", "无法解析这个组合键。示例：Ctrl+Alt+M、Win+Shift+F23。",
              "Cannot parse this combination. Examples: Ctrl+Alt+M, Win+Shift+F23.");
            A("ui.hk.needB.title", "需要引擎 B 配合", "Engine B is required for a full verdict");
            A("ui.hk.needB.body",
              "引擎 C 要区分「正常送达」和「被钩子截胡」，必须让引擎 B（按键归属）同时监听；"
              + "它不监听时，本次布防的结论只会诚实地标为「无法判定」。\r\n\r\n"
              + "是否现在一并启动引擎 B？\r\n"
              + "（启动后它会把上报 DLL 注入当前登录会话的进程——这是归属判定的工作原理；"
              + "选「否」也可以继续布防。）",
              "To tell \"delivered normally\" from \"intercepted by a hook\", engine C needs engine B (attribution) "
              + "listening at the same time; without it, verdicts of this probe can only honestly be "
              + "\"inconclusive\".\r\n\r\nStart engine B now as well?\r\n"
              + "(Once started it injects its report DLL into the processes of this logon session — that is how "
              + "attribution works. Choosing No still arms the probe.)");
            A("ui.hk.engineB.starting", "正在启动引擎 B（按键归属）…", "Starting engine B (attribution)…");
            A("ui.hk.engineB.failed", "引擎 B 启动失败：{0}（本次布防的截胡判定将标为「无法判定」）",
              "Engine B failed to start: {0} (interception verdicts of this probe will be marked inconclusive)");
            A("ui.hk.engineB.coStarted", "已联动启动引擎 B。「停止（卸载钩子）」不会停掉它；需要时到「按键归属」页停止。",
              "Engine B has been started together. Stop (unload hook) will not stop it; use the Attribution page when needed.");
            A("ui.hk.arming", "正在布防…", "Arming…");
            A("ui.hk.comboCaption", "组合键：", "Combination: ");
            A("ui.hk.installing", "正在安装低层键盘钩子…", "Installing low-level keyboard hook…");
            A("ui.hk.installFail", "钩子安装失败：{0}", "Failed to install hook: {0}");
            A("ui.hk.installFailDlg", "无法安装 WH_KEYBOARD_LL 钩子：\r\n{0}",
              "Cannot install the WH_KEYBOARD_LL hook:\r\n{0}");
            A("ui.hk.armFail", "布防失败：{0}", "Arming failed: {0}");
            A("ui.hk.armed", "已布防 {0}，请在 {1:0.0} 秒内按一次。",
              "Armed {0}; press it once within {1:0.0} s.");
            A("ui.hk.armedTick", "等待按键：{0}，剩余 {1:0.0} 秒。",
              "Waiting for a keypress: {0}, {1:0.0} s left.");
            A("ui.hk.stopped", "已停止并卸载低层键盘钩子（UnhookWindowsHookEx）。若引擎 B 是被本页联动启动的，它仍在监听。",
              "Stopped and unloaded the low-level keyboard hook (UnhookWindowsHookEx). If engine B was co-started "
              + "from this page, it is still listening.");
            A("ui.hk.verdictPrefix", "结论：{0}", "Verdict: {0}");
            A("ui.hk.noCandidates", "(没有候选进程在运行)", "(no candidate processes running)");
            A("ui.hk.compare.label", "对照：{0}", "Comparison: {0}");
            A("ui.hk.compare.saved",
              "已记录本次对照快照（{0} 行）。\r\n"
              + "接下来请手动退出一个可疑软件，再用同一组合键测一次并再次记录，然后对比两次结论。\r\n"
              + "本工具不会结束或暂停任何进程。",
              "Recorded this comparison snapshot ({0} line(s)).\r\n"
              + "Next, manually exit one suspect process, re-test the same combination and record again, then "
              + "compare the two verdicts.\r\nThis tool never ends or pauses any process.");
            A("ui.hk.compare.failed", "记录对照失败：{0}", "Failed to record comparison: {0}");

            // Report & about
            A("ui.rt.heading", "报告", "Report");
            A("ui.rt.author", "作者：Conste11ation117　·　github.com/Conste11ation117",
              "Author: Conste11ation117  -  github.com/Conste11ation117");
            A("ui.rt.pathLabel", "报告路径：", "Report path: ");
            A("ui.rt.open", "打开报告", "Open report");
            A("ui.rt.generate", "生成报告", "Generate report");
            A("ui.rt.noReport", "报告文件还不存在，请先生成报告。",
              "The report file does not exist yet; generate it first.");
            A("ui.rt.notGenerated", "尚未生成报告。先扫描一次。",
              "No report generated yet. Run a scan first.");
            A("ui.rt.release",
              "{0} {1} —— {2}\r\n运行框架：.NET Framework {3}（Windows 自带，无需安装）\r\n"
              + "机器：{4}　用户：{5}　管理员：{6}",
              "{0} {1} — {2}\r\nRuntime: .NET Framework {3} (bundled with Windows, no install needed)\r\n"
              + "Machine: {4}  User: {5}  Admin: {6}");
            A("ui.rt.limits",
              "已知限制：\r\n"
              + "· 只有点“开始监听”“布防”“开始监视”时才安装钩子；打开页面不安装钩子。\r\n"
              + "· 用键盘钩子实现的热键不占 RegisterHotKey，引擎 A 会报成空闲，由引擎 D 的配置声明补充。\r\n"
              + "· 非管理员看不到提权进程归属；32 位进程由单独的 32 位宿主覆盖。\r\n"
              + "· 全程只读：不写配置、不改注册表，探测用的注册立刻注销。",
              "Known limitations:\r\n"
              + "· Hooks are installed only when you click Start listening, Arm, or Start monitoring; opening a "
              + "page installs nothing.\r\n"
              + "· Hotkeys implemented with keyboard hooks do not occupy RegisterHotKey, so engine A reports them "
              + "as free; engine D's declarations fill the gap.\r\n"
              + "· Without administrator rights, attribution for elevated processes is invisible; 32-bit processes "
              + "are covered by a separate 32-bit host.\r\n"
              + "· Read-only throughout: no config is written, no registry change, and probe registrations are "
              + "released immediately.");
            A("ui.rt.needScan", "还没有扫描结果，请先到「扫描总览」点「开始扫描」。",
              "No scan result yet; go to Scan overview and click Start scan first.");
            A("ui.rt.generated", "已生成：{0}（{1:N0} 字节）", "Generated: {0} ({1:N0} bytes)");
            A("ui.rt.writeFailed", "报告写入失败：{0}", "Failed to write report: {0}");
            A("ui.rt.langLabel", "语言：", "Language: ");
            A("ui.rt.langRestart", "语言已切换为 {0}，重启后生效。",
              "Language switched to {0}; restart to apply.");
            A("ui.rt.themeLabel", "主题：", "Theme: ");
            A("ui.rt.themeSystem", "跟随系统", "Follow system");
            A("ui.rt.themeLight", "浅色", "Light");
            A("ui.rt.themeDark", "深色", "Dark");
            A("ui.rt.themeRestart", "主题已切换为「{0}」，请重启程序后生效。",
              "Theme switched to \"{0}\"; restart the program for it to take effect.");
        }

        /// <summary>
        /// 按键监视的文案。判定逻辑在 KeyMonitorLogic 里，这里只放中英文字符串，
        /// 保证 CLI 与界面用同一套说法。
        /// </summary>
        private static void BuildKeyMonitor()
        {
            A("ui.tab.keymonitor", "按键监视", "Key monitor");

            A("km.layer.raw", "Raw Input", "Raw input");
            A("km.layer.hook", "低层钩子", "Low-level hook");
            A("km.layer.window", "窗口消息", "Window message");

            A("km.event.rawDown", "设备层收到按下（Raw Input）", "Press seen at the device layer (raw input)");
            A("km.event.rawUp", "设备层收到松开（Raw Input）", "Release seen at the device layer (raw input)");
            A("km.event.hookDown", "到达低层键盘钩子链", "Reached the low-level keyboard hook chain");
            A("km.event.hookUp", "低层钩子看到松开", "Release seen by the low-level hook");
            A("km.event.windowDown", "本程序窗口收到按下", "This program's window received the press");
            A("km.event.windowUp", "本程序窗口收到松开", "This program's window received the release");

            A("km.layers.value", "Raw Input：{0}　低层钩子：{1}　窗口消息：{2}",
              "Raw input: {0}  low-level hook: {1}  window message: {2}");
            A("km.layers.yes", "有", "yes");
            A("km.layers.no", "无", "no");

            A("km.verdict.unknown", "等待判定", "Waiting");
            A("km.verdict.neverReached", "设备层就没上来", "Never reached the system");
            A("km.verdict.beforeHook", "钩子链之前被吞", "Swallowed before the hook chain");
            A("km.verdict.afterHook", "钩子之后被截胡", "Swallowed after the hook chain");
            A("km.verdict.delivered", "已送达", "Delivered");
            A("km.verdict.inconclusive", "无法确定", "Inconclusive");

            A("km.decide.never.evidence", "Raw Input 没有看到这个键。",
              "Raw input never saw this key.");
            A("km.decide.never.note",
              "设备层没收到按键。检查键盘、接口，再看下方列出的键盘类驱动。",
              "The device layer did not receive the key. Check the keyboard and port, then review the "
              + "keyboard-class drivers below.");
            A("km.decide.before.evidence", "Raw Input 看到了，低层键盘钩子没看到。",
              "Raw input saw it; the low-level keyboard hook did not.");
            A("km.decide.before.note",
              "设备层收到了按键，低层钩子没收到：更上游的东西吞掉了它。",
              "The device layer received the key, but the low-level hook did not: something upstream swallowed it.");
            A("km.decide.delivered.evidence", "三层都看到了这个按键。",
              "All three layers saw this key.");
            A("km.decide.delivered.note",
              "三层都收到按键。功能仍然不对时，问题在接收按键的软件本身。",
              "All three layers received the key. If it still misbehaves, the problem is in the receiving software.");
            A("km.decide.foreground.evidence", "Raw Input 与低层钩子看到了，本程序窗口没有收到。",
              "Raw input and the low-level hook saw it; this program's window did not.");
            A("km.decide.foreground.note",
              "按键被送到当前前台窗口，本程序没有聚焦。保持本程序聚焦再按一次。",
              "The key went to the foreground window because this program was not focused. Keep this program "
              + "focused and press again.");
            A("km.decide.attributed.evidence", "按键到达钩子链，引擎 B 报出了持有者。",
              "The key reached the hook chain and engine B reported its owner.");
            A("km.decide.attributed.note",
              "持有者已收到这个全局热键。功能没触发时，检查该软件自身的设置。",
              "The owner received this global hotkey. If it did not fire, check that software's own settings.");
            A("km.decide.after.evidence", "按键到达钩子链，但全局热键的持有者没有收到归属报告。",
              "The key reached the hook chain, but the global hotkey's owner did not report receiving it.");
            A("km.decide.after.note",
              "钩子链收到按键，但持有者没有收到归属报告。可同时开启按键归属监听复测。",
              "The hook chain received the key, but the owner sent no attribution report. Re-test with key "
              + "attribution listening as well.");
            A("km.decide.inconclusive.evidence", "按键到达钩子链，但当前没有运行归属引擎。",
              "The key reached the hook chain, but the attribution engine is not running.");
            A("km.decide.inconclusive.note",
              "按键归属没有在监听，无法判断是否送达。开启监听后重测。",
              "Key attribution is not listening, so delivery cannot be judged. Start listening and re-test.");
            A("km.decide.owner", "持有者：{0}（PID {1}）。", "Owner: {0} (PID {1}).");

            A("km.mod.ok", "正常", "OK");
            A("km.mod.osDownHookMissed", "系统说按着，钩子没看到按下", "OS says down; hook saw no press");
            A("km.mod.hookDownOsMissed", "钩子看到按着，系统说已松开", "Hook says down; OS says released");

            A("km.driver.startBoot", "0 引导", "0 boot");
            A("km.driver.startSystem", "1 系统", "1 system");
            A("km.driver.startAuto", "2 自动", "2 auto");
            A("km.driver.startDemand", "3 按需", "3 demand");
            A("km.driver.startDisabled", "4 已禁用", "4 disabled");
            A("km.driver.startOther", "Start={0}", "Start={0}");

            A("km.error.timeout", "按键监视线程未能就绪（超时）", "The key monitor thread did not become ready (timeout)");
            A("km.error.noLayers", "Raw Input 与低层键盘钩子都没有安装成功", "Neither raw input nor the low-level keyboard hook could be installed");
            A("km.error.class", "注册 Raw Input 窗口类失败，错误码 {0}", "Failed to register the raw-input window class, error {0}");
            A("km.error.window", "创建 Raw Input 接收窗口失败，错误码 {0}", "Failed to create the raw-input window, error {0}");
            A("km.error.raw", "RegisterRawInputDevices 失败，错误码 {0}", "RegisterRawInputDevices failed, error {0}");
            A("km.error.hook", "SetWindowsHookEx(WH_KEYBOARD_LL) 失败，错误码 {0}",
              "SetWindowsHookEx(WH_KEYBOARD_LL) failed, error {0}");
            A("km.error.thread", "按键监视线程异常：{0}", "Key monitor thread exception: {0}");
            A("km.error.stopTimeout", "警告：按键监视线程在 4 秒内没有退出", "Warning: the key monitor thread did not exit within 4 seconds");
            A("km.error.attach", "挂接窗口消息失败：{0}", "Failed to attach to window messages: {0}");
            A("km.log.rawReady", "Raw Input 已注册（窗口 0x{0}，RIDEV_INPUTSINK）",
              "Raw input registered (window 0x{0}, RIDEV_INPUTSINK)");
            A("km.log.hookReady", "WH_KEYBOARD_LL 已安装（句柄 0x{0}）", "WH_KEYBOARD_LL installed (handle 0x{0})");
            A("km.log.hookUnloaded", "已 UnhookWindowsHookEx（WH_KEYBOARD_LL，句柄 0x{0}）",
              "Called UnhookWindowsHookEx (WH_KEYBOARD_LL, handle 0x{0})");
            A("km.log.verdict", "{0} → {1}", "{0} -> {1}");

            A("ui.km.title", "按键监视", "Key monitor");
            A("ui.km.hint",
              "同时观察三层：Raw Input、低层钩子、窗口消息（仅本程序聚焦时）。",
              "Observes three layers: raw input, low-level hook, and window messages (only while this window is focused).");
            A("ui.km.start", "开始监视（安装键盘钩子）", "Start monitoring (installs keyboard hook)");
            A("ui.km.stop", "停止监视（卸载钩子）", "Stop monitoring (unload hook)");
            A("ui.km.clear", "清空", "Clear");
            A("ui.km.refresh", "刷新只读信息", "Refresh read-only info");
            A("ui.km.notStarted", "未开始监视。", "Not monitoring.");
            A("ui.km.starting", "正在安装键盘钩子…", "Installing keyboard hook…");
            A("ui.km.running", "监视中：Raw Input {0}，低层钩子 {1}。请按键。",
              "Monitoring: raw input {0}, low-level hook {1}. Press a key.");
            A("ui.km.stopped", "已停止监视，钩子已卸载。", "Stopped monitoring; hook unloaded.");
            A("ui.km.startFailed", "启动失败：{0}", "Start failed: {0}");
            A("ui.km.startFailedDetail",
              "无法启动按键监视：\r\n{0}\r\n\r\nRaw Input 与 WH_KEYBOARD_LL 至少需要一个可用。详见底部日志。",
              "Cannot start the key monitor:\r\n{0}\r\n\r\nAt least one of raw input and WH_KEYBOARD_LL must be "
              + "available. See the log at the bottom.");
            A("ui.km.yes", "可用", "available");
            A("ui.km.no", "不可用", "unavailable");

            A("ui.km.verdict.title", "判定结果", "Verdict");
            A("ui.km.verdict.idle", "等待按键…", "Waiting for a key…");
            A("ui.km.verdict.idleNote",
              "按键后会在这里显示三层观测结果与结论。",
              "After a key press, the three-layer observation and the conclusion appear here.");
            A("ui.km.verdict.key", "按键：{0}", "Key: {0}");
            A("ui.km.verdict.evidence", "证据：{0}", "Evidence: {0}");
            A("ui.km.verdict.note", "说明：{0}", "Explanation: {0}");
            A("ui.km.verdict.owner", "持有者：{0}", "Owner: {0}");

            A("ui.km.mod.title", "修饰键状态", "Modifier state");
            A("ui.km.mod.none", "未发现异常。", "No anomalies.");
            A("ui.km.mod.some", "发现 {0} 个异常：{1}", "{0} anomaly(ies): {1}");
            A("ui.km.mod.note",
              "监视开始前已按住的键可能显示为异常；松开再按一次可确认。",
              "A key already held before monitoring started may look like an anomaly; release and press it again.");

            A("ui.km.remap.title", "Scancode Map 重映射", "Scancode Map remaps");
            A("ui.km.remap.input", "按下（输入扫描码）", "Press (input scan code)");
            A("ui.km.remap.output", "系统产生（输出扫描码）", "System produces (output scan code)");
            A("ui.km.remap.empty", "注册表里没有 Scancode Map，或没有有效映射。",
              "No Scancode Map in the registry, or no valid mapping.");
            A("ui.km.drivers.title", "键盘类过滤驱动", "Keyboard-class filter drivers");
            A("ui.km.drivers.name", "驱动 / 服务名", "Driver / service");
            A("ui.km.drivers.group", "组", "Group");
            A("ui.km.drivers.start", "启动类型", "Start");
            A("ui.km.drivers.image", "镜像路径", "Image path");
            A("ui.km.drivers.empty", "没有发现符合「Type=1、未禁用、键盘类」特征的服务。",
              "No service matched (Type=1, not disabled, keyboard-class).");
            A("ui.km.drivers.note",
              "这里只把可能的键盘类内核驱动列出来供参考——它可能是正常的输入栈组件，也可能是拦截源。"
              + "本工具不判定、不停止、不卸载任何驱动；要排查请用设备管理器/厂商文档，并先创建还原点。",
              "This list only shows kernel drivers that look keyboard-related, for reference — they may be normal "
              + "input-stack components or an interception source. The tool never judges, stops or unloads a "
              + "driver; investigate with Device Manager/vendor documentation and create a restore point first.");

            A("ui.km.col.time", "时间", "Time");
            A("ui.km.col.layer", "层级", "Layer");
            A("ui.km.col.key", "键", "Key");
            A("ui.km.col.scancode", "扫描码", "Scan code");
            A("ui.km.col.flags", "事件标志", "Flags");
            A("ui.km.col.desc", "说明", "Description");
            A("ui.km.cleared", "已清空本页事件与判定。", "Cleared this page's events and verdicts.");
        }

        private static void BuildHelp()
        {
            A("help.title", "帮助", "Help");
            A("help.zh", "中文", "中文");
            A("help.en", "English", "English");
            A("help.openBrowser", "在浏览器中打开", "Open in browser");
            A("help.close", "关闭", "Close");
            A("help.exportFailed", "无法导出帮助文件：{0}", "Cannot export the help file: {0}");
            A("help.missing", "找不到内嵌的帮助文档资源。", "Embedded help document resource not found.");
        }

        // ------------------------------------------------------------------ 自动归属 / 钩子知识库

        private static void BuildProbeCatalog()
        {
            A("cli.help.attributeAll",
              "  HotkeyHound.exe --attribute-all [--report <路径>] [--max N] [--yes] [--dry-run]",
              "  HotkeyHound.exe --attribute-all [--report <path>] [--max N] [--yes] [--dry-run]");
            A("cli.help.attributeAllDesc",
              "                                                自动归属已占用组合；默认 dry-run，必须 --yes 才会真的按键",
              "                                                Auto-attribute occupied combinations; dry-run by default, --yes actually presses");
            A("cli.attr.title", "{0} {1} —— 自动归属（引擎 A × B）",
              "{0} {1} — automatic attribution (engines A x B)");
            A("cli.attr.failed", "自动归属执行失败：{0}", "Auto-attribution failed: {0}");
            A("cli.attr.meta", "已占用组合：{0} 个　上限：{1} 个　模式：{2}",
              "Occupied combinations: {0}  Max: {1}  Mode: {2}");
            A("cli.attr.mode.dryRun", "dry-run（不会按下任何键）", "dry-run (no key will be pressed)");
            A("cli.attr.mode.real", "真实按键（--yes）", "real key presses (--yes)");
            A("cli.attr.plan", "计划：将按 {0} 个组合，跳过 {1} 个。",
              "Plan: {0} combination(s) will be pressed, {1} skipped.");
            A("cli.attr.willPress", "  将按   {0}", "  press  {0}");
            A("cli.attr.skip", "  跳过   {0} —— {1}", "  skip   {0} — {1}");
            A("cli.attr.dryRunDone", "dry-run 结束：{0} 个组合待按，{1} 个被跳过；一次都没有真的按下。加 --yes 才会真的按。",
              "Dry-run finished: {0} combination(s) queued, {1} skipped; nothing was actually pressed. Add --yes to press for real.");
            A("cli.attr.needYes", "没有 --yes，本次只做 dry-run。", "Without --yes this run is dry-run only.");
            A("cli.attr.starting", "正在安装归属钩子并开始按键；随时按 Esc 立即中止…",
              "Installing the attribution hook and starting to press keys; press Esc at any time to abort…");
            A("cli.attr.engineFailed", "归属钩子启动失败，未按下任何键：{0}",
              "The attribution hook failed to start; no key was pressed: {0}");
            A("cli.attr.done", "自动归属结束：拿到归属 {0} 个，无响应 {1} 个，跳过 {2} 个。",
              "Auto-attribution finished: {0} attributed, {1} no response, {2} skipped.");
            A("cli.attr.aborted", "已按用户要求中止（Esc）。已完成的结果仍记录在报告里。",
              "Aborted on request (Esc). Completed results are still recorded in the report.");

            A("auto.skip.badCombo", "无效的组合键", "Invalid key combination");
            A("auto.skip.win", "含 Win 键，可能触发开始菜单/任务视图等系统界面（硬编码禁止）",
              "Contains Win and may trigger the Start menu, Task View or other system UI (hard-coded exclusion)");
            A("auto.skip.altF4", "会关闭当前程序（硬编码禁止）", "Closes the current program (hard-coded exclusion)");
            A("auto.skip.altTab", "会切换窗口（硬编码禁止）", "Switches windows (hard-coded exclusion)");
            A("auto.skip.altEsc", "会切换窗口（硬编码禁止）", "Switches windows (hard-coded exclusion)");
            A("auto.skip.altSpace", "会打开窗口系统菜单（硬编码禁止）", "Opens the window system menu (hard-coded exclusion)");
            A("auto.skip.ctrlEsc", "会打开开始菜单（硬编码禁止）", "Opens the Start menu (hard-coded exclusion)");
            A("auto.skip.ctrlAltDel", "安全注意序列，绝不能模拟（硬编码禁止）",
              "Secure attention sequence, never simulated (hard-coded exclusion)");
            A("auto.skip.ctrlShiftEsc", "会打开任务管理器（硬编码禁止）", "Opens Task Manager (hard-coded exclusion)");
            A("auto.skip.ctrlAltTab", "会打开窗口切换器（硬编码禁止）", "Opens the window switcher (hard-coded exclusion)");
            A("auto.skip.altShiftTab", "会打开反向窗口切换器（硬编码禁止）", "Opens the reverse window switcher (hard-coded exclusion)");
            A("auto.skip.displayRotate", "是显卡驱动的屏幕旋转热键，按下会把显示转 90°（硬编码禁止）",
              "Is the GPU driver's display-rotation hotkey and would rotate the screen (hard-coded exclusion)");
            A("auto.skip.moreReason", "另有 {0} 个同类组合被跳过（{1}）",
              "another {0} combination(s) of the same kind skipped ({1})");
            A("auto.skip.overMax", "超出 --max 上限 {0}，其余 {1} 个已占用组合本次不处理",
              "Beyond the --max limit of {0}; the remaining {1} occupied combination(s) are not processed this run");
            A("auto.skip.dryRun", "dry-run：计划按下，但本次一次都没有真的按",
              "dry-run: planned, but nothing was actually pressed this run");
            A("auto.skip.aborted", "已中止（Esc 或用户取消），没有按下",
              "Aborted (Esc or cancelled by the user); not pressed");
            A("auto.skip.engineNotRunning", "归属钩子没有运行，无法归属",
              "The attribution hook was not running, so nothing can be attributed");
            A("auto.skip.inputFailed", "SendInput 失败：{0}", "SendInput failed: {0}");
            A("auto.input.counts", "按下送入 {0}/{1}，抬起送入 {2}/{3}，错误码 {4}",
              "sent {0}/{1} key-down events and {2}/{3} key-up events, error {4}");
            A("auto.log.willPress", "将按 {0}", "will press {0}");
            A("auto.log.skipSummary", "跳过：{0}", "skip: {0}");
            A("auto.log.skip", "跳过 {0}：{1}", "skip {0}: {1}");
            A("auto.log.press", "正在按下 {0}", "pressing {0}");
            A("auto.log.attributed", "{0} → 归属 {1}（PID {2}）", "{0} -> attributed to {1} (PID {2})");
            A("auto.log.noResponse", "{0} → 按下去了，但 1.2 秒内没有任何归属报告",
              "{0} -> pressed, but no attribution report arrived within 1.2 s");
            A("auto.note.attributed", "PID {0}；路径：{1}", "PID {0}; path: {1}");
            A("auto.note.noResponse",
              "按下去了但持有者没有收到（1.2 秒内无归属报告）——高度怀疑该按键被钩子吞掉",
              "Pressed but the owner never received it (no attribution report within 1.2 s) — the key is very likely swallowed by a hook");
            A("auto.outcome.attributed", "已归属", "Attributed");
            A("auto.outcome.noResponse", "无响应", "No response");
            A("auto.outcome.skipped", "跳过", "Skipped");
            A("auto.flag.dryRun", "dry-run：没有按下任何键", "dry-run: no key was pressed");
            A("auto.flag.real", "真实按键", "real key presses");
            A("auto.flag.aborted", "已中止（Esc）", "aborted (Esc)");
            A("auto.counts", "已归属 {0}　无响应 {1}　跳过 {2}", "attributed {0}  no response {1}  skipped {2}");

            A("cat.embeddedMissing", "找不到内嵌的 hook-catalog.json 资源（构建时未内嵌？）。",
              "The embedded hook-catalog.json resource was not found (missing at build time?).");
            A("cat.userFailed", "用户 catalog.json 解析失败，已忽略：{0}",
              "The user catalog.json could not be parsed and was ignored: {0}");
            A("cat.loaded", "钩子知识库：{0} 条（内嵌 {1}，用户覆盖 {2}）",
              "Hook catalog: {0} entries ({1} embedded, {2} user overrides)");
            A("cat.enrichFailed", "条目 {0} 复用引擎 D 解析失败（已忽略）：{1}",
              "Entry {0} failed to reuse engine D's parser (ignored): {1}");
            A("cat.source.config", "本机配置", "Local config");
            A("cat.source.default", "官方默认", "Official default");
            A("cat.source.docs", "文档记载", "Documentation");
            A("cat.source.none", "（无配置来源）", "(no config source)");
            A("cat.note.configDerived", "键位来自本机配置。", "The key comes from the local configuration.");
            A("cat.note.defaultSource", "键位来自官方默认，本机未读到配置。",
              "The key is an official default; the local configuration could not be read.");
            A("cat.note.docsSource", "键位来自文档记载，本机未验证。",
              "The key comes from documentation and was not verified on this machine.");
            A("cat.note.processRunning", "进程在运行（{0}）。", "The process is running ({0}).");
            A("cat.note.processNotRunning", "软件当前没有运行，键位暂时不会生效。",
              "The software is not running, so the key is not active right now.");
            A("cat.note.configRead", "配置可读：{0}", "Config readable: {0}");
            A("cat.note.configUnreadable", "配置存在但读不出来（二进制/加密/被占用）：{0}",
              "Config exists but cannot be read (binary, encrypted or locked): {0}");
            A("cat.note.configMissing", "没有找到配置：{0}", "Config not found: {0}");
            A("cat.note.scanOccupied", "引擎 A 实测：该组合已占用。", "Engine A measured this combination as occupied.");
            A("cat.note.scanFree", "引擎 A 实测：该组合空闲——按下去可能被钩子先处理。",
              "Engine A measured this combination as free — a hook may handle it first.");
            A("cat.note.keyUnknown", "具体键位无法确定，只能提供进程与配置证据。",
              "The exact key cannot be determined; only process and config evidence is available.");
            A("cat.config.read", "可读", "Readable");
            A("cat.config.unreadable", "不可读", "Unreadable");
            A("cat.config.missing", "不存在", "Missing");
            A("cat.config.none", "无配置路径", "No config path");

            A("ev.confirmed", "已确认", "Confirmed");
            A("ev.highlySuspected", "高度怀疑", "Highly suspected");
            A("ev.knownDefault", "已知默认", "Known default");
            A("ev.reference", "仅供参考", "Reference only");
            A("ev.explain.confirmed",
              "配置里明确写了这个组合，且软件正在运行。",
              "The config explicitly declares this combination and the software is running.");
            A("ev.explain.highlySuspected",
              "只有一侧证据：配置里写了但软件没运行，或配置读不出来但进程在跑且已知它有钩子热键。",
              "Only one side of the evidence: the config declares it but the software is not running, or the config is unreadable while the process runs and is known to use hook hotkeys.");
            A("ev.explain.knownDefault",
              "该软件的出厂默认快捷键，本机没能读到配置。",
              "The software's factory-default shortcut; the local config could not be read.");
            A("ev.explain.reference",
              "该软件确实用钩子，但具体键位无法确定。",
              "The software does use hooks, but the exact keys cannot be determined.");

            A("scan.catalog.start", "正在加载钩子型热键知识库并与扫描结果关联",
              "Loading the hook-hotkey catalog and correlating it with the scan");
            A("scan.catalog.done", "钩子知识库：{0} 条条目，产出 {1} 条钩子嫌疑",
              "Hook catalog: {0} entries, {1} hook-suspect record(s)");
            A("scan.catalog.failed", "钩子知识库加载失败（已忽略，扫描结果不受影响）：{0}",
              "The hook catalog failed to load (ignored; the scan result is unaffected): {0}");

            A("ui.tab.hooksuspects", "钩子嫌疑", "Hook suspects");
            A("ui.hs.title", "钩子嫌疑", "Hook suspects");
            A("ui.hs.hint",
              "本页不安装钩子。结论来自配置、进程状态和占用扫描的对照。",
              "This page installs no hook. The conclusion comes from config, process state, and the occupancy scan.");
            A("ui.hs.levels",
              "已确认=配置写了且进程在跑；高度怀疑=只有一侧证据；已知默认=官方默认、本机没读到；仅供参考=确定用钩子但键位未知。",
              "Confirmed = config declares it and the process runs; Highly suspected = only one side of the evidence; "
              + "Known default = official default, not read locally; Reference only = definitely uses hooks but the keys are unknown.");
            A("ui.hs.filter.all", "全部证据等级", "All evidence levels");
            A("ui.hs.col.software", "软件", "Software");
            A("ui.hs.col.module", "模块", "Module");
            A("ui.hs.col.process", "进程是否在跑", "Process running");
            A("ui.hs.col.config", "配置是否可读", "Config readable");
            A("ui.hs.col.source", "来源路径", "Source path");
            A("ui.hs.openConfig", "打开配置所在目录", "Open config folder");
            A("ui.hs.openFailed", "无法打开：{0}\r\n{1}", "Cannot open: {0}\r\n{1}");
            A("ui.hs.noScan", "尚未扫描。先扫描才能对照占用。",
              "Not scanned yet. Run a scan first to compare against occupancy.");
            A("ui.hs.empty", "本次没有钩子嫌疑记录。", "No hook-suspect records this run.");
            A("ui.hs.count", "显示 {0} 条", "Showing {0}");
            A("ui.hs.pickRow", "请先选中一行。", "Select a row first.");
            A("ui.hs.noSource", "这一条没有可打开的配置路径。\r\n\r\n{0}", "This row has no config path to open.\r\n\r\n{0}");
            A("ui.hs.unknownCombo", "（键位未知）", "(key unknown)");
            A("ui.hs.processYes", "是", "Yes");
            A("ui.hs.processNo", "否", "No");

            A("ui.at.autoButton", "自动归属已占用组合（会真的按下这些热键）",
              "Auto-attribute occupied combinations (these hotkeys are really pressed)");
            A("ui.at.autoButtonStop", "中止自动归属（Esc）", "Abort auto-attribution (Esc)");
            A("ui.at.autoConfirmTitle", "自动归属 —— 会真的按下热键",
              "Auto-attribution — hotkeys will really be pressed");
            A("ui.at.autoConfirmBody",
              "接下来会逐个按下“已占用但不知道主人”的组合，会真的触发这些热键。\r\n\r\n"
              + "可能开始录屏/录音、打开或关闭面板、切换输入法、启动程序。\r\n"
              + "含 Win、Alt+F4、Alt+Tab 等危险键不会按。\r\n"
              + "电脑上应用较多时，可能长时间无法正常操作，Esc 也不一定能用。\r\n\r\n"
              + "继续？",
              "The program will press each \"occupied but unknown owner\" combination, which really triggers those "
              + "hotkeys.\r\n\r\n"
              + "It may start recording, open or close panels, switch input methods, or launch programs.\r\n"
              + "Dangerous combinations (anything with Win, Alt+F4, Alt+Tab, and similar) are never pressed.\r\n"
              + "On a machine with many apps, it may block normal use for a while, and Esc may not respond.\r\n\r\n"
              + "Continue?");
            A("ui.at.autoNeedScan", "还没有扫描结果。先扫描，才知道哪些组合已占用。",
              "No scan result yet. Scan first to know which combinations are occupied.");
            A("ui.at.autoStarting", "正在安装归属钩子（只在本次自动归属期间）…",
              "Installing the attribution hook (for this auto-attribution run only)…");
            A("ui.at.autoAborting", "正在中止…", "Aborting…");
            A("ui.at.autoProgress", "自动归属：{0} / {1}　{2}",
              "Auto-attribution: {0} / {1}  {2}");
            A("ui.at.autoDone", "自动归属结束：已归属 {0}，无响应 {1}，跳过 {2}。",
              "Auto-attribution finished: {0} attributed, {1} no response, {2} skipped.");
            A("ui.at.autoAborted", "已中止自动归属（Esc 或按钮）。已完成的结果已记录。",
              "Auto-attribution aborted (Esc or the button). Completed results were recorded.");
            A("ui.at.autoFailed", "自动归属失败：{0}", "Auto-attribution failed: {0}");
            A("ui.at.autoListTitle", "自动归属结果（无响应 = 持有者没收到）",
              "Auto-attribution results (no response = the owner did not receive it)");
            A("ui.at.autoCol.outcome", "结果", "Result");
            A("ui.at.autoCol.owner", "归属进程", "Owner process");
            A("ui.at.autoCol.note", "说明", "Explanation");
            A("ui.at.autoNoOwner", "（无归属）", "(no owner)");

            A("rep.card.hookSuspects", "钩子嫌疑", "Hook suspects");
            A("rep.hooksuspects.h2", "钩子嫌疑（知识库 × 实测占用）", "Hook suspects (catalog x measured occupancy)");
            A("rep.hooksuspects.hint",
              "这些结论来自三件事：读该软件的配置、看它是否在运行、对照引擎 A 的实测占用。"
              + "只有「已确认」表示配置明确写了且进程在跑；其余等级都带着不确定性，请按等级阅读。",
              "These conclusions come from three things: reading the software's config, checking whether it is running, "
              + "and comparing against engine A's measurement. Only Confirmed means the config declares the key and the "
              + "process runs; every other level carries uncertainty — read it accordingly.");
            A("rep.hooksuspects.levelsTitle", "证据等级定义", "Evidence levels");
            A("rep.hooksuspects.empty",
              "本次没有钩子嫌疑记录。可能原因：知识库里没有 hook/mixed 条目，或它们的键位都不在引擎 A 的探测网格内；"
              + "也可能是知识库加载失败（见运行日志）。",
              "No hook-suspect records this run. Possible reasons: the catalog has no hook/mixed entries, none of their "
              + "keys are inside engine A's probe grid, or the catalog failed to load (see the run log).");
            A("rep.hooksuspects.count", "（{0} 条）", " ({0})");
            A("rep.hooksuspects.col.software", "软件", "Software");
            A("rep.hooksuspects.col.module", "模块", "Module");
            A("rep.hooksuspects.col.process", "进程在跑", "Process running");
            A("rep.hooksuspects.col.config", "配置可读", "Config readable");
            A("rep.hooksuspects.col.source", "来源", "Source");

            A("rep.autoprobe.h2", "自动归属（引擎 A × B）", "Auto-attribution (engines A x B)");
            A("rep.autoprobe.hint",
              "对已占用组合逐个模拟按下，用引擎 B 的归属报告问出主人。"
              + "「无响应」= 按下去了但持有者没收到——高度怀疑被钩子吞掉。含 Win、Alt+F4、Alt+Tab 等危险组合被硬编码排除。",
              "Occupied combinations are pressed one by one and engine B's attribution reports are used to identify "
              + "the owner. \"No response\" means the key was pressed but the owner never received it — very likely "
              + "swallowed by a hook. Dangerous combinations (anything with Win, Alt+F4, Alt+Tab, etc.) are hard-coded exclusions.");
            A("rep.autoprobe.empty",
              "本次未采集自动归属。采集方式：命令行 <code>HotkeyHound.exe --attribute-all --yes [--max 30]</code>，"
              + "或在图形界面「按键归属」页点「自动归属已占用组合」（会先弹确认框）。默认不按任何键，必须先明确同意。",
              "No auto-attribution was collected this run. To collect it, run "
              + "<code>HotkeyHound.exe --attribute-all --yes [--max 30]</code>, or click Auto-attribute occupied "
              + "combinations on the Attribution page (a confirmation dialog appears first). Nothing is pressed by default.");
            A("rep.autoprobe.mode", "模式：{0}", "Mode: {0}");
            A("rep.autoprobe.counts", "已归属 {0}　无响应 {1}　跳过 {2}",
              "Attributed {0}  No response {1}  Skipped {2}");
            A("rep.autoprobe.col.combo", "组合键", "Combination");
            A("rep.autoprobe.col.outcome", "结果", "Result");
            A("rep.autoprobe.col.owner", "归属进程", "Owner process");
            A("rep.autoprobe.col.note", "说明", "Explanation");
            A("rep.autoprobe.nonePressed", "（dry-run 或中止：没有真正按下）", "(dry-run or aborted: nothing was actually pressed)");

            A("rep.advice.hookFree",
              "<div class=\"warn\"><b>{0}</b> 的钩子嫌疑命中 {1}（证据：{2}），但引擎 A 实测这些组合是空闲的。"
              + "按下去没反应？很可能被它的钩子吞了——去它的设置里改。来源：{3}</div>",
              "<div class=\"warn\"><b>{0}</b> has hook suspicions on {1} (evidence: {2}), yet engine A measured these "
              + "combinations as free. Nothing happens when you press them? They are very likely swallowed by its hook — "
              + "change it in that software's settings. Source: {3}</div>");

            A("rep.method.auto.h", "自动归属的风险边界", "Risk boundaries of auto-attribution");
            A("rep.method.auto.body",
              "<p>自动归属会<strong>真的按下</strong>热键，因此默认是 dry-run：不带 <code>--yes</code> 时只打印计划，"
              + "一次都不按；图形界面必须先通过确认框。含 Win 的组合、<code>Alt+F4</code>、<code>Alt+Tab</code>、"
              + "<code>Alt+Esc</code>、<code>Alt+Space</code>、<code>Ctrl+Esc</code>、<code>Ctrl+Alt+Del</code>、"
              + "<code>Ctrl+Shift+Esc</code>、<code>Ctrl+Alt+Tab</code> 是硬编码黑名单，不可配置放宽。"
              + "每次按键间隔至少 400 毫秒，归属等待 1.2 秒，按 Esc 立即中止；中止、超时与跳过的数量都如实计入统计。</p>",
              "<p>Auto-attribution <strong>really presses</strong> hotkeys, so it is dry-run by default: without "
              + "<code>--yes</code> it only prints the plan and presses nothing; the GUI requires a confirmation dialog. "
              + "Anything with Win, plus <code>Alt+F4</code>, <code>Alt+Tab</code>, <code>Alt+Esc</code>, "
              + "<code>Alt+Space</code>, <code>Ctrl+Esc</code>, <code>Ctrl+Alt+Del</code>, <code>Ctrl+Shift+Esc</code> and "
              + "<code>Ctrl+Alt+Tab</code> is a hard-coded exclusion and cannot be relaxed. There is at least 400 ms "
              + "between key presses and 1.2 s of attribution wait; Esc aborts immediately, and aborts, timeouts and skips "
              + "are all counted honestly.</p>");
            A("rep.method.catalog.h", "钩子知识库怎么读", "How to read the hook catalog");
            A("rep.method.catalog.body",
              "<p>知识库只读配置、只看进程，不注入、不结束任何进程。每条结论都带证据等级："
              + "<b>已确认</b>（配置写了 + 进程在跑）、<b>高度怀疑</b>（只有一侧证据）、"
              + "<b>已知默认</b>（官方默认，本机没读到配置）、<b>仅供参考</b>（确定用钩子但键位未知）。"
              + "「未在知识库中」不等于「没有钩子」——知识库只覆盖已知软件，判定按键是否真被吞掉仍应使用引擎 C。</p>",
              "<p>The catalog only reads configs and checks processes; it injects nothing and ends no process. Every "
              + "conclusion carries an evidence level: <b>Confirmed</b> (declared in config and the process runs), "
              + "<b>Highly suspected</b> (only one side of the evidence), <b>Known default</b> (official default; the local "
              + "config was unreadable), <b>Reference only</b> (definitely uses hooks, keys unknown). Not being in the "
              + "catalog does not mean there is no hook — it only covers known software. Use engine C to judge whether a "
              + "key is actually swallowed.</p>");

            A("rep.list.sep", "、", ", ");
        }

        // ------------------------------------------------------------------ 适配器 / 引擎（M1）
        // 约定：适配器只产出 L10n 键名，取词一律在渲染/记录边界做，保证语言在运行期才确定。

        private static void BuildAdapters()
        {
            A("reserved.software", "Windows 系统保留", "Windows reserved");
            A("reserved.module", "内置表", "Built-in table");
            A("reserved.source", "(内置静态表)", "(built-in static table)");
            A("reserved.taskbar.app", "启动/切换到任务栏第 {0} 个应用", "Launch/switch to taskbar app {0}");
            A("reserved.taskbar.appAdmin", "以管理员身份启动任务栏第 {0} 个应用", "Launch taskbar app {0} as administrator");
            A("reserved.taskbar.newInstance", "打开任务栏第 {0} 个应用的新实例", "Open taskbar app {0} in a new instance");
            A("reserved.taskbar.jumpList", "打开任务栏第 {0} 个应用的跳转列表", "Open the jump list of taskbar app {0}");
            A("reserved.lockComputer", "锁定计算机", "Lock the computer");
            A("reserved.showDesktop", "显示/隐藏桌面", "Show/hide the desktop");
            A("reserved.minimizeAll", "最小化所有窗口", "Minimize all windows");
            A("reserved.restoreMinimized", "还原最小化的窗口", "Restore minimized windows");
            A("reserved.fileExplorer", "打开文件资源管理器", "Open File Explorer");
            A("reserved.runDialog", "打开「运行」对话框", "Open the Run dialog");
            A("reserved.settings", "打开「设置」", "Open Settings");
            A("reserved.search", "打开搜索", "Open search");
            A("reserved.quickSettings", "打开快速设置", "Open Quick Settings");
            A("reserved.notificationCenter", "打开通知中心", "Open Notification Center");
            A("reserved.widgets", "打开小组件", "Open Widgets");
            A("reserved.clipboardHistory", "剪贴板历史", "Clipboard history");
            A("reserved.powerUserMenu", "打开高级用户菜单", "Open the Power User menu");
            A("reserved.projectDisplay", "投影/外接显示", "Project / external display");
            A("reserved.wirelessDisplay", "连接无线显示/音频", "Connect to a wireless display/audio");
            A("reserved.lockOrientation", "锁定设备方向", "Lock device orientation");
            A("reserved.accessibility", "打开「辅助功能」", "Open Accessibility");
            A("reserved.feedbackHub", "反馈中心", "Feedback Hub");
            A("reserved.voiceTyping", "语音输入", "Voice typing");
            A("reserved.focusTip", "聚焦提示", "Set focus to a tip");
            A("reserved.taskView", "任务视图", "Task View");
            A("reserved.narrator", "启动讲述人", "Launch Narrator");
            A("reserved.inputMethod", "切换输入法", "Switch input method");
            A("reserved.systemProperties", "系统属性", "System properties");
            A("reserved.screenshotPictures", "截屏并保存到图片文件夹", "Screenshot saved to the Pictures folder");
            A("reserved.snipRegion", "截图工具（区域截图）", "Snipping tool (region capture)");
            A("reserved.newDesktop", "新建虚拟桌面", "Create a new virtual desktop");
            A("reserved.desktopLeft", "切换到左侧虚拟桌面", "Switch to the virtual desktop on the left");
            A("reserved.desktopRight", "切换到右侧虚拟桌面", "Switch to the virtual desktop on the right");
            A("reserved.closeDesktop", "关闭当前虚拟桌面", "Close the current virtual desktop");
            A("reserved.resetGraphics", "重置显卡驱动", "Reset the graphics driver");
            A("reserved.narratorAlt", "启动讲述人（替代入口）", "Launch Narrator (alternative entry)");
            A("reserved.maximize", "窗口最大化", "Maximize the window");
            A("reserved.restoreMinimize", "窗口还原/最小化", "Restore/minimize the window");
            A("reserved.snapLeft", "窗口贴靠到左侧", "Snap the window to the left");
            A("reserved.snapRight", "窗口贴靠到右侧", "Snap the window to the right");
            A("reserved.minimizeOthers", "最小化除当前窗口外的所有窗口", "Minimize all windows except the current one");
            A("reserved.snapLayouts", "打开贴靠布局", "Open Snap Layouts");
            A("reserved.gameRecording", "开始/停止游戏录制", "Start/stop game recording");
            A("reserved.recordLast30", "录制最近 30 秒", "Record the last 30 seconds");
            A("reserved.gameScreenshot", "保存游戏截图", "Save a game screenshot");
            A("reserved.fullscreenSomeApps", "在部分应用里全屏", "Full screen in some apps");
            A("reserved.switchWindows", "切换窗口", "Switch windows");
            A("reserved.switchWindowsReverse", "反向切换窗口", "Switch windows in reverse");
            A("reserved.closeWindow",
              "向活动窗口发送关闭请求；在桌面通常会打开关机对话框",
              "Ask the active window to close; on the desktop it usually opens the shut-down dialog");
            A("reserved.cycleWindows", "按顺序切换窗口", "Cycle through windows in order");
            A("reserved.windowSystemMenu", "打开窗口系统菜单", "Open the window's system menu");
            A("reserved.copyWindow", "截取当前窗口到剪贴板", "Copy the current window to the clipboard");
            A("reserved.taskManager", "任务管理器", "Task Manager");
            A("reserved.secureAttention", "安全注意序列", "Secure attention sequence");
            A("reserved.runAsAdmin", "以管理员身份运行", "Run as administrator");
            A("reserved.startMenu", "打开开始菜单", "Open the Start menu");
            A("reserved.newFolder", "新建文件夹", "Create a new folder");
            A("reserved.copyScreen", "截取全屏到剪贴板", "Copy the whole screen to the clipboard");
            A("reserved.toggleNumLock", "切换数字键盘锁定", "Toggle Num Lock");
            A("reserved.toggleScrollLock", "切换滚动锁定", "Toggle Scroll Lock");
            A("reserved.toggleCapsLock", "切换大写锁定", "Toggle Caps Lock");

            A("adapter.warnPrefix", "[警告]", "[Warning]");
            A("adapter.collectFailed", "采集失败（已忽略）：{0}: {1}", "Collection failed (ignored): {0}: {1}");
            A("adapter.common.globalHotkey", "全局热键", "Global hotkey");
            A("adapter.common.config", "配置", "Config");
            A("adapter.common.registry", "注册表", "Registry");

            A("adapter.everything.newWindow", "打开新搜索窗口", "Open a new search window");
            A("adapter.everything.showWindow", "显示主窗口", "Show the main window");
            A("adapter.everything.toggleWindow", "显示/隐藏主窗口", "Show/hide the main window");
            A("adapter.everything.notFound", "未找到配置文件：{0}", "Config file not found: {0}");
            A("adapter.everything.parseFailed", "无法解析 {0} = {1}", "Cannot parse {0} = {1}");
            A("adapter.everything.parsed", "解析出 {0} 条热键", "Parsed {0} hotkey(s)");

            A("adapter.ptkey.configNotFound", "未找到键盘管理器配置：{0}", "Keyboard Manager config not found: {0}");
            A("adapter.ptkey.parseFailed", "配置解析失败：{0}", "Failed to parse config: {0}");
            A("adapter.ptkey.noRemapSection", "配置里没有 remapShortcuts 段", "Config has no remapShortcuts section");
            A("adapter.ptkey.noGlobalArray", "配置里没有 remapShortcuts.global 数组", "Config has no remapShortcuts.global array");
            A("adapter.ptkey.badOriginal", "无法解析 originalKeys = {0}", "Cannot parse originalKeys = {0}");
            A("adapter.ptkey.run", "运行 {0}", "Run {0}");
            A("adapter.ptkey.remap", "重映射 → {0}", "Remap → {0}");
            A("adapter.ptkey.unknownTarget", "(未知目标)", "(unknown target)");
            A("adapter.ptkey.parsed", "解析出 {0} 条全局重映射", "Parsed {0} global remap(s)");

            A("adapter.ptmod.notFound", "未安装或未找到配置目录：{0}", "Not installed, or config directory not found: {0}");
            A("adapter.ptmod.parseFailed", "配置解析失败（已跳过）：{0}", "Failed to parse config (skipped): {0}");
            A("adapter.ptmod.parsed", "共解析出 {0} 条热键声明", "Parsed {0} hotkey declaration(s) in total");
            A("adapter.ptmod.globalModule", "PowerToys 全局", "PowerToys global");
            A("adapter.ptmod.hotkey", "热键", "Hotkey");
            A("adapter.ptmod.disabled", "[已禁用] {0}", "[disabled] {0}");

            A("adapter.wallpaper.parseFailed", "配置解析失败：{0}", "Failed to parse config: {0}");
            A("adapter.wallpaper.notFound", "未找到 config.json（按设计静默跳过）",
              "config.json not found (silently skipped by design)");
            A("adapter.wallpaper.registryFailed", "注册表读取失败：{0}", "Failed to read the registry: {0}");
            A("adapter.wallpaper.hotkey", "热键 {0}", "Hotkey {0}");
            A("adapter.wallpaper.parsed", "解析出 {0} 条热键", "Parsed {0} hotkey(s)");

            A("adapter.msi.osdToggle", "切换屏幕显示", "Toggle the on-screen display");
            A("adapter.msi.osdOn", "开启屏幕显示", "Enable the on-screen display");
            A("adapter.msi.osdOff", "关闭屏幕显示", "Disable the on-screen display");
            A("adapter.msi.osdServerBlock", "屏蔽屏幕显示服务", "Block the OSD server");
            A("adapter.msi.limiterToggle", "切换帧率限制", "Toggle the framerate limit");
            A("adapter.msi.limiterOn", "开启帧率限制", "Enable the framerate limit");
            A("adapter.msi.limiterOff", "关闭帧率限制", "Disable the framerate limit");
            A("adapter.msi.screenshot", "截图", "Screenshot");
            A("adapter.msi.videoCapture", "视频录制", "Video capture");
            A("adapter.msi.prerecord", "预先录制", "Prerecord");
            A("adapter.msi.ptt", "按键通话", "Push to talk");
            A("adapter.msi.ptt2", "按键通话 2", "Push to talk 2");
            A("adapter.msi.beginRecord", "开始录制", "Start recording");
            A("adapter.msi.endRecord", "结束录制", "Stop recording");
            A("adapter.msi.notFound", "未找到配置文件：{0}", "Config file not found: {0}");
            A("adapter.msi.badEncoding", "无法解析热键编码 {0}={1}", "Cannot parse hotkey encoding {0}={1}");
            A("adapter.msi.profile", "切换到超频配置 {0}", "Switch to OC profile {0}");
            A("adapter.msi.parsed", "解析出 {0} 条热键（本机若全为未设置则为 0）",
              "Parsed {0} hotkey(s) (0 if every entry is unset on this machine)");

            A("adapter.obs.startStreaming", "开始推流", "Start streaming");
            A("adapter.obs.stopStreaming", "停止推流", "Stop streaming");
            A("adapter.obs.startRecording", "开始录制", "Start recording");
            A("adapter.obs.stopRecording", "停止录制", "Stop recording");
            A("adapter.obs.toggleRecording", "切换录制", "Toggle recording");
            A("adapter.obs.toggleStreaming", "切换推流", "Toggle streaming");
            A("adapter.obs.startReplayBuffer", "开启回放缓存", "Start the replay buffer");
            A("adapter.obs.stopReplayBuffer", "关闭回放缓存", "Stop the replay buffer");
            A("adapter.obs.saveReplay", "保存回放", "Save replay");
            A("adapter.obs.pauseRecording", "暂停录制", "Pause recording");
            A("adapter.obs.resumeRecording", "继续录制", "Resume recording");
            A("adapter.obs.startVirtualCam", "开启虚拟摄像头", "Start the virtual camera");
            A("adapter.obs.stopVirtualCam", "关闭虚拟摄像头", "Stop the virtual camera");
            A("adapter.obs.screenshot", "截图", "Screenshot");
            A("adapter.obs.pushToTalk", "按键通话", "Push to talk");
            A("adapter.obs.pushToMute", "按键静音", "Push to mute");
            A("adapter.obs.togglePreview", "切换预览", "Toggle the preview");
            A("adapter.obs.startReplayBuffer2", "开始回放缓存", "Start the replay buffer");
            A("adapter.obs.notFound", "未找到 OBS 配置目录：{0}", "OBS config directory not found: {0}");
            A("adapter.obs.globalModule", "全局", "Global");
            A("adapter.obs.profileSet", "配置集 {0}", "Profile set {0}");
            A("adapter.obs.badValue", "无法解析 {0} = {1}（{2}）", "Cannot parse {0} = {1} ({2})");
            A("adapter.obs.parsed", "解析出 {0} 条热键", "Parsed {0} hotkey(s)");

            A("adapter.nvidia.overlayToggle", "叠加层开关", "Overlay toggle");
            A("adapter.nvidia.screenshot", "截图", "Screenshot");
            A("adapter.nvidia.manualRecord", "手动录制", "Manual recording");
            A("adapter.nvidia.replayToggle", "即时回放开关", "Instant replay toggle");
            A("adapter.nvidia.replayPreset", "即时回放预设", "Instant replay preset");
            A("adapter.nvidia.replayStyle1", "即时回放预设样式 1", "Instant replay preset style 1");
            A("adapter.nvidia.replayStyle2", "即时回放预设样式 2", "Instant replay preset style 2");
            A("adapter.nvidia.replayStyle3", "即时回放预设样式 3", "Instant replay preset style 3");
            A("adapter.nvidia.replayUi", "即时回放界面", "Instant replay UI");
            A("adapter.nvidia.anselUi", "Ansel 拍照界面", "Ansel photo mode UI");
            A("adapter.nvidia.perfOverlay", "性能监控叠加层", "Performance overlay");
            A("adapter.nvidia.perfCycle", "性能监控指标切换", "Cycle performance metrics");
            A("adapter.nvidia.perfResetAverage", "重置性能监控平均值", "Reset performance average metrics");
            A("adapter.nvidia.perfLogging", "性能监控日志开关", "Toggle performance logging");
            A("adapter.nvidia.perfFont", "性能监控字体切换", "Cycle performance overlay font");
            A("adapter.nvidia.micToggle", "麦克风开关", "Microphone toggle");
            A("adapter.nvidia.micPtt", "麦克风按键通话", "Microphone push to talk");
            A("adapter.nvidia.chatToggle", "聊天界面开关", "Chat UI toggle");
            A("adapter.nvidia.chatPtt", "聊天按键通话", "Chat push to talk");
            A("adapter.nvidia.keyboardDialog", "键盘输入对话框", "Keyboard input dialog");
            A("adapter.nvidia.overlayB", "自定义叠加层 B", "Custom overlay B");
            A("adapter.nvidia.overlayC", "自定义叠加层 C", "Custom overlay C");
            A("adapter.nvidia.keyMissing", "注册表项不存在（未安装或从未配置）：{0}",
              "Registry key does not exist (not installed or never configured): {0}");
            A("adapter.nvidia.badCombo", "无法组合出有效热键：{0}", "Cannot compose a valid hotkey from {0}");
            A("adapter.nvidia.parsed", "解析出 {0} 条热键", "Parsed {0} hotkey(s)");
        }

        // ------------------------------------------------------------------ 引擎 / 运行时（M1）
        // 这些是 Say()/LastError 文案：会原样进报告与界面，必须在英文模式下也是英文。

        private static void BuildRuntime()
        {
            A("attrib.dllReleaseFailed", "hook DLL 释放失败：{0}", "Failed to release the hook DLL: {0}");
            A("attrib.threadTimeout", "钩子线程未能就绪（超时）", "The hook thread did not become ready (timed out)");
            A("attrib.threadJoinTimeout", "警告：钩子线程在 4 秒内没有退出",
              "Warning: the hook thread did not exit within 4 seconds");
            A("attrib.mapCreateFailed", "创建共享内存 {0} 失败，错误码 {1}",
              "Failed to create shared memory {0}; error code {1}");
            A("attrib.mapViewFailed", "映射共享内存失败，错误码 {0}", "Failed to map shared memory; error code {0}");
            A("attrib.loadLibraryFailed", "LoadLibrary 失败：{0}，错误码 {1}", "LoadLibrary failed: {0}; error code {1}");
            A("attrib.exportMissing", "DLL 里找不到导出 HotkeyHoundHookProc",
              "The DLL does not export HotkeyHoundHookProc");
            A("attrib.loadLibraryOk", "LoadLibrary 成功：hMod=0x{0}", "LoadLibrary succeeded: hMod=0x{0}");
            A("attrib.exportAddress", "导出地址 HotkeyHoundHookProc=0x{0}", "Export address HotkeyHoundHookProc=0x{0}");
            A("attrib.hookInstallFailed", "SetWindowsHookEx(WH_GETMESSAGE) 失败，错误码 {0}",
              "SetWindowsHookEx(WH_GETMESSAGE) failed; error code {0}");
            A("attrib.uipiHint",
              "提示：当前以{0}运行，UIPI 会阻止把 DLL 注入到普通进程。请把 exe 复制到普通目录后再运行。",
              "Note: running at {0}; UIPI blocks DLL injection into normal processes. Copy the exe to a normal "
              + "directory before running it again.");
            A("attrib.hookInstallOk", "SetWindowsHookEx 成功：hook=0x{0}，控制窗口=0x{1}",
              "SetWindowsHookEx succeeded: hook=0x{0}, control window=0x{1}");
            A("attrib.mapReady", "共享内存 {0} 已就绪（magic=0x484B5354, arm=1, ctl=0x{1}）",
              "Shared memory {0} is ready (magic=0x484B5354, arm=1, ctl=0x{1})");
            A("attrib.wmClose", "收到 WM_CLOSE，钩子线程退出", "Received WM_CLOSE; the hook thread is exiting");
            A("attrib.threadException", "钩子线程异常：{0}", "Hook thread exception: {0}");
            A("attrib.registerClassFailed", "RegisterClassEx 失败，错误码 {0}",
              "RegisterClassEx failed; error code {0}");
            A("attrib.createWindowFailed", "创建控制窗口失败，错误码 {0}",
              "Failed to create the control window; error code {0}");
            A("attrib.msgFilterFailed",
              "提示：放行归属上报消息失败（错误码 {0}）；以管理员身份运行时可能收不到来自普通权限进程的报告。",
              "Note: failed to allow the attribution report message (error code {0}); when running as "
              + "administrator, reports from normal-privilege processes may not arrive.");
            A("attrib.truncated", "归属记录已超过 {0} 条上限，开始丢弃最早的记录（界面与报告只保留最近的）。",
              "Attribution records exceeded the {0}-record limit; the oldest are dropped (the UI and report "
              + "keep only the most recent).");
            A("attrib.report", "归属报告：{0}{1}", "Attribution report: {0}{1}");
            A("attrib.unhooked", "已 UnhookWindowsHookEx（旧句柄 0x{0}）",
              "UnhookWindowsHookEx done (old handle 0x{0})");

            A("json.nullInput", "输入为 null", "Input is null");
            A("json.tooDeep", "JSON 嵌套超过 {0} 层，放弃解析", "JSON nesting exceeds {0} levels; giving up");
            A("json.unexpectedEnd", "JSON 意外结束", "Unexpected end of JSON");
            A("json.expected", "位置 {0} 处期望 {1}", "Expected {1} at position {0}");
            A("json.expectedMember", "位置 {0} 处期望对象成员名", "Expected an object member name at position {0}");
            A("json.expectedColon", "位置 {0} 处期望 ':'", "Expected ':' at position {0}");
            A("json.unclosedObject", "对象未闭合", "Object is not closed");
            A("json.expectedCommaOrBrace", "位置 {0} 处期望 ',' 或 '}'", "Expected ',' or '}' at position {0}");
            A("json.unclosedArray", "数组未闭合", "Array is not closed");
            A("json.expectedCommaOrBracket", "位置 {0} 处期望 ',' 或 ']'", "Expected ',' or ']' at position {0}");
            A("json.unclosedString", "字符串未闭合", "String is not closed");
            A("json.escapeUnfinished", "转义序列未完成", "Escape sequence is incomplete");
            A("json.unicodeEscapeIncomplete", "\\u 转义不完整", "Incomplete \\u escape");
            A("json.unknownEscape", "未知转义 \\{0}", "Unknown escape \\{0}");
            A("json.badValue", "位置 {0} 处不是合法 JSON 值", "Not a valid JSON value at position {0}");
            A("json.badNumber", "无法解析数字：{0}", "Cannot parse number: {0}");

            A("il.sidReadFailed", "(读取失败)", "(read failed)");
            A("il.embeddedReadFailed", "读取内嵌资源失败：{0}", "Failed to read the embedded resource: {0}");
            A("il.resourceMissing", "程序里没有内嵌 {0}（构建时原生 DLL 没编出来？）",
              "The executable does not embed {0} (was the native DLL not built?)");
            A("il.dllExtracted", "hook DLL 释放到：{0}（{1} 字节）", "Hook DLL extracted to: {0} ({1} bytes)");
            A("il.dllDirSkipped", "hook DLL 释放目录不可用，已跳过：{0} → {1}：{2}",
              "Hook DLL directory unusable, skipped: {0} → {1}: {2}");
            A("il.noWritableDir", "所有候选目录都写不进去。最后一次：{0}",
              "Could not write to any candidate directory. Last error: {0}");
            A("il.noCandidate", "无候选目录", "no candidate directory");

            A("sniff.threadTimeout", "低层键盘钩子线程未能就绪（超时）",
              "The low-level keyboard hook thread did not become ready (timed out)");
            A("sniff.threadJoinTimeout", "警告：钩子线程在 4 秒内没有退出",
              "Warning: the hook thread did not exit within 4 seconds");
            A("sniff.installFailed", "SetWindowsHookEx(WH_KEYBOARD_LL) 失败，错误码 {0}",
              "SetWindowsHookEx(WH_KEYBOARD_LL) failed; error code {0}");
            A("sniff.installed", "WH_KEYBOARD_LL 已安装：hook=0x{0}，线程 {1}",
              "WH_KEYBOARD_LL installed: hook=0x{0}, thread {1}");
            A("sniff.threadException", "钩子线程异常：{0}", "Hook thread exception: {0}");
            A("sniff.unhooked", "已 UnhookWindowsHookEx（WH_KEYBOARD_LL，句柄 0x{0}）",
              "UnhookWindowsHookEx done (WH_KEYBOARD_LL, handle 0x{0})");
            A("sniff.notReady", "低层键盘钩子还没装好", "The low-level keyboard hook is not installed yet");

            A("cfg.adapterCrashed", "[警告][{0}] 适配器崩溃（已忽略）：{1}", "[Warning][{0}] Adapter crashed (ignored): {1}");
            A("cfg.adapterContributed", "引擎 D：{0} 贡献 {1} 条声明",
              "Engine D: {0} contributed {1} declaration(s)");
            A("cfg.reservedFailed", "[警告][系统保留表] 失败： {0}", "[Warning][system reserved table] failed: {0}");
            A("cfg.done", "引擎 D 完成：共 {0} 条声明，{1} 条系统保留提示",
              "Engine D finished: {0} declaration(s) in total, {1} reserved-combination hint(s)");

            A("helper.parseFailed", "HELPER_ERROR 无法解析热键：{0}", "HELPER_ERROR cannot parse hotkey: {0}");
            A("helper.registerClassFailed", "HELPER_ERROR RegisterClassEx 失败，错误码 {0}",
              "HELPER_ERROR RegisterClassEx failed; error code {0}");
            A("helper.createWindowFailed", "HELPER_ERROR CreateWindowEx 失败，错误码 {0}",
              "HELPER_ERROR CreateWindowEx failed; error code {0}");

            A("elev.noExePath", "无法确定当前可执行文件路径", "Cannot determine the current executable path");
            A("elev.cancelled", "用户取消了提权（ERROR_CANCELLED）", "The user cancelled elevation (ERROR_CANCELLED)");
            A("elev.shellExecuteFailed", "ShellExecuteEx 失败，错误码 {0}", "ShellExecuteEx failed; error code {0}");

            A("cat.disabledPrefix", "[已禁用]", "[disabled]");

            A("shstate.tempDirSkipped", "临时目录不可写，已跳过：{0}", "Temp directory is not writable, skipped: {0}");
            A("shstate.noWritableTemp", "找不到可写的临时目录。最后一次失败：{0}",
              "No writable temp directory found. Last failure: {0}");

            A("helper32.processStartNull", "Process.Start 返回 null", "Process.Start returned null");

            A("ui.screenshot.windowShown", "窗口已显示，开始扫描", "Window shown; starting the scan");
            A("ui.screenshot.scanDone", "扫描结束：{0}", "Scan finished: {0}");
            A("ui.screenshot.scanOk", "成功", "succeeded");
            A("ui.screenshot.finished", "截图完成", "Screenshot capture finished");
            A("ui.screenshot.modeFailed", "截图模式失败：{0}", "Screenshot mode failed: {0}");
            A("ui.screenshot.captureFailed", "截图失败：{0}", "Screenshot capture failed: {0}");
            A("ui.screenshot.tabs", "标签页数：{0}，窗口尺寸：{1}x{2}", "Tab count: {0}; window size: {1}x{2}");
            A("ui.screenshot.render", "渲染 {0}（标签页 {1}，尺寸 {2}x{3}）",
              "Rendering {0} (tab {1}, size {2}x{3})");
            A("ui.screenshot.rendered", "已渲染 {0}.png", "Rendered {0}.png");
        }

        // ------------------------------------------------------------------ 术语表
        // 引擎 D 的适配器产出的是中文「用途/模块」短语，这些文本会进入报告和界面。
        // 为避免直接改十几个适配器（风险大且易漏），这里用一张「中文→英文」术语表，
        // 在渲染边界统一翻译（Reconciler / ReportBuilder / UI 调用 Term）。

        private static readonly Dictionary<string, string> Terms = new Dictionary<string, string>(StringComparer.Ordinal);

        private static void AddTerm(string zh, string en)
        {
            Terms[zh] = en;
        }

        private static void BuildTerms()
        {
            // 通用
            AddTerm("热键", "Hotkey");
            AddTerm("全局热键", "Global hotkey");
            AddTerm("内置表", "Built-in table");
            AddTerm("配置", "Config");
            AddTerm("注册表", "Registry");
            AddTerm("全局", "Global");

            // Everything
            AddTerm("打开新搜索窗口", "Open a new search window");
            AddTerm("显示主窗口", "Show the main window");
            AddTerm("显示/隐藏主窗口", "Show/hide the main window");

            // MSI Afterburner
            AddTerm("切换屏幕显示", "Toggle the on-screen display");
            AddTerm("开启屏幕显示", "Enable the on-screen display");
            AddTerm("关闭屏幕显示", "Disable the on-screen display");
            AddTerm("屏蔽屏幕显示服务", "Block the OSD server");
            AddTerm("切换帧率限制", "Toggle the framerate limit");
            AddTerm("开启帧率限制", "Enable the framerate limit");
            AddTerm("关闭帧率限制", "Disable the framerate limit");
            AddTerm("视频录制", "Video capture");
            AddTerm("预先录制", "Prerecord");
            AddTerm("按键通话", "Push to talk");
            AddTerm("按键通话 2", "Push to talk 2");
            AddTerm("开始录制", "Start recording");
            AddTerm("结束录制", "Stop recording");

            // NVIDIA ShadowPlay
            AddTerm("叠加层开关", "Overlay toggle");
            AddTerm("手动录制", "Manual recording");
            AddTerm("即时回放开关", "Instant replay toggle");
            AddTerm("即时回放预设", "Instant replay preset");
            AddTerm("即时回放预设样式 1", "Instant replay preset style 1");
            AddTerm("即时回放预设样式 2", "Instant replay preset style 2");
            AddTerm("即时回放预设样式 3", "Instant replay preset style 3");
            AddTerm("即时回放界面", "Instant replay UI");
            AddTerm("Ansel 拍照界面", "Ansel photo mode UI");
            AddTerm("性能监控叠加层", "Performance overlay");
            AddTerm("性能监控指标切换", "Cycle performance metrics");
            AddTerm("重置性能监控平均值", "Reset performance average metrics");
            AddTerm("性能监控日志开关", "Toggle performance logging");
            AddTerm("性能监控字体切换", "Cycle performance overlay font");
            AddTerm("麦克风开关", "Microphone toggle");
            AddTerm("麦克风按键通话", "Microphone push to talk");
            AddTerm("聊天界面开关", "Chat UI toggle");
            AddTerm("聊天按键通话", "Chat push to talk");
            AddTerm("键盘输入对话框", "Keyboard input dialog");
            AddTerm("自定义叠加层 B", "Custom overlay B");
            AddTerm("自定义叠加层 C", "Custom overlay C");

            // OBS Studio
            AddTerm("开始推流", "Start streaming");
            AddTerm("停止推流", "Stop streaming");
            AddTerm("停止录制", "Stop recording");
            AddTerm("切换录制", "Toggle recording");
            AddTerm("切换推流", "Toggle streaming");
            AddTerm("开启回放缓存", "Start the replay buffer");
            AddTerm("关闭回放缓存", "Stop the replay buffer");
            AddTerm("保存回放", "Save replay");
            AddTerm("暂停录制", "Pause recording");
            AddTerm("继续录制", "Resume recording");
            AddTerm("开启虚拟摄像头", "Start the virtual camera");
            AddTerm("关闭虚拟摄像头", "Stop the virtual camera");
            AddTerm("按键静音", "Push to mute");
            AddTerm("切换预览", "Toggle the preview");
            AddTerm("开始回放缓存", "Start the replay buffer");

            // 通用动作
            AddTerm("截图", "Screenshot");

            // Windows 系统保留（内置表）
            AddTerm("锁定计算机", "Lock the computer");
            AddTerm("显示/隐藏桌面", "Show/hide the desktop");
            AddTerm("最小化所有窗口", "Minimize all windows");
            AddTerm("还原最小化的窗口", "Restore minimized windows");
            AddTerm("打开文件资源管理器", "Open File Explorer");
            AddTerm("打开「运行」对话框", "Open the Run dialog");
            AddTerm("打开「设置」", "Open Settings");
            AddTerm("打开搜索", "Open search");
            AddTerm("打开快速设置", "Open Quick Settings");
            AddTerm("打开通知中心", "Open Notification Center");
            AddTerm("打开小组件", "Open Widgets");
            AddTerm("剪贴板历史", "Clipboard history");
            AddTerm("打开高级用户菜单", "Open the Power User menu");
            AddTerm("投影/外接显示", "Project / external display");
            AddTerm("连接无线显示/音频", "Connect to a wireless display/audio");
            AddTerm("锁定设备方向", "Lock device orientation");
            AddTerm("打开「辅助功能」", "Open Accessibility");
            AddTerm("反馈中心", "Feedback Hub");
            AddTerm("语音输入", "Voice typing");
            AddTerm("聚焦提示", "Set focus to a tip");
            AddTerm("任务视图", "Task View");
            AddTerm("启动讲述人", "Launch Narrator");
            AddTerm("切换输入法", "Switch input method");
            AddTerm("系统属性", "System properties");
            AddTerm("截屏并保存到图片文件夹", "Screenshot saved to the Pictures folder");
            AddTerm("截图工具（区域截图）", "Snipping tool (region capture)");
            AddTerm("新建虚拟桌面", "Create a new virtual desktop");
            AddTerm("切换到左侧虚拟桌面", "Switch to the virtual desktop on the left");
            AddTerm("切换到右侧虚拟桌面", "Switch to the virtual desktop on the right");
            AddTerm("关闭当前虚拟桌面", "Close the current virtual desktop");
            AddTerm("重置显卡驱动", "Reset the graphics driver");
            AddTerm("启动讲述人（替代入口）", "Launch Narrator (alternative entry)");
            AddTerm("窗口最大化", "Maximize the window");
            AddTerm("窗口还原/最小化", "Restore/minimize the window");
            AddTerm("窗口贴靠到左侧", "Snap the window to the left");
            AddTerm("窗口贴靠到右侧", "Snap the window to the right");
            AddTerm("最小化除当前窗口外的所有窗口", "Minimize all windows except the current one");
            AddTerm("打开贴靠布局", "Open Snap Layouts");
            AddTerm("开始/停止游戏录制", "Start/stop game recording");
            AddTerm("录制最近 30 秒", "Record the last 30 seconds");
            AddTerm("保存游戏截图", "Save a game screenshot");
            AddTerm("在部分应用里全屏", "Full screen in some apps");
            AddTerm("切换窗口", "Switch windows");
            AddTerm("反向切换窗口", "Switch windows in reverse");
            AddTerm("关闭当前窗口", "Close the current window");
            AddTerm("按顺序切换窗口", "Cycle through windows in order");
            AddTerm("打开窗口系统菜单", "Open the window's system menu");
            AddTerm("截取当前窗口到剪贴板", "Copy the current window to the clipboard");
            AddTerm("任务管理器", "Task Manager");
            AddTerm("安全注意序列", "Secure attention sequence");
            AddTerm("以管理员身份运行", "Run as administrator");
            AddTerm("打开开始菜单", "Open the Start menu");
            AddTerm("新建文件夹", "Create a new folder");
            AddTerm("截取全屏到剪贴板", "Copy the whole screen to the clipboard");
            AddTerm("切换数字键盘锁定", "Toggle Num Lock");
            AddTerm("切换滚动锁定", "Toggle Scroll Lock");
            AddTerm("切换大写锁定", "Toggle Caps Lock");
        }

        /// <summary>
        /// 把适配器产出的中文「用途/模块/提示」短语翻译成当前语言（仅英文模式翻译，中文模式原样返回）。
        /// 支持少量动态模板（前缀式用途、Win+数字 的归属提示）。
        /// </summary>
        public static string Term(string zh)
        {
            if (string.IsNullOrEmpty(zh) || !IsEnglish) return zh;

            string en;
            if (Terms.TryGetValue(zh, out en)) return en;

            if (zh.StartsWith("[已禁用] ", StringComparison.Ordinal)) return "[disabled] " + Term(zh.Substring(6));
            if (zh.StartsWith("重映射 → ", StringComparison.Ordinal)) return "Remap → " + zh.Substring(6);
            if (zh.StartsWith("运行 ", StringComparison.Ordinal)) return "Run " + zh.Substring(3);
            if (zh.StartsWith("热键 ", StringComparison.Ordinal)) return "Hotkey " + zh.Substring(3);
            if (zh.StartsWith("配置集 ", StringComparison.Ordinal)) return "Profile set " + zh.Substring(4);
            if (zh.StartsWith("切换到超频配置 ", StringComparison.Ordinal)) return "Switch to OC profile " + zh.Substring(8);

            // Win+数字 的归属提示（动态数字）
            if (zh.StartsWith("启动/切换到任务栏第 ", StringComparison.Ordinal))
                return "Launch/switch to taskbar app " + TaskbarIndex(zh);
            if (zh.StartsWith("以管理员身份启动任务栏第 ", StringComparison.Ordinal))
                return "Launch taskbar app " + TaskbarIndex(zh) + " as administrator";
            if (zh.StartsWith("打开任务栏第 ", StringComparison.Ordinal))
            {
                if (zh.EndsWith("个应用的新实例", StringComparison.Ordinal))
                    return "Open taskbar app " + TaskbarIndex(zh) + " in a new instance";
                if (zh.EndsWith("个应用的跳转列表", StringComparison.Ordinal))
                    return "Open the jump list of taskbar app " + TaskbarIndex(zh);
            }
            return zh;
        }

        private static string TaskbarIndex(string zh)
        {
            int i = zh.IndexOf("第 ", StringComparison.Ordinal);
            if (i < 0) return "";
            i += 2;
            int j = zh.IndexOf(" 个应用", i, StringComparison.Ordinal);
            if (j < 0) j = zh.Length;
            return zh.Substring(i, j - i).Trim();
        }

        /// <summary>
        /// 适配器把「已禁用」标记拼在用途前面；判定不能写死中文，否则英文模式下逻辑会失效。
        /// </summary>
        public static bool IsDisabledPurpose(string purpose)
        {
            return purpose != null && purpose.StartsWith(T("cat.disabledPrefix"), StringComparison.Ordinal);
        }
    }
}
