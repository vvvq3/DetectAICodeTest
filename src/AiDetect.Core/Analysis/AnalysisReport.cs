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

    public IEnumerable<SignalResult> TopAiSignals(int n) =>
        Signals.Where(s => s.Contribution > 0.05).OrderByDescending(s => s.Contribution).Take(n);

    public IEnumerable<SignalResult> TopHumanSignals(int n) =>
        Signals.Where(s => s.Contribution < -0.05).OrderBy(s => s.Contribution).Take(n);
}
