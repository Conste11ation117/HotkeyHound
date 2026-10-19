using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;

namespace HotkeyHound.Core
{
    public enum HookVerdict
    {
        Unknown,
        SwallowedUpstream,
        SwallowedBeforeHotkey,
        Delivered,
        NotGlobal,
        /// <summary>前提条件缺失（引擎 B 没在监听 / 没跑过扫描），诚实地说「判不了」。</summary>
        Inconclusive
    }

    public sealed class HookCheckRecord
    {
        public DateTime Time;
        public KeyCombo Combo;
        public HookVerdict Verdict;
        public string Evidence;
        public string Note;

        public static string VerdictName(HookVerdict v)
        {
            switch (v)
            {
                case HookVerdict.SwallowedUpstream: return L10n.T("verdict.swallowedUpstream");
                case HookVerdict.SwallowedBeforeHotkey: return L10n.T("verdict.swallowedBeforeHotkey");
                case HookVerdict.Delivered: return L10n.T("verdict.delivered");
                case HookVerdict.NotGlobal: return L10n.T("verdict.notGlobal");
                case HookVerdict.Inconclusive: return L10n.T("verdict.inconclusive");
                default: return L10n.T("verdict.unknown");
            }
        }
    }

    /// <summary>
    /// 引擎 C 的判定规则。写成纯函数，自测可以直接喂事件把各分支都走一遍，
    /// 不依赖真实按键。
    ///
    /// 2.0.0 修正：旧版没有「引擎 B 是否在监听」「引擎 A 占用表是否存在」两个前提，
    /// 只要用户没先去「按键归属」页点开始监听，任何正常工作的热键都会被判成红色的
    /// 「疑似被钩子截胡」——这是误报。现在缺前提时诚实地返回 Inconclusive，
    /// 并在说明里写清缺的是什么、怎么补。
    /// </summary>
    public static class HookVerdictLogic
    {
        public static HookVerdict Decide(bool seenBySniffer, bool occupiedPerEngineA, bool gotAttribution,
                                         bool timedOut, bool attributionActive, bool scanDone,
                                         out string evidence, out string note)
        {
            if (!timedOut)
            {
                evidence = L10n.T("verdictLogic.pending.evidence");
                note = L10n.T("verdictLogic.pending.note");
                return HookVerdict.Unknown;
            }

            if (gotAttribution)
            {
                evidence = L10n.T("verdictLogic.delivered.evidence");
                note = L10n.T("verdictLogic.delivered.note");
                return HookVerdict.Delivered;
            }

            if (!seenBySniffer)
            {
                // LL 钩子根本没看到 → 上游截胡。这个结论不依赖引擎 B。
                evidence = L10n.T("verdictLogic.swallowedUpstream.evidence");
                note = L10n.T("verdictLogic.swallowedUpstream.note");
                return HookVerdict.SwallowedUpstream;
            }

            if (!scanDone)
            {
                evidence = L10n.T("verdictLogic.inconclusiveNoScan.evidence");
                note = L10n.T("verdictLogic.inconclusiveNoScan.note");
                return HookVerdict.Inconclusive;
            }

            if (!occupiedPerEngineA)
            {
                evidence = L10n.T("verdictLogic.notGlobal.evidence");
                note = L10n.T("verdictLogic.notGlobal.note");
                return HookVerdict.NotGlobal;
            }

            if (!attributionActive)
            {
                // 没有归属报告可能只是因为引擎 B 没在监听——不能据此说「被截胡」。
                evidence = L10n.T("verdictLogic.inconclusiveNoB.evidence");
                note = L10n.T("verdictLogic.inconclusiveNoB.note");
                return HookVerdict.Inconclusive;
            }

            evidence = L10n.T("verdictLogic.beforeHotkey.evidence");
            note = L10n.T("verdictLogic.beforeHotkey.note");
            return HookVerdict.SwallowedBeforeHotkey;
        }
    }

    /// <summary>一次布防会话；窗口结束后由 HookSniffer 结算。</summary>
    public sealed class ProbeSession
    {
        public KeyCombo Combo;
        public DateTime ArmedAt;
        public DateTime Deadline;
        public bool Seen;
        public DateTime SeenAt;
        public bool GotAttribution;
        public AttributionRecord Attribution;
        public bool LateAttribution;
        public bool OccupiedPerEngineA;
        /// <summary>结算时引擎 B 是否在监听（决定「无归属报告」能否推出「被截胡」）。</summary>
        public bool AttributionActive;
        /// <summary>结算时引擎 A 的占用表是否可用（没扫描过 → 无法判「不是全局热键」）。</summary>
        public bool ScanDone;
        public HookVerdict Verdict = HookVerdict.Unknown;
        public string Evidence;
        public string Note;
        public bool Settled;

        public double SecondsLeft
        {
            get
            {
                double left = (Deadline - DateTime.Now).TotalSeconds;
                return left < 0 ? 0 : left;
            }
        }
    }

    /// <summary>对照排除工作流里的一条快照（「记录本次对照」）。</summary>
    public sealed class ComparisonSnapshot
    {
        public DateTime Time;
        public string Label;
        public List<string> Lines = new List<string>();

        public string ToText()
        {
            var sb = new System.Text.StringBuilder();
            sb.Append(Time.ToString("HH:mm:ss")).Append("  ").Append(Label);
            foreach (string line in Lines) sb.Append("\r\n    · ").Append(line);
            return sb.ToString();
        }
    }

    /// <summary>
    /// 引擎 C：在独立线程上装 WH_KEYBOARD_LL 低层键盘钩子，记录按键；
    /// 布防某个组合后在时间窗内结合「是否看到 / 引擎 A 是否记为占用 / 引擎 B 是否报出归属」
    /// 给出四分支结论。它只记录与判定，绝不吞键，也绝不结束任何进程。
    /// </summary>
    public sealed class HookSniffer : IDisposable
    {
        private const int WH_KEYBOARD_LL = 13;
        private const int WM_KEYDOWN = 0x0100;
        private const int WM_KEYUP = 0x0101;
        private const int WM_SYSKEYDOWN = 0x0104;
        private const int WM_SYSKEYUP = 0x0105;
        private const int WM_QUIT = 0x0012;

        /// <summary>布防后的判定窗口。</summary>
        public static readonly TimeSpan ArmWindow = TimeSpan.FromSeconds(3);
        /// <summary>归属报告必须在这个时间内到达，超过就算「截胡」。</summary>
        public static readonly TimeSpan AttributionWindow = TimeSpan.FromSeconds(1.5);

        private readonly object _gate = new object();
        private readonly List<string> _recentKeys = new List<string>();
        private readonly List<HookCheckRecord> _records = new List<HookCheckRecord>();
        private readonly List<ComparisonSnapshot> _comparisons = new List<ComparisonSnapshot>();

        // 吞键检测可以开着跑很久（用户一边用电脑一边观察），列表必须有上限：
        // _recentKeys 早就限了 200 条，这两个却一直没有——长时间布防会一直堆内存，
        // 界面的列表也会越拉越卡。上限按「远超人类按键速度」定，超了丢最早的。
        private const int MaxRecords = 20000;
        private const int MaxComparisons = 500;

        private Thread _thread;
        private uint _threadId;
        private volatile bool _ready;
        private volatile bool _stopRequested;
        private IntPtr _hook = IntPtr.Zero;
        private Timer _timer;

        private bool _shift, _ctrl, _alt, _win;

        private ProbeSession _session;

        private static LowLevelKeyboardProc _procKeepAlive;

        public event Action<string> Logged;
        public event Action<string> KeySeen;
        public event Action<HookCheckRecord> VerdictReady;

        /// <summary>引擎 A 的占用表查询；没设置时按「未占用」处理。</summary>
        public Func<KeyCombo, bool> IsOccupiedByEngineA { get; set; }

        /// <summary>引擎 B（按键归属）当前是否在监听；没设置时按「未监听」处理（判定会诚实降级为无法判定）。</summary>
        public Func<bool> AttributionActive { get; set; }

        /// <summary>引擎 A 的占用表是否可用（跑过扫描）；没设置时按「不可用」处理。</summary>
        public Func<bool> ScanDone { get; set; }

        public string LastError { get; private set; }
        public bool IsRunning { get { return _thread != null && _thread.IsAlive; } }
        public bool IsArmed { get { return _ready; } }
        public IntPtr HookHandle { get { return _hook; } }
        public uint ThreadId { get { return _threadId; } }

        public ProbeSession Current
        {
            get { lock (_gate) return _session; }
        }

        public HookCheckRecord[] Records
        {
            get { lock (_gate) return _records.ToArray(); }
        }

        public ComparisonSnapshot[] Comparisons
        {
            get { lock (_gate) return _comparisons.ToArray(); }
        }

        public string[] RecentKeys
        {
            get { lock (_gate) return _recentKeys.ToArray(); }
        }

        public bool LastKeySeen { get; private set; }

        private void Say(string message)
        {
            Action<string> h = Logged;
            if (h != null)
            {
                try { h(message); }
                catch (Exception) { }
            }
        }

        public void Clear()
        {
            lock (_gate)
            {
                _records.Clear();
                _recentKeys.Clear();
                _comparisons.Clear();
                _session = null;
            }
        }

        // ------------------------------------------------------------ 钩子生命周期

        public bool Start()
        {
            if (IsRunning) return true;
            LastError = null;
            _stopRequested = false;
            _ready = false;

            _thread = new Thread(HookThreadProc);
            _thread.IsBackground = true;
            _thread.Name = "HotkeyHound-Sniffer";
            _thread.Start();

            DateTime deadline = DateTime.Now.AddSeconds(5);
            while (!_ready && DateTime.Now < deadline && _thread.IsAlive) Thread.Sleep(20);
            if (!_ready)
            {
                if (LastError == null) LastError = L10n.T("sniff.threadTimeout");
                Say(LastError);
                return false;
            }

            _timer = new Timer(OnTick, null, 100, 100);
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

            // PostThreadMessageW 在线程还没创建消息队列时会失败（WM_QUIT 丢失 → 线程永远阻塞在
            // GetMessageW 里 → 4 秒 Join 超时 + 钩子泄漏）。所以：只对活着的线程发，失败就重试，
            // Join 超时后再补发一轮。
            if (t != null && tid != 0) PostQuitWithRetry(t, tid, 600);

            if (t != null)
            {
                try
                {
                    if (!t.Join(4000))
                    {
                        if (tid != 0) PostQuitWithRetry(t, tid, 600);
                        if (!t.Join(1500)) Say(L10n.T("sniff.threadJoinTimeout"));
                    }
                }
                catch (Exception) { }
            }
            _thread = null;
            _threadId = 0;
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

        private void HookThreadProc()
        {
            try
            {
                _threadId = NativeMethods.GetCurrentThreadId();
                _procKeepAlive = new LowLevelKeyboardProc(LowLevelProc);
                _hook = NativeMethods.SetWindowsHookExLL(WH_KEYBOARD_LL, _procKeepAlive, IntPtr.Zero, 0);
                if (_hook == IntPtr.Zero)
                {
                    LastError = L10n.F("sniff.installFailed", Marshal.GetLastWin32Error());
                    Say(LastError);
                    return;
                }
                Say(L10n.F("sniff.installed", _hook.ToInt64().ToString("X"), _threadId));
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
                LastError = L10n.F("sniff.threadException", ex);
                Say(LastError);
            }
            finally
            {
                _ready = false;
                // 线程 ID 会被系统回收复用；不清零的话，下一次 Stop() 可能把 WM_QUIT
                // 发到一个无关线程上，把别人的消息循环干掉。
                _threadId = 0;
                try
                {
                    if (_hook != IntPtr.Zero)
                    {
                        NativeMethods.UnhookWindowsHookEx(_hook);
                        Say(L10n.F("sniff.unhooked", _hook.ToInt64().ToString("X")));
                        _hook = IntPtr.Zero;
                    }
                }
                catch (Exception) { }
            }
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
                    if (down || up) ObserveKey(data.vkCode, down);
                }
            }
            catch (Exception) { }
            // 绝不吞键
            return NativeMethods.CallNextHookEx(IntPtr.Zero, nCode, wParam, lParam);
        }

        private static bool IsModifierVk(uint vk)
        {
            switch (vk)
            {
                case 0x10: case 0x11: case 0x12:
                case 0xA0: case 0xA1: case 0xA2: case 0xA3: case 0xA4: case 0xA5:
                case 0x5B: case 0x5C:
                    return true;
                default: return false;
            }
        }

        private void ObserveKey(uint vk, bool down)
        {
            bool shift = _shift, ctrl = _ctrl, alt = _alt, win = _win;
            switch (vk)
            {
                case 0x10: case 0xA0: case 0xA1: shift = down; break;
                case 0x11: case 0xA2: case 0xA3: ctrl = down; break;
                case 0x12: case 0xA4: case 0xA5: alt = down; break;
                case 0x5B: case 0x5C: win = down; break;
            }
            _shift = shift; _ctrl = ctrl; _alt = alt; _win = win;

            if (!down || IsModifierVk(vk)) return;

            var combo = new KeyCombo(win, ctrl, alt, shift, vk);
            NoteKeySeen(combo);
        }

        /// <summary>自测用：直接喂一个按键事件，等价于 LL 钩子看到了它。</summary>
        public void FeedKey(KeyCombo combo)
        {
            NoteKeySeen(combo);
        }

        private void NoteKeySeen(KeyCombo combo)
        {
            if (combo == null) return;
            LastKeySeen = true;

            string text = combo.Display();
            lock (_gate)
            {
                _recentKeys.Add(DateTime.Now.ToString("HH:mm:ss") + "  " + text);
                if (_recentKeys.Count > 200) _recentKeys.RemoveAt(0);

                if (_session != null && !_session.Settled && _session.Combo.Normalize() == combo.Normalize())
                {
                    if (!_session.Seen)
                    {
                        _session.Seen = true;
                        _session.SeenAt = DateTime.Now;
                    }
                }
            }
            Action<string> h = KeySeen;
            if (h != null)
            {
                try { h(text); }
                catch (Exception) { }
            }
        }

        // ------------------------------------------------------------ 布防与判定

        public bool Arm(KeyCombo combo)
        {
            if (!IsRunning)
            {
                LastError = L10n.T("sniff.notReady");
                return false;
            }
            if (combo == null) return false;

            var session = new ProbeSession();
            session.Combo = combo;
            session.ArmedAt = DateTime.Now;
            session.Deadline = session.ArmedAt + ArmWindow;
            session.OccupiedPerEngineA = IsOccupiedByEngineA != null && IsOccupiedByEngineA(combo);

            lock (_gate) _session = session;
            Say(L10n.F("sniffer.arm.log", combo.Display(), ArmWindow.TotalSeconds,
                       session.OccupiedPerEngineA ? L10n.T("sniffer.arm.occupied") : L10n.T("sniffer.arm.free")));
            return true;
        }

        /// <summary>引擎 B 收到归属报告时调用，把报告接到当前会话上。</summary>
        public void NoteAttribution(AttributionRecord record)
        {
            if (record == null || record.Combo == null) return;
            lock (_gate)
            {
                ProbeSession s = _session;
                if (s == null || s.Settled) return;
                if (s.Combo.Normalize() != record.Combo.Normalize()) return;

                if (!s.Seen)
                {
                    // 引擎 B 报出来了，等价于「按键确实到达了系统」
                    s.Seen = true;
                    s.SeenAt = DateTime.Now;
                }
                if (DateTime.Now - s.SeenAt <= AttributionWindow)
                {
                    s.GotAttribution = true;
                    s.Attribution = record;
                }
                else
                {
                    s.LateAttribution = true;
                    s.Attribution = record;
                }
            }
        }

        private void OnTick(object state)
        {
            ProbeSession s;
            lock (_gate)
            {
                s = _session;
                if (s == null || s.Settled) return;
                if (DateTime.Now < s.Deadline) return;
                // 先在锁里占住结算权：Timer 回调在线程池上，可能有两个 tick 同时到期，
                // 旧实现会让同一个布防结算两次、报两条结论。
                s.Settled = true;
            }

            bool attributionActive = SafeFlag(delegate () { return AttributionActive != null && AttributionActive(); });
            bool scanDone = SafeFlag(delegate () { return ScanDone != null && ScanDone(); });

            string evidence, note;
            HookVerdict verdict = HookVerdictLogic.Decide(s.Seen, s.OccupiedPerEngineA, s.GotAttribution, true,
                                                          attributionActive, scanDone, out evidence, out note);
            if (s.LateAttribution)
            {
                note += L10n.F("verdictLogic.late", s.Attribution.ProcessName, AttributionWindow.TotalSeconds);
            }

            var record = new HookCheckRecord();
            record.Time = DateTime.Now;
            record.Combo = s.Combo;
            record.Verdict = verdict;
            record.Evidence = evidence;
            record.Note = note;

            lock (_gate)
            {
                s.Verdict = verdict;
                s.Evidence = evidence;
                s.Note = note;
                s.AttributionActive = attributionActive;
                s.ScanDone = scanDone;
                _records.Add(record);
                if (_records.Count > MaxRecords) _records.RemoveRange(0, _records.Count - MaxRecords);
            }

            Say(L10n.F("sniffer.verdict.log", s.Combo.Display(), HookCheckRecord.VerdictName(verdict)));

            Action<HookCheckRecord> h = VerdictReady;
            if (h != null)
            {
                try { h(record); }
                catch (Exception) { }
            }
        }

        private static bool SafeFlag(Func<bool> eval)
        {
            try { return eval(); }
            catch (Exception) { return false; }
        }

        /// <summary>提前结算当前布防（不改判定，只是立刻走一次结算）。</summary>
        public void SettleNow()
        {
            ProbeSession s;
            lock (_gate) s = _session;
            if (s == null || s.Settled) return;
            s.Deadline = DateTime.Now;
            OnTick(null);
        }

        // ------------------------------------------------------------ 对照排除

        /// <summary>
        /// 记录一条对照快照：当前所有结论 + 当前运行的候选进程。
        /// 工具只记录，绝不结束或暂停任何进程。
        /// </summary>
        public ComparisonSnapshot RecordComparison(string label, IEnumerable<string> runningCandidates)
        {
            var snap = new ComparisonSnapshot();
            snap.Time = DateTime.Now;
            snap.Label = string.IsNullOrEmpty(label) ? L10n.T("sniffer.compare.unnamed") : label;

            HookCheckRecord[] records = Records;
            if (records.Length == 0) snap.Lines.Add(L10n.T("sniffer.compare.none"));
            foreach (HookCheckRecord r in records)
            {
                snap.Lines.Add(string.Format("{0} → {1}", r.Combo.Display(), HookCheckRecord.VerdictName(r.Verdict)));
            }

            if (runningCandidates != null)
            {
                var list = new List<string>(runningCandidates);
                snap.Lines.Add(list.Count == 0
                    ? L10n.T("sniffer.compare.candNone")
                    : L10n.F("sniffer.compare.candList", list.Count, string.Join("、", list.ToArray())));
            }

            lock (_gate)
            {
                _comparisons.Add(snap);
                if (_comparisons.Count > MaxComparisons) _comparisons.RemoveRange(0, _comparisons.Count - MaxComparisons);
            }
            return snap;
        }
    }

    internal delegate IntPtr LowLevelKeyboardProc(int nCode, IntPtr wParam, IntPtr lParam);
}
