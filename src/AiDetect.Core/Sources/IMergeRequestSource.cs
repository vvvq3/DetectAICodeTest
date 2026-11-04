using AiDetect.Core.Model;

namespace AiDetect.Core.Sources;

public interface IMergeRequestSource
{
    Task<MergeRequestData> LoadAsync(CancellationToken cancellationToken = default);
}

/// <summary>Ошибка получения данных MR с понятным пользователю сообщением.</summary>
public sealed class SourceException(string message, Exception? inner = null) : Exception(message, inner);

/// <summary>Ограничения на объём загрузки, чтобы анализ огромных MR не длился вечно.</summary>
public sealed class SourceLimits
{
    public int MaxFiles { get; set; } = 300;
    public int MaxFileBytes { get; set; } = 400_000;
    public int MaxCommitsWithStats { get; set; } = 60;
    public int MaxBaselineFiles { get; set; } = 14;
    public int MaxBaselineFileBytes { get; set; } = 80_000;
}

public static class BaselineSelection
{
    /// <summary>Каталоги, из которых берём соседние файлы для базовой линии (каталоги изменённых .cs-файлов и их родители).</summary>
    public static List<string> CandidateDirectories(MergeRequestData mr)
    {
        var dirs = new List<string>();
        foreach (var c in mr.Changes.Where(c => c.Path.EndsWith(".cs", StringComparison.OrdinalIgnoreCase)))
        {
            var dir = Path.GetDirectoryName(c.Path)?.Replace('\\', '/') ?? "";
            for (int depth = 0; depth < 2; depth++)
            {
                if (!dirs.Contains(dir))
                {
                    dirs.Add(dir);
                }

                if (dir.Length == 0)
                {
                    break;
                }

                dir = Path.GetDirectoryName(dir)?.Replace('\\', '/') ?? "";
            }
        }

        // Сначала самые глубокие (ближайшие к изменённым файлам).
        return dirs.OrderByDescending(d => d.Count(ch => ch == '/')).ThenByDescending(d => d.Length).ToList();
    }

    public static bool IsBaselineCandidate(string path) =>
        path.EndsWith(".cs", StringComparison.OrdinalIgnoreCase) &&
        !Preprocessing.FileClassifier.IsGenerated(path, null);
}
