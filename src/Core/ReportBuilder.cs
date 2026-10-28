using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace HotkeyHound.Core
{
    /// <summary>自包含 HTML 报告（内联 CSS/JS，无外链）。文案全部走 L10n，报告语言跟随当前语言。</summary>
    public static class ReportBuilder
    {
        public static void Write(ScanSnapshot s, string path)
        {
            string dir = Path.GetDirectoryName(Path.GetFullPath(path));
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir)) Directory.CreateDirectory(dir);
            File.WriteAllText(path, BuildHtml(s), new UTF8Encoding(true));
        }

        public static string DefaultFileName()
        {
            return "report-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".html";
        }

        public static string BuildHtml(ScanSnapshot s)
        {
            var sb = new StringBuilder(256 * 1024);
            sb.Append("<!DOCTYPE html>\r\n<html lang=\"").Append(Html.Escape(L10n.Current)).Append("\">\r\n<head>\r\n<meta charset=\"utf-8\">\r\n");
            sb.Append("<meta name=\"viewport\" content=\"width=device-width, initial-scale=1\">\r\n");
            sb.Append("<title>").Append(Html.Escape(L10n.F("rep.title", AppInfo.Title,
                      s.GeneratedAt.ToString("yyyy-MM-dd HH:mm:ss")))).Append("</title>\r\n");
            sb.Append(Css());
            sb.Append("</head>\r\n<body>\r\n");

            BuildHeader(sb, s);
            BuildOccupancy(sb, s);
            BuildTruth(sb, s);
            BuildAttribution(sb, s);
            BuildHookCheck(sb, s);
            BuildHookSuspects(sb, s);
            BuildAutoProbe(sb, s);
            BuildDeclared(sb, s);
            BuildAdvice(sb, s);
            BuildMethod(sb, s);
            BuildFooter(sb, s);

            sb.Append(Script());
            sb.Append("</body>\r\n</html>\r\n");
            return sb.ToString();
        }

        private static string Css()
        {
            return @"<style>
:root { color-scheme: light dark; }
* { box-sizing: border-box; }
body { margin: 0; padding: 0 20px 60px; font-family: 'Microsoft YaHei UI','Microsoft YaHei',Segoe UI,sans-serif;
       background: #f5f6f8; color: #1b1d21; line-height: 1.6; }
h1 { font-size: 24px; margin: 24px 0 4px; }
h2 { font-size: 19px; margin: 34px 0 10px; padding-bottom: 6px; border-bottom: 2px solid #d8dbe2; }
h3 { font-size: 15px; margin: 18px 0 6px; }
a { color: #0b6bcb; }
.sub { color: #5a6070; font-size: 13px; }
.cards { display: flex; flex-wrap: wrap; gap: 10px; margin: 16px 0 6px; }
.card { background: #fff; border: 1px solid #dfe3ea; border-radius: 8px; padding: 10px 16px; min-width: 132px; }
.card .n { font-size: 22px; font-weight: 700; }
.card .t { font-size: 12px; color: #5a6070; }
.warn { background: #fff6e5; border: 1px solid #f0d69a; border-radius: 8px; padding: 10px 14px; margin: 12px 0; font-size: 13px; }
.ok { background: #ecf8ee; border: 1px solid #a8d8b0; border-radius: 8px; padding: 10px 14px; margin: 12px 0; font-size: 13px; }
table { border-collapse: collapse; width: 100%; background: #fff; font-size: 13px; }
th, td { border: 1px solid #e2e5ec; padding: 5px 8px; text-align: left; vertical-align: top; }
th { background: #eef1f6; position: sticky; top: 0; z-index: 2; }
tr.occ td.st { color: #b3261e; font-weight: 600; }
tr.free td.st { color: #3a7d44; }
.k { font-family: Consolas, 'Courier New', monospace; font-weight: 600; white-space: nowrap; }
.toolbar { display: flex; gap: 14px; align-items: center; margin: 10px 0; flex-wrap: wrap; }
.toolbar input[type=text] { padding: 6px 10px; border: 1px solid #c8cdd8; border-radius: 6px; min-width: 260px; font-size: 13px; }
.toolbar label { font-size: 13px; }
.hint { color: #5a6070; font-size: 12px; }
.group { border: 1px solid #e2e5ec; border-radius: 8px; margin: 12px 0; overflow: hidden; }
.group > .hd { background: #eef1f6; padding: 8px 12px; font-weight: 600; font-size: 14px; }
.group > .bd { padding: 10px 12px; font-size: 13px; background: #fff; }
pre.log { background: #1f2430; color: #d8dee9; padding: 12px; border-radius: 8px; overflow: auto;
          max-height: 340px; font-size: 12px; line-height: 1.45; }
ul { margin: 6px 0 6px 20px; padding: 0; }
li { margin: 3px 0; }
code { background: #eef1f6; padding: 1px 5px; border-radius: 4px; font-family: Consolas, monospace; }
footer { margin-top: 28px; padding-top: 10px; border-top: 1px solid #d8dbe2; }
@media (prefers-color-scheme: dark) {
  body { background: #16181d; color: #e6e8ec; }
  table, .card, .group > .bd { background: #1e2127; }
  th, .group > .hd { background: #262b34; }
  th, td { border-color: #333844; }
  h2 { border-color: #333844; }
  .sub, .hint, .card .t { color: #9aa2b1; }
  code { background: #262b34; }
  .warn { background: #3a3320; border-color: #5c5028; }
  .ok { background: #1f3324; border-color: #35573d; }
  footer { border-color: #333844; }
}
</style>
";
        }

        private static void BuildHeader(StringBuilder sb, ScanSnapshot s)
        {
            sb.Append("<h1>").Append(Html.Escape(L10n.T("rep.h1"))).Append("</h1>\r\n");
            string sep = L10n.T("rep.meta.sep");
            sb.Append("<div class=\"sub\">")
              .Append(Html.Escape(L10n.T("rep.meta.generated"))).Append(Html.Escape(s.GeneratedAt.ToString("yyyy-MM-dd HH:mm:ss"))).Append(sep)
              .Append(Html.Escape(L10n.T("rep.meta.machine"))).Append(Html.Escape(s.MachineName)).Append(sep)
              .Append(Html.Escape(L10n.T("rep.meta.admin")))
              .Append(Html.Escape(s.IsAdministrator ? L10n.T("rep.meta.adminYes") : L10n.T("rep.meta.adminNo"))).Append(sep)
              .Append(Html.Escape(L10n.T("rep.meta.version"))).Append(Html.Escape(s.Version)).Append(sep)
              .Append(Html.Escape(L10n.T("rep.meta.lang"))).Append(Html.Escape(L10n.CurrentDisplay))
              .Append("</div>\r\n");

            sb.Append("<div class=\"cards\">\r\n");
            Card(sb, s.OccupiedCount, L10n.T("rep.card.occupied"));
            Card(sb, s.FreeCount, L10n.T("rep.card.free"));
            Card(sb, s.DeclaredCount, L10n.T("rep.card.declared"));
            Card(sb, s.Truth.Rows.Count, L10n.T("rep.card.truthRows"));
            Card(sb, s.HookSuspects == null ? 0 : s.HookSuspects.Count, L10n.T("rep.card.hookSuspects"));
            Card(sb, s.Truth.CountOf(TruthKind.MatchesDeclared), L10n.T("rep.card.matched"));
            Card(sb, s.Truth.CountOf(TruthKind.DeclaredButNotRegistered), L10n.T("rep.card.declaredOnly"));
            Card(sb, s.Truth.CountOf(TruthKind.RegisteredButUnowned), L10n.T("rep.card.unowned"));
            Card(sb, s.Truth.CountOf(TruthKind.FreeAndUndeclared), L10n.T("rep.card.freeOnly"));
            sb.Append("</div>\r\n");

            sb.Append("<div class=\"group\"><div class=\"hd\">").Append(Html.Escape(L10n.T("rep.engines.title")))
              .Append("</div><div class=\"bd\">\r\n<ul>\r\n");
            sb.Append(L10n.F("rep.engines.a", OccupancyScanner.TotalCombos, s.OccupiedCount, s.ScanMilliseconds)).Append("\r\n");
            sb.Append(L10n.T("rep.engines.b")).Append("\r\n");
            sb.Append(L10n.T("rep.engines.c")).Append("\r\n");
            sb.Append(L10n.F("rep.engines.d", CountSources(s.Config.Declared), s.DeclaredCount, s.Config.Reserved.Count)).Append("\r\n");
            sb.Append("</ul>\r\n</div></div>\r\n");
        }

        private static int CountSources(List<DeclaredHotkey> list)
        {
            var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (DeclaredHotkey d in list) if (!string.IsNullOrEmpty(d.SourcePath)) set.Add(d.SourcePath);
            return set.Count;
        }

        private static void Card(StringBuilder sb, int number, string title)
        {
            sb.Append("<div class=\"card\"><div class=\"n\">").Append(number)
              .Append("</div><div class=\"t\">").Append(Html.Escape(title)).Append("</div></div>\r\n");
        }

        private static void BuildOccupancy(StringBuilder sb, ScanSnapshot s)
        {
            sb.Append("<h2 id=\"occupancy\">").Append(Html.Escape(L10n.T("rep.occupancy.h2"))).Append("</h2>\r\n");
            sb.Append("<div class=\"hint\">").Append(Html.Escape(L10n.T("rep.occupancy.hint"))).Append("</div>\r\n");
            sb.Append("<div class=\"toolbar\">\r\n<label>").Append(Html.Escape(L10n.T("rep.occupancy.search")))
              .Append("<input type=\"text\" id=\"occ-search\" placeholder=\"")
              .Append(Html.Escape(L10n.T("rep.occupancy.searchPh"))).Append("\"></label>\r\n");
            sb.Append("<label><input type=\"checkbox\" id=\"occ-only\"> ").Append(Html.Escape(L10n.T("rep.occupancy.onlyOccupied")))
              .Append("</label>\r\n");
            sb.Append("<span class=\"hint\" id=\"occ-count\"></span>\r\n</div>\r\n");
            sb.Append("<div class=\"group\"><div class=\"bd\" style=\"padding:0\">\r\n");
            sb.Append("<table id=\"occ-table\"><thead><tr>")
              .Append("<th style=\"width:190px\">").Append(Html.Escape(L10n.T("ui.col.combo"))).Append("</th>")
              .Append("<th style=\"width:78px\">").Append(Html.Escape(L10n.T("ui.col.state"))).Append("</th>")
              .Append("<th>").Append(Html.Escape(L10n.T("ui.col.owner"))).Append("</th>")
              .Append("<th style=\"width:88px\">").Append(Html.Escape(L10n.T("ui.col.evidence"))).Append("</th>")
              .Append("<th>").Append(Html.Escape(L10n.T("ui.col.note"))).Append("</th>")
              .Append("</tr></thead><tbody>\r\n");

            foreach (TruthRow r in s.Truth.Rows)
            {
                bool occ = r.Kind == TruthKind.MatchesDeclared || r.Kind == TruthKind.RegisteredButUnowned
                    || r.Kind == TruthKind.SystemReserved;
                sb.Append("<tr class=\"").Append(occ ? "occ" : "free").Append("\" data-state=\"")
                  .Append(occ ? "1" : "0").Append("\">");
                sb.Append("<td class=\"k\">").Append(Html.Escape(r.Combo.Display())).Append("</td>");
                sb.Append("<td class=\"st\">").Append(Html.Escape(occ ? L10n.T("rep.state.occupied") : L10n.T("rep.state.free"))).Append("</td>");
                sb.Append("<td>").Append(OwnerText(r)).Append("</td>");
                sb.Append("<td>").Append(Html.Escape(r.Evidence)).Append("</td>");
                sb.Append("<td>").Append(Html.Escape(r.Note)).Append("</td>");
                sb.Append("</tr>\r\n");
            }

            sb.Append("</tbody></table>\r\n</div></div>\r\n");
        }

        private static string OwnerText(TruthRow r)
        {
            string declared = string.IsNullOrEmpty(r.DeclaredBy) ? "" : Html.Escape(r.DeclaredBy);
            string hint = string.IsNullOrEmpty(r.OwnerHint) ? "" : ("<span class=\"hint\">"
                + Html.Escape(L10n.T("rep.occupancy.systemReserved"))
                + Html.Escape(L10n.Term(r.OwnerHint)) + "</span>");
            if (declared.Length == 0 && hint.Length == 0) return "<span class=\"hint\">" + Html.Escape(L10n.T("rep.occupancy.noneOwner")) + "</span>";
            if (declared.Length == 0) return hint;
            if (hint.Length == 0) return declared;
            return declared + "<br>" + hint;
        }

        private static void BuildTruth(StringBuilder sb, ScanSnapshot s)
        {
            sb.Append("<h2 id=\"truth\">").Append(Html.Escape(L10n.T("rep.truth.h2"))).Append("</h2>\r\n");
            sb.Append("<div class=\"hint\">").Append(Html.Escape(L10n.T("rep.truth.hint"))).Append("</div>\r\n");

            TruthKind[] order = new TruthKind[]
            {
                TruthKind.MatchesDeclared, TruthKind.DeclaredButNotRegistered,
                TruthKind.RegisteredButUnowned, TruthKind.SystemReserved, TruthKind.FreeAndUndeclared
            };

            foreach (TruthKind kind in order)
            {
                List<TruthRow> rows = s.Truth.ByKind(kind);
                sb.Append("<div class=\"group\" id=\"truth-").Append(KindSlug(kind)).Append("\"><div class=\"hd\">")
                  .Append(Html.Escape(TruthTable.KindName(kind)))
                  .Append(Html.Escape(L10n.F("rep.truth.count", rows.Count))).Append("</div>\r\n");
                sb.Append("<div class=\"bd\"><p>").Append(Html.Escape(TruthTable.KindExplain(kind))).Append("</p>\r\n");

                if (rows.Count == 0)
                {
                    sb.Append("<p class=\"hint\">").Append(Html.Escape(L10n.T("rep.truth.emptyKind"))).Append("</p></div></div>\r\n");
                    continue;
                }

                int shown = kind == TruthKind.FreeAndUndeclared ? Math.Min(30, rows.Count) : rows.Count;
                if (kind == TruthKind.FreeAndUndeclared && rows.Count > shown)
                {
                    sb.Append("<p class=\"hint\">").Append(Html.Escape(L10n.F("rep.truth.sampled", rows.Count, shown)))
                      .Append("</p>\r\n");
                }

                sb.Append("<div style=\"overflow:auto;max-height:460px\"><table><thead><tr>")
                  .Append("<th style=\"width:190px\">").Append(Html.Escape(L10n.T("ui.col.combo"))).Append("</th>")
                  .Append("<th>").Append(Html.Escape(L10n.T("ui.col.declaredBy"))).Append("</th>")
                  .Append("<th>").Append(Html.Escape(L10n.T("ui.col.ownerHint"))).Append("</th>")
                  .Append("<th style=\"width:88px\">").Append(Html.Escape(L10n.T("ui.col.evidence"))).Append("</th>")
                  .Append("<th>").Append(Html.Escape(L10n.T("ui.col.note"))).Append("</th>")
                  .Append("</tr></thead><tbody>\r\n");
                for (int i = 0; i < shown; i++)
                {
                    TruthRow r = rows[i];
                    sb.Append("<tr><td class=\"k\">").Append(Html.Escape(r.Combo.Display())).Append("</td>");
                    sb.Append("<td>").Append(string.IsNullOrEmpty(r.DeclaredBy) ? "<span class=\"hint\">" + Html.Escape(L10n.T("common.none")) + "</span>" : Html.Escape(r.DeclaredBy)).Append("</td>");
                    sb.Append("<td>").Append(string.IsNullOrEmpty(r.OwnerHint) ? "<span class=\"hint\">" + Html.Escape(L10n.T("common.none")) + "</span>" : Html.Escape(L10n.Term(r.OwnerHint))).Append("</td>");
                    sb.Append("<td>").Append(Html.Escape(r.Evidence)).Append("</td>");
                    sb.Append("<td>").Append(Html.Escape(r.Note)).Append("</td></tr>\r\n");
                }
                sb.Append("</tbody></table></div>\r\n</div></div>\r\n");
            }
        }

        private static string KindSlug(TruthKind kind)
        {
            switch (kind)
            {
                case TruthKind.MatchesDeclared: return "matched";
                case TruthKind.DeclaredButNotRegistered: return "declared-only";
                case TruthKind.RegisteredButUnowned: return "unowned";
                case TruthKind.SystemReserved: return "reserved";
                default: return "free";
            }
        }

        private static void BuildAttribution(StringBuilder sb, ScanSnapshot s)
        {
            sb.Append("<h2 id=\"attribution\">").Append(Html.Escape(L10n.T("rep.attribution.h2"))).Append("</h2>\r\n");

            AttributionRecord[] records = SessionRecords.Attribution();
            if (records.Length == 0)
            {
                sb.Append("<div class=\"hint\">").Append(L10n.T("rep.attribution.empty")).Append("</div>\r\n");
                return;
            }

            sb.Append("<div class=\"hint\">").Append(Html.Escape(L10n.T("rep.attribution.hint"))).Append("</div>\r\n");
            sb.Append("<div style=\"overflow:auto;max-height:460px\"><table><thead><tr>")
              .Append("<th style=\"width:96px\">").Append(Html.Escape(L10n.T("ui.at.col.time"))).Append("</th>")
              .Append("<th>").Append(Html.Escape(L10n.T("ui.at.col.process"))).Append("</th>")
              .Append("<th style=\"width:70px\">").Append(Html.Escape(L10n.T("ui.at.col.pid"))).Append("</th>")
              .Append("<th style=\"width:60px\">").Append(Html.Escape(L10n.T("ui.at.col.bitness"))).Append("</th>")
              .Append("<th style=\"width:180px\">").Append(Html.Escape(L10n.T("ui.col.combo"))).Append("</th>")
              .Append("<th>").Append(Html.Escape(L10n.T("ui.col.path"))).Append("</th>")
              .Append("</tr></thead><tbody>\r\n");
            foreach (AttributionRecord r in records)
            {
                sb.Append("<tr><td>").Append(Html.Escape(r.Time.ToString("HH:mm:ss"))).Append("</td>");
                sb.Append("<td>").Append(string.IsNullOrEmpty(r.ProcessName) ? "<span class=\"hint\">" + Html.Escape(L10n.T("attr.unknownProc")) + "</span>" : Html.Escape(r.ProcessName));
                if (r.IsElevated) sb.Append(" <span class=\"hint\">" + Html.Escape(L10n.T("attr.elevatedTag").Trim()) + "</span>");
                sb.Append("</td>");
                sb.Append("<td>").Append(r.Pid).Append("</td>");
                sb.Append("<td>").Append(string.IsNullOrEmpty(r.Bitness) ? "?" : Html.Escape(r.Bitness)).Append("</td>");
                sb.Append("<td class=\"k\">").Append(r.Combo == null ? "<span class=\"hint\">" + Html.Escape(L10n.T("attr.unknownCombo")) + "</span>" : Html.Escape(r.Combo.Display())).Append("</td>");
                sb.Append("<td class=\"hint\">").Append(string.IsNullOrEmpty(r.ProcessPath) ? Html.Escape(L10n.T("rep.attribution.pathMissing")) : Html.Escape(r.ProcessPath)).Append("</td>");
                sb.Append("</tr>\r\n");
            }
            sb.Append("</tbody></table></div>\r\n");
        }

        private static void BuildHookCheck(StringBuilder sb, ScanSnapshot s)
        {
            sb.Append("<h2 id=\"hookcheck\">").Append(Html.Escape(L10n.T("rep.hookcheck.h2"))).Append("</h2>\r\n");

            HookCheckRecord[] records = SessionRecords.HookRecords();
            if (records.Length == 0)
            {
                sb.Append("<div class=\"hint\">").Append(L10n.T("rep.hookcheck.empty")).Append("</div>\r\n");
                return;
            }

            sb.Append("<div class=\"hint\">").Append(L10n.T("rep.hookcheck.hint")).Append("</div>\r\n");
            sb.Append("<div style=\"overflow:auto;max-height:460px\"><table><thead><tr>")
              .Append("<th style=\"width:96px\">").Append(Html.Escape(L10n.T("ui.at.col.time"))).Append("</th>")
              .Append("<th style=\"width:180px\">").Append(Html.Escape(L10n.T("ui.col.combo"))).Append("</th>")
              .Append("<th style=\"width:150px\">").Append(Html.Escape(L10n.T("ui.hk.col.verdict"))).Append("</th>")
              .Append("<th>").Append(Html.Escape(L10n.T("ui.col.evidence"))).Append("</th>")
              .Append("<th>").Append(Html.Escape(L10n.T("ui.hk.col.note"))).Append("</th>")
              .Append("</tr></thead><tbody>\r\n");
            foreach (HookCheckRecord r in records)
            {
                sb.Append("<tr><td>").Append(Html.Escape(r.Time.ToString("HH:mm:ss"))).Append("</td>");
                sb.Append("<td class=\"k\">").Append(r.Combo == null ? "<span class=\"hint\">" + Html.Escape(L10n.T("attr.unknownCombo")) + "</span>" : Html.Escape(r.Combo.Display())).Append("</td>");
                sb.Append("<td>").Append(Html.Escape(HookCheckRecord.VerdictName(r.Verdict))).Append("</td>");
                sb.Append("<td>").Append(Html.Escape(r.Evidence)).Append("</td>");
                sb.Append("<td>").Append(Html.Escape(r.Note)).Append("</td></tr>\r\n");
            }
            sb.Append("</tbody></table></div>\r\n");

            ComparisonSnapshot[] comparisons = SessionRecords.Comparisons();
            if (comparisons.Length > 0)
            {
                sb.Append("<h3>").Append(Html.Escape(L10n.T("rep.hookcheck.compareH3"))).Append("</h3>\r\n");
                sb.Append("<div class=\"hint\">").Append(Html.Escape(L10n.T("rep.hookcheck.compareHint"))).Append("</div>\r\n");
                sb.Append("<div class=\"group\"><div class=\"bd\">\r\n<ul>\r\n");
                foreach (ComparisonSnapshot snap in comparisons)
                {
                    sb.Append("<li><b>").Append(Html.Escape(snap.Time.ToString("HH:mm:ss")))
                      .Append(L10n.IsEnglish ? " " : "　").Append(Html.Escape(snap.Label)).Append("</b>");
                    if (snap.Lines.Count > 0)
                    {
                        sb.Append("<ul>\r\n");
                        foreach (string line in snap.Lines)
                        {
                            sb.Append("<li>").Append(Html.Escape(line)).Append("</li>\r\n");
                        }
                        sb.Append("</ul>\r\n");
                    }
                    sb.Append("</li>\r\n");
                }
                sb.Append("</ul>\r\n</div></div>\r\n");
            }
        }

        private static void BuildHookSuspects(StringBuilder sb, ScanSnapshot s)
        {
            sb.Append("<h2 id=\"hooksuspects\">").Append(Html.Escape(L10n.T("rep.hooksuspects.h2"))).Append("</h2>\r\n");
            sb.Append("<div class=\"hint\">").Append(Html.Escape(L10n.T("rep.hooksuspects.hint"))).Append("</div>\r\n");

            HookEvidence[] order = new HookEvidence[]
            {
                HookEvidence.Confirmed, HookEvidence.HighlySuspected,
                HookEvidence.KnownDefault, HookEvidence.ReferenceOnly
            };

            sb.Append("<div class=\"group\"><div class=\"hd\">").Append(Html.Escape(L10n.T("rep.hooksuspects.levelsTitle")))
              .Append("</div><div class=\"bd\"><ul>\r\n");
            foreach (HookEvidence ev in order)
            {
                // 标签后面这个冒号必须跟着语言走：英文报告里冒出全角冒号，看起来就是没做完的翻译。
                sb.Append("<li><b>").Append(Html.Escape(HookCatalog.EvidenceName(ev)))
                  .Append(L10n.IsEnglish ? "</b>: " : "</b>：")
                  .Append(Html.Escape(HookCatalog.EvidenceExplain(ev))).Append("</li>\r\n");
            }
            sb.Append("</ul></div></div>\r\n");

            List<HookSuspect> list = s.HookSuspects;
            if (list == null || list.Count == 0)
            {
                sb.Append("<div class=\"hint\">").Append(Html.Escape(L10n.T("rep.hooksuspects.empty"))).Append("</div>\r\n");
                return;
            }

            foreach (HookEvidence ev in order)
            {
                var rows = new List<HookSuspect>();
                foreach (HookSuspect r in list) if (r.Evidence == ev) rows.Add(r);

                sb.Append("<div class=\"group\" id=\"hooks-").Append(EvidenceSlug(ev)).Append("\"><div class=\"hd\">")
                  .Append(Html.Escape(HookCatalog.EvidenceName(ev)))
                  .Append(Html.Escape(L10n.F("rep.hooksuspects.count", rows.Count))).Append("</div>\r\n");
                sb.Append("<div class=\"bd\">\r\n<p class=\"hint\">").Append(Html.Escape(HookCatalog.EvidenceExplain(ev))).Append("</p>\r\n");
                if (rows.Count == 0)
                {
                    sb.Append("<p class=\"hint\">").Append(Html.Escape(L10n.T("rep.truth.emptyKind"))).Append("</p></div></div>\r\n");
                    continue;
                }

                sb.Append("<div style=\"overflow:auto;max-height:460px\"><table><thead><tr>")
                  .Append("<th style=\"width:170px\">").Append(Html.Escape(L10n.T("rep.autoprobe.col.combo"))).Append("</th>")
                  .Append("<th style=\"width:150px\">").Append(Html.Escape(L10n.T("rep.hooksuspects.col.software"))).Append("</th>")
                  .Append("<th style=\"width:130px\">").Append(Html.Escape(L10n.T("rep.hooksuspects.col.module"))).Append("</th>")
                  .Append("<th style=\"width:80px\">").Append(Html.Escape(L10n.T("rep.hooksuspects.col.process"))).Append("</th>")
                  .Append("<th style=\"width:80px\">").Append(Html.Escape(L10n.T("rep.hooksuspects.col.config"))).Append("</th>")
                  .Append("<th>").Append(Html.Escape(L10n.T("ui.col.note"))).Append("</th>")
                  .Append("<th style=\"width:220px\">").Append(Html.Escape(L10n.T("rep.hooksuspects.col.source"))).Append("</th>")
                  .Append("</tr></thead><tbody>\r\n");

                foreach (HookSuspect r in rows)
                {
                    sb.Append("<tr>");
                    sb.Append("<td class=\"k\">").Append(r.Combo == null ? "<span class=\"hint\">" + Html.Escape(L10n.T("ui.hs.unknownCombo")) + "</span>" : Html.Escape(r.Combo.Display())).Append("</td>");
                    sb.Append("<td>").Append(Html.Escape(r.Software)).Append("</td>");
                    sb.Append("<td>").Append(Html.Escape(r.Module)).Append("</td>");
                    sb.Append("<td>").Append(Html.Escape(r.ProcessRunning ? L10n.T("common.yes") : L10n.T("common.no"))).Append("</td>");
                    sb.Append("<td>").Append(Html.Escape(HookCatalog.ConfigStateName(r.ConfigState))).Append("</td>");
                    sb.Append("<td>").Append(Html.Escape(r.Note)).Append("</td>");
                    sb.Append("<td class=\"hint\">");
                    if (!string.IsNullOrEmpty(r.SourceLabel)) sb.Append(Html.Escape(r.SourceLabel)).Append("<br>");
                    sb.Append(string.IsNullOrEmpty(r.SourcePath) ? Html.Escape(L10n.T("common.none")) : Html.Escape(r.SourcePath));
                    sb.Append("</td></tr>\r\n");
                }
                sb.Append("</tbody></table></div>\r\n</div></div>\r\n");
            }
        }

        private static string EvidenceSlug(HookEvidence ev)
        {
            switch (ev)
            {
                case HookEvidence.Confirmed: return "confirmed";
                case HookEvidence.HighlySuspected: return "suspected";
                case HookEvidence.KnownDefault: return "default";
                default: return "reference";
            }
        }

        private static void BuildAutoProbe(StringBuilder sb, ScanSnapshot s)
        {
            sb.Append("<h2 id=\"autoprobe\">").Append(Html.Escape(L10n.T("rep.autoprobe.h2"))).Append("</h2>\r\n");
            sb.Append("<div class=\"hint\">").Append(Html.Escape(L10n.T("rep.autoprobe.hint"))).Append("</div>\r\n");

            AutoProbeResult result = SessionRecords.AutoProbe();
            if (result == null)
            {
                sb.Append("<div class=\"hint\">").Append(L10n.T("rep.autoprobe.empty")).Append("</div>\r\n");
                return;
            }

            sb.Append("<p>").Append(Html.Escape(L10n.F("rep.autoprobe.mode",
                result.DryRun ? L10n.T("auto.flag.dryRun") : L10n.T("auto.flag.real"))));
            if (result.Aborted) sb.Append(L10n.IsEnglish ? " " : "　").Append(Html.Escape(L10n.T("auto.flag.aborted")));
            sb.Append("</p>\r\n<div class=\"cards\">\r\n");
            Card(sb, result.CountOf(AutoProbeOutcome.Attributed), L10n.T("auto.outcome.attributed"));
            Card(sb, result.CountOf(AutoProbeOutcome.NoResponse), L10n.T("auto.outcome.noResponse"));
            Card(sb, result.SkippedTotal, L10n.T("auto.outcome.skipped"));
            sb.Append("</div>\r\n");

            if (result.Records.Count == 0)
            {
                sb.Append("<div class=\"hint\">").Append(Html.Escape(L10n.T("rep.autoprobe.nonePressed"))).Append("</div>\r\n");
                return;
            }

            sb.Append("<div style=\"overflow:auto;max-height:460px\"><table><thead><tr>")
              .Append("<th style=\"width:190px\">").Append(Html.Escape(L10n.T("rep.autoprobe.col.combo"))).Append("</th>")
              .Append("<th style=\"width:110px\">").Append(Html.Escape(L10n.T("rep.autoprobe.col.outcome"))).Append("</th>")
              .Append("<th style=\"width:200px\">").Append(Html.Escape(L10n.T("rep.autoprobe.col.owner"))).Append("</th>")
              .Append("<th>").Append(Html.Escape(L10n.T("rep.autoprobe.col.note"))).Append("</th>")
              .Append("</tr></thead><tbody>\r\n");
            foreach (AutoProbeRecord r in result.Records)
            {
                sb.Append("<tr><td class=\"k\">").Append(r.Combo == null ? "<span class=\"hint\">" + Html.Escape(L10n.T("common.none")) + "</span>" : Html.Escape(r.Combo.Display())).Append("</td>");
                sb.Append("<td>").Append(Html.Escape(AutoProbeOutcomeName(r.Outcome))).Append("</td>");
                sb.Append("<td>").Append(r.Outcome == AutoProbeOutcome.Attributed
                    ? Html.Escape((r.ProcessName ?? L10n.T("attr.unknownProc")) + " (PID " + r.Pid + ")")
                    : "<span class=\"hint\">" + Html.Escape(L10n.T("ui.at.autoNoOwner")) + "</span>").Append("</td>");
                sb.Append("<td>").Append(Html.Escape(r.Note)).Append("</td></tr>\r\n");
            }
            sb.Append("</tbody></table></div>\r\n");
        }

        private static string AutoProbeOutcomeName(AutoProbeOutcome outcome)
        {
            switch (outcome)
            {
                case AutoProbeOutcome.Attributed: return L10n.T("auto.outcome.attributed");
                case AutoProbeOutcome.NoResponse: return L10n.T("auto.outcome.noResponse");
                default: return L10n.T("auto.outcome.skipped");
            }
        }

        private static void BuildDeclared(StringBuilder sb, ScanSnapshot s)
        {
            sb.Append("<h2 id=\"declared\">").Append(Html.Escape(L10n.T("rep.declared.h2"))).Append("</h2>\r\n");

            if (s.DeclaredCount == 0)
            {
                sb.Append("<p class=\"hint\">").Append(Html.Escape(L10n.T("rep.declared.empty"))).Append("</p>\r\n");
                return;
            }

            var bySoftware = new Dictionary<string, List<DeclaredHotkey>>(StringComparer.OrdinalIgnoreCase);
            var order = new List<string>();
            foreach (DeclaredHotkey d in s.Config.Declared)
            {
                List<DeclaredHotkey> list;
                if (!bySoftware.TryGetValue(d.Software, out list))
                {
                    list = new List<DeclaredHotkey>();
                    bySoftware[d.Software] = list;
                    order.Add(d.Software);
                }
                list.Add(d);
            }
            order.Sort(StringComparer.OrdinalIgnoreCase);

            foreach (string software in order)
            {
                List<DeclaredHotkey> list = bySoftware[software];
                sb.Append("<div class=\"group\"><div class=\"hd\">").Append(Html.Escape(software))
                  .Append(Html.Escape(L10n.F("rep.declared.count", list.Count))).Append("</div>\r\n");
                sb.Append("<div class=\"bd\" style=\"padding:0\"><table><thead><tr>")
                  .Append("<th style=\"width:170px\">").Append(Html.Escape(L10n.T("ui.col.combo"))).Append("</th>")
                  .Append("<th style=\"width:150px\">").Append(Html.Escape(L10n.T("rep.declared.module"))).Append("</th>")
                  .Append("<th>").Append(Html.Escape(L10n.T("rep.declared.purpose"))).Append("</th>")
                  .Append("<th style=\"width:150px\">").Append(Html.Escape(L10n.T("rep.declared.raw"))).Append("</th>")
                  .Append("<th>").Append(Html.Escape(L10n.T("rep.declared.source"))).Append("</th>")
                  .Append("</tr></thead><tbody>\r\n");
                foreach (DeclaredHotkey d in list)
                {
                    sb.Append("<tr><td class=\"k\">").Append(Html.Escape(d.Combo.Display())).Append("</td>");
                    sb.Append("<td>").Append(Html.Escape(L10n.Term(d.Module))).Append("</td>");
                    sb.Append("<td>").Append(Html.Escape(L10n.Term(d.Purpose))).Append("</td>");
                    sb.Append("<td class=\"k\">").Append(Html.Escape(d.RawValue)).Append("</td>");
                    sb.Append("<td class=\"hint\">").Append(Html.Escape(d.SourcePath)).Append("</td></tr>\r\n");
                }
                sb.Append("</tbody></table></div></div>\r\n");
            }
        }

        private static void BuildAdvice(StringBuilder sb, ScanSnapshot s)
        {
            sb.Append("<h2 id=\"advice\">").Append(Html.Escape(L10n.T("rep.advice.h2"))).Append("</h2>\r\n");
            int count = 0;

            // 先按模块收集行，再聚合成一条建议：
            // ① 只认精确匹配的行——FuzzyOnly 的行只是「忽略 Shift 后可能对得上」的提示，
            //    早先版本把它们当成事实写进建议，产出了配置里根本不存在的组合；
            // ② 同一模块的多条重映射合并成一条，列组合键，而不是把同一段话重复 N 遍。
            var kmRows = new List<TruthRow>();
            var peekRows = new List<TruthRow>();
            foreach (TruthRow r in s.Truth.Rows)
            {
                if (r.FuzzyOnly) continue;
                if (r.Kind != TruthKind.MatchesDeclared && r.Kind != TruthKind.DeclaredButNotRegistered) continue;
                if (string.IsNullOrEmpty(r.DeclaredBy)) continue;

                if (r.DeclaredBy.IndexOf("Keyboard Manager", StringComparison.OrdinalIgnoreCase) >= 0) kmRows.Add(r);
                else if (r.DeclaredBy.IndexOf("Peek", StringComparison.OrdinalIgnoreCase) >= 0) peekRows.Add(r);
            }

            if (kmRows.Count > 0)
            {
                sb.Append("<div class=\"warn\">\r\n<ul>\r\n");
                sb.Append(L10n.F("rep.advice.km", kmRows.Count, FormatComboList(kmRows))).Append("\r\n");
                sb.Append("</ul>\r\n</div>\r\n");
                count += kmRows.Count;
            }

            if (peekRows.Count > 0)
            {
                int peekOccupied = 0;
                for (int i = 0; i < peekRows.Count; i++)
                {
                    if (peekRows[i].Kind == TruthKind.MatchesDeclared) peekOccupied++;
                }
                string tail = peekOccupied > 0 ? L10n.T("rep.advice.peekOccupied") : L10n.T("rep.advice.peekNotReg");
                sb.Append("<div class=\"warn\">\r\n<ul>\r\n");
                sb.Append(L10n.F("rep.advice.peek", peekRows.Count, FormatComboList(peekRows), tail)).Append("\r\n");
                sb.Append("</ul>\r\n</div>\r\n");
                count += peekRows.Count;
            }

            int unowned = s.Truth.CountOf(TruthKind.RegisteredButUnowned);
            if (unowned > 0)
            {
                sb.Append(L10n.F("rep.advice.unowned", unowned)).Append("\r\n");
            }

            int declaredNotReg = s.Truth.CountOf(TruthKind.DeclaredButNotRegistered);
            if (declaredNotReg > 0)
            {
                sb.Append(L10n.F("rep.advice.declaredNotReg", declaredNotReg)).Append("\r\n");
            }

            int freeCount = s.Truth.CountOf(TruthKind.FreeAndUndeclared);
            if (freeCount > 0)
            {
                var samples = s.Truth.ByKind(TruthKind.FreeAndUndeclared);
                var sb2 = new StringBuilder();
                int taken = 0;
                for (int i = 0; i < samples.Count && taken < 12; i++)
                {
                    if (samples[i].Combo.HasModifier && samples[i].Combo.Vk >= 0x70 && samples[i].Combo.Vk <= 0x87)
                    {
                                    if (taken > 0) sb2.Append(L10n.T("rep.list.sep"));
                        sb2.Append("<code>").Append(Html.Escape(samples[i].Combo.Display())).Append("</code>");
                        taken++;
                    }
                }
                sb.Append(L10n.F("rep.advice.freeOk1", freeCount));
                if (taken > 0) sb.Append(L10n.F("rep.advice.freeOk2", sb2.ToString()));
                sb.Append(L10n.T("rep.advice.freeOkEnd")).Append("\r\n");
            }

            var freeCombos = new HashSet<string>(StringComparer.Ordinal);
            foreach (OccupancyResult occ in s.Occupancy)
            {
                if (occ == null || occ.Combo == null || occ.Occupied) continue;
                freeCombos.Add(occ.Combo.Normalize());
            }
            if (s.HookSuspects != null)
            {
                var groups = new Dictionary<string, List<HookSuspect>>(StringComparer.Ordinal);
                var groupOrder = new List<string>();
                foreach (HookSuspect r in s.HookSuspects)
                {
                    if (r.Combo == null) continue;
                    if (r.Evidence != HookEvidence.Confirmed && r.Evidence != HookEvidence.HighlySuspected) continue;
                    if (!freeCombos.Contains(r.Combo.Normalize())) continue;
                    string key = r.Software + "|" + (int)r.Evidence;
                    List<HookSuspect> bucket;
                    if (!groups.TryGetValue(key, out bucket))
                    {
                        bucket = new List<HookSuspect>();
                        groups[key] = bucket;
                        groupOrder.Add(key);
                    }
                    bucket.Add(r);
                }
                foreach (string key in groupOrder)
                {
                    List<HookSuspect> bucket = groups[key];
                    string source = string.IsNullOrEmpty(bucket[0].SourcePath) ? "" : bucket[0].SourcePath;
                    sb.Append(L10n.F("rep.advice.hookFree", Html.Escape(bucket[0].Software),
                                     FormatSuspectCombos(bucket), HookCatalog.EvidenceName(bucket[0].Evidence),
                                     Html.Escape(source))).Append("\r\n");
                    count++;
                }
            }

            if (unowned == 0 && declaredNotReg == 0 && count == 0 && freeCount == 0)
            {
                sb.Append(L10n.T("rep.advice.none")).Append("\r\n");
            }
        }

        /// <summary>
        /// 把若干行的组合键列成 "A、B、C"，最多内联 8 个（数量由调用方在句首说明）。
        /// </summary>
        private static string FormatComboList(List<TruthRow> rows)
        {
            const int maxInline = 8;
            var sb = new StringBuilder();
            int shown = rows.Count <= maxInline ? rows.Count : maxInline;
            for (int i = 0; i < shown; i++)
            {
                if (i > 0) sb.Append(L10n.T("rep.list.sep"));
                sb.Append("<code>").Append(Html.Escape(rows[i].Combo.Display())).Append("</code>");
            }
            if (rows.Count > maxInline) sb.Append(L10n.T("rep.list.more"));
            return sb.ToString();
        }

        private static string FormatSuspectCombos(List<HookSuspect> rows)
        {
            const int maxInline = 8;
            var sb = new StringBuilder();
            int shown = rows.Count <= maxInline ? rows.Count : maxInline;
            for (int i = 0; i < shown; i++)
            {
                if (i > 0) sb.Append(L10n.T("rep.list.sep"));
                sb.Append("<code>").Append(Html.Escape(rows[i].Combo.Display())).Append("</code>");
            }
            if (rows.Count > maxInline) sb.Append(L10n.T("rep.list.more"));
            return sb.ToString();
        }

        private static void BuildMethod(StringBuilder sb, ScanSnapshot s)
        {
            sb.Append("<h2 id=\"method\">").Append(Html.Escape(L10n.T("rep.method.h2"))).Append("</h2>\r\n");
            sb.Append("<div class=\"group\"><div class=\"bd\">\r\n");
            sb.Append("<h3>").Append(Html.Escape(L10n.T("rep.method.howA"))).Append("</h3>\r\n");
            sb.Append(L10n.T("rep.method.howA.body")).Append("\r\n");

            sb.Append("<h3>").Append(Html.Escape(L10n.T("rep.method.why.h"))).Append("</h3>\r\n");
            sb.Append("<ul>\r\n");
            sb.Append(L10n.T("rep.method.why.b1")).Append("\r\n");
            sb.Append(L10n.T("rep.method.why.b2")).Append("\r\n");
            sb.Append(L10n.T("rep.method.why.b3")).Append("\r\n");
            sb.Append("</ul>\r\n");

            sb.Append("<h3>").Append(Html.Escape(L10n.T("rep.method.miss.h"))).Append("</h3>\r\n");
            sb.Append("<ul>\r\n");
            sb.Append(L10n.T("rep.method.miss.b1")).Append("\r\n");
            sb.Append(L10n.T("rep.method.miss.b2"));
            if (!s.IsAdministrator) sb.Append(L10n.T("rep.method.miss.b2this"));
            sb.Append("</li>\r\n");
            if (ProcessIntegrity.IsBelowMedium)
            {
                sb.Append(L10n.F("rep.method.miss.b3", ProcessIntegrity.Describe(ProcessIntegrity.Current))).Append("\r\n");
            }
            sb.Append("</ul>\r\n");

            sb.Append("<h3>").Append(Html.Escape(L10n.T("rep.method.decl.h"))).Append("</h3>\r\n");
            sb.Append("<ul>\r\n");
            sb.Append(L10n.T("rep.method.decl.b1")).Append("\r\n");
            sb.Append(L10n.T("rep.method.decl.b2")).Append("\r\n");
            sb.Append(L10n.T("rep.method.decl.b3")).Append("\r\n");
            sb.Append("</ul>\r\n");

            sb.Append("<h3>").Append(Html.Escape(L10n.T("rep.method.auto.h"))).Append("</h3>\r\n");
            sb.Append(L10n.T("rep.method.auto.body")).Append("\r\n");
            sb.Append("<h3>").Append(Html.Escape(L10n.T("rep.method.catalog.h"))).Append("</h3>\r\n");
            sb.Append(L10n.T("rep.method.catalog.body")).Append("\r\n");

            sb.Append("<h3>").Append(Html.Escape(L10n.T("rep.method.log.h"))).Append("</h3>\r\n<pre class=\"log\">");
            foreach (string line in s.Log) sb.Append(Html.Escape(line)).Append('\n');
            sb.Append("</pre>\r\n");
            sb.Append("</div></div>\r\n");
        }

        private static void BuildFooter(StringBuilder sb, ScanSnapshot s)
        {
            sb.Append("<footer class=\"sub\">")
              .Append(Html.Escape(L10n.F("rep.footer", AppInfo.Title, AppInfo.Version, L10n.CurrentDisplay)))
              .Append("</footer>\r\n");
        }

        private static string Script()
        {
            string shownFmt = L10n.T("rep.js.shown");
            string js = @"<script>
(function () {
  var table = document.getElementById('occ-table');
  if (!table) return;
  var box = document.getElementById('occ-search');
  var only = document.getElementById('occ-only');
  var counter = document.getElementById('occ-count');
  var rows = table.tBodies[0].rows;
  var shownFmt = __SHOWN_FMT__;

  function apply() {
    var q = (box && box.value ? box.value : '').toLowerCase().replace(/\s+/g, '');
    var occOnly = !!(only && only.checked);
    var shown = 0;
    for (var i = 0; i < rows.length; i++) {
      var r = rows[i];
      var text = (r.cells[0].textContent + r.cells[2].textContent).toLowerCase().replace(/\s+/g, '');
      var ok = (!q || text.indexOf(q) >= 0);
      if (ok && occOnly && r.getAttribute('data-state') !== '1') ok = false;
      r.style.display = ok ? '' : 'none';
      if (ok) shown++;
    }
    if (counter) counter.textContent = shownFmt.replace('{0}', shown).replace('{1}', rows.length);
  }

  if (box) box.addEventListener('input', apply);
  if (only) only.addEventListener('change', apply);
  apply();
})();
</script>
";
            return js.Replace("__SHOWN_FMT__", JsString(shownFmt));
        }

        /// <summary>把字符串安全地嵌进 JS 双引号字面量。</summary>
        private static string JsString(string s)
        {
            var sb = new StringBuilder("\"");
            foreach (char c in s)
            {
                switch (c)
                {
                    case '\\': sb.Append("\\\\"); break;
                    case '"': sb.Append("\\\""); break;
                    case '\r': sb.Append("\\r"); break;
                    case '\n': sb.Append("\\n"); break;
                    case '<': sb.Append("\\u003c"); break;
                    case '>': sb.Append("\\u003e"); break;
                    default: sb.Append(c); break;
                }
            }
            sb.Append("\"");
            return sb.ToString();
        }
    }
}
