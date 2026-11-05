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
        if (!string.IsNullOrEmpty(r.LanguageTitle))
        {
            sb.AppendLine(" " + C("2", "язык: " + r.LanguageTitle + (r.LanguageAuto ? " (определён автоматически)" : "")));
        }

        if (!string.IsNullOrEmpty(r.Stack))
        {
            sb.AppendLine(" " + C("2", "стек: " + r.Stack));
        }
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

        if (!string.IsNullOrEmpty(r.Summary))
        {
            sb.AppendLine();
            foreach (var wrapped in Wrap(r.Summary, 76))
            {
                sb.AppendLine("  " + wrapped);
            }
        }

        sb.AppendLine();
        var ai = r.TopAiSignals(verbose ? 40 : 8).ToList();
        if (ai.Count > 0)
        {
            sb.AppendLine(C("1;31", "  Главные признаки ИИ:"));
            foreach (var s in ai)
            {
                AppendSignal(sb, r, s, "31");
            }

            sb.AppendLine();
        }

        var human = r.TopHumanSignals(verbose ? 40 : 5).ToList();
        if (human.Count > 0)
        {
            sb.AppendLine(C("1;32", "  Признаки человека:"));
            foreach (var s in human)
            {
                AppendSignal(sb, r, s, "32");
            }

            sb.AppendLine();
        }

        if (r.FileScores.Count >= 2)
        {
            sb.AppendLine(C("1", "  Файлы (оценка только по коду, самые «ИИ-подобные» сверху):"));
            foreach (var f in r.FileScores.Take(verbose ? 30 : 6))
            {
                int fp = (int)Math.Round(f.Probability * 100);
                string col = fp >= 65 ? "31" : fp > 35 ? "33" : "32";
                var why = f.TopSignals.Count > 0 ? C("2", "  " + Truncate(string.Join("; ", f.TopSignals), 70)) : "";
                sb.AppendLine($"    {C(col, $"{fp,3}%")}  {Truncate(ShortPath(f.Path), 48),-48} +{f.AddedLines}{why}");
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
            sb.AppendLine($"    {Truncate(r.GroupTitle(g.Group), 34),-34} {g.Capped,5:+0.0;-0.0}  {arrows}{resistant}");
        }

        sb.AppendLine();
        sb.AppendLine(C("2", $"  Проанализировано: {r.FilesAnalyzed} {Noun(r)}-файлов из {r.FilesChanged} изменённых; +{r.AddedCodeLines} строк кода, +{r.AddedTestLines} строк тестов; " +
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

    private static string Noun(AnalysisReport r) => string.IsNullOrEmpty(r.CodeNoun) ? "C#" : r.CodeNoun;

    private void AppendSignal(StringBuilder sb, AnalysisReport r, SignalResult s, string colorCode)
    {
        sb.AppendLine($"   {C(colorCode, $"{s.Contribution,5:+0.0;-0.0}")}  {s.Title}  {C("2", "[" + r.GroupTitle(s.Group) + "]")}");
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

    private static IEnumerable<string> Wrap(string text, int width)
    {
        var line = new StringBuilder();
        foreach (var word in text.Split(' '))
        {
            if (line.Length + word.Length + 1 > width && line.Length > 0)
            {
                yield return line.ToString();
                line.Clear();
            }

            if (line.Length > 0)
            {
                line.Append(' ');
            }

            line.Append(word);
        }

        if (line.Length > 0)
        {
            yield return line.ToString();
        }
    }

    internal static string ShortPath(string path)
    {
        var parts = path.Replace('\\', '/').Split('/');
        return parts.Length <= 2 ? path : string.Join('/', parts[^2..]);
    }

    private static string Truncate(string s, int n) => s.Length <= n ? s : s[..(n - 1)] + "…";
}
