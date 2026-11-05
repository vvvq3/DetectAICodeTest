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
