namespace AiDetect.Core.Preprocessing;

/// <summary>
/// Архитектурный слой, к которому относится файл. Нужен для признака «связный многослойный дифф» и для веса шаблонного кода.
/// Один список на все языки: каждый язык использует свою часть (см. ILanguageAnalyzer.MeaningfulLayers).
/// </summary>
public enum Layer
{
    Other,
    Controller,
    Service,
    Handler,
    Repository,
    Domain,
    Contracts,
    DependencyInjection,
    Validation,
    Options,
    Mapping,
    Middleware,
    Migration,
    Tests,
    Documentation,
    Configuration,
    BuildProject,

    // Фронтенд
    Component,
    Hook,
    State,
    ApiClient,
    Routing,
    Styles,
    Template,
    Types,
    Utils,
    Constants,
    Stories,
    I18n,
    Module,
}

/// <summary>Общие для всех языков правила о файлах.</summary>
public static class CommonFiles
{
    public static bool IsMarkdown(string path) => path.EndsWith(".md", StringComparison.OrdinalIgnoreCase);

    public static string Normalize(string path) => path.Replace('\\', '/').ToLowerInvariant();

    public static string LayerTitle(Layer layer) => layer switch
    {
        Layer.Controller => "контроллеры/эндпоинты",
        Layer.Service => "сервисы",
        Layer.Handler => "обработчики/команды",
        Layer.Repository => "доступ к данным",
        Layer.Domain => "доменные модели",
        Layer.Contracts => "DTO/контракты",
        Layer.DependencyInjection => "регистрация DI",
        Layer.Validation => "валидация",
        Layer.Options => "настройки/options",
        Layer.Mapping => "маппинг",
        Layer.Middleware => "middleware/фильтры",
        Layer.Migration => "миграции",
        Layer.Tests => "тесты",
        Layer.Documentation => "документация",
        Layer.Configuration => "конфигурация",
        Layer.BuildProject => "проектные файлы",
        Layer.Component => "компоненты",
        Layer.Hook => "хуки/composables",
        Layer.State => "состояние (store)",
        Layer.ApiClient => "API-клиент",
        Layer.Routing => "маршрутизация",
        Layer.Styles => "стили",
        Layer.Template => "шаблоны",
        Layer.Types => "типы/модели",
        Layer.Utils => "утилиты",
        Layer.Constants => "константы",
        Layer.Stories => "сторис (Storybook)",
        Layer.I18n => "локализация",
        Layer.Module => "модули",
        _ => "прочее",
    };
}
