using AiDetect.Core.Analysis;
using AiDetect.Core.Features;
using AiDetect.Core.Model;
using AiDetect.Core.Preprocessing;
using AiDetect.Core.Signals;

namespace AiDetect.Core.Languages;

/// <summary>
/// Детектор одного языка (стека): какие файлы он анализирует, как извлекает признаки кода, какие сигналы по коду считает,
/// какие конвенции проекта учитывает и как определяет стек. Всё остальное (источники MR, сигналы MR и процесса, скоринг,
/// калибровка, отчёты, веб) — общее ядро. Новый язык = новая реализация этого интерфейса + строка регистрации в реестре.
/// </summary>
public interface ILanguageAnalyzer
{
    /// <summary>Идентификатор: «csharp», «frontend». Используется в --lang, в имени файла весов (weights.{Id}.json) и в корпусе.</summary>
    string Id { get; }

    /// <summary>Название для выбора языка на странице: «C# (.NET)».</summary>
    string Title { get; }

    /// <summary>Короткое имя языка для текстов: «C#» → «C#-кода», «C#-файлов».</summary>
    string CodeNoun { get; }

    /// <summary>Другие имена для --lang: «cs», «c#», «js», «ts»…</summary>
    IReadOnlyList<string> Aliases { get; }

    /// <summary>Исходный код языка, который анализируется (признаки кода, строки кода, базовая линия).</summary>
    bool IsSourceFile(string path);

    /// <summary>Сгенерированный/служебный код, который исключается из анализа.</summary>
    bool IsGenerated(string path, string? content);

    bool IsTestFile(string path, string? content);

    /// <summary>Архитектурный слой любого изменённого файла (в том числе не кода: .md, конфигурация).</summary>
    Layer ClassifyLayer(string path, string? content);

    /// <summary>Слои, которые считаются в признаке «связное изменение многих слоёв».</summary>
    IReadOnlyCollection<Layer> MeaningfulLayers { get; }

    /// <summary>Шаблонный код (DTO, options, DI, стили…), чей вклад в признаки уменьшается.</summary>
    bool IsBoilerplateLayer(Layer layer);

    /// <summary>Признаки кода файла. <paramref name="inScope"/> — какие строки (1-based) считать (добавленные в MR).</summary>
    CodeStats Extract(string path, string content, Func<int, bool> inScope, bool isTestFile, bool isNewFile);

    /// <summary>Сигналы по коду (стилометрия, идиомы) — добавляются в <paramref name="b"/>.</summary>
    void EvaluateCodeSignals(AnalysisContext ctx, SignalBuilder b);

    /// <summary>Сигналы формы MR, зависящие от языка (например, «интерфейс + реализация + DI», единообразие стиля по файлам).</summary>
    void EvaluateShapeSignals(AnalysisContext ctx, SignalBuilder b);

    /// <summary>Конвенции проекта (линтеры, форматтеры, обязательная документация), ослабляющие соответствующие признаки.</summary>
    ProjectConventions DetectConventions(RepositoryContext repository);

    /// <summary>Стек проекта для отчёта: язык, фреймворки, библиотеки, тесты.</summary>
    StackInfo DetectStack(MergeRequestData mr);

    /// <summary>Конфигурационный файл в корне репозитория, который стоит загрузить (имя файла без каталога).</summary>
    bool IsRootConfigFile(string fileName);

    /// <summary>Файл проекта/пакета в каталогах изменённых файлов (*.csproj, package.json, tsconfig.json…).</summary>
    bool IsProjectFile(string fileName);

    /// <summary>Файл-маркер корня пакета (package.json) — ищется вверх по дереву от изменённых файлов; null — не нужен.</summary>
    string? PackageMarker { get; }

    /// <summary>Название группы сигналов для этого языка (null — общее название).</summary>
    string? GroupTitle(SignalGroup group);
}
