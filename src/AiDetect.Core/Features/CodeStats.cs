namespace AiDetect.Core.Features;

public sealed record CodeExample(string File, int Line, string Snippet);

/// <summary>
/// Счётчики признаков по набору кода (добавленный код МР или код базовой линии проекта).
/// Ключи — константы из <see cref="K"/>. Для каждого счётчика хранится до нескольких примеров для отчёта.
/// </summary>
public sealed class CodeStats
{
    private const int MaxExamples = 4;

    public Dictionary<string, double> Counts { get; } = new(StringComparer.Ordinal);

    public Dictionary<string, List<CodeExample>> Examples { get; } = new(StringComparer.Ordinal);

    public List<int> MethodLengths { get; } = [];

    public List<int> MethodComplexities { get; } = [];

    public List<string> LocalNames { get; } = [];

    public List<string> ParameterNames { get; } = [];

    public List<string> LambdaParameterNames { get; } = [];

    public double this[string key] => Counts.TryGetValue(key, out var v) ? v : 0;

    public void Add(string key, double value = 1)
    {
        Counts[key] = this[key] + value;
    }

    public void Hit(string key, string file, int line, string snippet)
    {
        Add(key);
        AddExample(key, file, line, snippet);
    }

    public void AddExample(string key, string file, int line, string snippet)
    {
        if (!Examples.TryGetValue(key, out var list))
        {
            list = [];
            Examples[key] = list;
        }

        if (list.Count < MaxExamples)
        {
            list.Add(new CodeExample(file, line, Trim(snippet)));
        }
    }

    public IReadOnlyList<CodeExample> GetExamples(string key) =>
        Examples.TryGetValue(key, out var list) ? list : [];

    public void Merge(CodeStats other, double factor = 1.0)
    {
        foreach (var (k, v) in other.Counts)
        {
            Add(k, v * factor);
        }

        foreach (var (k, list) in other.Examples)
        {
            foreach (var e in list)
            {
                AddExample(k, e.File, e.Line, e.Snippet);
            }
        }

        MethodLengths.AddRange(other.MethodLengths);
        MethodComplexities.AddRange(other.MethodComplexities);
        LocalNames.AddRange(other.LocalNames);
        ParameterNames.AddRange(other.ParameterNames);
        LambdaParameterNames.AddRange(other.LambdaParameterNames);
    }

    /// <summary>Доля hits/total; null, если знаменатель нулевой.</summary>
    public double? Rate(string hits, string total)
    {
        var t = this[total];
        return t <= 0 ? null : this[hits] / t;
    }

    /// <summary>Количество на 100 строк кода.</summary>
    public double Per100Lines(string key)
    {
        var lines = this[StatKeys.CodeLines];
        return lines <= 0 ? 0 : this[key] * 100.0 / lines;
    }

    private static string Trim(string s)
    {
        s = s.Trim();
        return s.Length > 120 ? s[..117] + "..." : s;
    }
}

/// <summary>Имена общих для всех языков счётчиков объёма (языки добавляют свои ключи).</summary>
public static class StatKeys
{
    public const string Files = "files";
    public const string NewFiles = "files.new";
    public const string CodeLines = "lines.code";
    public const string CommentLines = "lines.comment";
    public const string TotalLines = "lines.total";
}
