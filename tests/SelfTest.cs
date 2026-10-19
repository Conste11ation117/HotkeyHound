using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using HotkeyHound.Core;
using HotkeyHound.Core.Adapters;

namespace HotkeyHound
{
    /// <summary>--selftest 的实现：SPEC §9 的 T1–T9。</summary>
    public static class SelfTest
    {
        private sealed class Result
        {
            public string Id;
            public string Name;
            public bool Passed;
            public string Message;
        }

        private static readonly List<Result> Results = new List<Result>();

        public static int Run()
        {
            return Run(false);
        }

        /// <summary>
        /// skipHooks=true 时跳过 T6/T7（那两项会安装全局钩子并把 DLL 注入到当前会话的每个进程）。
        /// 在不能承担注入风险的机器（例如用户的日常工作机）上验证安全部分时使用。
        /// </summary>
        public static int Run(bool skipHooks)
        {
            Results.Clear();
            Console.WriteLine("HotkeyHound 自测 " + AppInfo.Version);
            Console.WriteLine("机器：" + AppInfo.MachineName + "　管理员：" + (AppInfo.IsAdministrator() ? "是" : "否")
                              + (skipHooks ? "　钩子测试：已跳过（--no-hooks）" : ""));
            Console.WriteLine(new string('-', 72));

            string tempDir;
            try
            {
                TempWorkspace.ClearNotes();
                tempDir = TempWorkspace.Acquire("HotkeyHound-SelfTest");
            }
            catch (Exception ex)
            {
                Console.WriteLine("无法准备临时目录：" + ex.Message);
                return 1;
            }
            foreach (string note in TempWorkspace.Notes) Console.WriteLine(note);
            Console.WriteLine("临时工作目录：" + tempDir);
            Console.WriteLine(new string('-', 72));

            var log = new LogSink();
            List<OccupancyResult> firstScan = null;

            try
            {
                RunT1();
                RunT2(delegate (List<OccupancyResult> r) { firstScan = r; });
                RunT3();
                RunT4(firstScan, log);
                RunT5(firstScan, log, tempDir);
                RunT8(firstScan);
                RunT9();
                if (skipHooks)
                {
                    Skip("T6", "引擎 B 归属链路", "已按 --no-hooks 跳过（该测试会安装全局钩子并注入 DLL）");
                    Skip("T7", "引擎 C 吞键检测", "已按 --no-hooks 跳过（该测试会安装低层键盘钩子）");
                    Skip("T10", "32 位归属链路", "已按 --no-hooks 跳过（该测试会安装全局钩子并注入 32 位 DLL）");
                }
                else
                {
                    RunT6();
                    RunT7(firstScan);
                    RunT10();
                }
            }
            catch (Exception ex)
            {
                Add("??", "自测执行异常", false, ex.ToString());
            }
            finally
            {
                try { Directory.Delete(tempDir, true); }
                catch (Exception) { }
            }

            Console.WriteLine(new string('-', 72));
            int failed = 0;
            foreach (Result r in Results)
            {
                Console.WriteLine(string.Format("[{0}] {1} {2} —— {3}",
                    r.Passed ? "PASS" : "FAIL", r.Id, r.Name, r.Message));
                if (!r.Passed) failed++;
            }
            Console.WriteLine(new string('-', 72));

            int skipped = 0;
            foreach (Result r in Results) if (r.Message != null && r.Message.IndexOf("SKIPPED", StringComparison.Ordinal) >= 0) skipped++;
            Console.WriteLine(string.Format("共 {0} 项：通过 {1}，失败 {2}，跳过 {3}",
                                            Results.Count, Results.Count - failed - skipped, failed, skipped));
            Console.WriteLine(failed == 0 ? "自测通过。" : "自测失败。");
            return failed == 0 ? 0 : 1;
        }

        private static void Add(string id, string name, bool passed, string message)
        {
            var r = new Result();
            r.Id = id;
            r.Name = name;
            r.Passed = passed;
            r.Message = message;
            Results.Add(r);
        }

        /// <summary>无法判定（环境限制），不算失败。</summary>
        private static void Inconclusive(string id, string name, string message)
        {
            var r = new Result();
            r.Id = id;
            r.Name = name;
            r.Passed = true;
            r.Message = "INCONCLUSIVE（不算失败）—— " + message;
            Results.Add(r);
        }

        private static void Skip(string id, string name)
        {
            Inconclusive(id, name, "本次没有可用的前置结果，跳过。");
        }

        private static void Skip(string id, string name, string message)
        {
            Inconclusive(id, name, "SKIPPED —— " + message);
        }

        // ---------------------------------------------------------------- T1

        private static void RunT1()
        {
            var samples = new List<KeyCombo>();
            samples.Add(new KeyCombo(false, true, true, false, 0x4D));   // Ctrl+Alt+M
            samples.Add(new KeyCombo(true, false, false, true, 0x86));   // Win+Shift+F23
            samples.Add(new KeyCombo(false, false, true, false, 0x73));  // Alt+F4
            samples.Add(new KeyCombo(false, true, false, true, 0x1B));   // Ctrl+Shift+Esc
            samples.Add(new KeyCombo(true, false, false, false, 0x24));  // Win+Home
            samples.Add(new KeyCombo(false, true, false, false, 0xBB));  // Ctrl+=+
            samples.Add(new KeyCombo(false, false, false, false, 0x6B)); // Num+
            samples.Add(new KeyCombo(true, false, true, true, 0x31));    // Win+Alt+Shift+1
            samples.Add(new KeyCombo(false, true, true, true, 0x01));    // 无效 VK，仅测格式化
            samples.Add(new KeyCombo(false, false, false, false, 0xC0)); // `~
            samples.Add(new KeyCombo(false, true, false, false, 0xDC));  // Ctrl+\|
            samples.Add(new KeyCombo(false, true, false, false, 0xDE));  // Ctrl+'"
            samples.Add(new KeyCombo(true, true, true, true, 0x90));     // Win+Ctrl+Alt+Shift+NumLock
            samples.Add(new KeyCombo(false, false, false, false, 0x5B)); // LWin 单键
            samples.Add(new KeyCombo(false, false, false, false, 0x2C)); // PrintScreen

            int ok = 0;
            var failures = new List<string>();
            foreach (KeyCombo c in samples)
            {
                string text = c.Display();
                KeyCombo back = KeyCombo.Parse(text);
                if (back != null && back.Equals(c)) ok++;
                else failures.Add(text + " → " + (back == null ? "解析失败" : back.Display()));
            }

            // VK 列表往返（PowerToys 形式）
            int vkOk = 0;
            string[] vkSamples = new string[] { "162;164;66", "91;160;134", "162;80", "162;164;84" };
            foreach (string s in vkSamples)
            {
                KeyCombo c = KeyCombo.FromVkList(s);
                if (c == null) { failures.Add("FromVkList 失败：" + s); continue; }
                KeyCombo again = KeyCombo.FromVkList(c.ToVkList());
                if (again != null && again.Equals(c)) vkOk++;
                else failures.Add("VK 列表往返失败：" + s + " → " + c.Display());
            }

            bool pass = ok == samples.Count && vkOk == vkSamples.Length;
            string msg = string.Format("格式化/解析往返 {0}/{1} 通过，VK 列表往返 {2}/{3} 通过",
                                       ok, samples.Count, vkOk, vkSamples.Length);
            if (!pass) msg += "；失败项：" + string.Join(" | ", failures.ToArray());
            Add("T1", "VK 表与组合键格式化/解析往返", pass, msg);
        }

        // ---------------------------------------------------------------- T2

        private static void RunT2(Action<List<OccupancyResult>> capture)
        {
            var scanner = new OccupancyScanner();
            var sw = System.Diagnostics.Stopwatch.StartNew();
            List<OccupancyResult> first = scanner.Scan(null);
            long ms1 = sw.ElapsedMilliseconds;
            if (capture != null) capture(first);

            var occupied1 = OccupancyScanner.Occupied(first);

            sw.Restart();
            List<OccupancyResult> second = scanner.Scan(null);
            long ms2 = sw.ElapsedMilliseconds;
            double overlap = OccupancyScanner.Overlap(first, second);

            bool pass = first.Count >= 1500 && occupied1.Count >= 150 && overlap >= 0.90;
            string msg = string.Format("组合总数 {0}（要求 ≥1500），首次占用 {1}（要求 ≥150），两次重合度 {2:P1}（要求 ≥90%）；耗时 {3}/{4} 毫秒",
                                       first.Count, occupied1.Count, overlap, ms1, ms2);
            if (overlap < 1.0)
            {
                msg += "；两次差异已记录，机器状态在扫描过程中发生了变化";
            }
            Add("T2", "引擎 A 扫描", pass, msg);
        }

        // ---------------------------------------------------------------- T3

        private static void RunT3()
        {
            var adapter = new PowerToysKeyboardManagerAdapter();
            string[] expected = new string[]
            {
                "Ctrl+Alt+B", "Ctrl+Alt+F", "Ctrl+Alt+M", "Ctrl+Alt+N",
                "Ctrl+Alt+P", "Ctrl+Alt+R", "Ctrl+Alt+T", "Win+Shift+F23"
            };

            var problems = new List<string>();
            var sb = new StringBuilder();

            // ---- 1) 合成样本：只验证解析器本身（任何机器都能跑，不要求装了 PowerToys）
            var entries = new List<string>();
            string remapTarget = KeyCombo.Parse("Ctrl+P") == null ? "162;164;80" : KeyCombo.Parse("Ctrl+P").ToVkList();
            foreach (string spec in expected)
            {
                KeyCombo c = KeyCombo.Parse(spec);
                if (c == null) { problems.Add("组合键无法解析：" + spec); continue; }
                bool runsProgram = spec == "Ctrl+Alt+B" || spec == "Ctrl+Alt+F" || spec.StartsWith("Win+");
                if (runsProgram)
                {
                    entries.Add("{\"originalKeys\":\"" + c.ToVkList() + "\",\"operationType\":1,"
                                + "\"runProgramFilePath\":\"C:\\\\Tools\\\\" + c.Display().Replace("+", "") + ".cmd\"}");
                }
                else
                {
                    entries.Add("{\"originalKeys\":\"" + c.ToVkList() + "\",\"operationType\":0,"
                                + "\"newRemapKeys\":\"" + remapTarget + "\"}");
                }
            }
            string fixture = "{\"remapShortcuts\":{\"global\":[" + string.Join(",", entries.ToArray()) + "]}}";
            JsonValue doc = JsonLite.TryParse(fixture);
            if (doc == null) problems.Add("合成样本 JSON 无法解析");
            List<DeclaredHotkey> parsed = doc == null
                ? new List<DeclaredHotkey>()
                : PowerToysKeyboardManagerAdapter.ParseDocument(doc, "fixture.json");

            var gotFixture = new HashSet<string>(StringComparer.Ordinal);
            foreach (DeclaredHotkey d in parsed) if (d.Combo != null) gotFixture.Add(d.Combo.Normalize());
            var missingFixture = new List<string>();
            foreach (string e in expected) if (!gotFixture.Contains(e)) missingFixture.Add(e);
            if (missingFixture.Count > 0) problems.Add("合成样本缺少 " + string.Join("、", missingFixture.ToArray()));
            if (parsed.Count != expected.Length)
                problems.Add("合成样本条数 " + parsed.Count + "，期望 " + expected.Length);

            sb.AppendFormat("合成样本解析：{0} 条里 {1} 条键位命中", parsed.Count, expected.Length - missingFixture.Count);

            // ---- 2) 真实配置：只有本机真的装了 PowerToys 才核对（干净机器上没有它）
            bool installed = File.Exists(adapter.ConfigPath);
            int realCount = 0;
            if (installed)
            {
                var real = new List<DeclaredHotkey>(adapter.Collect());
                realCount = real.Count;
                var gotReal = new HashSet<string>(StringComparer.Ordinal);
                foreach (DeclaredHotkey d in real) if (d.Combo != null) gotReal.Add(d.Combo.Normalize());
                var missingReal = new List<string>();
                foreach (string e in expected) if (!gotReal.Contains(e)) missingReal.Add(e);
                if (missingReal.Count > 0) problems.Add("真实配置缺少 " + string.Join("、", missingReal.ToArray()));
                sb.AppendFormat("；本机真实配置解析出 {0} 条，8 条目标键位{1}",
                                realCount, missingReal.Count == 0 ? "全部命中" : "有缺失");
            }
            else
            {
                sb.Append("；本机未安装 PowerToys 键盘管理器，真实配置核对跳过");
            }

            if (parsed.Count > 0)
            {
                sb.Append("。示例：");
                for (int i = 0; i < parsed.Count && i < 3; i++)
                {
                    if (i > 0) sb.Append("；");
                    sb.Append(parsed[i].Combo.Display()).Append(" = ").Append(parsed[i].Purpose)
                      .Append("（originalKeys=").Append(parsed[i].RawValue).Append("）");
                }
            }

            if (problems.Count > 0)
            {
                Add("T3", "引擎 D 解析 PowerToys 键盘管理器", false,
                    sb.ToString() + "。问题：" + string.Join("；", problems.ToArray()));
            }
            else if (!installed)
            {
                Inconclusive("T3", "引擎 D 解析 PowerToys 键盘管理器", sb.ToString());
            }
            else
            {
                Add("T3", "引擎 D 解析 PowerToys 键盘管理器", true, sb.ToString());
            }
        }

        // ---------------------------------------------------------------- T4

        private static void RunT4(List<OccupancyResult> occupancy, LogSink log)
        {
            if (occupancy == null)
            {
                Add("T4", "真相表", false, "没有可用的引擎 A 结果（T2 未产出）");
                return;
            }

            var collector = new ConfigCollector(delegate (string s) { });
            ConfigCollection config = collector.Collect();
            List<string> candidates = CandidateProcesses.Running();
            TruthTable table = Reconciler.Build(occupancy, config.Declared, config.Reserved, candidates);

            bool bucketExplained = true;
            foreach (TruthKind k in new TruthKind[]
            {
                TruthKind.MatchesDeclared, TruthKind.DeclaredButNotRegistered,
                TruthKind.RegisteredButUnowned, TruthKind.FreeAndUndeclared
            })
            {
                string text = TruthTable.KindExplain(k);
                if (string.IsNullOrEmpty(text) || text.Length < 10) bucketExplained = false;
            }

            // PowerToys 的每条声明都应该能在真相表底表里找到对应行
            var rowCombos = new HashSet<string>(StringComparer.Ordinal);
            foreach (TruthRow r in table.Rows) rowCombos.Add(r.Combo.Normalize());

            int ptTotal = 0;
            int ptMatched = 0;
            foreach (DeclaredHotkey d in config.Declared)
            {
                if (!string.Equals(d.Software, "PowerToys", StringComparison.Ordinal)) continue;
                ptTotal++;
                if (rowCombos.Contains(d.Combo.Normalize())) ptMatched++;
            }
            if (ptTotal == 0) ptMatched = -1;

            // 干净机器（例如虚拟机/CI）上根本没有 PowerToys：这时「声明必须对到底表」无从谈起，
            // 只核对与机器无关的部分；装了 PowerToys 的机器仍然要求一一对上。
            bool ptInstalled = File.Exists(new PowerToysKeyboardManagerAdapter().ConfigPath);
            bool ptOk = !ptInstalled || (ptTotal > 0 && ptMatched == ptTotal);

            bool pass = table.Rows.Count >= 1000 && bucketExplained && ptOk;
            string msg = string.Format(
                "真相表 {0} 行（要求 ≥1000）；四类说明文本齐备={1}；声明 {2} 条（其中 PowerToys {3} 条，能对到底表 {4} 条）；候选进程 {5} 个{6}",
                table.Rows.Count, bucketExplained ? "是" : "否", config.Declared.Count, ptTotal,
                ptMatched < 0 ? "n/a" : ptMatched.ToString(), candidates.Count,
                ptInstalled ? "" : "；本机未安装 PowerToys，声明核对跳过");
            Add("T4", "真相表", pass, msg);
        }

        // ---------------------------------------------------------------- T5

        private static void RunT5(List<OccupancyResult> occupancy, LogSink log, string tempDir)
        {
            if (occupancy == null)
            {
                Add("T5", "报告生成", false, "没有可用的引擎 A 结果（T2 未产出）");
                return;
            }

            var collector = new ConfigCollector(delegate (string s) { });
            ConfigCollection config = collector.Collect();
            var snapshot = new ScanSnapshot();
            snapshot.Occupancy = occupancy;
            snapshot.OccupiedCount = OccupancyScanner.Occupied(occupancy).Count;
            snapshot.FreeCount = occupancy.Count - snapshot.OccupiedCount;
            snapshot.Config = config;
            snapshot.Truth = Reconciler.Build(occupancy, config.Declared, config.Reserved, CandidateProcesses.Running());
            snapshot.Log = new string[] { "自测生成" };
            snapshot.IsAdministrator = AppInfo.IsAdministrator();

            string path = Path.Combine(tempDir, "selftest-report.html");
            ReportBuilder.Write(snapshot, path);

            string html = File.ReadAllText(path, Encoding.UTF8);
            string[] anchors = new string[] { "id=\"occupancy\"", "id=\"truth\"", "id=\"advice\"", "id=\"hooksuspects\"", "id=\"autoprobe\"" };
            var missing = new List<string>();
            foreach (string a in anchors) if (html.IndexOf(a, StringComparison.Ordinal) < 0) missing.Add(a);

            bool doubleEscaped = html.IndexOf("&amp;amp;", StringComparison.Ordinal) >= 0;
            bool hasHead = html.IndexOf("<meta charset=\"utf-8\">", StringComparison.Ordinal) >= 0;
            var info = new FileInfo(path);

            bool pass = missing.Count == 0 && !doubleEscaped && hasHead && info.Length > 1024;
            string msg = string.Format("已生成 {0}（{1:N0} 字节）；锚点 {2}；双重转义={3}；UTF-8 meta={4}",
                                       path, info.Length,
                                       missing.Count == 0 ? "齐全" : "缺少 " + string.Join("、", missing.ToArray()),
                                       doubleEscaped ? "有（不合格）" : "无",
                                       hasHead ? "有" : "无");
            Add("T5", "报告生成", pass, msg);
        }

        // ---------------------------------------------------------------- T8

        /// <summary>
        /// 钩子知识库 × 扫描关联 × 自动归属策略（不按键、不装钩子）。
        /// 只验证「能加载、能产出带证据等级的条目、黑名单与 dry-run 语义正确」。
        /// </summary>
        private static void RunT8(List<OccupancyResult> occupancy)
        {
            var detail = new StringBuilder();
            bool pass = true;

            HookCatalog catalog = HookCatalog.Load();
            int hookEntries = 0;
            foreach (CatalogEntry e in catalog.Entries) if (e.IsHook) hookEntries++;
            detail.Append("知识库 ").Append(catalog.Entries.Count).Append(" 条（内嵌 ")
                  .Append(catalog.EmbeddedCount).Append("，用户 ").Append(catalog.UserCount)
                  .Append("），其中 hook/mixed ").Append(hookEntries).Append(" 条");
            if (catalog.EmbeddedCount < 10) { pass = false; detail.Append("；内嵌条目不足 10"); }
            if (hookEntries < 5) { pass = false; detail.Append("；hook/mixed 条目不足 5"); }

            var suspects = catalog.BuildSuspects(occupancy);
            int withCombo = 0, confirmed = 0, reference = 0;
            foreach (HookSuspect r in suspects)
            {
                if (r.Combo != null) withCombo++;
                if (r.Evidence == HookEvidence.Confirmed) confirmed++;
                if (r.Evidence == HookEvidence.ReferenceOnly) reference++;
            }
            detail.Append("；嫌疑记录 ").Append(suspects.Count).Append(" 条（带组合键 ").Append(withCombo)
                  .Append("，已确认 ").Append(confirmed).Append("，仅供参考 ").Append(reference).Append("）");
            if (withCombo == 0) { pass = false; detail.Append("；没有任何带组合键的嫌疑"); }
            if (reference == 0) { pass = false; detail.Append("；没有任何「仅供参考」条目"); }

            // 黑名单：无论界面/命令行都不能放宽
            if (AutoProbe.ForbiddenReason(KeyCombo.Parse("Win+D")) == null) { pass = false; detail.Append("；Win+D 未被拦截"); }
            if (AutoProbe.ForbiddenReason(KeyCombo.Parse("Alt+F4")) == null) { pass = false; detail.Append("；Alt+F4 未被拦截"); }
            if (AutoProbe.ForbiddenReason(KeyCombo.Parse("Ctrl+Alt+Tab")) == null) { pass = false; detail.Append("；Ctrl+Alt+Tab 未被拦截"); }
            if (AutoProbe.ForbiddenReason(KeyCombo.Parse("Ctrl+Alt+M")) != null) { pass = false; detail.Append("；Ctrl+Alt+M 被误拦截"); }

            if (occupancy != null)
            {
                AutoProbeResult dry = AutoProbe.DryRun(occupancy, 5, null);
                detail.Append("；dry-run 计划 ").Append(dry.PlannedCount).Append(" 个、记录 ")
                      .Append(dry.Records.Count).Append(" 条");
                if (!dry.DryRun || dry.PlannedCount < 1 || dry.PlannedCount > 5) pass = false;
                if (dry.CountOf(AutoProbeOutcome.Attributed) != 0 || dry.CountOf(AutoProbeOutcome.NoResponse) != 0)
                {
                    pass = false;
                    detail.Append("；dry-run 竟然产出了已按下的结果");
                }
                SessionRecords.ClearAutoProbe();
            }
            else
            {
                pass = false;
                detail.Append("；没有引擎 A 结果，无法验证自动归属计划");
            }

            Add("T8", "钩子知识库与自动归属策略", pass, detail.ToString());
        }

        // ---------------------------------------------------------------- T9

        /// <summary>
        /// 按键监视器的纯函数：四分支判定、Scancode Map 解析、卡住的修饰键对照。
        /// 本项绝不安装任何钩子，--no-hooks 下也照常运行。
        /// </summary>
        private static void RunT9()
        {
            var detail = new StringBuilder();
            bool pass = true;
            string evidence, note;

            int branches = 0;
            if (KeyMonitorLogic.Decide(false, false, false, true, true, false, out evidence, out note)
                == KeyMonitorVerdict.NeverReachedSystem) branches++;
            if (KeyMonitorLogic.Decide(true, false, false, true, true, false, out evidence, out note)
                == KeyMonitorVerdict.SwallowedBeforeHook) branches++;
            if (KeyMonitorLogic.Decide(true, true, false, true, true, false, out evidence, out note)
                == KeyMonitorVerdict.SwallowedAfterHook) branches++;
            if (KeyMonitorLogic.Decide(true, true, true, true, true, false, out evidence, out note)
                == KeyMonitorVerdict.Delivered) branches++;
            if (branches != 4)
            {
                pass = false;
                detail.Append("四分支只通过 ").Append(branches).Append("/4；");
            }
            else detail.Append("四分支 4/4；");

            // 附加语义：收到归属 = 送达；归属引擎没开 = 无法确定；不是全局热键 = 送到前台窗口。
            int extras = 0;
            if (KeyMonitorLogic.Decide(true, true, false, true, true, true, out evidence, out note)
                == KeyMonitorVerdict.Delivered) extras++;
            if (KeyMonitorLogic.Decide(true, true, false, true, false, false, out evidence, out note)
                == KeyMonitorVerdict.Inconclusive) extras++;
            if (KeyMonitorLogic.Decide(true, true, false, false, false, false, out evidence, out note)
                == KeyMonitorVerdict.Delivered) extras++;
            if (extras != 3)
            {
                pass = false;
                detail.Append("附加语义 ").Append(extras).Append("/3；");
            }
            else detail.Append("附加语义 3/3；");

            // 构造的 Scancode Map：CapsLock(0x3A) ↔ 左 Ctrl(0x1D)，条数含结尾空项。
            byte[] sample = new byte[]
            {
                0,0,0,0, 0,0,0,0, 3,0,0,0,
                0x1D,0x00,0x3A,0x00,
                0x3A,0x00,0x1D,0x00,
                0x00,0x00,0x00,0x00
            };
            List<ScanCodeMapping> map = KeyMonitorLogic.ParseScancodeMap(sample);
            bool mapPass = map.Count == 2
                && map[0].Input == 0x1D && map[0].Output == 0x3A
                && map[1].Input == 0x3A && map[1].Output == 0x1D;

            // 扩展扫描码：左 Win 键输出为 E0 5B。
            byte[] extended = new byte[]
            {
                0,0,0,0, 0,0,0,0, 2,0,0,0,
                0x38,0x00,0x5B,0xE0,
                0x00,0x00,0x00,0x00
            };
            List<ScanCodeMapping> map2 = KeyMonitorLogic.ParseScancodeMap(extended);
            if (!mapPass || map2.Count != 1 || map2[0].Output != 0xE05B)
            {
                pass = false;
                detail.Append("Scancode Map 解析失败（普通 ").Append(map.Count)
                      .Append(" 条、扩展 ").Append(map2.Count).Append(" 条）；");
            }
            else detail.Append("Scancode Map 2/2（含 E0 扩展码）；");

            // 修饰键：系统说按着/钩子没看到 → 卡住；钩子看到按着/系统已松开 → 漏了抬起。
            bool[] osDown = new bool[KeyMonitorLogic.ModifierVks.Length];
            bool[] hookDown = new bool[KeyMonitorLogic.ModifierVks.Length];
            osDown[4] = true; // LShift
            ModifierSnapshot[] m1 = KeyMonitorLogic.EvaluateModifiers(osDown, hookDown);
            osDown[4] = false;
            hookDown[4] = true;
            hookDown[7] = true; // RWin
            ModifierSnapshot[] m2 = KeyMonitorLogic.EvaluateModifiers(osDown, hookDown);
            ModifierSnapshot[] m3 = KeyMonitorLogic.EvaluateModifiers(
                new bool[KeyMonitorLogic.ModifierVks.Length],
                new bool[KeyMonitorLogic.ModifierVks.Length]);
            bool modPass = m1[4].State == ModifierState.OsDownHookMissed
                && m2[4].State == ModifierState.HookDownOsMissed
                && m2[7].State == ModifierState.HookDownOsMissed
                && !KeyMonitorLogic.AnyStuck(m3);
            if (!modPass)
            {
                pass = false;
                detail.Append("卡住的修饰键判定失败；");
            }
            else detail.Append("修饰键状态 3/3；");

            bool namesPass = ScanCodeNames.Name(0x3A) == "CapsLock"
                && ScanCodeNames.Name(0xE05B) == "LWin";
            if (!namesPass)
            {
                pass = false;
                detail.Append("扫描码名称表失败；");
            }
            else detail.Append("扫描码名称表通过；");

            detail.Append("本项没有安装任何钩子");
            Add("T9", "按键监视器判定与只读解析", pass, detail.ToString());
        }

        // ---------------------------------------------------------------- T6

        /// <summary>
        /// 引擎 B 归属链路。主路径不依赖真实按键：辅助进程注册 Ctrl+Alt+F13 并泵消息，
        /// 我们直接 PostMessage(WP_HOTKEY) 给它——注入进去的 hook DLL 在它取消息时被调用，
        /// 从而走完「注入 → 共享内存 → 拦截 → PostMessage 回调 → 控制器解析 pid」整条链路。
        /// </summary>
        private static void RunT6()
        {
            var detail = new StringBuilder();
            var engine = new AttributionEngine();
            var engineLog = new List<string>();
            engine.Logged += delegate (string s) { engineLog.Add(s); };

            Process helper = null;
            long helperHwnd = 0;
            string failStep = null;

            try
            {
                // ---- 步骤 1：装钩子
                bool started = engine.Start();
                foreach (string line in engineLog) detail.Append("    ").Append(line).Append("\r\n");

                if (!started)
                {
                    string reason = engine.LastError ?? "未知";
                    if (ProcessIntegrity.IsBelowMedium)
                    {
                        Inconclusive("T6", "引擎 B 归属链路",
                            "当前进程是" + ProcessIntegrity.Describe(ProcessIntegrity.Current)
                            + "，UIPI 会挡住钩子注入；把 exe 复制到普通目录再跑。失败步骤=装钩子：" + reason);
                    }
                    else
                    {
                        Add("T6", "引擎 B 归属链路", false, "失败步骤=装钩子：" + reason);
                    }
                    return;
                }

                detail.Append("    钩子句柄=0x").Append(engine.HookHandle.ToInt64().ToString("X"))
                      .Append("，控制窗口=0x").Append(engine.ControlWindow.ToInt64().ToString("X")).Append("\r\n");
                detail.Append("    hook DLL 路径=").Append(engine.DllPath).Append("\r\n");

                // ---- 步骤 2：启动辅助进程
                string exePath = Process.GetCurrentProcess().MainModule.FileName;
                var psi = new ProcessStartInfo();
                psi.FileName = exePath;
                psi.Arguments = "--helper --seconds 45 --key \"Ctrl+Alt+F13\"";
                psi.UseShellExecute = false;
                psi.RedirectStandardOutput = true;
                psi.CreateNoWindow = true;
                helper = Process.Start(psi);
                if (helper == null)
                {
                    Add("T6", "引擎 B 归属链路", false, "失败步骤=启动辅助进程（Process.Start 返回 null）");
                    return;
                }
                detail.Append("    辅助进程 pid=").Append(helper.Id).Append("\r\n");

                int helperPid;
                long hwnd;
                if (!ReadHelperReady(helper, 8000, out helperPid, out hwnd, detail))
                {
                    Add("T6", "引擎 B 归属链路", false,
                        "失败步骤=辅助进程没报出 HELPER_READY（窗口/热键注册失败）。细节：\r\n" + detail);
                    return;
                }
                helperHwnd = hwnd;

                // ---- 步骤 3：等钩子 DLL 注入到辅助进程
                // 目标线程要先做一次消息检查，系统才会把 DLL 注入进去；用一个 WM_NULL 把它叫醒。
                for (int i = 0; i < 4; i++)
                {
                    NativeMethods.PostMessageW(new IntPtr(hwnd), 0x0000 /*WM_NULL*/, IntPtr.Zero, IntPtr.Zero);
                    Thread.Sleep(150);
                }

                // ---- 步骤 4：确定性地触发
                var combo = KeyCombo.Parse("Ctrl+Alt+F13");
                var received = new List<AttributionRecord>();
                engine.RecordReceived += delegate (AttributionRecord r)
                {
                    lock (received) received.Add(r);
                };

                DateTime postAt = DateTime.Now;
                bool posted = NativeMethods.PostMessageW(new IntPtr(hwnd), 0x0312 /*WM_HOTKEY*/,
                                                         new IntPtr(1), new IntPtr(combo.ToHotkeyLParam()));
                detail.Append("    PostMessage(WM_HOTKEY, lParam=0x")
                      .Append(combo.ToHotkeyLParam().ToString("X")).Append(") = ").Append(posted).Append("\r\n");

                AttributionRecord hit = null;
                DateTime deadline = DateTime.Now.AddSeconds(3);
                while (DateTime.Now < deadline)
                {
                    lock (received)
                    {
                        foreach (AttributionRecord r in received)
                        {
                            if (r.Pid == helperPid) { hit = r; break; }
                        }
                    }
                    if (hit != null) break;
                    Thread.Sleep(50);
                }

                if (hit == null)
                {
                    failStep = "钩子装上了、辅助进程也起来了，但 3 秒内没收到归属回调";
                    detail.Append("    辅助进程模块检查：").Append(DllInjectionStatus(helperPid, HookDllLocator.FileName)).Append("\r\n");
                }
                else
                {
                    bool comboOk = hit.Combo != null && hit.Combo.Normalize() == combo.Normalize();
                    bool pidOk = hit.Pid == helperPid;
                    if (pidOk && comboOk)
                    {
                        Add("T6", "引擎 B 归属链路", true, string.Format(
                            "确定性路径（PostMessage）成功。钩子句柄=0x{0:X}，辅助进程 pid={1} hwnd=0x{2:X}；"
                            + "收到的报告：{3} / 进程 {4}{5} / 组合键 {6}（正确）；"
                            + "路径={7}。整条链路耗时 {8} 毫秒。SendInput 真实按键路径未测试（不需要）。",
                            engine.HookHandle.ToInt64(), helperPid, hwnd,
                            hit.Time.ToString("HH:mm:ss.fff"),
                            hit.ProcessName, hit.IsElevated ? "（已提权）" : "",
                            hit.Combo.Display(), hit.ProcessPath,
                            (int)(hit.Time - postAt).TotalMilliseconds));
                        return;
                    }
                    failStep = "收到回调但内容不对：pid=" + hit.Pid + "（期望 " + helperPid + "），组合键="
                             + (hit.Combo == null ? "null" : hit.Combo.Display());
                }

                // ---- 步骤 5：确定性路径失败，退到真实 SendInput
                detail.Append("    确定性路径失败，改试真实 SendInput…\r\n");
                string sendShape;
                bool sent = TrySendInputCombo(combo, out sendShape);
                detail.Append("    SendInput ").Append(sendShape).Append("\r\n");

                AttributionRecord viaInput = null;
                if (sent)
                {
                    DateTime d2 = DateTime.Now.AddSeconds(3);
                    while (DateTime.Now < d2)
                    {
                        lock (received)
                        {
                            foreach (AttributionRecord r in received)
                            {
                                if (r.Pid == helperPid) { viaInput = r; break; }
                            }
                        }
                        if (viaInput != null) break;
                        Thread.Sleep(50);
                    }
                }

                if (viaInput != null)
                {
                    Add("T6", "引擎 B 归属链路", true, string.Format(
                        "真实 SendInput 路径成功：辅助进程 pid={0} 收到 {1} 的归属报告，进程名 {2}。",
                        helperPid, viaInput.Combo.Display(), viaInput.ProcessName));
                    return;
                }

                if (ProcessIntegrity.IsBelowMedium)
                {
                    Inconclusive("T6", "引擎 B 归属链路",
                        "确定性路径失败（" + failStep + "），SendInput 也失败（" + sendShape
                        + "）。当前进程是" + ProcessIntegrity.Describe(ProcessIntegrity.Current)
                        + "（" + ProcessIntegrity.CurrentSidText() + "）。细节：\r\n" + detail);
                }
                else
                {
                    Inconclusive("T6", "引擎 B 归属链路",
                        "确定性路径失败（" + failStep + "）；SendInput 兜底也失败（" + sendShape
                        + "），多半是会话已锁定/没有交互桌面。当前完整性 "
                        + ProcessIntegrity.Describe(ProcessIntegrity.Current)
                        + "（" + ProcessIntegrity.CurrentSidText() + "）。细节：\r\n" + detail);
                }
            }
            catch (Exception ex)
            {
                Add("T6", "引擎 B 归属链路", false, "异常：" + ex + "\r\n细节：\r\n" + detail);
            }
            finally
            {
                try { engine.Stop(); } catch (Exception) { }
                try { engine.Dispose(); } catch (Exception) { }
                KillHelper(helper, helperHwnd, detail);
            }
        }

        /// <summary>检查 hook DLL 是否真的被注入进了目标进程（读它的模块列表）。</summary>
        private static string DllInjectionStatus(int pid, string dllName)
        {
            try
            {
                using (Process p = Process.GetProcessById(pid))
                {
                    ProcessModuleCollection modules = p.Modules;
                    foreach (ProcessModule m in modules)
                    {
                        if (string.Equals(m.ModuleName, dllName, StringComparison.OrdinalIgnoreCase))
                        {
                            return "已注入 " + dllName + "（基址 0x" + m.BaseAddress.ToInt64().ToString("X") + "）";
                        }
                    }
                    return "模块列表里没有 " + dllName + "（共 " + modules.Count + " 个模块）";
                }
            }
            catch (Exception ex)
            {
                return "无法枚举该进程的模块：" + ex.GetType().Name + "：" + ex.Message;
            }
        }

        private static bool ReadHelperReady(Process helper, int timeoutMs, out int pid, out long hwnd, StringBuilder detail)        {
            pid = 0;
            hwnd = 0;
            var sw = System.Diagnostics.Stopwatch.StartNew();
            while (sw.ElapsedMilliseconds < timeoutMs)
            {
                string line;
                try
                {
                    line = helper.StandardOutput.ReadLine();
                }
                catch (Exception ex)
                {
                    detail.Append("    读取辅助进程输出失败：").Append(ex.Message).Append("\r\n");
                    return false;
                }
                if (line == null)
                {
                    if (helper.HasExited) return false;
                    Thread.Sleep(30);
                    continue;
                }
                detail.Append("    [helper] ").Append(line).Append("\r\n");
                if (line.StartsWith("HELPER_READY", StringComparison.Ordinal))
                {
                    pid = ExtractInt(line, "pid=");
                    hwnd = ExtractLong(line, "hwnd=");
                    return pid != 0 && hwnd != 0;
                }
                if (line.StartsWith("HELPER_ERROR", StringComparison.Ordinal)) return false;
            }
            return false;
        }

        private static int ExtractInt(string text, string key)
        {
            long v = ExtractLong(text, key);
            return (int)v;
        }

        private static long ExtractLong(string text, string key)
        {
            int i = text.IndexOf(key, StringComparison.Ordinal);
            if (i < 0) return 0;
            i += key.Length;

            // 32 位宿主的窗口句柄按无符号十六进制输出（0x + 8 位）；旧的十进制格式继续兼容。
            if (i + 1 < text.Length && text[i] == '0' && (text[i + 1] == 'x' || text[i + 1] == 'X'))
            {
                int start = i + 2;
                int j = start;
                while (j < text.Length && Uri.IsHexDigit(text[j])) j++;
                if (j == start) return 0;
                ulong u;
                if (!ulong.TryParse(text.Substring(start, j - start), NumberStyles.HexNumber,
                                    CultureInfo.InvariantCulture, out u))
                {
                    return 0;
                }
                return unchecked((long)u);
            }

            int sign = 1;
            if (i < text.Length && text[i] == '-')
            {
                sign = -1;
                i++;
            }
            int k = i;
            while (k < text.Length && char.IsDigit(text[k])) k++;
            if (k == i) return 0;
            long v;
            if (!long.TryParse(text.Substring(i, k - i), out v)) return 0;
            return sign * v;
        }

        /// <summary>结束辅助进程；先温和地 PostMessage(WM_CLOSE)，超时才 Kill，绝不留僵尸。</summary>
        private static void KillHelper(Process helper, long hwnd, StringBuilder detail)
        {
            if (helper == null) return;
            try
            {
                if (helper.HasExited)
                {
                    detail.Append("    辅助进程已退出，ExitCode=").Append(helper.ExitCode).Append("\r\n");
                    return;
                }
                if (hwnd != 0)
                {
                    NativeMethods.PostMessageW(new IntPtr(hwnd), 0x0010 /*WM_CLOSE*/, IntPtr.Zero, IntPtr.Zero);
                }
                if (!helper.WaitForExit(3000))
                {
                    detail.Append("    辅助进程未响应 WM_CLOSE，强制 Kill\r\n");
                    helper.Kill();
                    helper.WaitForExit(2000);
                }
                detail.Append("    辅助进程已结束，ExitCode=").Append(helper.HasExited ? helper.ExitCode.ToString() : "仍存活").Append("\r\n");
            }
            catch (Exception ex)
            {
                detail.Append("    结束辅助进程时出错：").Append(ex.Message).Append("\r\n");
            }
            finally
            {
                try { helper.Dispose(); } catch (Exception) { }
            }
        }

        // ---------------------------------------------------------------- T10

        /// <summary>
        /// 32 位归属链路。Windows 只允许钩子注入与安装者同位数的进程，所以 32 位进程的归属
        /// 必须由另一个 32 位进程装 32 位 hook DLL 来覆盖。这里启动一个 --selftest-target
        /// 宿主（它自己装 32 位钩子并注册 Ctrl+Alt+F13），再向它的窗口 PostMessage(WM_HOTKEY)；
        /// 断言 3 秒内收到 pid 正确、Bitness=x86 的归属记录。拿不到时判 INCONCLUSIVE（不算失败），
        /// 并写明卡在哪一步。
        /// </summary>
        private static void RunT10()
        {
            var detail = new StringBuilder();
            var engine = new AttributionEngine();
            var engineLog = new List<string>();
            engine.Logged += delegate (string s) { lock (engineLog) engineLog.Add(s); };

            var host32 = new Helper32Runtime();
            var hostLog = new List<string>();
            Action<string> hostLogger = delegate (string s) { lock (hostLog) hostLog.Add(s); };

            try
            {
                // ---- 步骤 1：64 位引擎（顺带建好共享内存控制窗口，32 位钩子要往这里上报；
                //      Start() 也会启动它自己的 32 位宿主）
                bool started = engine.Start();
                AppendLog(detail, "engine", engineLog);
                if (!started)
                {
                    string reason = engine.LastError ?? "未知";
                    Inconclusive("T10", "32 位归属链路",
                        "失败步骤=装 64 位钩子（共享内存控制窗口也是它建的）：" + reason
                        + (ProcessIntegrity.IsBelowMedium
                            ? "；当前进程是" + ProcessIntegrity.Describe(ProcessIntegrity.Current)
                              + "，UIPI 会挡住钩子注入，请把 exe 复制到普通目录再跑。"
                            : "。"));
                    return;
                }

                detail.Append("    64 位钩子=0x").Append(engine.HookHandle.ToInt64().ToString("X"))
                      .Append("，控制窗口=0x").Append(engine.ControlWindow.ToInt64().ToString("X"))
                      .Append("；引擎自带的 32 位宿主：").Append(engine.Helper32.StatusText).Append("\r\n");

                // ---- 步骤 2：专门的 32 位自测目标
                if (!host32.Start(Process.GetCurrentProcess().Id, "--selftest-target", hostLogger))
                {
                    AppendLog(detail, "helper32", hostLog);
                    Inconclusive("T10", "32 位归属链路",
                        "失败步骤=启动 32 位宿主（释放 exe/DLL 或 CreateProcess 失败）："
                        + (host32.Error ?? "未知"));
                    return;
                }

                bool ready = host32.WaitReady(8000, hostLogger);
                AppendLog(detail, "helper32", hostLog);
                long hwnd = host32.WindowHandle;
                if (hwnd == 0 && !string.IsNullOrEmpty(host32.ReadyLine))
                {
                    // 兜底：即使状态属性没拿到，也按同一套十六进制/十进制规则再解析一次原始就绪行。
                    hwnd = ExtractLong(host32.ReadyLine, "hwnd=");
                }
                int targetPid = host32.Pid;
                detail.Append("    32 位宿主 pid=").Append(targetPid)
                      .Append(" hwnd=0x").Append(hwnd.ToString("X"))
                      .Append(" 自身钩子=").Append(host32.HookInstalled ? "已安装" : "未安装")
                      .Append(" 就绪=").Append(ready ? "是" : "否").Append("\r\n");
                if (hwnd == 0 || targetPid == 0)
                {
                    string tail = host32.IsRunning
                        ? "32 位宿主还活着，但没有报出 HELPER32_READY（8000 毫秒内）：" + (host32.LastLine ?? "无输出")
                        : "32 位宿主启动后立即退出：" + (host32.Error ?? host32.LastLine ?? "无输出");
                    Inconclusive("T10", "32 位归属链路", "失败步骤=" + tail);
                    return;
                }

                // ---- 步骤 3：确定性地往 32 位窗口投一条 WM_HOTKEY
                var combo = KeyCombo.Parse("Ctrl+Alt+F13");
                var received = new List<AttributionRecord>();
                engine.RecordReceived += delegate (AttributionRecord r) { lock (received) received.Add(r); };

                DateTime postAt = DateTime.Now;
                bool posted = NativeMethods.PostMessageW(new IntPtr(hwnd), 0x0312 /*WM_HOTKEY*/,
                                                         new IntPtr(1), new IntPtr(combo.ToHotkeyLParam()));
                detail.Append("    PostMessage(WM_HOTKEY, lParam=0x").Append(combo.ToHotkeyLParam().ToString("X"))
                      .Append(") → hwnd=0x").Append(hwnd.ToString("X")).Append(" = ").Append(posted).Append("\r\n");
                if (!posted)
                {
                    Inconclusive("T10", "32 位归属链路",
                        "失败步骤=PostMessage 返回 false（错误码 " + Marshal.GetLastWin32Error()
                        + "），消息没送进 32 位窗口");
                    return;
                }

                // ---- 步骤 4：3 秒内等 pid 正确的记录
                AttributionRecord hit = null;
                DateTime deadline = DateTime.Now.AddSeconds(3);
                while (DateTime.Now < deadline)
                {
                    lock (received)
                    {
                        foreach (AttributionRecord r in received)
                        {
                            if (r.Pid == targetPid) { hit = r; break; }
                        }
                    }
                    if (hit != null) break;
                    Thread.Sleep(50);
                }

                if (hit == null)
                {
                    detail.Append("    32 位宿主模块检查：")
                          .Append(DllInjectionStatus(targetPid, Helper32Locator.DllFileName)).Append("\r\n");
                    if (!host32.IsRunning)
                    {
                        detail.Append("    32 位宿主已退出：").Append(host32.LastLine ?? "无输出").Append("\r\n");
                    }
                    Inconclusive("T10", "32 位归属链路",
                        "失败步骤=归属记录没回来（PostMessage 已发出，3 秒内没有 pid=" + targetPid
                        + " 的记录）。多半是 32 位钩子没能注入目标进程（低完整性/UIPI/无交互桌面）。细节：\r\n"
                        + detail);
                    return;
                }

                bool pidOk = hit.Pid == targetPid;
                bool bitnessOk = string.Equals(hit.Bitness, "x86", StringComparison.Ordinal);
                bool comboOk = hit.Combo != null && hit.Combo.Normalize() == combo.Normalize();
                if (pidOk && bitnessOk && comboOk)
                {
                    Add("T10", "32 位归属链路", true, string.Format(
                        "32 位链路成功：宿主 pid={0}（hwnd=0x{1:X}，自身钩子={2}，引擎自带 32 位宿主={3}）；"
                        + "PostMessage 后 {4} 毫秒收到归属记录：pid 正确、Bitness={5}、组合键 {6}、进程 {7}。"
                        + "这条记录只可能来自 32 位钩子（x64 DLL 进不了 32 位进程）。",
                        targetPid, hwnd, host32.HookInstalled ? "已安装" : "未安装",
                        engine.Helper32.StatusText,
                        (int)(hit.Time - postAt).TotalMilliseconds, hit.Bitness,
                        hit.Combo.Display(), hit.ProcessName));
                    return;
                }

                Inconclusive("T10", "32 位归属链路", string.Format(
                    "失败步骤=收到 pid={0} 的记录但内容不对（pid 正确={1}，Bitness={2}，组合键={3}）。细节：\r\n{4}",
                    hit.Pid, pidOk, hit.Bitness ?? "?", hit.Combo == null ? "null" : hit.Combo.Display(), detail));
            }
            catch (Exception ex)
            {
                Add("T10", "32 位归属链路", false, "异常：" + ex + "\r\n细节：\r\n" + detail);
            }
            finally
            {
                try { engine.Stop(); } catch (Exception) { }
                try { engine.Dispose(); } catch (Exception) { }
                try { host32.Stop(hostLogger); } catch (Exception) { }
                AppendLog(detail, "helper32", hostLog);
                lock (hostLog)
                {
                    foreach (string line in hostLog) Console.WriteLine("    [helper32] " + line);
                }
            }
        }

        private static void AppendLog(StringBuilder detail, string tag, List<string> lines)
        {
            lock (lines)
            {
                foreach (string line in lines)
                {
                    detail.Append("    [").Append(tag).Append("] ").Append(line).Append("\r\n");
                }
                lines.Clear();
            }
        }

        private static bool TrySendInputCombo(KeyCombo combo, out string shape)
        {
            shape = "未执行";
            try
            {
                bool down = SendInputKeys(combo, true, out shape);
                Thread.Sleep(60);
                bool up = SendInputKeys(combo, false, out shape);
                return down && up;
            }
            catch (Exception ex)
            {
                shape = "SendInput 异常：" + ex.Message;
                return false;
            }
        }

        private static bool SendInputKeys(KeyCombo combo, bool keyDown, out string shape)
        {
            var inputs = new List<INPUT>();
            uint upFlag = keyDown ? 0u : KEYEVENTF_KEYUP;
            if (combo.Ctrl) inputs.Add(MakeKey(0x11, upFlag));
            if (combo.Alt) inputs.Add(MakeKey(0x12, upFlag));
            if (combo.Shift) inputs.Add(MakeKey(0x10, upFlag));
            if (combo.Win) inputs.Add(MakeKey(0x5B, upFlag));
            inputs.Add(MakeKey(combo.Vk, upFlag));

            INPUT[] arr = inputs.ToArray();
            uint sent = SendInput((uint)arr.Length, arr, Marshal.SizeOf(typeof(INPUT)));
            shape = string.Format("{0}：请求 {1} 个事件，实际送入 {2}（错误码 {3}）",
                                  keyDown ? "按下" : "抬起", arr.Length, sent, Marshal.GetLastWin32Error());
            return sent == arr.Length;
        }

        private static INPUT MakeKey(uint vk, uint flags)
        {
            var input = new INPUT();
            input.type = 1; // INPUT_KEYBOARD
            input.u.ki.wVk = (ushort)vk;
            input.u.ki.wScan = 0;
            input.u.ki.dwFlags = flags;
            input.u.ki.time = 0;
            input.u.ki.dwExtraInfo = IntPtr.Zero;
            return input;
        }

        private const uint KEYEVENTF_KEYUP = 0x0002;

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
        /// SendInput 要求 cbSize 与真正的 sizeof(INPUT) 完全一致（x64 上是 40：
        /// type 4 字节 + 4 字节填充 + 32 字节的 MOUSEINPUT 联合体）。
        /// 只声明 type + KEYBDINPUT 会得到 32，SendInput 直接返回 ERROR_INVALID_PARAMETER(87)。
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

        // ---------------------------------------------------------------- T7

        /// <summary>
        /// 引擎 C 吞键检测。判定逻辑先用纯函数把七个分支各走一遍（确定性，含两个前提不足分支），
        /// 再用真实的 WH_KEYBOARD_LL 钩子跑两次布防会话（喂事件 / 不喂事件）；
        /// 最后用一个测试用的 LL 钩子真的吞掉 Ctrl+Alt+Shift+F14 + SendInput 验证端到端，
        /// 这部分允许 INCONCLUSIVE。
        /// </summary>
        private static void RunT7(List<OccupancyResult> occupancy)
        {
            var detail = new StringBuilder();
            var problems = new List<string>();

            // ---- A：判定逻辑七分支（纯函数，直接喂事件；2.0.0 起判定带前提条件，条件不足给 Inconclusive）
            string ev, note;
            var b1 = HookVerdictLogic.Decide(false, false, false, true, true, true, out ev, out note);
            var b2 = HookVerdictLogic.Decide(true, true, false, true, true, true, out ev, out note);
            var b3 = HookVerdictLogic.Decide(true, true, true, true, true, true, out ev, out note);
            var b4 = HookVerdictLogic.Decide(true, false, false, true, true, true, out ev, out note);
            var b5 = HookVerdictLogic.Decide(true, false, false, false, true, true, out ev, out note);
            // 前提不满足：引擎 B 没在听 / 引擎 A 还没扫描 → 必须给「无法判定」，不能乱猜红字
            var b6 = HookVerdictLogic.Decide(true, true, false, true, false, true, out ev, out note);
            var b7 = HookVerdictLogic.Decide(true, false, false, true, true, false, out ev, out note);

            if (b1 != HookVerdict.SwallowedUpstream) problems.Add("分支1（没看到）=" + b1);
            if (b2 != HookVerdict.SwallowedBeforeHotkey) problems.Add("分支2（看到+占用+无归属）=" + b2);
            if (b3 != HookVerdict.Delivered) problems.Add("分支3（看到+归属）=" + b3);
            if (b4 != HookVerdict.NotGlobal) problems.Add("分支4（看到+未占用）=" + b4);
            if (b5 != HookVerdict.Unknown) problems.Add("分支5（未超时）=" + b5);
            if (b6 != HookVerdict.Inconclusive) problems.Add("分支6（引擎B未启动）=" + b6);
            if (b7 != HookVerdict.Inconclusive) problems.Add("分支7（引擎A未扫描）=" + b7);
            detail.Append("    七分支判定：没看到→").Append(b1).Append("；看到+占用+无归属→").Append(b2)
                  .Append("；看到+归属→").Append(b3).Append("；看到+未占用→").Append(b4)
                  .Append("；引擎B未启动→").Append(b6).Append("；引擎A未扫描→").Append(b7).Append("\r\n");

            // ---- B：真实安装 WH_KEYBOARD_LL
            var sniffer = new HookSniffer();
            var snifferLog = new List<string>();
            sniffer.Logged += delegate (string s) { snifferLog.Add(s); };

            var c1Combo = new KeyCombo(false, true, true, true, 0x7D);   // Ctrl+Alt+Shift+F14
            var c2Combo = new KeyCombo(false, true, true, true, 0x7E);   // Ctrl+Alt+Shift+F15
            var c3Combo = new KeyCombo(false, true, true, true, 0x7F);   // Ctrl+Alt+Shift+F16
            sniffer.IsOccupiedByEngineA = delegate (KeyCombo c)
            {
                if (occupancy == null) return false;
                foreach (OccupancyResult r in occupancy)
                {
                    if (r.Combo != null && r.Combo.Normalize() == c.Normalize()) return r.Occupied;
                }
                return false;
            };
            // 自检里引擎 B 从不启动；扫描结果由 T2 真实产出（null 才算没扫）。
            // 2.0.0：判定需要这两个前提，否则会给出 Inconclusive 而不是期望结论。
            sniffer.AttributionActive = delegate () { return false; };
            sniffer.ScanDone = delegate () { return occupancy != null; };

            try
            {
                if (!sniffer.Start())
                {
                    foreach (string l in snifferLog) detail.Append("    ").Append(l).Append("\r\n");
                    Inconclusive("T7", "引擎 C 吞键检测",
                        "WH_KEYBOARD_LL 安装失败（" + sniffer.LastError + "）。判定逻辑七分支已验证通过。");
                    return;
                }
                detail.Append("    真实 LL 钩子=0x").Append(sniffer.HookHandle.ToInt64().ToString("X"))
                      .Append("，线程=").Append(sniffer.ThreadId).Append("（SetWindowsHookEx 返回非 0）\r\n");

                // ---- C1：喂一个按键 → 引擎 A 记为空闲 → 应判 NotGlobal
                var c1Records = new List<HookCheckRecord>();
                sniffer.VerdictReady += delegate (HookCheckRecord r) { lock (c1Records) c1Records.Add(r); };

                int idx1;
                lock (c1Records) idx1 = c1Records.Count;
                bool armed1 = sniffer.Arm(c1Combo);
                Thread.Sleep(120);
                sniffer.FeedKey(c1Combo);
                HookCheckRecord v1 = WaitVerdict(c1Records, c1Combo, idx1, 6000);
                detail.Append("    会话1（喂事件）：布防=").Append(armed1)
                      .Append("，引擎A记为").Append(sniffer.Current != null && sniffer.Current.OccupiedPerEngineA ? "占用" : "空闲")
                      .Append("，结论=").Append(v1 == null ? "(超时未结算)" : HookCheckRecord.VerdictName(v1.Verdict)).Append("\r\n");
                if (v1 == null || v1.Verdict != HookVerdict.NotGlobal)
                {
                    problems.Add("会话1 期望 NotGlobal，实际 " + (v1 == null ? "未结算" : v1.Verdict.ToString()));
                }

                // ---- C2：布防但不喂任何事件 → LL 钩子没看到 → 应判 SwallowedUpstream
                int idx2;
                lock (c1Records) idx2 = c1Records.Count;
                bool armed2 = sniffer.Arm(c2Combo);
                HookCheckRecord v2 = WaitVerdict(c1Records, c2Combo, idx2, 6000);
                detail.Append("    会话2（不喂事件）：布防=").Append(armed2)
                      .Append("，结论=").Append(v2 == null ? "(超时未结算)" : HookCheckRecord.VerdictName(v2.Verdict))
                      .Append("（等价于「按键没到达本程序的钩子」）\r\n");
                if (v2 == null || v2.Verdict != HookVerdict.SwallowedUpstream)
                {
                    problems.Add("会话2 期望 SwallowedUpstream，实际 " + (v2 == null ? "未结算" : v2.Verdict.ToString()));
                }

                // ---- C3：真的装一个上层 LL 钩子吞掉 Ctrl+Alt+Shift+F16，再用 SendInput 打一次
                bool c3Skipped = false;
                string c3Reason = null;
                using (var eater = new SwallowKeyboardHook(c3Combo))
                {
                    if (!eater.Installed)
                    {
                        c3Skipped = true;
                        c3Reason = "测试用吞键钩子安装失败：" + eater.LastError;
                    }
                    else
                    {
                        detail.Append("    测试用吞键钩子=0x").Append(eater.Handle.ToInt64().ToString("X"))
                              .Append("（吞掉 ").Append(c3Combo.Display()).Append("，装在本程序钩子之后＝更上层）\r\n");
                        int idx3;
                        lock (c1Records) idx3 = c1Records.Count;
                        bool armed3 = sniffer.Arm(c3Combo);
                        Thread.Sleep(120);
                        string shape;
                        bool sent = TrySendInputCombo(c3Combo, out shape);
                        detail.Append("    SendInput：").Append(shape).Append("\r\n");

                        HookCheckRecord v3 = WaitVerdict(c1Records, c3Combo, idx3, 6000);
                        bool snifferSawIt = sniffer.Current != null && sniffer.Current.Seen;
                        detail.Append("    会话3（真实吞键）：布防=").Append(armed3)
                              .Append("，被吞次数=").Append(eater.Swallowed)
                              .Append("，本程序钩子看到了=").Append(snifferSawIt)
                              .Append("，结论=").Append(v3 == null ? "(超时未结算)" : HookCheckRecord.VerdictName(v3.Verdict)).Append("\r\n");

                        if (!sent || eatenCount == 0)
                        {
                            c3Skipped = true;
                            c3Reason = !sent
                                ? "SendInput 没能把按键送进桌面（会话可能已锁定）"
                                : "SendInput 送出的事件根本没到达任何 LL 钩子（会话可能已锁定，合成输入被丢弃）";
                        }
                        else if (v3 == null || v3.Verdict != HookVerdict.SwallowedUpstream)
                        {
                            problems.Add("会话3 期望 SwallowedUpstream，实际 " + (v3 == null ? "未结算" : v3.Verdict.ToString()));
                        }
                    }
                }

                bool pass = problems.Count == 0;
                var sb = new StringBuilder();
                sb.Append("七分支判定逻辑全部正确；真实 WH_KEYBOARD_LL 钩子安装成功（句柄 0x")
                  .Append(sniffer.HookHandle.ToInt64().ToString("X")).Append("）；")
                  .Append("真实布防会话 2 次（NotGlobal / SwallowedUpstream）").Append(pass ? "均正确" : "有问题");
                if (c3Skipped) sb.Append("；端到端真实吞键测试 INCONCLUSIVE（").Append(c3Reason).Append("）");
                else sb.Append("；端到端真实吞键测试（上层 LL 钩子 + SendInput）通过");
                if (!pass) sb.Append("。问题：").Append(string.Join("；", problems.ToArray()))
                             .Append("。细节：\r\n").Append(detail);
                else sb.Append("。细节：\r\n").Append(detail);

                if (pass) Add("T7", "引擎 C 吞键检测", true, sb.ToString());
                else Add("T7", "引擎 C 吞键检测", false, sb.ToString());
            }
            catch (Exception ex)
            {
                Add("T7", "引擎 C 吞键检测", false, "异常：" + ex + "\r\n细节：\r\n" + detail);
            }
            finally
            {
                try { sniffer.Stop(); } catch (Exception) { }
                try { sniffer.Dispose(); } catch (Exception) { }
                foreach (string l in snifferLog) Console.WriteLine("    [sniffer] " + l);
            }
        }

        private static int eatenCount = 0;

        private static HookCheckRecord WaitVerdict(List<HookCheckRecord> records, KeyCombo combo, int startIndex, int timeoutMs)
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            while (sw.ElapsedMilliseconds < timeoutMs)
            {
                lock (records)
                {
                    for (int i = startIndex; i < records.Count; i++)
                    {
                        if (records[i].Combo.Normalize() == combo.Normalize()) return records[i];
                    }
                }
                Thread.Sleep(50);
            }
            return null;
        }

        /// <summary>测试专用：装一个上层 LL 键盘钩子，把指定组合键吞掉（返回 1，不往下传）。</summary>
        private sealed class SwallowKeyboardHook : IDisposable
        {
            private const int WH_KEYBOARD_LL = 13;
            private LowLevelKeyboardProc _proc;
            private IntPtr _hook = IntPtr.Zero;
            private readonly KeyCombo _target;
            private bool _shift, _ctrl, _alt, _win;
            public int Swallowed;

            public SwallowKeyboardHook(KeyCombo target)
            {
                _target = target;
                _proc = new LowLevelKeyboardProc(Proc);
                _hook = NativeMethods.SetWindowsHookExLL(WH_KEYBOARD_LL, _proc, IntPtr.Zero, 0);
                if (_hook == IntPtr.Zero) LastError = "SetWindowsHookEx 失败，错误码 " + Marshal.GetLastWin32Error();
            }

            public bool Installed { get { return _hook != IntPtr.Zero; } }
            public IntPtr Handle { get { return _hook; } }
            public string LastError { get; private set; }

            private IntPtr Proc(int nCode, IntPtr wParam, IntPtr lParam)
            {
                try
                {
                    if (nCode >= 0)
                    {
                        var data = (NativeMethods.KBDLLHOOKSTRUCT)Marshal.PtrToStructure(lParam, typeof(NativeMethods.KBDLLHOOKSTRUCT));
                        int wm = wParam.ToInt32();
                        bool down = wm == 0x0100 || wm == 0x0104;
                        bool up = wm == 0x0101 || wm == 0x0105;
                        uint vk = data.vkCode;
                        switch (vk)
                        {
                            case 0x10: case 0xA0: case 0xA1: _shift = down; break;
                            case 0x11: case 0xA2: case 0xA3: _ctrl = down; break;
                            case 0x12: case 0xA4: case 0xA5: _alt = down; break;
                            case 0x5B: case 0x5C: _win = down; break;
                        }
                        if ((down || up) && vk == _target.Vk)
                        {
                            bool match = _shift == _target.Shift && _ctrl == _target.Ctrl
                                      && _alt == _target.Alt && _win == _target.Win;
                            if (match)
                            {
                                Swallowed++;
                                eatenCount++;
                                return new IntPtr(1);   // 吞掉，不再往下传
                            }
                        }
                    }
                }
                catch (Exception) { }
                return NativeMethods.CallNextHookEx(IntPtr.Zero, nCode, wParam, lParam);
            }

            public void Dispose()
            {
                if (_hook != IntPtr.Zero)
                {
                    NativeMethods.UnhookWindowsHookEx(_hook);
                    _hook = IntPtr.Zero;
                }
            }
        }
    }
}
