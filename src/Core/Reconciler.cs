using System;
using System.Collections.Generic;
using System.Text;
using HotkeyHound.Core.Adapters;

namespace HotkeyHound.Core
{
    public enum TruthKind { MatchesDeclared, DeclaredButNotRegistered, RegisteredButUnowned, FreeAndUndeclared }

    public sealed class TruthRow
    {
        public KeyCombo Combo;
        public TruthKind Kind;
        public string DeclaredBy;
        public string OwnerHint;
        public string Evidence;
        public string Note;
        /// <summary>
        /// 这一行不是精确匹配的结果：本行组合在配置里并没有被声明，
        /// 只是恰好存在一个「忽略 Shift 后相同」的声明。仅用于提示，
        /// 任何面向用户的结论（尤其建议文本）都不允许把它当作事实引用。
        /// </summary>
        public bool FuzzyOnly;
    }

    public sealed class TruthTable
    {
        public List<TruthRow> Rows = new List<TruthRow>();
        public Dictionary<TruthKind, int> Counts = new Dictionary<TruthKind, int>();
        public List<string> RunningCandidates = new List<string>();

        public int CountOf(TruthKind kind)
        {
            int n;
            return Counts.TryGetValue(kind, out n) ? n : 0;
        }

        public List<TruthRow> ByKind(TruthKind kind)
        {
            var list = new List<TruthRow>();
            foreach (TruthRow r in Rows) if (r.Kind == kind) list.Add(r);
            return list;
        }

        public static string KindName(TruthKind kind)
        {
            switch (kind)
            {
                case TruthKind.MatchesDeclared: return L10n.T("truth.kind.matched");
                case TruthKind.DeclaredButNotRegistered: return L10n.T("truth.kind.declaredOnly");
                case TruthKind.RegisteredButUnowned: return L10n.T("truth.kind.unowned");
                default: return L10n.T("truth.kind.free");
            }
        }

        public static string KindExplain(TruthKind kind)
        {
            switch (kind)
            {
                case TruthKind.MatchesDeclared:
                    return L10n.T("truth.explain.matched");
                case TruthKind.DeclaredButNotRegistered:
                    return L10n.T("truth.explain.declaredOnly");
                case TruthKind.RegisteredButUnowned:
                    return L10n.T("truth.explain.unowned");
                default:
                    return L10n.T("truth.explain.free");
            }
        }
    }

    /// <summary>A×D 真相表：以引擎 A 的全部组合为底表，逐条匹配引擎 D 的声明。</summary>
    public static class Reconciler
    {
        public static TruthTable Build(List<OccupancyResult> occupancy,
                                       List<DeclaredHotkey> declared,
                                       List<DeclaredHotkey> reserved,
                                       List<string> runningCandidates)
        {
            var table = new TruthTable();
            if (runningCandidates != null) table.RunningCandidates.AddRange(runningCandidates);

            var byCombo = new Dictionary<string, List<DeclaredHotkey>>(StringComparer.Ordinal);
            var byComboNoShift = new Dictionary<string, List<DeclaredHotkey>>(StringComparer.Ordinal);
            foreach (DeclaredHotkey d in declared)
            {
                if (d == null || d.Combo == null) continue;
                Add(byCombo, d.Combo.Normalize(), d);
                Add(byComboNoShift, d.Combo.NormalizeIgnoreShift(), d);
            }

            var hints = SystemReservedTable.Hints();

            var covered = new HashSet<string>(StringComparer.Ordinal);
            foreach (OccupancyResult occ in occupancy)
            {
                var row = new TruthRow();
                row.Combo = occ.Combo;
                covered.Add(occ.Combo.Normalize());

                List<DeclaredHotkey> decl;
                bool hasDecl = byCombo.TryGetValue(occ.Combo.Normalize(), out decl) && decl.Count > 0;

                string hint;
                hints.TryGetValue(occ.Combo.Normalize(), out hint);
                row.OwnerHint = hint;

                if (occ.Occupied && hasDecl)
                {
                    row.Kind = TruthKind.MatchesDeclared;
                    row.Evidence = L10n.T("truth.evidence.both");
                }
                else if (!occ.Occupied && hasDecl)
                {
                    row.Kind = TruthKind.DeclaredButNotRegistered;
                    row.Evidence = L10n.T("truth.evidence.config");
                }
                else if (occ.Occupied)
                {
                    row.Kind = TruthKind.RegisteredButUnowned;
                    row.Evidence = L10n.T("truth.evidence.measured");
                }
                else
                {
                    row.Kind = TruthKind.FreeAndUndeclared;
                    row.Evidence = L10n.T("truth.evidence.measured");
                }

                if (hasDecl) row.DeclaredBy = Summarize(decl);
                row.Note = BuildNote(row, occ, decl, table.RunningCandidates);

                // 忽略 Shift 的模糊匹配：只在精确匹配失败时提示，并且必须打上 FuzzyOnly 标记，
                // 否则下游（尤其是建议文本）会把「Ctrl+K」这种配置里根本没声明的组合当成事实写出去。
                if (!hasDecl)
                {
                    List<DeclaredHotkey> fuzzy;
                    if (byComboNoShift.TryGetValue(occ.Combo.NormalizeIgnoreShift(), out fuzzy) && fuzzy.Count > 0)
                    {
                        string extra = L10n.F("truth.note.shiftVariant", fuzzy[0].Combo.Display(),
                                              fuzzy[0].Software, fuzzy[0].Module);
                        // 报告是拼接出来的：分隔符必须跟着语言走。以前这里写死全角「；」，
                        // 英文报告的句子里就会漏出中文标点。
                        row.Note = string.IsNullOrEmpty(row.Note) ? extra : row.Note + (L10n.IsEnglish ? "; " : "；") + extra;
                        row.FuzzyOnly = true;
                        if (row.Kind == TruthKind.FreeAndUndeclared && occ.Occupied == false)
                        {
                            // 声明了但没注册上，只是 Shift 对不上——按「有声明但未注册」处理更贴近事实
                            row.Kind = TruthKind.DeclaredButNotRegistered;
                            row.Evidence = L10n.T("truth.evidence.config");
                            row.DeclaredBy = Summarize(fuzzy);
                        }
                    }
                }

                table.Rows.Add(row);
            }

            // 声明了但不在探测网格内的组合，也补一行，避免漏报
            foreach (var kv in byCombo)
            {
                if (covered.Contains(kv.Key)) continue;
                var d = kv.Value[0];
                var row = new TruthRow();
                row.Combo = d.Combo;
                row.Kind = TruthKind.DeclaredButNotRegistered;
                row.DeclaredBy = Summarize(kv.Value);
                row.Evidence = L10n.T("truth.evidence.config");
                string hint;
                hints.TryGetValue(kv.Key, out hint);
                row.OwnerHint = hint;
                row.Note = L10n.T("truth.note.offGrid");
                table.Rows.Add(row);
            }

            foreach (TruthRow r in table.Rows)
            {
                int n;
                table.Counts.TryGetValue(r.Kind, out n);
                table.Counts[r.Kind] = n + 1;
            }
            return table;
        }

        private static void Add(Dictionary<string, List<DeclaredHotkey>> map, string key, DeclaredHotkey d)
        {
            List<DeclaredHotkey> list;
            if (!map.TryGetValue(key, out list))
            {
                list = new List<DeclaredHotkey>();
                map[key] = list;
            }
            list.Add(d);
        }

        private static string Summarize(List<DeclaredHotkey> decl)
        {
            var sb = new StringBuilder();
            int shown = 0;
            foreach (DeclaredHotkey d in decl)
            {
                if (shown >= 4) { sb.Append(L10n.F("truth.note.moreDeclared", decl.Count)); break; }
                if (shown > 0) sb.Append(L10n.IsEnglish ? "; " : "；");
                sb.Append(d.Software).Append('/').Append(L10n.Term(d.Module))
                  .Append(L10n.IsEnglish ? " (" : "（").Append(L10n.Term(d.Purpose)).Append(L10n.IsEnglish ? ")" : "）");
                shown++;
            }
            return sb.ToString();
        }

        private static string BuildNote(TruthRow row, OccupancyResult occ, List<DeclaredHotkey> decl, List<string> candidates)
        {
            switch (row.Kind)
            {
                case TruthKind.MatchesDeclared:
                    return L10n.T("truth.note.matched");

                case TruthKind.DeclaredButNotRegistered:
                    {
                        var sb = new StringBuilder();
                        sb.Append(L10n.T("truth.note.declaredOnly"));
                        if (decl != null)
                        {
                            foreach (DeclaredHotkey d in decl)
                            {
                                if (d.IsDefaultValue) { sb.Append(L10n.T("truth.note.declaredOnly.defaultSuffix")); break; }
                            }
                        }
                        return sb.ToString();
                    }

                case TruthKind.RegisteredButUnowned:
                    {
                        var sb = new StringBuilder();
                        sb.Append(L10n.T("truth.note.unowned"));
                        if (occ.Win32Error != 0 && occ.Win32Error != NativeHotkey.ERROR_HOTKEY_ALREADY_REGISTERED)
                        {
                            sb.Append(L10n.F("truth.note.unowned.errSuffix", occ.Win32Error));
                        }
                        if (candidates != null && candidates.Count > 0)
                        {
                            sb.Append(L10n.T("truth.note.unowned.candidates"));
                            int shown = 0;
                            foreach (string c in candidates)
                            {
                                if (shown >= 8) { sb.Append(L10n.F("truth.note.unowned.more", candidates.Count)); break; }
                                if (shown > 0) sb.Append(L10n.IsEnglish ? ", " : "、");
                                sb.Append(c);
                                shown++;
                            }
                            sb.Append(L10n.IsEnglish ? "." : "。");
                        }
                        return sb.ToString();
                    }

                default:
                    return L10n.T("truth.note.free");
            }
        }
    }
}
