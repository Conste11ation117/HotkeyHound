using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;

namespace HotkeyHound.Core
{
    public enum AutoProbeOutcome
    {
        Attributed,
        NoResponse,
        Skipped
    }

    public sealed class AutoProbePlanItem
    {
        public KeyCombo Combo;
        /// <summary>非 null 表示这一条不会真的按下，值就是给用户看的原因。</summary>
        public string SkipReason;
        public bool WillPress;
    }

    public sealed class AutoProbeRecord
    {
        public KeyCombo Combo;
        public AutoProbeOutcome Outcome;
        public string ProcessName;
        public int Pid;
        public string Note;
        /// <summary>计划阶段就决定要按、但实际没有按（dry-run 或中止）时为 true。</summary>
        public bool NotPressed;
        public DateTime Time;
    }

    public sealed class AutoProbeResult
    {
        public bool DryRun = true;
        public bool Aborted;
        public int Max;
        public DateTime Started;
        public DateTime Finished;
        public List<AutoProbePlanItem> Plan = new List<AutoProbePlanItem>();
        public List<AutoProbeRecord> Records = new List<AutoProbeRecord>();
        /// <summary>本次因黑名单/上限/中止/输入失败而没有按下的组合总数（不是代表样本数）。</summary>
        public int SkippedTotal;

        public int CountOf(AutoProbeOutcome outcome)
        {
            int n = 0;
            foreach (AutoProbeRecord r in Records) if (r.Outcome == outcome) n++;
            return n;
        }

        public int PlannedCount
        {
            get
            {
                int n = 0;
                foreach (AutoProbePlanItem p in Plan) if (p.WillPress) n++;
                return n;
            }
        }
    }

    /// <summary>
    /// 自动归属：把引擎 A 里「已占用但不知道主人」的组合逐个模拟按下，用引擎 B 的归属报告
    /// 把主人问出来。按下去没人响应（NoResponse）就是最强的钩子嫌疑信号。
    ///
    /// 安全边界：默认只做 dry-run；黑名单硬编码、不可放宽；Esc 立即中止；
    /// 只有调用方显式要求（CLI --yes / 界面确认框）时才会真的按下。
    /// </summary>
    public static class AutoProbe
    {
        public const int DefaultMax = 30;
        public const int HardMax = 200;
        public const int KeyGapMilliseconds = 400;
        public const int AttribWaitMilliseconds = 1200;
        private const int HoldMilliseconds = 60;
        private const uint VK_ESCAPE = 0x1B;

        /// <summary>计划阶段：从引擎 A 的已占用组合里挑出最多 max 个可安全模拟的组合。</summary>
        public static AutoProbeResult Plan(List<OccupancyResult> occupancy, int max)
        {
            var result = new AutoProbeResult();
            result.DryRun = true;
            result.Max = ClampMax(max);
            result.Started = DateTime.Now;

            int planned = 0;
            int omitted = 0;
            int occupiedTotal = 0;
            const int maxPerReason = 12;
            var reasonShown = new Dictionary<string, int>(StringComparer.Ordinal);
            var reasonTotal = new Dictionary<string, int>(StringComparer.Ordinal);
            var reasonOrder = new List<string>();

            foreach (OccupancyResult occ in occupancy)
            {
                if (occ == null || occ.Combo == null || !occ.Occupied) continue;
                occupiedTotal++;
                if (planned >= result.Max)
                {
                    omitted++;
                    continue;
                }
                var item = new AutoProbePlanItem();
                item.Combo = occ.Combo;
                item.SkipReason = ForbiddenReason(occ.Combo);
                item.WillPress = item.SkipReason == null;
                if (item.WillPress)
                {
                    planned++;
                    result.Plan.Add(item);
                    continue;
                }

                // 黑名单跳过记录只保留前 12 条同类样本，其余聚合成一条摘要，
                // 否则「全部含 Win 的组合」会把控制台和报告都撑爆。
                int total;
                reasonTotal.TryGetValue(item.SkipReason, out total);
                reasonTotal[item.SkipReason] = total + 1;
                int shown;
                reasonShown.TryGetValue(item.SkipReason, out shown);
                if (shown < maxPerReason)
                {
                    result.Plan.Add(item);
                    reasonShown[item.SkipReason] = shown + 1;
                }
                if (!reasonOrder.Contains(item.SkipReason)) reasonOrder.Add(item.SkipReason);
            }

            foreach (string reason in reasonOrder)
            {
                int total = reasonTotal[reason];
                int shown;
                reasonShown.TryGetValue(reason, out shown);
                if (total <= shown) continue;
                var item = new AutoProbePlanItem();
                item.Combo = null;
                item.SkipReason = L10n.F("auto.skip.moreReason", total - shown, reason);
                item.WillPress = false;
                result.Plan.Add(item);
            }

            if (omitted > 0)
            {
                var item = new AutoProbePlanItem();
                item.Combo = null;
                item.SkipReason = L10n.F("auto.skip.overMax", result.Max, omitted);
                item.WillPress = false;
                result.Plan.Add(item);
            }
            result.SkippedTotal = occupiedTotal - planned;
            result.Finished = DateTime.Now;
            return result;
        }

        /// <summary>dry-run：只生成计划与「将按/跳过」记录，一个键都不按。</summary>
        public static AutoProbeResult DryRun(List<OccupancyResult> occupancy, int max, LogSink log)
        {
            AutoProbeResult result = Plan(occupancy, max);
            result.DryRun = true;
            DateTime now = DateTime.Now;
            foreach (AutoProbePlanItem item in result.Plan)
            {
                var record = new AutoProbeRecord();
                record.Combo = item.Combo;
                record.Outcome = AutoProbeOutcome.Skipped;
                record.Time = now;
                if (item.WillPress)
                {
                    record.Note = L10n.T("auto.skip.dryRun");
                    record.NotPressed = true;
                }
                else
                {
                    record.Note = item.SkipReason;
                }
                result.Records.Add(record);
                if (log != null)
                {
                    if (item.WillPress) log.Write(L10n.F("auto.log.willPress", item.Combo.Display()));
                    else if (item.Combo == null) log.Write(L10n.F("auto.log.skipSummary", item.SkipReason));
                    else log.Write(L10n.F("auto.log.skip", item.Combo.Display(), item.SkipReason));
                }
            }
            result.Finished = DateTime.Now;
            SessionRecords.SetAutoProbe(result);
            return result;
        }

        /// <summary>
        /// 真的逐个按下。调用方必须先装好引擎 B 的钩子，并且已经取得用户同意。
        /// extraAbort 用于界面上的「中止」按钮；Esc 由本方法自行轮询。
        /// </summary>
        public static AutoProbeResult Run(AttributionEngine engine, List<OccupancyResult> occupancy, int max,
                                          LogSink log, Func<bool> extraAbort, Action<AutoProbeRecord> progress)
        {
            AutoProbeResult result = Plan(occupancy, max);
            result.DryRun = false;
            result.Started = DateTime.Now;

            void Add(AutoProbeRecord record)
            {
                result.Records.Add(record);
                if (progress != null)
                {
                    try { progress(record); }
                    catch (Exception) { }
                }
            }

            if (engine == null || !engine.IsRunning)
            {
                result.Aborted = true;
                result.SkippedTotal += result.PlannedCount;
                var record = new AutoProbeRecord();
                record.Outcome = AutoProbeOutcome.Skipped;
                record.Note = L10n.T("auto.skip.engineNotRunning");
                record.NotPressed = true;
                record.Time = DateTime.Now;
                Add(record);
                result.Finished = DateTime.Now;
                SessionRecords.SetAutoProbe(result);
                return result;
            }

            // 用户如果正按着 Esc 进来，先等它松开，避免一开始就误判为中止。
            WaitEscReleased(2000);

            bool abort = false;
            DateTime lastRelease = DateTime.MinValue;
            foreach (AutoProbePlanItem item in result.Plan)
            {
                if (abort || IsAbortRequested(extraAbort))
                {
                    abort = true;
                    result.Aborted = true;
                    if (item.WillPress) result.SkippedTotal++;
                    Add(SkippedRecord(item.Combo, L10n.T("auto.skip.aborted")));
                    continue;
                }
                if (!item.WillPress)
                {
                    var skipped = new AutoProbeRecord();
                    skipped.Combo = item.Combo;
                    skipped.Outcome = AutoProbeOutcome.Skipped;
                    skipped.Note = item.SkipReason;
                    skipped.Time = DateTime.Now;
                    Add(skipped);
                    continue;
                }

                int gap = KeyGapMilliseconds - (int)(DateTime.Now - lastRelease).TotalMilliseconds;
                if (gap > 0) Thread.Sleep(gap);

                if (log != null) log.Write(L10n.F("auto.log.press", item.Combo.Display()));
                DateTime pressedAt = DateTime.Now;
                string inputError;
                if (!SendCombo(item.Combo, out inputError))
                {
                    result.SkippedTotal++;
                    Add(SkippedRecord(item.Combo, L10n.F("auto.skip.inputFailed", inputError)));
                    continue;
                }

                AttributionRecord hit = WaitAttribution(engine, item.Combo, pressedAt, extraAbort);
                lastRelease = DateTime.Now;

                if (hit != null)
                {
                    var ok = new AutoProbeRecord();
                    ok.Combo = item.Combo;
                    ok.Outcome = AutoProbeOutcome.Attributed;
                    ok.ProcessName = hit.ProcessName;
                    ok.Pid = hit.Pid;
                    ok.Time = DateTime.Now;
                    ok.Note = L10n.F("auto.note.attributed", hit.Pid,
                                      string.IsNullOrEmpty(hit.ProcessPath) ? L10n.T("ui.at.unknownPath") : hit.ProcessPath);
                    Add(ok);
                    if (log != null) log.Write(L10n.F("auto.log.attributed", item.Combo.Display(), hit.ProcessName, hit.Pid));
                    continue;
                }

                if (IsAbortRequested(extraAbort))
                {
                    // 中止恰好发生在等待窗口里：这一条按过了但没等到归属，按「中止」记，不算 NoResponse。
                    abort = true;
                    result.Aborted = true;
                    result.SkippedTotal++;
                    Add(SkippedRecord(item.Combo, L10n.T("auto.skip.aborted")));
                    continue;
                }

                var miss = new AutoProbeRecord();
                miss.Combo = item.Combo;
                miss.Outcome = AutoProbeOutcome.NoResponse;
                miss.Time = DateTime.Now;
                miss.Note = L10n.T("auto.note.noResponse");
                Add(miss);
                if (log != null) log.Write(L10n.F("auto.log.noResponse", item.Combo.Display()));
            }

            result.Finished = DateTime.Now;
            SessionRecords.SetAutoProbe(result);
            return result;
        }

        public static int ClampMax(int max)
        {
            if (max <= 0) return DefaultMax;
            if (max > HardMax) return HardMax;
            return max;
        }

        /// <summary>
        /// 黑名单是硬编码的：这些组合按下去会切窗口、关程序或触发系统界面。
        /// 不提供配置项，也不允许调用方放宽。
        /// </summary>
        public static string ForbiddenReason(KeyCombo combo)
        {
            if (combo == null) return L10n.T("auto.skip.badCombo");
            if (combo.Win) return L10n.T("auto.skip.win");
            if (IsCombo(combo, false, false, true, false, 0x73)) return L10n.T("auto.skip.altF4");
            if (IsCombo(combo, false, false, true, false, 0x09)) return L10n.T("auto.skip.altTab");
            if (IsCombo(combo, false, false, true, false, 0x1B)) return L10n.T("auto.skip.altEsc");
            if (IsCombo(combo, false, false, true, false, 0x20)) return L10n.T("auto.skip.altSpace");
            if (IsCombo(combo, false, true, false, false, 0x1B)) return L10n.T("auto.skip.ctrlEsc");
            if (IsCombo(combo, false, true, true, false, 0x2E)) return L10n.T("auto.skip.ctrlAltDel");
            if (IsCombo(combo, false, true, false, true, 0x1B)) return L10n.T("auto.skip.ctrlShiftEsc");
            if (IsCombo(combo, false, true, true, false, 0x09)) return L10n.T("auto.skip.ctrlAltTab");
            if (IsCombo(combo, false, false, true, true, 0x09)) return L10n.T("auto.skip.altShiftTab");
            // 显卡驱动的屏幕旋转热键（Intel 默认 Ctrl+Alt+方向键）：按下去真的会转屏，
            // 用户会以为电脑坏了。这一条是实测 dry-run 里发现的，别删。
            if (combo.Ctrl && combo.Alt && !combo.Win && !combo.Shift &&
                (combo.Vk == 0x25 || combo.Vk == 0x26 || combo.Vk == 0x27 || combo.Vk == 0x28))
            {
                return L10n.T("auto.skip.displayRotate");
            }
            return null;
        }

        private static bool IsCombo(KeyCombo c, bool win, bool ctrl, bool alt, bool shift, uint vk)
        {
            return c.Win == win && c.Ctrl == ctrl && c.Alt == alt && c.Shift == shift && c.Vk == vk;
        }

        private static AutoProbeRecord SkippedRecord(KeyCombo combo, string note)
        {
            var record = new AutoProbeRecord();
            record.Combo = combo;
            record.Outcome = AutoProbeOutcome.Skipped;
            record.Note = note;
            record.NotPressed = true;
            record.Time = DateTime.Now;
            return record;
        }

        private static AttributionRecord WaitAttribution(AttributionEngine engine, KeyCombo combo, DateTime since,
                                                         Func<bool> extraAbort)
        {
            DateTime deadline = DateTime.Now.AddMilliseconds(AttribWaitMilliseconds);
            while (DateTime.Now < deadline)
            {
                if (IsAbortRequested(extraAbort)) return null;
                AttributionRecord match = FindRecord(engine, combo, since);
                if (match != null) return match;
                Thread.Sleep(40);
            }
            return FindRecord(engine, combo, since);
        }

        private static AttributionRecord FindRecord(AttributionEngine engine, KeyCombo combo, DateTime since)
        {
            if (engine == null) return null;
            string key = combo.Normalize();
            AttributionRecord[] records = engine.Records;
            foreach (AttributionRecord r in records)
            {
                if (r.Combo == null || r.Combo.Normalize() != key) continue;
                if (r.Time < since.AddMilliseconds(-200)) continue;
                return r;
            }
            return null;
        }

        private static bool IsAbortRequested(Func<bool> extraAbort)
        {
            if (IsEscDown()) return true;
            if (extraAbort != null)
            {
                try { return extraAbort(); }
                catch (Exception) { }
            }
            return false;
        }

        private static void WaitEscReleased(int timeoutMs)
        {
            DateTime deadline = DateTime.Now.AddMilliseconds(timeoutMs);
            while (DateTime.Now < deadline && IsEscDown()) Thread.Sleep(30);
        }

        private static bool IsEscDown()
        {
            try { return (GetAsyncKeyState(VK_ESCAPE) & 0x8000) != 0; }
            catch (Exception) { return false; }
        }

        // ------------------------------------------------------------ 合成按键

        private const uint KEYEVENTF_KEYUP = 0x0002;
        private const int INPUT_KEYBOARD = 1;

        [StructLayout(LayoutKind.Sequential)]
        private struct KEYBDINPUT
        {
            public ushort wVk;
            public ushort wScan;
            public uint dwFlags;
            public uint time;
            public IntPtr dwExtraInfo;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct MOUSEINPUT
        {
            public int dx;
            public int dy;
            public uint mouseData;
            public uint dwFlags;
            public uint time;
            public IntPtr dwExtraInfo;
        }

        /// <summary>
        /// SendInput 要求 cbSize 与真正的 sizeof(INPUT) 完全一致（x64 上是 40）：
        /// type 4 字节 + 4 字节填充 + 32 字节联合体。只声明 KEYBDINPUT 会得到 32，调用直接报 87。
        /// </summary>
        [StructLayout(LayoutKind.Explicit)]
        private struct INPUTUNION
        {
            [FieldOffset(0)] public MOUSEINPUT mi;
            [FieldOffset(0)] public KEYBDINPUT ki;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct INPUT
        {
            public int type;
            public INPUTUNION u;
        }

        [DllImport("user32.dll", SetLastError = true)]
        private static extern uint SendInput(uint count, INPUT[] inputs, int size);

        [DllImport("user32.dll")]
        private static extern short GetAsyncKeyState(uint vk);

        private static INPUT MakeKey(uint vk, bool up)
        {
            var input = new INPUT();
            input.type = INPUT_KEYBOARD;
            input.u.ki.wVk = (ushort)vk;
            input.u.ki.wScan = 0;
            input.u.ki.dwFlags = up ? KEYEVENTF_KEYUP : 0;
            input.u.ki.time = 0;
            input.u.ki.dwExtraInfo = IntPtr.Zero;
            return input;
        }

        private static bool SendCombo(KeyCombo combo, out string error)
        {
            error = null;
            try
            {
                var keys = new List<uint>();
                if (combo.Ctrl) keys.Add(0x11);
                if (combo.Alt) keys.Add(0x12);
                if (combo.Shift) keys.Add(0x10);
                if (combo.Win) keys.Add(0x5B);
                keys.Add(combo.Vk);

                var down = new List<INPUT>();
                foreach (uint vk in keys) down.Add(MakeKey(vk, false));
                INPUT[] downArr = down.ToArray();
                uint sentDown = SendInput((uint)downArr.Length, downArr, Marshal.SizeOf(typeof(INPUT)));

                Thread.Sleep(HoldMilliseconds);

                var up = new List<INPUT>();
                for (int i = keys.Count - 1; i >= 0; i--) up.Add(MakeKey(keys[i], true));
                INPUT[] upArr = up.ToArray();
                uint sentUp = SendInput((uint)upArr.Length, upArr, Marshal.SizeOf(typeof(INPUT)));

                if (sentDown != downArr.Length || sentUp != upArr.Length)
                {
                    error = L10n.F("auto.input.counts", sentDown, downArr.Length, sentUp, upArr.Length,
                                   Marshal.GetLastWin32Error());
                    return false;
                }
                return true;
            }
            catch (Exception ex)
            {
                error = ex.GetType().Name + ": " + ex.Message;
                return false;
            }
        }
    }
}
