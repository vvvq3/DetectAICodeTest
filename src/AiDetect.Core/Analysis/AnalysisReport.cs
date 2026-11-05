using AiDetect.Core.Signals;

namespace AiDetect.Core.Analysis;

/// <summary>Итог анализа MR: вероятность, вердикт, вклад групп и признаков, сведения об объёме данных.</summary>
public sealed class AnalysisReport
{
    public string Source { get; init; } = "";
    public string Title { get; init; } = "";
    public string Author { get; init; } = "";
    public string SourceBranch { get; init; } = "";

    /// <summary>Вероятность того, что MR написан Claude Code (0..1).</summary>
    public double Probability { get; init; }

    public double Logit { get; init; }

    public string Verdict { get; init; } = "";

    public string Confidence { get; init; } = "";

    public string ConfidenceNote { get; init; } = "";

    /// <summary>Вероятность без учёта прямых артефактов (только код и процесс).</summary>
    public double ProbabilityWithoutArtifacts { get; init; }

    public bool ArtifactsFound { get; init; }

    public List<GroupScore> Groups { get; init; } = [];

    public List<SignalResult> Signals { get; init; } = [];

    public int FilesChanged { get; init; }
    public int FilesAnalyzed { get; init; }
    public List<string> ExcludedFiles { get; init; } = [];
    public int AddedCodeLines { get; init; }
    public int AddedTestLines { get; init; }
    public int CodeChars { get; init; }
    public int Commits { get; init; }
    public int BaselineFiles { get; init; }
    public List<string> Warnings { get; init; } = [];

    /// <summary>Краткое объяснение решения обычным языком.</summary>
    public string Summary { get; init; } = "";

    /// <summary>Оценка по отдельным файлам (только признаки кода), самые «ИИ-подобные» первыми.</summary>
    public List<FileScore> FileScores { get; init; } = [];

    /// <summary>Язык анализа: «csharp», «frontend».</summary>
    public string LanguageId { get; init; } = "";

    /// <summary>Название языка: «C# (.NET)».</summary>
    public string LanguageTitle { get; init; } = "";

    /// <summary>Короткое имя языка для текстов: «C#», «JS/TS».</summary>
    public string CodeNoun { get; init; } = "";

    /// <summary>Язык определён автоматически по файлам MR.</summary>
    public bool LanguageAuto { get; init; }

    /// <summary>Стек проекта: «TypeScript 4.1 · React 17 · Jest».</summary>
    public string Stack { get; init; } = "";

    public List<string> StackTags { get; init; } = [];

    /// <summary>Названия групп сигналов для языка анализа.</summary>
    public Dictionary<SignalGroup, string> GroupTitles { get; init; } = [];

    public string GroupTitle(SignalGroup group) => GroupTitles.TryGetValue(group, out var t) ? t : GroupInfo.Title(group);

    public IEnumerable<SignalResult> TopAiSignals(int n) =>
        Signals.Where(s => s.Contribution > 0.05).OrderByDescending(s => s.Contribution).Take(n);

    public IEnumerable<SignalResult> TopHumanSignals(int n) =>
        Signals.Where(s => s.Contribution < -0.05).OrderBy(s => s.Contribution).Take(n);
}
