using System.Text;
using AiDetect.Core.Analysis;
using AiDetect.Core.Signals;

namespace AiDetect.Core.Reporting;

/// <summary>Текстовый отчёт для терминала (с ANSI-цветами, если они включены).</summary>
public sealed class ConsoleReportWriter(bool color = true, bool verbose = false)
{
    private string C(string code, string text) => color ? $"\u001b[{code}m{text}\u001b[0m" : text;

    public string Render(AnalysisReport r)
    {
        var sb = new StringBuilder();
        var line = new string('═', 78);
        sb.AppendLine(C("2", line));
        sb.AppendLine(" " + C("1", "Анализ MR: " + Truncate(r.Title, 64)));
        if (!string.IsNullOrEmpty(r.Source))
        {
            sb.AppendLine(" " + C("2", r.Source));
        }

        var meta = new List<string>();
        if (!string.IsNullOrEmpty(r.Author))
        {
            meta.Add("автор: " + r.Author);
        }

        if (!string.IsNullOrEmpty(r.SourceBranch))
        {
            meta.Add("ветка: " + r.SourceBranch);
        }

        meta.Add($"коммитов: {r.Commits}");
        sb.AppendLine(" " + C("2", string.Join(" · ", meta)));
        sb.AppendLine(C("2", line));
        sb.AppendLine();

        int pct = (int)Math.Round(r.Probability * 100);
        string pColor = pct >= 65 ? "1;31" : pct > 35 ? "1;33" : "1;32";
        sb.Append("  Вероятность, что код написан Claude Code: ").Append(C(pColor, $"{pct}%")).Append("  ").AppendLine(Bar(r.Probability, 30, pColor));
        sb.AppendLine("  Вердикт: " + C("1", r.Verdict));
        sb.AppendLine($"  Надёжность оценки: {r.Confidence} — {r.ConfidenceNote}");
        if (r.ArtifactsFound)
        {
            sb.AppendLine($"  Без учёта прямых следов (только код и процесс): {(int)Math.Round(r.ProbabilityWithoutArtifacts * 100)}%");
        }

        sb.AppendLine();
        var ai = r.TopAiSignals(verbose ? 40 : 8).ToList();
        if (ai.Count > 0)
        {
            sb.AppendLine(C("1;31", "  Главные признаки ИИ:"));
            foreach (var s in ai)
            {
                AppendSignal(sb, s, "31");
            }

            sb.AppendLine();
        }

        var human = r.TopHumanSignals(verbose ? 40 : 5).ToList();
        if (human.Count > 0)
        {
            sb.AppendLine(C("1;32", "  Признаки человека:"));
            foreach (var s in human)
            {
                AppendSignal(sb, s, "32");
            }

            sb.AppendLine();
        }

        sb.AppendLine(C("1", "  Вклад групп сигналов (в логитах; ▲ — за ИИ, ▼ — за человека):"));
        foreach (var g in r.Groups.Where(g => Math.Abs(g.Capped) >= 0.05))
        {
            string arrows = g.Capped > 0
                ? C("31", new string('▲', Math.Clamp((int)Math.Round(g.Capped * 2), 1, 16)))
                : C("32", new string('▼', Math.Clamp((int)Math.Round(-g.Capped * 2), 1, 16)));
            string resistant = GroupInfo.IsMimicryResistant(g.Group) ? C("2", " (устойчив к маскировке)") : "";
            sb.AppendLine($"    {GroupInfo.Title(g.Group),-30} {g.Capped,5:+0.0;-0.0}  {arrows}{resistant}");
        }

        sb.AppendLine();
        sb.AppendLine(C("2", $"  Проанализировано: {r.FilesAnalyzed} C#-файлов из {r.FilesChanged} изменённых; +{r.AddedCodeLines} строк кода, +{r.AddedTestLines} строк тестов; " +
                             $"база проекта: {r.BaselineFiles} файлов."));
        if (r.ExcludedFiles.Count > 0)
        {
            sb.AppendLine(C("2", $"  Исключены: {string.Join("; ", r.ExcludedFiles.Take(5))}{(r.ExcludedFiles.Count > 5 ? $" и ещё {r.ExcludedFiles.Count - 5}" : "")}"));
        }

        foreach (var w in r.Warnings)
        {
            sb.AppendLine(C("33", "  ⚠ " + w));
        }

        sb.AppendLine(C("2", "  Оценка вероятностная и не является доказательством авторства."));
        return sb.ToString();
    }

    private void AppendSignal(StringBuilder sb, SignalResult s, string colorCode)
    {
        sb.AppendLine($"   {C(colorCode, $"{s.Contribution,5:+0.0;-0.0}")}  {s.Title}  {C("2", "[" + GroupInfo.Title(s.Group) + "]")}");
        if (!string.IsNullOrEmpty(s.Detail))
        {
            sb.AppendLine("          " + C("2", s.Detail));
        }

        foreach (var ex in s.Examples.Take(verbose ? 3 : 1))
        {
            var where = ex.Line > 0 ? $"{ShortPath(ex.File)}:{ex.Line}" : ex.File;
            sb.AppendLine("          " + C("36", where) + "  " + Truncate(ex.Snippet, 90));
        }
    }

    private string Bar(double p, int width, string colorCode)
    {
        int filled = (int)Math.Round(p * width);
        return C(colorCode, new string('█', filled)) + C("2", new string('░', width - filled));
    }

    internal static string ShortPath(string path)
    {
        var parts = path.Replace('\\', '/').Split('/');
        return parts.Length <= 2 ? path : string.Join('/', parts[^2..]);
    }

    private static string Truncate(string s, int n) => s.Length <= n ? s : s[..(n - 1)] + "…";
}
