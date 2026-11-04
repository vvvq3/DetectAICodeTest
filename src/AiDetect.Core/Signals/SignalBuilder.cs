using System.Globalization;
using AiDetect.Core.Analysis;
using AiDetect.Core.Features;

namespace AiDetect.Core.Signals;

/// <summary>Сбор результатов признаков с учётом весов модели и базовой линии проекта.</summary>
internal sealed class SignalBuilder(AnalysisContext ctx, ModelParameters parameters)
{
    public List<SignalResult> Results { get; } = [];

    public List<ShiftItem> ShiftItems { get; } = [];

    internal sealed record ShiftItem(string Id, string Title, double MrValue, double BaseValue, double Delta, Func<double, string> Format);

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

    /// <summary>Ослабление признаков, которые в проекте обязательны по конвенциям (StyleCop, .editorconfig, commitlint).</summary>
    private double ConventionFactor(string id)
    {
        var c = ctx.Conventions;
        double f = 1.0;
        if (c.XmlDocRequired && id.StartsWith("doc.", StringComparison.Ordinal))
        {
            f *= 0.2;
        }

        if (c.StyleCop && id is "ord.members" or "acc.explicit" or "fmt.blankBeforeReturn" or "cmt.case" or "fld.readonly" or "fmt.irregular")
        {
            f *= id is "fld.readonly" or "fmt.irregular" ? 0.6 : 0.3;
        }

        if (c.EditorConfigStyleRules && id is "syn.nullPattern" or "syn.modern" or "api.stringEmpty" or "syn.fileScopedNs" or "fmt.irregular"
                or "prop.stringEmptyInit" or "acc.explicit" or "cls.sealed")
        {
            f *= 0.6;
        }

        if (c.FileScopedNamespacesEnforced && id == "syn.fileScopedNs")
        {
            f = 0;
        }

        if (c.ConventionalCommits && id == "proc.commitStyle")
        {
            f *= 0.4;
        }

        return f;
    }

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

        Results.Add(new SignalResult
        {
            Id = id,
            Group = group,
            Title = title,
            Evidence = Math.Clamp(evidence, -1, 1),
            Reliability = Math.Clamp(reliability, 0, 1),
            Weight = parameters.WeightFor(id, defaultWeight) * ConventionFactor(id),
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
