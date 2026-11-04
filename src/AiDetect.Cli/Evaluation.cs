using AiDetect.Core.Analysis;
using AiDetect.Core.Evaluation;

namespace AiDetect.Cli;

/// <summary>Команды eval и calibrate: проверка точности на размеченном корпусе и подбор весов.</summary>
public static class Evaluation
{
    public static async Task<int> RunEvalAsync(CliOptions o)
    {
        var root = o.Target ?? "corpus";
        var items = await CorpusEvaluator.LoadCorpusAsync(root);
        if (items.Count == 0)
        {
            Console.Error.WriteLine($"В {root} нет размеченных MR (ожидаются подкаталоги human*/ и ai*/ с *.json).");
            return 1;
        }

        var options = o.ToAnalyzerOptions();
        var scored = CorpusEvaluator.Score(items, options);
        Console.WriteLine($"{"Категория",-16} {"MR",-44} {"P(ИИ)",6}  главные признаки");
        foreach (var s in scored.OrderBy(s => s.Item.Category).ThenByDescending(s => s.Probability))
        {
            var top = s.Signals.OrderByDescending(x => Math.Abs(x.Contribution)).Take(3)
                .Select(x => $"{x.Id}{x.Contribution:+0.0;-0.0}");
            bool wrong = (s.Probability >= 0.5 ? 1 : 0) != s.Item.Label;
            Console.WriteLine($"{s.Item.Category,-16} {Trim(s.Item.Name, 44),-44} {s.Probability * 100,5:0}% {(wrong ? "✗" : " ")} {string.Join(", ", top)}");
        }

        Console.WriteLine();
        PrintMetrics("Весь корпус", scored.Select(s => (s.Item.Label, s.Probability)).ToList());
        foreach (var cat in scored.Select(s => s.Item.Category).Distinct().Where(c => c.StartsWith("ai", StringComparison.OrdinalIgnoreCase)))
        {
            var subset = scored.Where(s => s.Item.Category == cat || s.Item.Label == 0).Select(s => (s.Item.Label, s.Probability)).ToList();
            PrintMetrics($"{cat} против human", subset);
        }

        if (o.Verbose)
        {
            Console.WriteLine();
            Console.WriteLine($"{"Признак",-26} {"человек",8} {"ИИ",8} {"разница",8}  (среднее свидетельство×надёжность, n)");
            foreach (var f in CorpusEvaluator.FeatureSeparation(scored).OrderByDescending(f => double.IsNaN(f.MeanAi - f.MeanHuman) ? -9 : f.MeanAi - f.MeanHuman))
            {
                Console.WriteLine($"{f.Id,-26} {f.MeanHuman,8:0.00} {f.MeanAi,8:0.00} {f.MeanAi - f.MeanHuman,8:+0.00;-0.00}  (h={f.NHuman}, ai={f.NAi})");
            }
        }

        return 0;
    }

    public static async Task<int> RunCalibrateAsync(CliOptions o)
    {
        var root = o.Target ?? "corpus";
        var items = await CorpusEvaluator.LoadCorpusAsync(root);
        if (items.Count < 6)
        {
            Console.Error.WriteLine("Для калибровки нужно хотя бы 6 размеченных MR (лучше 30+).");
            return 1;
        }

        var start = o.WeightsPath is not null ? ModelParameters.Load(o.WeightsPath) : new ModelParameters();
        var options = new AnalyzerOptions { IgnoreArtifacts = true, Parameters = start };
        var scored = CorpusEvaluator.Score(items, options);
        var samples = Calibrator.BuildSamples(scored);
        var prior = Calibrator.DefaultWeights(scored);

        PrintMetrics("До калибровки (экспертные веса)", scored.Select(s => (s.Item.Label, s.Probability)).ToList());
        var cv = Calibrator.CrossValidate(samples, prior, start);
        PrintMetrics("После калибровки, 5-кратная перекрёстная проверка", cv);

        var fitted = Calibrator.Fit(samples, prior, start);
        var outPath = o.OutPath ?? "weights.json";
        fitted.Save(outPath);
        Console.WriteLine($"Веса сохранены в {Path.GetFullPath(outPath)}. Использование: --weights {outPath} (или положите weights.json рядом с программой).");
        Console.WriteLine("Наибольшие изменения весов:");
        foreach (var (id, w) in fitted.Weights.OrderByDescending(kv => Math.Abs(kv.Value - prior.GetValueOrDefault(kv.Key))).Take(12))
        {
            Console.WriteLine($"  {id,-26} {prior.GetValueOrDefault(id),5:0.00} → {w,5:0.00}");
        }

        return 0;
    }

    private static void PrintMetrics(string title, List<(int Label, double P)> scored)
    {
        var m = CorpusEvaluator.Metrics(scored);
        Console.WriteLine($"{title}: n={m.Count} (ИИ {m.Positives}), ROC AUC={m.Auc:0.000}, точность={m.Accuracy * 100:0}%, " +
                          $"ложные «ИИ» на человеческом коде={m.Fpr * 100:0}%, найдено ИИ={m.Tpr * 100:0}%, Brier={m.Brier:0.000}");
    }

    private static string Trim(string s, int n) => s.Length <= n ? s : s[..(n - 1)] + "…";
}
