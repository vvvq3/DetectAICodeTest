using AiDetect.Core.Signals;

namespace AiDetect.Core.Analysis;

/// <summary>Объединение сигналов: сумма вкладов по группе с ограничением, затем сигмоида с температурой.</summary>
public static class Scorer
{
    public static (List<GroupScore> Groups, double Logit) Combine(IEnumerable<SignalResult> signals, ModelParameters parameters, bool includeArtifacts = true)
    {
        var groups = signals
            .Where(s => includeArtifacts || s.Group != SignalGroup.Artifacts)
            .GroupBy(s => s.Group)
            .Select(g =>
            {
                double raw = g.Sum(s => s.Contribution);
                double cap = GroupInfo.Cap(g.Key);
                return new GroupScore
                {
                    Group = g.Key,
                    RawSum = raw,
                    Capped = Math.Clamp(raw, -cap, cap),
                    Signals = g.OrderByDescending(s => Math.Abs(s.Contribution)).ToList(),
                };
            })
            .OrderByDescending(g => Math.Abs(g.Capped))
            .ToList();

        double logit = parameters.Bias + groups.Sum(g => g.Capped);
        return (groups, logit);
    }

    public static double Probability(double logit, ModelParameters parameters)
    {
        double t = parameters.Temperature <= 0 ? 1 : parameters.Temperature;
        return 1.0 / (1.0 + Math.Exp(-logit / t));
    }

    public static string Verdict(double p) => p switch
    {
        >= 0.85 => "Почти наверняка написан с помощью Claude Code",
        >= 0.65 => "Скорее написан с помощью Claude Code",
        > 0.35 => "Неопределённо: признаки смешанные",
        > 0.15 => "Скорее написан человеком",
        _ => "Почти наверняка написан человеком",
    };
}
