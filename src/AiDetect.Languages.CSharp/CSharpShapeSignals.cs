using AiDetect.Core.Analysis;
using AiDetect.Core.Features;
using AiDetect.Core.Preprocessing;
using AiDetect.Core.Signals;

namespace AiDetect.Languages.CSharp;

/// <summary>Признаки формы MR, специфичные для C#: «полная архитектура» (интерфейс + реализация + DI), единообразие стиля по файлам.</summary>
internal sealed class CSharpShapeSignals(AnalysisContext ctx, SignalBuilder b)
{
    public void Evaluate()
    {
        var cs = ctx.AnalyzedFiles.ToList();

        // «Полная архитектура на маленькую задачу»: интерфейс + реализация + регистрация DI (+ options/валидатор).
        var newNames = cs.Where(f => f.IsNew).Select(f => Path.GetFileNameWithoutExtension(f.Path)).ToHashSet(StringComparer.Ordinal);
        int pairs = newNames.Count(n => n.Length > 1 && n[0] == 'I' && char.IsUpper(n[1]) && newNames.Contains(n[1..]));
        bool di = cs.Any(f => f.Layer == Layer.DependencyInjection);
        bool extras = cs.Any(f => f.Layer is Layer.Options or Layer.Validation);
        if (pairs >= 1 && di)
        {
            double score = Math.Min(1, 0.3 + (0.2 * pairs) + (extras ? 0.4 : 0));
            b.Add("mr.fullArchitecture", SignalGroup.MrShape,
                "«Полная» архитектура: новый интерфейс + реализация + регистрация в DI" + (extras ? " + options/валидатор" : ""),
                score, 1, 0.3, $"пар «интерфейс + реализация»: {pairs}", [], pairs);
        }

        // Единообразие: признаки «ИИ-стиля» одинаково выражены во всех файлах (человек пишет неровно).
        Uniformity(cs);
    }

    private void Uniformity(List<FileAnalysis> cs)
    {
        var perFile = new List<double>();
        foreach (var f in cs.Where(f => f.Stats is not null && f.AddedLines >= 25 && !f.IsTest))
        {
            var s = f.Stats!;
            var scores = new List<double>();
            if (s[K.DocTargets] >= 2)
            {
                scores.Add(SignalBuilder.Map(s[K.DocTargetsDocumented] / s[K.DocTargets], 0.05, 0.85));
            }

            if (s[K.AsyncMethods] >= 2)
            {
                scores.Add(SignalBuilder.Map(s[K.AsyncWithCt] / s[K.AsyncMethods], 0.1, 0.85));
            }

            double nullChecks = s[K.NullPatternChecks] + s[K.NullEqualityChecks];
            if (nullChecks >= 2)
            {
                scores.Add(SignalBuilder.Map(s[K.NullPatternChecks] / nullChecks, 0.15, 0.95));
            }

            double logs = s[K.LogTemplated] + s[K.LogInterpolated];
            if (logs >= 2)
            {
                scores.Add(SignalBuilder.Map(s[K.LogTemplated] / logs, 0.2, 1.0));
            }

            if (s[K.CodeLines] >= 30)
            {
                double irregular = (s[K.TrailingWhitespace] + s[K.KeywordNoSpace] + s[K.OperatorNoSpace] + s[K.CommaNoSpace]) * 100 / s[K.CodeLines];
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
                Math.Min(1, mean * 1.5), SignalBuilder.Rel(perFile.Count, 3), 0.3,
                $"{perFile.Count} файлов, средняя оценка {mean:0.00}, разброс {sd:0.00}", [], sd);
        }
        else if (sd >= 0.45)
        {
            b.Add("mr.uniformity", SignalGroup.MrShape, "Стиль сильно различается между файлами MR (неровно, как у человека)",
                -0.6, SignalBuilder.Rel(perFile.Count, 3), 0.3, $"{perFile.Count} файлов, разброс оценок {sd:0.00}", [], sd);
        }
    }

}
