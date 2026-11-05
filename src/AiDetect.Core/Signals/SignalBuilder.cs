using System.Globalization;
using AiDetect.Core.Analysis;
using AiDetect.Core.Features;

namespace AiDetect.Core.Signals;

/// <summary>Сбор результатов признаков с учётом весов модели, базовой линии проекта и его конвенций.</summary>
public sealed class SignalBuilder(AnalysisContext ctx, ModelParameters parameters)
{
    public List<SignalResult> Results { get; } = [];

    public AnalysisContext Context => ctx;

    public List<ShiftItem> ShiftItems { get; } = [];

    public sealed record ShiftItem(string Id, string Title, double MrValue, double BaseValue, double Delta, Func<double, string> Format);

    /// <summary>Кусочно-линейное отображение: значение humanAt → −1, aiAt → +1 (работает и для убывающих признаков).</summary>
    public static double Map(double x, double humanAt, double aiAt)
    {
        if (Math.Abs(aiAt - humanAt) < 1e-9)
        {
            return 0;
        }

        return Math.Clamp((2 * (x - humanAt) / (aiAt - humanAt)) - 1, -1, 1);
    }

    /// <summary>Надёжность по числу наблюдений: n/(n+k).</summary>
    public static double Rel(double n, double k) => n <= 0 ? 0 : n / (n + k);

    /// <summary>«1 раз», «3 раза», «5 раз».</summary>
    public static string Times(double count)
    {
        long n = (long)Math.Round(count);
        long mod100 = n % 100;
        long mod10 = n % 10;
        string word = mod100 is >= 11 and <= 14 ? "раз" : mod10 is >= 2 and <= 4 ? "раза" : "раз";
        return $"{n} {word}";
    }

    public static string Pct(double v) => (v * 100).ToString("0", CultureInfo.InvariantCulture) + "%";

    /// <summary>
    /// Поправка на норму проекта: если код проекта уже обладает тем же свойством (например, XML-doc обязателен по StyleCop),
    /// свидетельство ослабляется на alpha × свидетельство базовой линии.
    /// </summary>
    public static double AdjustForBaseline(double e, double? eBase, double alpha)
    {
        if (eBase is not { } eb || alpha <= 0 || e == 0 || Math.Sign(e) != Math.Sign(eb))
        {
            return e;
        }

        return Math.Sign(e) * Math.Max(0, Math.Abs(e) - (alpha * Math.Abs(eb)));
    }

    /// <summary>Ослабление признаков, которые в проекте обязательны по конвенциям (линтеры, форматтеры, commitlint).</summary>
    private double ConventionFactor(string id) => ctx.Conventions.Factor(id);

    public void TrackShift(string id, string title, double mrValue, double? baseValue, double e, double? eBase, Func<double, string> format)
    {
        if (baseValue is { } bv && eBase is { } eb)
        {
            ShiftItems.Add(new ShiftItem(id, title, mrValue, bv, (e - eb) / 2, format));
        }
    }

    public void Add(string id, SignalGroup group, string title, double evidence, double reliability, double defaultWeight, string detail,
        IReadOnlyList<CodeExample> examples, double? raw = null)
    {
        if (reliability <= 0)
        {
            return;
        }

        double baseWeight = parameters.WeightFor(id, defaultWeight);
        double conventionFactor = ConventionFactor(id);
        Results.Add(new SignalResult
        {
            BaseWeight = baseWeight,
            ConventionFactor = conventionFactor,
            Id = id,
            Group = group,
            Title = title,
            Evidence = Math.Clamp(evidence, -1, 1),
            Reliability = Math.Clamp(reliability, 0, 1),
            Weight = baseWeight * conventionFactor,
            Detail = detail,
            Examples = examples,
            RawValue = raw,
        });
    }

    /// <summary>
    /// Признак-доля: hits / total (или hits / сумма totalKeys). При наличии базовой линии с тем же признаком
    /// свидетельство ослабляется (alpha) и признак попадает в расчёт «сдвига относительно проекта».
    /// </summary>
    public void Rate(string id, SignalGroup group, string ai, string human, string hits, string? total, double humanAt, double aiAt, double k,
        double weight, double alpha, Func<double, double, string> detail, string[]? totalKeys = null, string? exAi = null, string? exHuman = null,
        double minTotal = 1)
    {
        var a = ctx.Added;
        double t = Total(a, total, totalKeys);
        if (t < minTotal)
        {
            return;
        }

        double h = a[hits];
        double rate = h / t;
        double e = Map(rate, humanAt, aiAt);
        double eRaw = e;
        string baseNote = "";
        var bs = ctx.Baseline;
        if (bs is not null)
        {
            double bt = Total(bs, total, totalKeys);
            if (bt >= 3)
            {
                double bRate = bs[hits] / bt;
                double eb = Map(bRate, humanAt, aiAt);
                e = AdjustForBaseline(e, eb, alpha);
                baseNote = $" (в коде проекта: {Pct(bRate)})";
                if (alpha > 0)
                {
                    TrackShift(id, ShortTitle(ai), rate, bRate, eRaw, eb, Pct);
                }
            }
        }

        bool aiSide = eRaw >= 0;
        var exKey = aiSide ? exAi : exHuman;
        Add(id, group, aiSide ? ai : human, e, Rel(t, k), weight, detail(h, t) + baseNote,
            exKey is null ? [] : a.GetExamples(exKey), rate);
    }

    private static double Total(CodeStats s, string? total, string[]? totalKeys)
    {
        if (totalKeys is not null)
        {
            return totalKeys.Sum(key => s[key]);
        }

        return total is null ? 0 : s[total];
    }

    private static string ShortTitle(string title)
    {
        var idx = title.IndexOfAny([':', '(']);
        var t = idx > 0 ? title[..idx] : title;
        return t.Length > 50 ? t[..47] + "..." : t.Trim();
    }
}
