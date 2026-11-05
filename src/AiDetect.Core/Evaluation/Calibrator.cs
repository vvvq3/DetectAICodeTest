using AiDetect.Core.Analysis;
using AiDetect.Core.Signals;

namespace AiDetect.Core.Evaluation;

/// <summary>
/// Подбор весов признаков на размеченном корпусе: логистическая регрессия по x_i = свидетельство × надёжность
/// с L2-штрафом к экспертным весам (на маленьком корпусе веса не уходят далеко от исследования) и
/// неотрицательными весами (направление признака задано экспертно). Прямые артефакты не калибруются.
/// </summary>
public static class Calibrator
{
    public sealed record Sample(Dictionary<string, double> X, double DefaultLogitPart, int Label);

    public static List<Sample> BuildSamples(List<ScoredItem> scored) =>
        scored.Select(s => new Sample(
            s.Signals.Where(x => x.Group != SignalGroup.Artifacts).GroupBy(x => x.Id)
                .ToDictionary(g => g.Key, g => g.Sum(x => x.Evidence * x.Reliability * x.ConventionFactor)),
            0,
            s.Item.Label)).ToList();

    public static Dictionary<string, double> DefaultWeights(List<ScoredItem> scored) =>
        scored.SelectMany(s => s.Signals).Where(x => x.Group != SignalGroup.Artifacts)
            .GroupBy(x => x.Id).ToDictionary(g => g.Key, g => g.First().BaseWeight);

    public static ModelParameters Fit(List<Sample> samples, Dictionary<string, double> prior, ModelParameters start, double lambda = 2.0, int iterations = 4000, double lr = 0.05,
        bool balanced = true)
    {
        var ids = prior.Keys.OrderBy(k => k, StringComparer.Ordinal).ToList();
        var w = ids.ToDictionary(id => id, id => prior[id]);
        double bias = start.Bias;
        double t = start.Temperature <= 0 ? 1 : start.Temperature;
        int n = samples.Count;
        if (n == 0)
        {
            return start;
        }

        // Балансировка классов: итог не зависит от доли ИИ-примеров в корпусе (априорно 50/50).
        int pos = samples.Count(x => x.Label == 1);
        int neg = n - pos;
        double wPos = balanced && pos > 0 ? n / (2.0 * pos) : 1;
        double wNeg = balanced && neg > 0 ? n / (2.0 * neg) : 1;

        for (int it = 0; it < iterations; it++)
        {
            var grad = ids.ToDictionary(id => id, _ => 0.0);
            double gb = 0;
            foreach (var s in samples)
            {
                double z = bias;
                foreach (var (id, x) in s.X)
                {
                    if (w.TryGetValue(id, out var wi))
                    {
                        z += wi * x;
                    }
                }

                double p = 1 / (1 + Math.Exp(-z / t));
                double err = (p - s.Label) / t * (s.Label == 1 ? wPos : wNeg);
                gb += err;
                foreach (var (id, x) in s.X)
                {
                    if (grad.ContainsKey(id))
                    {
                        grad[id] += err * x;
                    }
                }
            }

            foreach (var id in ids)
            {
                double g = (grad[id] / n) + (lambda * (w[id] - prior[id]) / n);
                w[id] = Math.Max(0, w[id] - (lr * g));
            }

            bias -= lr * ((gb / n) + (lambda * bias / n));
        }

        var result = new ModelParameters
        {
            Bias = Math.Round(bias, 4),
            Temperature = t,
            Description = $"Калибровка на {n} MR, lambda={lambda}",
        };
        foreach (var (k, v) in start.Weights)
        {
            result.Weights[k] = v;
        }

        foreach (var id in ids)
        {
            result.Weights[id] = Math.Round(w[id], 4);
        }

        return result;
    }

    /// <summary>
    /// Консервативный порог: сдвигает смещение так, чтобы на обучающих человеческих MR доля оценок ≥ 50% не превышала targetFpr.
    /// Смещение только уменьшается (никогда не делает модель смелее).
    /// </summary>
    public static ModelParameters ApplyTargetFpr(ModelParameters p, List<Sample> samples, Dictionary<string, double> prior, double targetFpr)
    {
        var humans = samples.Where(s => s.Label == 0)
            .Select(s => s.X.Sum(kv => p.WeightFor(kv.Key, prior.GetValueOrDefault(kv.Key)) * kv.Value))
            .OrderByDescending(z => z)
            .ToList();
        if (humans.Count == 0 || targetFpr <= 0)
        {
            return p;
        }

        // Логит без смещения у человека на границе квантиля; смещение = −этот логит (p = 0.5 ровно на границе), с небольшим запасом.
        int idx = Math.Min(humans.Count - 1, (int)Math.Floor(targetFpr * humans.Count));
        double needed = -humans[idx] - 0.05;
        if (needed < p.Bias)
        {
            p.Bias = Math.Round(needed, 4);
            p.Description += $"; смещение сдвинуто для доли ложных «ИИ» ≤ {targetFpr:P0}";
        }

        return p;
    }

    /// <summary>k-кратная перекрёстная проверка калибровки: честная оценка AUC на отложенных MR.</summary>
    public static List<(int Label, double P)> CrossValidate(List<Sample> samples, Dictionary<string, double> prior, ModelParameters start, int folds = 5, double lambda = 2.0,
        bool balanced = true, double targetFpr = 0)
    {
        var result = new List<(int, double)>();
        var indexed = samples.Select((s, i) => (s, i)).ToList();
        for (int f = 0; f < folds; f++)
        {
            var train = indexed.Where(x => x.i % folds != f).Select(x => x.s).ToList();
            var test = indexed.Where(x => x.i % folds == f).Select(x => x.s).ToList();
            var p = Fit(train, prior, start, lambda, balanced: balanced);
            if (targetFpr > 0)
            {
                p = ApplyTargetFpr(p, train, prior, targetFpr);
            }

            foreach (var s in test)
            {
                double z = p.Bias + s.X.Sum(kv => p.WeightFor(kv.Key, prior.GetValueOrDefault(kv.Key)) * kv.Value);
                result.Add((s.Label, 1 / (1 + Math.Exp(-z / p.Temperature))));
            }
        }

        return result;
    }
}
