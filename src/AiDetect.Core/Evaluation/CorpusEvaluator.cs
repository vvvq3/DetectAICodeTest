using AiDetect.Core.Analysis;
using AiDetect.Core.Model;
using AiDetect.Core.Signals;
using AiDetect.Core.Sources;

namespace AiDetect.Core.Evaluation;

public sealed record CorpusItem(string Name, string Category, int Label, MergeRequestData Data);

public sealed record ScoredItem(CorpusItem Item, double Probability, List<SignalResult> Signals);

public sealed record EvalMetrics(int Count, int Positives, double Auc, double Accuracy, double Fpr, double Tpr, double Brier, double Threshold);

/// <summary>Оценка качества на размеченном корпусе: каталоги human*/ (метка 0) и ai*/ (метка 1) с MR в JSON.</summary>
public static class CorpusEvaluator
{
    public static async Task<List<CorpusItem>> LoadCorpusAsync(string root, CancellationToken cancellationToken = default)
    {
        var items = new List<CorpusItem>();
        foreach (var dir in Directory.GetDirectories(root).OrderBy(d => d, StringComparer.Ordinal))
        {
            var category = Path.GetFileName(dir);
            int? label = category.StartsWith("human", StringComparison.OrdinalIgnoreCase) ? 0
                : category.StartsWith("ai", StringComparison.OrdinalIgnoreCase) ? 1 : null;
            if (label is null)
            {
                continue;
            }

            foreach (var file in Directory.GetFiles(dir, "*.json").OrderBy(f => f, StringComparer.Ordinal))
            {
                var data = await new JsonMergeRequestStore(file).LoadAsync(cancellationToken);
                items.Add(new CorpusItem(Path.GetFileNameWithoutExtension(file), category, label.Value, data));
            }
        }

        return items;
    }

    public static List<ScoredItem> Score(IEnumerable<CorpusItem> items, AnalyzerOptions options, Languages.LanguageRegistry registry)
    {
        var analyzer = new MergeRequestAnalyzer(registry);
        return items.Select(i =>
        {
            var report = analyzer.Analyze(i.Data, options);
            return new ScoredItem(i, report.Probability, report.Signals);
        }).ToList();
    }

    public static EvalMetrics Metrics(IReadOnlyList<(int Label, double P)> scored, double threshold = 0.5)
    {
        int n = scored.Count;
        int pos = scored.Count(s => s.Label == 1);
        int neg = n - pos;
        double auc = Auc(scored);
        int tp = scored.Count(s => s.Label == 1 && s.P >= threshold);
        int fp = scored.Count(s => s.Label == 0 && s.P >= threshold);
        int tn = neg - fp;
        double acc = n == 0 ? 0 : (double)(tp + tn) / n;
        double fpr = neg == 0 ? 0 : (double)fp / neg;
        double tpr = pos == 0 ? 0 : (double)tp / pos;
        double brier = n == 0 ? 0 : scored.Average(s => (s.P - s.Label) * (s.P - s.Label));
        return new EvalMetrics(n, pos, auc, acc, fpr, tpr, brier, threshold);
    }

    /// <summary>ROC AUC через статистику Манна–Уитни.</summary>
    public static double Auc(IReadOnlyList<(int Label, double P)> scored)
    {
        var pos = scored.Where(s => s.Label == 1).Select(s => s.P).ToList();
        var neg = scored.Where(s => s.Label == 0).Select(s => s.P).ToList();
        if (pos.Count == 0 || neg.Count == 0)
        {
            return double.NaN;
        }

        double sum = 0;
        foreach (var p in pos)
        {
            foreach (var q in neg)
            {
                sum += p > q ? 1 : p == q ? 0.5 : 0;
            }
        }

        return sum / (pos.Count * neg.Count);
    }

    /// <summary>Средние значения свидетельства каждого признака по классам — какие признаки реально различают.</summary>
    public static List<(string Id, double MeanHuman, double MeanAi, int NHuman, int NAi)> FeatureSeparation(List<ScoredItem> scored)
    {
        var ids = scored.SelectMany(s => s.Signals.Select(x => x.Id)).Distinct().OrderBy(x => x, StringComparer.Ordinal);
        var result = new List<(string, double, double, int, int)>();
        foreach (var id in ids)
        {
            var h = scored.Where(s => s.Item.Label == 0).SelectMany(s => s.Signals.Where(x => x.Id == id)).Select(x => x.Evidence * x.Reliability).ToList();
            var a = scored.Where(s => s.Item.Label == 1).SelectMany(s => s.Signals.Where(x => x.Id == id)).Select(x => x.Evidence * x.Reliability).ToList();
            result.Add((id, h.Count > 0 ? h.Average() : double.NaN, a.Count > 0 ? a.Average() : double.NaN, h.Count, a.Count));
        }

        return result;
    }
}
