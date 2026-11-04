using System.Net;
using System.Text;
using AiDetect.Core.Analysis;
using AiDetect.Core.Signals;

namespace AiDetect.Core.Reporting;

/// <summary>HTML-отчёт (самодостаточная страница). Используется для --html и веб-режима.</summary>
public static class HtmlReportWriter
{
    public const string Styles = """
        :root{--bg:#f7f7f5;--card:#fff;--text:#1d1d1b;--muted:#6b6b66;--line:#e4e3de;--ai:#c2410c;--ai-bg:#fff1e8;--human:#15803d;--human-bg:#ecf8ef;--mid:#a16207;--accent:#2563eb}
        @media (prefers-color-scheme: dark){:root{--bg:#141413;--card:#1f1f1d;--text:#ecebe6;--muted:#9c9b94;--line:#33332f;--ai:#fb923c;--ai-bg:#3a2417;--human:#4ade80;--human-bg:#15301f;--mid:#facc15;--accent:#60a5fa}}
        *{box-sizing:border-box}body{margin:0;background:var(--bg);color:var(--text);font:15px/1.5 -apple-system,BlinkMacSystemFont,"Segoe UI",Roboto,sans-serif}
        main{max-width:980px;margin:0 auto;padding:24px 16px 48px}h1{font-size:22px;margin:0 0 4px}h2{font-size:17px;margin:28px 0 10px}
        .muted{color:var(--muted)}.card{background:var(--card);border:1px solid var(--line);border-radius:12px;padding:18px 20px;margin:14px 0}
        .score{display:flex;gap:24px;align-items:center;flex-wrap:wrap}.pct{font-size:56px;font-weight:700;line-height:1}
        .bar{height:10px;border-radius:5px;background:var(--line);overflow:hidden;min-width:220px;flex:1}.bar>div{height:100%}
        .sig{border-top:1px solid var(--line);padding:10px 0}.sig:first-child{border-top:0}.sig .t{font-weight:600}
        .badge{display:inline-block;font-size:12px;padding:1px 8px;border-radius:10px;margin-left:6px;background:var(--line);color:var(--muted)}
        .c-ai{color:var(--ai)}.c-human{color:var(--human)}.num{font-variant-numeric:tabular-nums;font-weight:700;display:inline-block;min-width:44px}
        code,pre{font:13px/1.4 ui-monospace,SFMono-Regular,Menlo,monospace}pre{background:var(--bg);border:1px solid var(--line);border-radius:8px;padding:6px 10px;margin:6px 0 0;white-space:pre-wrap;word-break:break-word}
        table{border-collapse:collapse;width:100%}td{padding:5px 6px;border-top:1px solid var(--line);vertical-align:middle}td.v{text-align:right;width:60px}
        .gbar{height:8px;border-radius:4px}.warn{background:var(--ai-bg);border-color:transparent}
        form{display:flex;gap:8px;flex-wrap:wrap}input[type=text],input[type=password]{flex:1;min-width:240px;padding:10px 12px;border:1px solid var(--line);border-radius:8px;background:var(--card);color:var(--text);font-size:15px}
        button{padding:10px 18px;border:0;border-radius:8px;background:var(--accent);color:#fff;font-size:15px;cursor:pointer}label{font-size:13px}
        """;

    public static string RenderPage(AnalysisReport report, string? formHtml = null)
    {
        var sb = new StringBuilder();
        sb.Append("<!doctype html><html lang=\"ru\"><head><meta charset=\"utf-8\"><meta name=\"viewport\" content=\"width=device-width,initial-scale=1\">");
        sb.Append("<title>Детектор ИИ-кода</title><style>").Append(Styles).Append("</style></head><body><main>");
        if (formHtml is not null)
        {
            sb.Append(formHtml);
        }

        sb.Append(RenderBody(report));
        sb.Append("</main></body></html>");
        return sb.ToString();
    }

    public static string RenderBody(AnalysisReport r)
    {
        var sb = new StringBuilder();
        int pct = (int)Math.Round(r.Probability * 100);
        string cls = pct >= 65 ? "c-ai" : pct > 35 ? "" : "c-human";
        string barColor = pct >= 65 ? "var(--ai)" : pct > 35 ? "var(--mid)" : "var(--human)";

        sb.Append("<div class=\"card\"><h1>").Append(E(r.Title)).Append("</h1>");
        sb.Append("<div class=\"muted\">").Append(E(r.Source)).Append("</div>");
        sb.Append("<div class=\"muted\">").Append(E($"автор: {r.Author} · ветка: {r.SourceBranch} · коммитов: {r.Commits}")).Append("</div></div>");

        sb.Append("<div class=\"card\"><div class=\"score\"><div class=\"pct ").Append(cls).Append("\">").Append(pct).Append("%</div><div style=\"flex:1\">");
        sb.Append("<div>Вероятность, что код написан <b>Claude Code</b></div>");
        sb.Append("<div class=\"bar\"><div style=\"width:").Append(pct).Append("%;background:").Append(barColor).Append("\"></div></div>");
        sb.Append("<div style=\"margin-top:6px\"><b>").Append(E(r.Verdict)).Append("</b></div>");
        sb.Append("<div class=\"muted\">Надёжность: ").Append(E(r.Confidence)).Append(" — ").Append(E(r.ConfidenceNote)).Append("</div>");
        if (r.ArtifactsFound)
        {
            sb.Append("<div class=\"muted\">Без учёта прямых следов (только код и процесс): ").Append((int)Math.Round(r.ProbabilityWithoutArtifacts * 100)).Append("%</div>");
        }

        sb.Append("</div></div>");
        if (!string.IsNullOrEmpty(r.Summary))
        {
            sb.Append("<div style=\"margin-top:12px\">").Append(E(r.Summary)).Append("</div>");
        }

        sb.Append("</div>");

        AppendSignals(sb, "Главные признаки ИИ", r.TopAiSignals(10), "c-ai");
        AppendSignals(sb, "Признаки человека", r.TopHumanSignals(8), "c-human");

        if (r.FileScores.Count >= 2)
        {
            sb.Append("<h2>Файлы (оценка только по коду)</h2><div class=\"card\"><table>");
            foreach (var f in r.FileScores.Take(15))
            {
                int fp = (int)Math.Round(f.Probability * 100);
                string fc = fp >= 65 ? "c-ai" : fp > 35 ? "" : "c-human";
                sb.Append("<tr><td class=\"num ").Append(fc).Append("\">").Append(fp).Append("%</td><td><code>").Append(E(f.Path)).Append("</code> <span class=\"muted\">+")
                    .Append(f.AddedLines).Append("</span><div class=\"muted\">").Append(E(string.Join("; ", f.TopSignals))).Append("</div></td></tr>");
            }

            sb.Append("</table></div>");
        }

        sb.Append("<h2>Вклад групп сигналов</h2><div class=\"card\"><table>");
        double max = Math.Max(1, r.Groups.Select(g => Math.Abs(g.Capped)).DefaultIfEmpty(1).Max());
        foreach (var g in r.Groups.Where(g => Math.Abs(g.Capped) >= 0.05))
        {
            int w = (int)Math.Round(Math.Abs(g.Capped) / max * 100);
            string color = g.Capped > 0 ? "var(--ai)" : "var(--human)";
            sb.Append("<tr><td>").Append(E(GroupInfo.Title(g.Group)));
            if (GroupInfo.IsMimicryResistant(g.Group))
            {
                sb.Append("<span class=\"badge\">устойчив к маскировке</span>");
            }

            sb.Append("</td><td class=\"v num\">").Append(g.Capped.ToString("+0.0;-0.0")).Append("</td><td style=\"width:40%\"><div class=\"gbar\" style=\"width:")
                .Append(w).Append("%;background:").Append(color).Append("\"></div></td></tr>");
        }

        sb.Append("</table></div>");

        sb.Append("<div class=\"card muted\">Проанализировано: ").Append(r.FilesAnalyzed).Append(" C#-файлов из ").Append(r.FilesChanged)
            .Append(" изменённых; +").Append(r.AddedCodeLines).Append(" строк кода, +").Append(r.AddedTestLines).Append(" строк тестов; база проекта: ")
            .Append(r.BaselineFiles).Append(" файлов.");
        if (r.ExcludedFiles.Count > 0)
        {
            sb.Append("<br>Исключены: ").Append(E(string.Join("; ", r.ExcludedFiles.Take(8))));
        }

        sb.Append("<br>Оценка вероятностная и не является доказательством авторства.</div>");
        foreach (var w in r.Warnings)
        {
            sb.Append("<div class=\"card warn\">⚠ ").Append(E(w)).Append("</div>");
        }

        return sb.ToString();
    }

    private static void AppendSignals(StringBuilder sb, string title, IEnumerable<SignalResult> signals, string cls)
    {
        var list = signals.ToList();
        if (list.Count == 0)
        {
            return;
        }

        sb.Append("<h2>").Append(E(title)).Append("</h2><div class=\"card\">");
        foreach (var s in list)
        {
            sb.Append("<div class=\"sig\"><span class=\"num ").Append(cls).Append("\">").Append(s.Contribution.ToString("+0.0;-0.0")).Append("</span> <span class=\"t\">")
                .Append(E(s.Title)).Append("</span><span class=\"badge\">").Append(E(GroupInfo.Title(s.Group))).Append("</span>");
            if (!string.IsNullOrEmpty(s.Detail))
            {
                sb.Append("<div class=\"muted\">").Append(E(s.Detail)).Append("</div>");
            }

            foreach (var ex in s.Examples.Take(2))
            {
                var where = ex.Line > 0 ? $"{ex.File}:{ex.Line}" : ex.File;
                sb.Append("<pre><span class=\"muted\">").Append(E(where)).Append("</span>\n").Append(E(ex.Snippet)).Append("</pre>");
            }

            sb.Append("</div>");
        }

        sb.Append("</div>");
    }

    public static string E(string? s) => WebUtility.HtmlEncode(s ?? "");
}
