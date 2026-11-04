namespace AiDetect.Core.Analysis;

/// <summary>Оценка отдельного файла MR только по признакам кода (без процесса и формы MR).</summary>
public sealed record FileScore(string Path, int AddedLines, double Probability, IReadOnlyList<string> TopSignals);
