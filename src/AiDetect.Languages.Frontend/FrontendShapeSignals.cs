using AiDetect.Core.Analysis;
using AiDetect.Core.Features;
using AiDetect.Core.Preprocessing;
using AiDetect.Core.Signals;

namespace AiDetect.Languages.Frontend;

/// <summary>
/// Форма MR, специфичная для фронтенда: новый компонент «комплектом» (типы, константы, хуки, index, тест, сторис, стили),
/// barrel-файлы, единообразие ИИ-признаков по файлам.
/// </summary>
internal sealed class FrontendShapeSignals(AnalysisContext ctx, SignalBuilder b)
{
    public void Evaluate()
    {
        var files = ctx.AnalyzedFiles.ToList();
        ComponentKit(files);
        Barrels(files);
        Uniformity(files);
    }

    private void ComponentKit(List<FileAnalysis> files)
    {
        var newFiles = files.Where(f => f.IsNew).ToList();
        var components = newFiles.Where(f => f.Layer == Layer.Component && f.AddedLines >= 20 && !f.Path.EndsWith(".html", StringComparison.OrdinalIgnoreCase)).ToList();
        if (components.Count == 0)
        {
            return;
        }

        int kits = 0;
        var notes = new List<string>();
        foreach (var c in components)
        {
            var dir = Path.GetDirectoryName(c.Path)?.Replace('\\', '/') ?? "";
            var stem = Stem(c.Path);
            var companions = newFiles.Where(f => f != c && (Path.GetDirectoryName(f.Path)?.Replace('\\', '/') ?? "") is var d &&
                                                 (d == dir || d.StartsWith(dir + "/", StringComparison.Ordinal) || dir.StartsWith(d + "/", StringComparison.Ordinal) && Stem(f.Path) == stem))
                .ToList();
            var kinds = new HashSet<string>(StringComparer.Ordinal);
            foreach (var f in companions)
            {
                var name = Path.GetFileName(f.Path).ToLowerInvariant();
                if (f.Layer == Layer.Tests)
                {
                    kinds.Add("тест");
                }
                else if (f.Layer == Layer.Stories)
                {
                    kinds.Add("сторис");
                }
                else if (f.Layer == Layer.Types)
                {
                    kinds.Add("типы");
                }
                else if (f.Layer == Layer.Constants)
                {
                    kinds.Add("константы");
                }
                else if (f.Layer == Layer.Hook)
                {
                    kinds.Add("хук");
                }
                else if (name is "index.ts" or "index.js" or "index.tsx")
                {
                    kinds.Add("index");
                }
                else if (f.Layer == Layer.Styles && Stem(f.Path) == stem)
                {
                    kinds.Add("стили");
                }
            }

            // Стили и шаблон рядом с компонентом Angular CLI создаёт сам — «комплектом» считаем 3+ вида спутников.
            if (kinds.Count >= 3)
            {
                kits++;
                notes.Add($"{Path.GetFileName(c.Path)}: {string.Join(", ", kinds)}");
            }
        }

        double share = kits / (double)components.Count;
        b.Add("mr.componentKit", SignalGroup.MrShape,
            share > 0 ? "Новые компоненты «комплектом»: тесты, типы, константы, хуки, index, сторис" : "Новые компоненты без «комплекта» спутников",
            share > 0 ? Math.Min(1, 0.4 + share) : -0.2, SignalBuilder.Rel(components.Count, 1), 0.5,
            share > 0 ? string.Join("; ", notes.Take(3)) : $"{components.Count} новых компонентов", [], share);
    }

    private static string Stem(string path)
    {
        var name = Path.GetFileName(path).ToLowerInvariant();
        int dot = name.IndexOf('.');
        return dot > 0 ? name[..dot] : name;
    }

    private void Barrels(List<FileAnalysis> files)
    {
        var barrels = files.Where(f => f.IsNew && Path.GetFileName(f.Path) is "index.ts" or "index.js" && f.Stats is { } s && s[FK.ScriptLines] <= 15 &&
                                       f.AddedLines <= 20).ToList();
        if (barrels.Count > 0)
        {
            b.Add("mr.barrel", SignalGroup.MrShape, "Новые barrel-файлы index.ts только с ре-экспортами",
                Math.Min(1, 0.5 + 0.25 * barrels.Count), 1, 0.3, string.Join(", ", barrels.Select(f => f.Path).Take(3)), [], barrels.Count);
        }
    }

    /// <summary>ИИ-признаки одинаково выражены во всех файлах (человек пишет неровно).</summary>
    private void Uniformity(List<FileAnalysis> files)
    {
        var perFile = new List<double>();
        foreach (var f in files.Where(f => f.Stats is not null && f.AddedLines >= 25 && !f.IsTest))
        {
            var s = f.Stats!;
            var scores = new List<double>();
            void Add(double hits, double total, double humanAt, double aiAt, double min)
            {
                if (total >= min)
                {
                    scores.Add(SignalBuilder.Map(hits / total, humanAt, aiAt));
                }
            }

            Add(s[FK.Nullish], s[FK.Nullish] + s[FK.OrDefault], 0.1, 0.8, 2);
            Add(s[FK.CatchParamError], s[FK.CatchParams], 0.1, 0.9, 1);
            Add(s[FK.ConsoleErrorTemplated], s[FK.ConsoleErrors], 0.1, 0.8, 1);
            Add(s[FK.ButtonsTyped], s[FK.Buttons], 0.1, 0.9, 1);
            Add(s[FK.LetDecl], s[FK.LetDecl] + s[FK.ConstDecl], 0.4, 0.05, 4);
            if (s[FK.CodeLines] >= 30)
            {
                double irregular = (s[FK.TrailingWhitespace] + s[FK.KeywordNoSpace] + s[FK.OperatorNoSpace] + s[FK.BraceNoSpace]) * 100 / s[FK.CodeLines];
                scores.Add(irregular <= 0.01 ? 0.5 : SignalBuilder.Map(irregular, 3, 0));
            }

            if (scores.Count >= 2)
            {
                perFile.Add(scores.Average());
            }
        }

        if (perFile.Count < 3)
        {
            return;
        }

        double mean = perFile.Average();
        double sd = Math.Sqrt(perFile.Average(v => (v - mean) * (v - mean)));
        if (mean > 0.2 && sd < 0.3)
        {
            b.Add("mr.uniformity", SignalGroup.MrShape, "ИИ-признаки одинаково выражены во всех файлах MR (ровный стиль)",
                Math.Min(1, mean * 1.5), SignalBuilder.Rel(perFile.Count, 3), 0.3, $"{perFile.Count} файлов, средняя оценка {mean:0.00}, разброс {sd:0.00}", [], sd);
        }
        else if (sd >= 0.45)
        {
            b.Add("mr.uniformity", SignalGroup.MrShape, "Стиль сильно различается между файлами MR (неровно, как у человека)",
                -0.6, SignalBuilder.Rel(perFile.Count, 3), 0.3, $"{perFile.Count} файлов, разброс оценок {sd:0.00}", [], sd);
        }
    }
}
