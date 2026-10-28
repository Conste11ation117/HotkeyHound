using System;
using System.Collections.Generic;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Threading;
using Microsoft.Win32;

namespace HotkeyHound.Core
{
    /// <summary>三层观测的层级。顺序即判断顺序：设备层 → 钩子链 → 本程序窗口。</summary>
    public enum KeyMonitorLayer
    {
        RawInput = 0,
        LowLevelHook = 1,
        WindowMessage = 2
    }

    /// <summary>
    /// 三层观测的结论。前三项回答「在哪一层丢的」，Delivered 表示系统确实把它送到了某个接收者。
    /// Inconclusive 单独存在，是因为「全局热键 + 归属引擎没开」时无法诚实地给出确定结论。
    /// </summary>
    public enum KeyMonitorVerdict
    {
        Unknown = 0,
        NeverReachedSystem,
        SwallowedBeforeHook,
        SwallowedAfterHook,
        Delivered,
        Inconclusive
    }

    /// <summary>卡住的修饰键：系统状态与钩子观测不一致。</summary>
    public enum ModifierState
    {
        Ok = 0,
        /// <summary>系统说按着，钩子没看到按下（可能是监视开始前就按着，或键卡住了）。</summary>
        OsDownHookMissed,
        /// <summary>钩子看到按下、之后没看到松开，但系统说已松开（漏了抬起事件）。</summary>
        HookDownOsMissed
    }

    public sealed class KeyLayerEvent
    {
        public DateTime Time;
        public KeyMonitorLayer Layer;
        public uint Vk;
        public uint ScanCode;
        public uint Flags;
        public bool Down;

        public string LayerName { get { return KeyMonitorLogic.LayerName(Layer); } }
        public string KeyName { get { return VkNames.Name(Vk); } }

        public string Description
        {
            get
            {
                switch (Layer)
                {
                    case KeyMonitorLayer.RawInput:
                        return Down ? L10n.T("km.event.rawDown") : L10n.T("km.event.rawUp");
                    case KeyMonitorLayer.LowLevelHook:
                        return Down ? L10n.T("km.event.hookDown") : L10n.T("km.event.hookUp");
                    default:
                        return Down ? L10n.T("km.event.windowDown") : L10n.T("km.event.windowUp");
                }
            }
        }

        public string FlagsText
        {
            get { return KeyMonitorLogic.DescribeFlags(Layer, Flags); }
        }
    }

    public sealed class KeyMonitorVerdictRecord
    {
        public DateTime Time;
        public KeyCombo Combo;
        public uint Vk;
        public KeyMonitorVerdict Verdict;
        public string Evidence;
        public string Note;
        public string Owner;
        public bool SeenRaw;
        public bool SeenHook;
        public bool SeenWindow;
        public bool IsGlobalHotkey;
        public bool AttributionChecked;
        public bool GotAttribution;
        public uint ScanCode;

        public string VerdictName { get { return KeyMonitorLogic.VerdictName(Verdict); } }

        public string LayersText
        {
            get
            {
                return L10n.F("km.layers.value",
                    SeenRaw ? L10n.T("km.layers.yes") : L10n.T("km.layers.no"),
                    SeenHook ? L10n.T("km.layers.yes") : L10n.T("km.layers.no"),
                    SeenWindow ? L10n.T("km.layers.yes") : L10n.T("km.layers.no"));
            }
        }
    }

    public sealed class ModifierSnapshot
    {
        public int Index;
        public uint Vk;
        public string Name;
        public bool AsyncDown;
        public bool HookDown;
        public ModifierState State;

        public bool Stuck { get { return State != ModifierState.Ok; } }

        public string StateText
        {
            get
            {
                switch (State)
                {
                    case ModifierState.OsDownHookMissed: return L10n.T("km.mod.osDownHookMissed");
                    case ModifierState.HookDownOsMissed: return L10n.T("km.mod.hookDownOsMissed");
                    default: return L10n.T("km.mod.ok");
                }
            }
        }
    }

    /// <summary>Scancode Map 的一条映射：按下 Input 扫描码，系统产生 Output 扫描码。</summary>
    public sealed class ScanCodeMapping
    {
        public ushort Input;
        public ushort Output;

        public string InputName { get { return ScanCodeNames.Name(Input); } }
        public string OutputName { get { return ScanCodeNames.Name(Output); } }
    }

    /// <summary>键盘类过滤驱动（只读列出，不做判定，更不卸载）。</summary>
    public sealed class KeyboardFilterDriver
    {
        public string Name;
        public string Group;
        public string ImagePath;
        public int Start;
        public int Type;

        public string StartName
        {
            get
            {
                switch (Start)
                {
                    case 0: return L10n.T("km.driver.startBoot");
                    case 1: return L10n.T("km.driver.startSystem");
                    case 2: return L10n.T("km.driver.startAuto");
                    case 3: return L10n.T("km.driver.startDemand");
                    case 4: return L10n.T("km.driver.startDisabled");
                    default: return L10n.F("km.driver.startOther", Start);
                }
            }
        }
    }

    /// <summary>
    /// 按键监视的纯判定逻辑。全部写成不依赖真实键盘与钩子的函数，供 T9 直接喂构造数据。
    /// </summary>
    public static class KeyMonitorLogic
    {
        /// <summary>SHIFT、CTRL、ALT、WIN 各左右两个，顺序与 ModifierVks 一致。</summary>
        public static readonly uint[] ModifierVks = new uint[]
        {
            0xA2, 0xA3, 0xA4, 0xA5, 0xA0, 0xA1, 0x5B, 0x5C
        };

        public static readonly string[] ModifierNames = new string[]
        {
            "LCtrl", "RCtrl", "LAlt", "RAlt", "LShift", "RShift", "LWin", "RWin"
        };

        public static string LayerName(KeyMonitorLayer layer)
        {
            switch (layer)
            {
                case KeyMonitorLayer.RawInput: return L10n.T("km.layer.raw");
                case KeyMonitorLayer.LowLevelHook: return L10n.T("km.layer.hook");
                default: return L10n.T("km.layer.window");
            }
        }

        public static string VerdictName(KeyMonitorVerdict verdict)
        {
            switch (verdict)
            {
                case KeyMonitorVerdict.NeverReachedSystem: return L10n.T("km.verdict.neverReached");
                case KeyMonitorVerdict.SwallowedBeforeHook: return L10n.T("km.verdict.beforeHook");
                case KeyMonitorVerdict.SwallowedAfterHook: return L10n.T("km.verdict.afterHook");
                case KeyMonitorVerdict.Delivered: return L10n.T("km.verdict.delivered");
                case KeyMonitorVerdict.Inconclusive: return L10n.T("km.verdict.inconclusive");
                default: return L10n.T("km.verdict.unknown");
            }
        }

        /// <summary>
        /// 四分支判定：没 raw → 设备层就没上来；有 raw 没 hook → 钩子链之前被吞；
        /// 有 hook 没窗口、且是全局热键 → 看引擎 B 的归属；三层都有 → 送达。
        /// 不是全局热键又没有窗口消息时，按键只是送到了别的前台窗口，算 Delivered。
        /// </summary>
        public static KeyMonitorVerdict Decide(bool seenRaw, bool seenHook, bool seenWindow,
                                               bool isGlobalHotkey, bool attributionChecked, bool gotAttribution,
                                               out string evidence, out string note)
        {
            evidence = "";
            note = "";

            if (!seenRaw)
            {
                evidence = L10n.T("km.decide.never.evidence");
                note = L10n.T("km.decide.never.note");
                return KeyMonitorVerdict.NeverReachedSystem;
            }

            if (!seenHook)
            {
                evidence = L10n.T("km.decide.before.evidence");
                note = L10n.T("km.decide.before.note");
                return KeyMonitorVerdict.SwallowedBeforeHook;
            }

            if (seenWindow)
            {
                evidence = L10n.T("km.decide.delivered.evidence");
                note = L10n.T("km.decide.delivered.note");
                return KeyMonitorVerdict.Delivered;
            }

            if (!isGlobalHotkey)
            {
                evidence = L10n.T("km.decide.foreground.evidence");
                note = L10n.T("km.decide.foreground.note");
                return KeyMonitorVerdict.Delivered;
            }

            if (gotAttribution)
            {
                evidence = L10n.T("km.decide.attributed.evidence");
                note = L10n.T("km.decide.attributed.note");
                return KeyMonitorVerdict.Delivered;
            }

            if (!attributionChecked)
            {
                evidence = L10n.T("km.decide.inconclusive.evidence");
                note = L10n.T("km.decide.inconclusive.note");
                return KeyMonitorVerdict.Inconclusive;
            }

            evidence = L10n.T("km.decide.after.evidence");
            note = L10n.T("km.decide.after.note");
            return KeyMonitorVerdict.SwallowedAfterHook;
        }

        /// <summary>
        /// 解析 HKLM\SYSTEM\CurrentControlSet\Control\Keyboard Layout 的 Scancode Map。
        /// 格式：8 字节头 + DWORD 条数（含结尾的空项）+ 每条 DWORD（低字=输入，高字=输出）+ 空 DWORD。
        /// </summary>
        public static List<ScanCodeMapping> ParseScancodeMap(byte[] data)
        {
            var list = new List<ScanCodeMapping>();
            if (data == null || data.Length < 16) return list;

            int count;
            try { count = BitConverter.ToInt32(data, 8); }
            catch (Exception) { return list; }
            if (count <= 0 || count > 1024) return list;

            int entries = count - 1;
            int max = Math.Min(entries, (data.Length - 12) / 4);
            for (int i = 0; i < max; i++)
            {
                uint value = BitConverter.ToUInt32(data, 12 + i * 4);
                if (value == 0) break;
                ushort input = (ushort)(value & 0xFFFF);
                ushort output = (ushort)((value >> 16) & 0xFFFF);
                if (input == 0 || output == 0) continue;
                var m = new ScanCodeMapping();
                m.Input = input;
                m.Output = output;
                list.Add(m);
            }
            return list;
        }

        /// <summary>把「系统认为的修饰键状态」与「钩子看到的状态」逐项对照，找出卡住/漏事件的修饰键。</summary>
        public static ModifierSnapshot[] EvaluateModifiers(bool[] asyncDown, bool[] hookDown)
        {
            var list = new ModifierSnapshot[ModifierVks.Length];
            for (int i = 0; i < ModifierVks.Length; i++)
            {
                bool os = asyncDown != null && i < asyncDown.Length && asyncDown[i];
                bool hk = hookDown != null && i < hookDown.Length && hookDown[i];
                var s = new ModifierSnapshot();
                s.Index = i;
                s.Vk = ModifierVks[i];
                s.Name = ModifierNames[i];
                s.AsyncDown = os;
                s.HookDown = hk;
                if (os == hk) s.State = ModifierState.Ok;
                else s.State = os ? ModifierState.OsDownHookMissed : ModifierState.HookDownOsMissed;
                list[i] = s;
            }
            return list;
        }

        public static bool AnyStuck(ModifierSnapshot[] states)
        {
            if (states == null) return false;
            foreach (ModifierSnapshot s in states) if (s != null && s.Stuck) return true;
            return false;
        }

        public static bool IsModifierVk(uint vk)
        {
            for (int i = 0; i < ModifierVks.Length; i++) if (ModifierVks[i] == vk) return true;
            // 通用的 Shift/Ctrl/Alt 也可能由某些注入方式报出来
            return vk == 0x10 || vk == 0x11 || vk == 0x12;
        }

        public static int ModifierIndex(uint vk)
        {
            for (int i = 0; i < ModifierVks.Length; i++) if (ModifierVks[i] == vk) return i;
            return -1;
        }

        public static string DescribeFlags(KeyMonitorLayer layer, uint flags)
        {
            switch (layer)
            {
                case KeyMonitorLayer.RawInput:
                    {
                        var parts = new List<string>();
                        if ((flags & 0x0001) != 0) parts.Add("BREAK");
                        if ((flags & 0x0002) != 0) parts.Add("E0");
                        if ((flags & 0x0004) != 0) parts.Add("E1");
                        if ((flags & 0x0008) != 0) parts.Add("TERMSRV_SETLED");
                        if ((flags & 0x0010) != 0) parts.Add("TERMSRV_SHADOW");
                        return parts.Count == 0 ? "MAKE" : string.Join("|", parts.ToArray());
                    }
                case KeyMonitorLayer.LowLevelHook:
                    {
                        var parts = new List<string>();
                        if ((flags & 0x01) != 0) parts.Add("EXTENDED");
                        if ((flags & 0x02) != 0) parts.Add("LOWER_IL_INJECTED");
                        if ((flags & 0x10) != 0) parts.Add("INJECTED");
                        if ((flags & 0x20) != 0) parts.Add("ALTDOWN");
                        if ((flags & 0x80) != 0) parts.Add("UP");
                        return parts.Count == 0 ? "0" : string.Join("|", parts.ToArray());
                    }
                default:
                    {
                        var parts = new List<string>();
                        if ((flags & 0x01) != 0) parts.Add("EXTENDED");
                        if ((flags & 0x40) != 0) parts.Add("PREV_DOWN");
                        if ((flags & 0x80) != 0) parts.Add("UP");
                        return parts.Count == 0 ? "0" : string.Join("|", parts.ToArray());
                    }
            }
        }

        /// <summary>只对「像全局热键」的组合做 RegisterHotKey 试探，避免每敲一个字母都去注册一次。</summary>
        public static bool LooksLikeGlobalHotkey(KeyCombo combo)
        {
            if (combo == null) return false;
            if (combo.HasModifier) return true;
            return combo.Vk >= 0x70 && combo.Vk <= 0x87; // F1–F24
        }
    }

    /// <summary>
    /// 扫描码 → 可读键名。Scancode Map 是扫描码级别的，不能用 VK 表代替。
    /// </summary>
    public static class ScanCodeNames
    {
        private static readonly Dictionary<ushort, string> Names = Build();

        private static Dictionary<ushort, string> Build()
        {
            var d = new Dictionary<ushort, string>();
            d[0x01] = "Esc";
            string[] row1 = new string[] { "1", "2", "3", "4", "5", "6", "7", "8", "9", "0", "-", "=" };
            for (int i = 0; i < row1.Length; i++) d[(ushort)(0x02 + i)] = row1[i];
            d[0x0E] = "Backspace";
            d[0x0F] = "Tab";
            string row2 = "QWERTYUIOP";
            for (int i = 0; i < row2.Length; i++) d[(ushort)(0x10 + i)] = row2[i].ToString();
            d[0x1A] = "[";
            d[0x1B] = "]";
            d[0x1C] = "Enter";
            d[0x1D] = "LCtrl";
            string row3 = "ASDFGHJKL";
            for (int i = 0; i < row3.Length; i++) d[(ushort)(0x1E + i)] = row3[i].ToString();
            d[0x27] = ";";
            d[0x28] = "'";
            d[0x29] = "`";
            d[0x2A] = "LShift";
            d[0x2B] = "\\";
            string row4 = "ZXCVBNM";
            for (int i = 0; i < row4.Length; i++) d[(ushort)(0x2C + i)] = row4[i].ToString();
            d[0x33] = ",";
            d[0x34] = ".";
            d[0x35] = "/";
            d[0x36] = "RShift";
            d[0x37] = "Num*";
            d[0x38] = "LAlt";
            d[0x39] = "Space";
            d[0x3A] = "CapsLock";
            for (int i = 0; i < 10; i++) d[(ushort)(0x3B + i)] = "F" + (i + 1);
            d[0x44] = "F10";
            d[0x45] = "NumLock";
            d[0x46] = "ScrollLock";
            d[0x47] = "Num7";
            d[0x48] = "Num8";
            d[0x49] = "Num9";
            d[0x4A] = "Num-";
            d[0x4B] = "Num4";
            d[0x4C] = "Num5";
            d[0x4D] = "Num6";
            d[0x4E] = "Num+";
            d[0x4F] = "Num1";
            d[0x50] = "Num2";
            d[0x51] = "Num3";
            d[0x52] = "Num0";
            d[0x53] = "Num.";
            d[0x57] = "F11";
            d[0x58] = "F12";
            d[0xE01C] = "NumEnter";
            d[0xE01D] = "RCtrl";
            d[0xE035] = "Num/";
            d[0xE037] = "PrintScreen";
            d[0xE038] = "RAlt";
            d[0xE047] = "Home";
            d[0xE048] = "Up";
            d[0xE049] = "PgUp";
            d[0xE04B] = "Left";
            d[0xE04D] = "Right";
            d[0xE04F] = "End";
            d[0xE050] = "Down";
            d[0xE051] = "PgDn";
            d[0xE052] = "Ins";
            d[0xE053] = "Del";
            d[0xE05B] = "LWin";
            d[0xE05C] = "RWin";
            d[0xE05D] = "Apps";
            return d;
        }

        public static string Name(ushort code)
        {
            string n;
            if (Names.TryGetValue(code, out n)) return n;
            if ((code & 0xFF00) == 0xE000)
                return "E0 " + (code & 0xFF).ToString("X2", CultureInfo.InvariantCulture);
            return "0x" + code.ToString("X2", CultureInfo.InvariantCulture);
        }
    }

    /// <summary>
    /// 按键监视器：同时在三个层级观察同一次按键。
    ///
    /// 铁律：构造对象不装任何钩子；只有显式调用 Start() 才安装 WH_KEYBOARD_LL 与 Raw Input，
    /// Stop() 立刻卸载。本类只观察，绝不吞键、绝不改消息。
    /// </summary>
    public sealed class KeyMonitor : IDisposable
    {
        private const int WH_KEYBOARD_LL = 13;
        private const int WM_INPUT = 0x00FF;
        private const int WM_KEYDOWN = 0x0100;
        private const int WM_KEYUP = 0x0101;
        private const int WM_SYSKEYDOWN = 0x0104;
        private const int WM_SYSKEYUP = 0x0105;
        private const int WM_QUIT = 0x0012;

        private const uint RID_INPUT = 0x10000003;
        private const uint RIM_TYPEKEYBOARD = 1;
        private const uint RIDEV_INPUTSINK = 0x00000100;
        private const uint RIDEV_REMOVE = 0x00000001;

        private const int DefaultSettleMs = 1200;
        private const int WindowSettleMs = 400;

        private sealed class PendingPress
        {
            public uint Vk;
            public uint ScanCode;
            public KeyCombo Combo;
            public DateTime FirstSeen;
            public DateTime Deadline;
            public bool SeenRaw;
            public bool SeenHook;
            public bool SeenWindow;
            public bool Settled;

            public void See(KeyMonitorLayer layer, DateTime time)
            {
                if (time < FirstSeen) FirstSeen = time;
                switch (layer)
                {
                    case KeyMonitorLayer.RawInput: SeenRaw = true; break;
                    case KeyMonitorLayer.LowLevelHook: SeenHook = true; break;
                    default: SeenWindow = true; break;
                }
                int settle = SeenWindow ? WindowSettleMs : DefaultSettleMs;
                Deadline = FirstSeen.AddMilliseconds(settle);
            }
        }

        private readonly object _gate = new object();
        private readonly List<KeyLayerEvent> _events = new List<KeyLayerEvent>();
        private readonly List<KeyMonitorVerdictRecord> _verdicts = new List<KeyMonitorVerdictRecord>();
        private readonly List<PendingPress> _pending = new List<PendingPress>();
        private readonly bool[] _hookModifierDown = new bool[8];

        private Thread _thread;
        private uint _threadId;
        private volatile bool _ready;
        private volatile bool _rawReady;
        private volatile bool _hookReady;
        private volatile bool _stopRequested;
        private IntPtr _hook = IntPtr.Zero;
        private IntPtr _rawHwnd = IntPtr.Zero;
        private string _className;
        private WndProcDelegate _wndProc;
        private LowLevelKeyboardProc _hookProc;
        private Timer _timer;
        private ModifierSnapshot[] _lastModifiers = new ModifierSnapshot[0];

        private bool _shift, _ctrl, _alt, _win;

        public event Action<KeyLayerEvent> KeyEvent;
        public event Action<KeyMonitorVerdictRecord> VerdictReady;
        public event Action<ModifierSnapshot[]> ModifiersChanged;
        public event Action<string> Logged;

        /// <summary>该组合是不是系统级全局热键。为 null 时按「不是」处理。</summary>
        public Func<KeyCombo, bool> IsGlobalHotkey { get; set; }

        /// <summary>去引擎 B 的记录里找归属；第二个参数是「不早于这个时间」。</summary>
        public Func<KeyCombo, DateTime, AttributionRecord> AttributionLookup { get; set; }

        /// <summary>引擎 B 此刻是否在监听；没在监听就不能把「没有归属」当成吞键证据。</summary>
        public Func<bool> AttributionActive { get; set; }

        public string LastError { get; private set; }
        public bool IsRunning { get { return _thread != null && _thread.IsAlive; } }
        public bool RawReady { get { return _rawReady; } }
        public bool HookReady { get { return _hookReady; } }
        public IntPtr HookHandle { get { return _hook; } }
        public IntPtr RawWindow { get { return _rawHwnd; } }
        public uint ThreadId { get { return _threadId; } }

        public KeyLayerEvent[] Events
        {
            get { lock (_gate) return _events.ToArray(); }
        }

        public KeyMonitorVerdictRecord[] Verdicts
        {
            get { lock (_gate) return _verdicts.ToArray(); }
        }

        public ModifierSnapshot[] Modifiers
        {
            get { lock (_gate) return (ModifierSnapshot[])_lastModifiers.Clone(); }
        }

        private void Say(string message)
        {
            Action<string> h = Logged;
            if (h == null) return;
            try { h(message); }
            catch (Exception) { }
        }

        // ------------------------------------------------------------ 生命周期

        public bool Start()
        {
            if (IsRunning) return true;
            LastError = null;
            _stopRequested = false;
            _ready = false;
            _rawReady = false;
            _hookReady = false;
            lock (_gate)
            {
                _events.Clear();
                _verdicts.Clear();
                _pending.Clear();
                _lastModifiers = new ModifierSnapshot[0];
                // 上一轮残留的修饰键状态会让新一轮一开始就报出「卡住的修饰键」。
                Array.Clear(_hookModifierDown, 0, _hookModifierDown.Length);
            }
            _shift = _ctrl = _alt = _win = false;

            _thread = new Thread(MonitorThreadProc);
            _thread.IsBackground = true;
            _thread.Name = "HotkeyHound-KeyMonitor";
            _thread.Start();

            DateTime deadline = DateTime.Now.AddSeconds(6);
            while (!_ready && DateTime.Now < deadline && _thread.IsAlive) Thread.Sleep(20);
            if (!_ready)
            {
                if (LastError == null) LastError = L10n.T("km.error.timeout");
                Say(LastError);
                // 超时不等于线程没起来：它可能正卡在装钩子的路上，不 Stop 就会留下一条无人管理的钩子。
                Stop();
                return false;
            }
            if (!_hookReady && !_rawReady)
            {
                if (LastError == null) LastError = L10n.T("km.error.noLayers");
                Say(LastError);
                Stop();
                return false;
            }

            _timer = new Timer(OnTick, null, 120, 120);
            return true;
        }

        public void Stop()
        {
            _stopRequested = true;
            try
            {
                if (_timer != null) { _timer.Dispose(); _timer = null; }
            }
            catch (Exception) { }

            Thread t = _thread;
            uint tid = _threadId;

            // 与引擎 C 同理：线程还没建好消息队列时 PostThreadMessageW 会失败，WM_QUIT 丢失就变成
            // 「钩子留着、线程卡在 GetMessageW」。所以失败要重试，Join 超时后再补一轮。
            if (t != null && tid != 0) PostQuitWithRetry(t, tid, 600);

            if (t != null)
            {
                try
                {
                    if (!t.Join(4000))
                    {
                        if (tid != 0) PostQuitWithRetry(t, tid, 600);
                        if (!t.Join(1500)) Say(L10n.T("km.error.stopTimeout"));
                    }
                }
                catch (Exception) { }
            }
            // 线程还活着就绝不把它丢掉：丢掉引用等于允许再 Start 一次，机器上会留下两条钩子，
            // 而界面上只会看到后建的那一条。保留引用，IsRunning 仍为 true，调用方能据此提示用户。
            if (_thread != null && !_thread.IsAlive)
            {
                _thread = null;
                _threadId = 0;
            }
            _ready = false;
        }

        private static void PostQuitWithRetry(Thread t, uint tid, int budgetMs)
        {
            DateTime deadline = DateTime.Now.AddMilliseconds(budgetMs);
            while (t.IsAlive && DateTime.Now < deadline)
            {
                try
                {
                    if (NativeMethods.PostThreadMessageW(tid, WM_QUIT, IntPtr.Zero, IntPtr.Zero)) return;
                }
                catch (Exception) { return; }
                Thread.Sleep(40);
            }
        }

        public void Dispose()
        {
            Stop();
        }

        private void MonitorThreadProc()
        {
            uint classAtom = 0;
            try
            {
                _threadId = NativeMethods.GetCurrentThreadId();
                _className = "HotkeyHound.KeyMonitor." + _threadId.ToString(CultureInfo.InvariantCulture);
                _wndProc = new WndProcDelegate(RawWndProc);

                IntPtr hInstance = NativeMethods.GetModuleHandleW(null);
                var wc = new NativeMethods.WNDCLASSEX();
                wc.cbSize = Marshal.SizeOf(typeof(NativeMethods.WNDCLASSEX));
                wc.lpfnWndProc = Marshal.GetFunctionPointerForDelegate(_wndProc);
                wc.hInstance = hInstance;
                wc.lpszClassName = _className;
                classAtom = NativeMethods.RegisterClassExW(ref wc);
                if (classAtom == 0)
                {
                    LastError = L10n.F("km.error.class", Marshal.GetLastWin32Error());
                    Say(LastError);
                }
                else
                {
                    _rawHwnd = NativeMethods.CreateWindowExW(0, _className, "HotkeyHoundRawInput", 0,
                                                              0, 0, 0, 0, IntPtr.Zero, IntPtr.Zero,
                                                              hInstance, IntPtr.Zero);
                }

                if (_rawHwnd == IntPtr.Zero)
                {
                    LastError = L10n.F("km.error.window", Marshal.GetLastWin32Error());
                    Say(LastError);
                }
                else
                {
                    _rawReady = RegisterRawInput(_rawHwnd);
                    if (!_rawReady) Say(LastError);
                    else Say(L10n.F("km.log.rawReady", _rawHwnd.ToInt64().ToString("X")));
                }

                _hookProc = new LowLevelKeyboardProc(LowLevelProc);
                _hook = NativeMethods.SetWindowsHookExLL(WH_KEYBOARD_LL, _hookProc, IntPtr.Zero, 0);
                if (_hook == IntPtr.Zero)
                {
                    LastError = L10n.F("km.error.hook", Marshal.GetLastWin32Error());
                    Say(LastError);
                }
                else
                {
                    _hookReady = true;
                    Say(L10n.F("km.log.hookReady", _hook.ToInt64().ToString("X")));
                }

                _ready = true;

                NativeMethods.MSG msg;
                while (!_stopRequested)
                {
                    int r = NativeMethods.GetMessageW(out msg, IntPtr.Zero, 0, 0);
                    if (r <= 0) break;
                    NativeMethods.TranslateMessage(ref msg);
                    NativeMethods.DispatchMessageW(ref msg);
                }
            }
            catch (Exception ex)
            {
                LastError = L10n.F("km.error.thread", ex.Message);
                Say(LastError);
            }
            finally
            {
                _ready = false;
                try
                {
                    if (_hook != IntPtr.Zero)
                    {
                        NativeMethods.UnhookWindowsHookEx(_hook);
                        Say(L10n.F("km.log.hookUnloaded", _hook.ToInt64().ToString("X")));
                        _hook = IntPtr.Zero;
                    }
                }
                catch (Exception) { }
                _hookReady = false;

                try
                {
                    if (_rawHwnd != IntPtr.Zero)
                    {
                        var rid = new RAWINPUTDEVICE();
                        rid.usUsagePage = 0x01;
                        rid.usUsage = 0x06;
                        rid.dwFlags = RIDEV_REMOVE;
                        rid.hwndTarget = IntPtr.Zero;
                        KeyMonitorNative.RegisterRawInputDevices(new RAWINPUTDEVICE[] { rid }, 1,
                            (uint)Marshal.SizeOf(typeof(RAWINPUTDEVICE)));
                        NativeMethods.DestroyWindow(_rawHwnd);
                        _rawHwnd = IntPtr.Zero;
                    }
                }
                catch (Exception) { }
                _rawReady = false;

                try
                {
                    if (classAtom != 0 && _className != null)
                        KeyMonitorNative.UnregisterClassW(_className, NativeMethods.GetModuleHandleW(null));
                }
                catch (Exception) { }
            }
        }

        private bool RegisterRawInput(IntPtr hwnd)
        {
            var rid = new RAWINPUTDEVICE();
            rid.usUsagePage = 0x01;
            rid.usUsage = 0x06;
            rid.dwFlags = RIDEV_INPUTSINK;
            rid.hwndTarget = hwnd;
            if (KeyMonitorNative.RegisterRawInputDevices(new RAWINPUTDEVICE[] { rid }, 1,
                    (uint)Marshal.SizeOf(typeof(RAWINPUTDEVICE))))
                return true;
            LastError = L10n.F("km.error.raw", Marshal.GetLastWin32Error());
            return false;
        }

        // ------------------------------------------------------------ 三层观测

        private IntPtr RawWndProc(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam)
        {
            try
            {
                if (msg == WM_INPUT)
                {
                    HandleRawInput(lParam);
                    // WM_INPUT 必须交给 DefWindowProc 做清理，不能直接 return 0。
                    return NativeMethods.DefWindowProcW(hWnd, msg, wParam, lParam);
                }
            }
            catch (Exception) { }
            return NativeMethods.DefWindowProcW(hWnd, msg, wParam, lParam);
        }

        private void HandleRawInput(IntPtr rawInput)
        {
            if (rawInput == IntPtr.Zero) return;
            uint headerSize = (uint)Marshal.SizeOf(typeof(RAWINPUTHEADER));
            uint size = 0;
            if (KeyMonitorNative.GetRawInputData(rawInput, RID_INPUT, IntPtr.Zero, ref size, headerSize) != 0) return;
            if (size == 0) return;

            IntPtr buffer = Marshal.AllocHGlobal((int)size);
            try
            {
                uint got = KeyMonitorNative.GetRawInputData(rawInput, RID_INPUT, buffer, ref size, headerSize);
                if (got == uint.MaxValue || got == 0) return;
                var raw = (RAWINPUT)Marshal.PtrToStructure(buffer, typeof(RAWINPUT));
                if (raw.header.dwType != RIM_TYPEKEYBOARD) return;
                ushort flags = raw.keyboard.Flags;
                bool down = (flags & 0x0001) == 0;
                Observe(KeyMonitorLayer.RawInput, raw.keyboard.VKey, raw.keyboard.MakeCode,
                        flags, down, DateTime.Now);
            }
            finally { Marshal.FreeHGlobal(buffer); }
        }

        private IntPtr LowLevelProc(int nCode, IntPtr wParam, IntPtr lParam)
        {
            try
            {
                if (nCode >= 0)
                {
                    var data = (NativeMethods.KBDLLHOOKSTRUCT)Marshal.PtrToStructure(lParam, typeof(NativeMethods.KBDLLHOOKSTRUCT));
                    int wm = wParam.ToInt32();
                    bool down = wm == WM_KEYDOWN || wm == WM_SYSKEYDOWN;
                    bool up = wm == WM_KEYUP || wm == WM_SYSKEYUP;
                    if (down || up)
                    {
                        UpdateModifiers(data.vkCode, down, true);
                        Observe(KeyMonitorLayer.LowLevelHook, data.vkCode, data.scanCode, data.flags, down, DateTime.Now);
                    }
                }
            }
            catch (Exception) { }
            // 只观察，绝不吞键。
            return NativeMethods.CallNextHookEx(IntPtr.Zero, nCode, wParam, lParam);
        }

        private void Observe(KeyMonitorLayer layer, uint vk, uint scanCode, uint flags, bool down, DateTime time)
        {
            if (vk == 0) return;
            var e = new KeyLayerEvent();
            e.Time = time;
            e.Layer = layer;
            e.Vk = vk;
            e.ScanCode = scanCode;
            e.Flags = flags;
            e.Down = down;

            lock (_gate)
            {
                _events.Add(e);
                if (_events.Count > 2000) _events.RemoveAt(0);
            }
            Action<KeyLayerEvent> h = KeyEvent;
            if (h != null) { try { h(e); } catch (Exception) { } }

            if (layer == KeyMonitorLayer.RawInput || layer == KeyMonitorLayer.WindowMessage)
                UpdateModifiers(vk, down, false);

            if (down && !KeyMonitorLogic.IsModifierVk(vk)) NotePress(layer, vk, scanCode, time);
        }

        private void NotePress(KeyMonitorLayer layer, uint vk, uint scanCode, DateTime time)
        {
            lock (_gate)
            {
                PendingPress p = null;
                for (int i = _pending.Count - 1; i >= 0; i--)
                {
                    if (_pending[i].Vk == vk && !_pending[i].Settled)
                    {
                        p = _pending[i];
                        break;
                    }
                }
                if (p == null)
                {
                    p = new PendingPress();
                    p.Vk = vk;
                    p.ScanCode = scanCode;
                    p.FirstSeen = time;
                    p.Combo = BuildComboLocked(vk);
                    _pending.Add(p);
                }
                // 钩子层最清楚按下瞬间的修饰键状态，用它的组合键覆盖 Raw Input 先到时算出的临时值。
                if (layer == KeyMonitorLayer.LowLevelHook) p.Combo = BuildComboLocked(vk);
                p.See(layer, time);

                for (int i = _pending.Count - 1; i >= 0; i--)
                    if (time - _pending[i].FirstSeen > TimeSpan.FromSeconds(15)) _pending.RemoveAt(i);
            }
        }

        private void RecordWindowMessage(int msg, IntPtr wParam, IntPtr lParam)
        {
            bool down = msg == WM_KEYDOWN || msg == WM_SYSKEYDOWN;
            long bits = lParam.ToInt64();
            uint scan = (uint)((bits >> 16) & 0xFF);
            uint flags = (uint)((bits >> 24) & 0xFF);
            Observe(KeyMonitorLayer.WindowMessage, (uint)wParam.ToInt32(), scan, flags, down, DateTime.Now);
        }

        /// <summary>fromHook=false 表示这一层不是低层钩子：组合键状态要更新，但「钩子层是否看到按下」
        /// 这份观测只能由低层钩子写，否则 Raw Input/窗口消息会把漏事件掩盖掉。</summary>
        private void UpdateModifiers(uint vk, bool down, bool fromHook)
        {
            lock (_gate)
            {
                int idx = KeyMonitorLogic.ModifierIndex(vk);
                if (idx >= 0 && fromHook) _hookModifierDown[idx] = down;
            }
            switch (vk)
            {
                case 0x10: case 0xA0: case 0xA1: _shift = down; break;
                case 0x11: case 0xA2: case 0xA3: _ctrl = down; break;
                case 0x12: case 0xA4: case 0xA5: _alt = down; break;
                case 0x5B: case 0x5C: _win = down; break;
            }
        }

        private KeyCombo BuildComboLocked(uint vk)
        {
            return new KeyCombo(_win, _ctrl, _alt, _shift, vk);
        }

        // ------------------------------------------------------------ 判定

        private void OnTick(object state)
        {
            if (_stopRequested || !IsRunning) return;
            try
            {
                List<PendingPress> due = null;
                DateTime now = DateTime.Now;
                lock (_gate)
                {
                    for (int i = 0; i < _pending.Count; i++)
                    {
                        PendingPress p = _pending[i];
                        if (p.Settled || now < p.Deadline) continue;
                        if (due == null) due = new List<PendingPress>();
                        due.Add(p);
                    }
                }
                if (due != null)
                {
                    foreach (PendingPress p in due) Settle(p);
                }

                CheckModifiersPeriodic();
            }
            catch (Exception) { }
        }

        private void Settle(PendingPress p)
        {
            if (p == null) return;
            // 定时器的两次回调可能重叠：结算前先在锁里原子占位，避免同一个按下被结算两次、
            // 界面上出现两条重复结论。占位之后 p.Settled 同时也是「不要再接新事件」的标记。
            lock (_gate)
            {
                if (p.Settled) return;
                p.Settled = true;
            }

            bool attributionChecked = false;
            AttributionRecord attribution = null;
            bool isGlobal = false;
            try
            {
                if (IsGlobalHotkey != null && p.Combo != null) isGlobal = IsGlobalHotkey(p.Combo);
                if (isGlobal && AttributionActive != null && AttributionLookup != null)
                {
                    attributionChecked = AttributionActive();
                    if (attributionChecked)
                        attribution = AttributionLookup(p.Combo, p.FirstSeen.AddMilliseconds(-50));
                }
            }
            catch (Exception) { }

            string evidence, note;
            KeyMonitorVerdict verdict = KeyMonitorLogic.Decide(p.SeenRaw, p.SeenHook, p.SeenWindow,
                isGlobal, attributionChecked, attribution != null, out evidence, out note);
            if (attribution != null && !string.IsNullOrEmpty(attribution.ProcessName))
                note += " " + L10n.F("km.decide.owner", attribution.ProcessName, attribution.Pid);

            var record = new KeyMonitorVerdictRecord();
            record.Time = DateTime.Now;
            record.Combo = p.Combo;
            record.Vk = p.Vk;
            record.ScanCode = p.ScanCode;
            record.Verdict = verdict;
            record.Evidence = evidence;
            record.Note = note;
            record.SeenRaw = p.SeenRaw;
            record.SeenHook = p.SeenHook;
            record.SeenWindow = p.SeenWindow;
            record.IsGlobalHotkey = isGlobal;
            record.AttributionChecked = attributionChecked;
            record.GotAttribution = attribution != null;
            if (attribution != null)
                record.Owner = string.IsNullOrEmpty(attribution.ProcessName)
                    ? ("PID " + attribution.Pid.ToString(CultureInfo.InvariantCulture))
                    : attribution.ProcessName + " (PID " + attribution.Pid.ToString(CultureInfo.InvariantCulture) + ")";

            lock (_gate)
            {
                p.Settled = true;
                _verdicts.Add(record);
                if (_verdicts.Count > 200) _verdicts.RemoveAt(0);
            }

            Say(L10n.F("km.log.verdict", p.Combo == null ? VkNames.Name(p.Vk) : p.Combo.Display(),
                       KeyMonitorLogic.VerdictName(verdict)));
            Action<KeyMonitorVerdictRecord> h = VerdictReady;
            if (h != null) { try { h(record); } catch (Exception) { } }
        }

        private void CheckModifiersPeriodic()
        {
            ModifierSnapshot[] now = CheckModifiers();
            bool changed = _lastModifiers == null || _lastModifiers.Length != now.Length;
            if (!changed)
            {
                for (int i = 0; i < now.Length; i++)
                {
                    if (_lastModifiers[i] == null || _lastModifiers[i].State != now[i].State)
                    {
                        changed = true;
                        break;
                    }
                }
            }
            if (!changed) return;
            _lastModifiers = now;
            Action<ModifierSnapshot[]> h = ModifiersChanged;
            if (h != null) { try { h(now); } catch (Exception) { } }
        }

        /// <summary>逐个查左右 Ctrl/Alt/Shift/Win 的 GetAsyncKeyState，并与钩子观测对照。</summary>
        public ModifierSnapshot[] CheckModifiers()
        {
            var async = new bool[KeyMonitorLogic.ModifierVks.Length];
            for (int i = 0; i < async.Length; i++)
                async[i] = (KeyMonitorNative.GetAsyncKeyState((int)KeyMonitorLogic.ModifierVks[i]) & 0x8000) != 0;
            bool[] hook;
            lock (_gate) hook = (bool[])_hookModifierDown.Clone();
            return KeyMonitorLogic.EvaluateModifiers(async, hook);
        }

        // ------------------------------------------------------------ 窗口消息层

        /// <summary>
        /// 由 UI 层的窗口子类把 WM_KEYDOWN/UP 喂进来。只有主窗口聚焦时这一层才可能有事件。
        /// Core 不依赖 WinForms，所以子类化留在界面层。
        /// </summary>
        public void FeedWindowMessage(int msg, long wParam, long lParam)
        {
            bool down = msg == WM_KEYDOWN || msg == WM_SYSKEYDOWN;
            bool up = msg == WM_KEYUP || msg == WM_SYSKEYUP;
            if (!down && !up) return;
            uint scan = (uint)((lParam >> 16) & 0xFF);
            uint flags = (uint)((lParam >> 24) & 0xFF);
            Observe(KeyMonitorLayer.WindowMessage, (uint)wParam, scan, flags, down, DateTime.Now);
        }

        // ------------------------------------------------------------ 只读系统信息

        /// <summary>读 HKLM 的 Scancode Map（REG_BINARY）并解析；不存在时返回空列表。</summary>
        public static List<ScanCodeMapping> ReadScancodeMap()
        {
            try
            {
                using (RegistryKey key = Registry.LocalMachine.OpenSubKey(
                    @"SYSTEM\CurrentControlSet\Control\Keyboard Layout", false))
                {
                    if (key == null) return new List<ScanCodeMapping>();
                    byte[] data = key.GetValue("Scancode Map") as byte[];
                    if (data == null) return new List<ScanCodeMapping>();
                    return KeyMonitorLogic.ParseScancodeMap(data);
                }
            }
            catch (Exception) { return new List<ScanCodeMapping>(); }
        }

        /// <summary>
        /// 列出键盘类内核驱动供参考：Type=1、Start 不为 4（未禁用），且组名含 Keyboard
        /// 或驱动名含 kbd/keyboard/i8042/hidkbd。只列出，不判定、不停止、不卸载。
        /// </summary>
        public static List<KeyboardFilterDriver> ReadKeyboardDrivers()
        {
            var list = new List<KeyboardFilterDriver>();
            try
            {
                using (RegistryKey services = Registry.LocalMachine.OpenSubKey(
                    @"SYSTEM\CurrentControlSet\Services", false))
                {
                    if (services == null) return list;
                    foreach (string name in services.GetSubKeyNames())
                    {
                        try
                        {
                            using (RegistryKey sub = services.OpenSubKey(name, false))
                            {
                                if (sub == null) continue;
                                int type = ReadInt(sub.GetValue("Type"), -1);
                                if (type != 1) continue;
                                int start = ReadInt(sub.GetValue("Start"), -1);
                                if (start == 4) continue;
                                string group = sub.GetValue("Group") as string;
                                string image = sub.GetValue("ImagePath") as string;
                                if (!LooksLikeKeyboardDriver(name, group)) continue;
                                var d = new KeyboardFilterDriver();
                                d.Name = name;
                                d.Group = group ?? "";
                                d.ImagePath = image ?? "";
                                d.Start = start;
                                d.Type = type;
                                list.Add(d);
                            }
                        }
                        catch (Exception) { }
                    }
                }
            }
            catch (Exception) { }
            list.Sort(delegate (KeyboardFilterDriver a, KeyboardFilterDriver b)
            {
                return string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase);
            });
            return list;
        }

        private static bool LooksLikeKeyboardDriver(string name, string group)
        {
            if (!string.IsNullOrEmpty(group) && group.IndexOf("Keyboard", StringComparison.OrdinalIgnoreCase) >= 0)
                return true;
            if (string.IsNullOrEmpty(name)) return false;
            return name.IndexOf("kbd", StringComparison.OrdinalIgnoreCase) >= 0
                || name.IndexOf("keyboard", StringComparison.OrdinalIgnoreCase) >= 0
                || name.IndexOf("i8042", StringComparison.OrdinalIgnoreCase) >= 0
                || name.IndexOf("hidkbd", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static int ReadInt(object value, int fallback)
        {
            if (value == null) return fallback;
            try
            {
                if (value is int) return (int)value;
                int parsed;
                if (int.TryParse(Convert.ToString(value, CultureInfo.InvariantCulture), out parsed)) return parsed;
            }
            catch (Exception) { }
            return fallback;
        }
    }

    internal static class KeyMonitorNative
    {
        public const uint RID_INPUT = 0x10000003;

        [DllImport("user32.dll", SetLastError = true)]
        public static extern bool RegisterRawInputDevices(RAWINPUTDEVICE[] devices, uint count, uint size);

        [DllImport("user32.dll", SetLastError = true)]
        public static extern uint GetRawInputData(IntPtr rawInput, uint command, IntPtr data,
                                                  ref uint size, uint headerSize);

        [DllImport("user32.dll")]
        public static extern short GetAsyncKeyState(int vk);

        [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        public static extern bool UnregisterClassW(string className, IntPtr hInstance);
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct RAWINPUTDEVICE
    {
        public ushort usUsagePage;
        public ushort usUsage;
        public uint dwFlags;
        public IntPtr hwndTarget;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct RAWINPUTHEADER
    {
        public uint dwType;
        public uint dwSize;
        public IntPtr hDevice;
        public IntPtr wParam;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct RAWKEYBOARD
    {
        public ushort MakeCode;
        public ushort Flags;
        public ushort Reserved;
        public ushort VKey;
        public uint Message;
        public uint ExtraInformation;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct RAWINPUT
    {
        public RAWINPUTHEADER header;
        public RAWKEYBOARD keyboard;
    }
}
