namespace AiDetect.Core.Model;

/// <summary>
/// Всё, что детектор знает о мердж-реквесте: метаданные, коммиты, изменённые файлы
/// (с содержимым до и после) и контекст репозитория для базовой линии.
/// Объект одинаково заполняется из GitLab, из локального git-репозитория или из сохранённого JSON.
/// </summary>
public sealed class MergeRequestData
{
    public string Source { get; set; } = "";
    public string Title { get; set; } = "";
    public string Description { get; set; } = "";

    /// <summary>true, если есть настоящие метаданные MR (описание, автор); false — для диапазона локального git.</summary>
    public bool HasMrMetadata { get; set; } = true;
    public string AuthorName { get; set; } = "";
    public string AuthorUsername { get; set; } = "";
    public string SourceBranch { get; set; } = "";
    public string TargetBranch { get; set; } = "";
    public DateTimeOffset? CreatedAt { get; set; }
    public DateTimeOffset? MergedAt { get; set; }
    public string? BaseSha { get; set; }
    public string? HeadSha { get; set; }
    public List<CommitInfo> Commits { get; set; } = [];
    public List<FileChange> Changes { get; set; } = [];
    public RepositoryContext Repository { get; set; } = new();
}

public sealed class CommitInfo
{
    public string Sha { get; set; } = "";
    public string Title { get; set; } = "";
    public string Message { get; set; } = "";
    public string AuthorName { get; set; } = "";
    public string AuthorEmail { get; set; } = "";
    public DateTimeOffset? AuthoredDate { get; set; }
    public DateTimeOffset? CommittedDate { get; set; }

    /// <summary>Добавленные строки в коммите (если источник умеет их считать).</summary>
    public int? Additions { get; set; }

    public int? Deletions { get; set; }

    /// <summary>Пути файлов, изменённых коммитом (если известны).</summary>
    public List<string> ChangedPaths { get; set; } = [];
}

public sealed class FileChange
{
    public string OldPath { get; set; } = "";
    public string NewPath { get; set; } = "";
    public bool IsNew { get; set; }
    public bool IsDeleted { get; set; }
    public bool IsRenamed { get; set; }

    /// <summary>Унифицированный дифф (как его отдаёт GitLab: начиная с заголовка ханка @@).</summary>
    public string Diff { get; set; } = "";

    /// <summary>Содержимое файла до изменения (null для новых файлов или если не удалось загрузить).</summary>
    public string? OldContent { get; set; }

    /// <summary>Содержимое файла после изменения (null для удалённых файлов или если не удалось загрузить).</summary>
    public string? NewContent { get; set; }

    public string Path => IsDeleted ? OldPath : NewPath;
}

public sealed class RepositoryContext
{
    /// <summary>Служебные пути, найденные в репозитории на момент head (CLAUDE.md, .claude/settings.json и т.п.).</summary>
    public List<string> AgentFilesAtHead { get; set; } = [];

    /// <summary>Содержимое .gitignore на момент head (если есть).</summary>
    public string? GitIgnore { get; set; }

    /// <summary>
    /// Существующий код проекта до МР (соседние файлы в тех же каталогах), для базовой линии стиля проекта.
    /// </summary>
    public List<SourceFile> BaselineFiles { get; set; } = [];

    /// <summary>Файлы конфигурации стиля (.editorconfig, Directory.Build.props, *.csproj) на момент base.</summary>
    public List<SourceFile> ConfigFiles { get; set; } = [];
}

public sealed class SourceFile
{
    public string Path { get; set; } = "";
    public string Content { get; set; } = "";
}
