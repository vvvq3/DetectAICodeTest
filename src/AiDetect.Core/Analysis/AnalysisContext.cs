using AiDetect.Core.Features;
using AiDetect.Core.Model;
using AiDetect.Core.Preprocessing;

namespace AiDetect.Core.Analysis;

public sealed class AnalyzerOptions
{
    /// <summary>Не учитывать прямые артефакты (трейлеры, ссылки на сессию, CLAUDE.md) — проверка «только по коду».</summary>
    public bool IgnoreArtifacts { get; set; }

    /// <summary>Множитель веса для шаблонного кода (DTO, options, DI-регистрации).</summary>
    public double BoilerplateWeight { get; set; } = 0.5;

    /// <summary>Параметры модели (веса, смещение, температура). null — значения по умолчанию.</summary>
    public ModelParameters? Parameters { get; set; }
}

/// <summary>Результат анализа одного изменённого файла.</summary>
public sealed class FileAnalysis
{
    public required string Path { get; init; }
    public Layer Layer { get; init; }
    public bool IsCSharp { get; init; }
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

/// <summary>Всё, что нужно сигналам: MR, статистика добавленного кода, базовая линия проекта.</summary>
public sealed class AnalysisContext
{
    public required MergeRequestData Mr { get; init; }

    public required IReadOnlyList<FileAnalysis> Files { get; init; }

    /// <summary>Признаки добавленного кода (все анализируемые C#-файлы, с учётом весов).</summary>
    public required CodeStats Added { get; init; }

    /// <summary>Признаки существующего кода проекта (null, если базовой линии нет).</summary>
    public CodeStats? Baseline { get; init; }

    public int BaselineFiles { get; init; }

    public required AnalyzerOptions Options { get; init; }

    public ProjectConventions Conventions { get; init; } = ProjectConventions.Detect(new());

    public IEnumerable<FileAnalysis> AnalyzedCs => Files.Where(f => f.IsCSharp && !f.Excluded && !f.IsDeleted);

    public int AddedProdLines => AnalyzedCs.Where(f => !f.IsTest).Sum(f => f.AddedLines);

    public int AddedTestLines => AnalyzedCs.Where(f => f.IsTest).Sum(f => f.AddedLines);
}
