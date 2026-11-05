using AiDetect.Core.Features;
using AiDetect.Core.Languages;
using AiDetect.Core.Model;
using AiDetect.Core.Preprocessing;

namespace AiDetect.Core.Analysis;

public sealed class AnalyzerOptions
{
    /// <summary>Не учитывать прямые артефакты (трейлеры, ссылки на сессию, CLAUDE.md) — проверка «только по коду».</summary>
    public bool IgnoreArtifacts { get; set; }

    /// <summary>Множитель веса для шаблонного кода (DTO, options, DI-регистрации, стили).</summary>
    public double BoilerplateWeight { get; set; } = 0.5;

    /// <summary>Параметры модели (веса, смещение, температура). null — веса языка по умолчанию (weights.{язык}.json).</summary>
    public ModelParameters? Parameters { get; set; }

    /// <summary>Язык анализа: id или псевдоним («csharp», «frontend», «js»…) либо «auto» — по файлам MR.</summary>
    public string Language { get; set; } = LanguageRegistry.Auto;
}

/// <summary>Результат анализа одного изменённого файла.</summary>
public sealed class FileAnalysis
{
    public required string Path { get; init; }
    public Layer Layer { get; init; }

    /// <summary>Файл — исходный код выбранного языка.</summary>
    public bool IsSource { get; init; }
    public bool IsTest { get; init; }
    public bool IsNew { get; init; }
    public bool IsDeleted { get; init; }
    public bool Excluded { get; init; }
    public string? ExcludeReason { get; init; }
    public int AddedLines { get; init; }
    public int RemovedLines { get; init; }
    public double Weight { get; init; } = 1.0;
    public CodeStats? Stats { get; init; }
}

/// <summary>Всё, что нужно сигналам: MR, язык, статистика добавленного кода, базовая линия проекта.</summary>
public sealed class AnalysisContext
{
    public required MergeRequestData Mr { get; init; }

    public required ILanguageAnalyzer Language { get; init; }

    public required IReadOnlyList<FileAnalysis> Files { get; init; }

    /// <summary>Признаки добавленного кода (все анализируемые файлы языка, с учётом весов).</summary>
    public required CodeStats Added { get; init; }

    /// <summary>Признаки существующего кода проекта (null, если базовой линии нет).</summary>
    public CodeStats? Baseline { get; init; }

    public int BaselineFiles { get; init; }

    public required AnalyzerOptions Options { get; init; }

    public ProjectConventions Conventions { get; init; } = ProjectConventions.Detect(new());

    /// <summary>Анализируемые файлы выбранного языка (без исключённых и удалённых).</summary>
    public IEnumerable<FileAnalysis> AnalyzedFiles => Files.Where(f => f.IsSource && !f.Excluded && !f.IsDeleted);

    public int AddedProdLines => AnalyzedFiles.Where(f => !f.IsTest).Sum(f => f.AddedLines);

    public int AddedTestLines => AnalyzedFiles.Where(f => f.IsTest).Sum(f => f.AddedLines);

    /// <summary>Добавленные строки кода выбранного языка в коммите (для признаков скорости и реакции на ревью).</summary>
    public int? CommitAdditions(CommitInfo commit) => commit.FileStats is { } stats
        ? stats.Where(s => Language.IsSourceFile(s.Path) && !Language.IsGenerated(s.Path, null)).Sum(s => s.Added)
        : commit.Additions;

    public int? CommitDeletions(CommitInfo commit) => commit.FileStats is { } stats
        ? stats.Where(s => Language.IsSourceFile(s.Path) && !Language.IsGenerated(s.Path, null)).Sum(s => s.Removed)
        : commit.Deletions;
}
