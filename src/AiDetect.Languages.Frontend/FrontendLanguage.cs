using AiDetect.Core.Analysis;
using AiDetect.Core.Features;
using AiDetect.Core.Languages;
using AiDetect.Core.Model;
using AiDetect.Core.Preprocessing;
using AiDetect.Core.Signals;

namespace AiDetect.Languages.Frontend;

/// <summary>
/// Детектор фронтенда: JavaScript/TypeScript (React, Angular, Vue), шаблоны Angular, Vue SFC, CSS/SCSS/LESS.
/// Разбор — собственный лексер и мини-парсер на C#, без Node.js и внешних сервисов.
/// </summary>
public sealed class FrontendLanguage : ILanguageAnalyzer
{
    public const string LanguageId = "frontend";

    /// <summary>
    /// Слои для признака «много слоёв». Шаблон, стили и модуль Angular — части одного компонента (ng generate создаёт их вместе),
    /// поэтому отдельными слоями не считаются.
    /// </summary>
    private static readonly Layer[] Meaningful =
    [
        Layer.Component, Layer.Hook, Layer.State, Layer.Service, Layer.ApiClient, Layer.Routing, Layer.Types, Layer.Utils,
        Layer.Constants, Layer.Stories, Layer.Tests, Layer.Documentation, Layer.Validation, Layer.Middleware, Layer.I18n,
    ];

    public string Id => LanguageId;

    public string Title => "JavaScript / TypeScript (фронтенд)";

    public string CodeNoun => "JS/TS";

    public IReadOnlyList<string> Aliases { get; } =
        ["js", "ts", "javascript", "typescript", "front", "fe", "web", "react", "angular", "vue", "jsts", "js/ts"];

    public bool IsSourceFile(string path) => FrontendFileClassifier.IsSource(path);

    public bool IsGenerated(string path, string? content) => FrontendFileClassifier.IsGenerated(path, content);

    public bool IsTestFile(string path, string? content) => FrontendFileClassifier.IsTestFile(path, content);

    public Layer ClassifyLayer(string path, string? content) => FrontendFileClassifier.ClassifyLayer(path, content);

    public IReadOnlyCollection<Layer> MeaningfulLayers => Meaningful;

    public bool IsBoilerplateLayer(Layer layer) =>
        layer is Layer.Types or Layer.Constants or Layer.I18n or Layer.Routing or Layer.Module or Layer.Stories or Layer.State or Layer.Configuration;

    public CodeStats Extract(string path, string content, Func<int, bool> inScope, bool isTestFile, bool isNewFile) =>
        FrontendFeatureExtractor.Extract(path, content, inScope, isTestFile, isNewFile);

    public void EvaluateCodeSignals(AnalysisContext ctx, SignalBuilder b) => new FrontendCodeSignals(ctx, b).Evaluate();

    public void EvaluateShapeSignals(AnalysisContext ctx, SignalBuilder b) => new FrontendShapeSignals(ctx, b).Evaluate();

    public ProjectConventions DetectConventions(RepositoryContext repository) => FrontendConventions.Detect(repository);

    public StackInfo DetectStack(MergeRequestData mr) => FrontendStack.Detect(mr);

    public bool IsRootConfigFile(string fileName) => FrontendConventions.IsConfigFile(fileName);

    public bool IsProjectFile(string fileName) => FrontendConventions.IsConfigFile(fileName);

    public string? PackageMarker => "package.json";

    public string? GroupTitle(SignalGroup group) => group switch
    {
        SignalGroup.Documentation => "JSDoc/TSDoc",
        SignalGroup.ModernSyntax => "Современный синтаксис JS/TS",
        SignalGroup.Defensive => "Обработка ошибок и проверки",
        SignalGroup.Async => "Отмена, отписка и асинхронность",
        SignalGroup.Logging => "Логирование (console)",
        SignalGroup.Structure => "Структура функций и компонентов",
        _ => null,
    };
}
