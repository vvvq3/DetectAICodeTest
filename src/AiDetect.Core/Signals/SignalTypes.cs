using AiDetect.Core.Features;

namespace AiDetect.Core.Signals;

/// <summary>Независимые группы сигналов. У каждой группы ограничен суммарный вклад (см. <see cref="GroupInfo"/>).</summary>
public enum SignalGroup
{
    Artifacts,
    Documentation,
    Comments,
    Naming,
    ModernSyntax,
    Defensive,
    Async,
    Logging,
    Exceptions,
    Structure,
    HumanHabits,
    Formatting,
    Tests,
    MrShape,
    Process,
    ProjectShift,
}

public static class GroupInfo
{
    public static string Title(SignalGroup g) => g switch
    {
        SignalGroup.Artifacts => "Прямые следы Claude Code",
        SignalGroup.Documentation => "XML-документация",
        SignalGroup.Comments => "Комментарии",
        SignalGroup.Naming => "Именование",
        SignalGroup.ModernSyntax => "Современный синтаксис и API",
        SignalGroup.Defensive => "Защитный код",
        SignalGroup.Async => "Async и токены отмены",
        SignalGroup.Logging => "Логирование",
        SignalGroup.Exceptions => "Исключения",
        SignalGroup.Structure => "Структура классов",
        SignalGroup.HumanHabits => "Ручные привычки",
        SignalGroup.Formatting => "Форматирование",
        SignalGroup.Tests => "Тесты",
        SignalGroup.MrShape => "Форма МР",
        SignalGroup.Process => "Коммиты и процесс",
        SignalGroup.ProjectShift => "Отличие от стиля проекта",
        _ => g.ToString(),
    };

    /// <summary>Максимальный по модулю вклад группы в логит (чтобы одна группа не решала всё).</summary>
    public static double Cap(SignalGroup g) => g switch
    {
        SignalGroup.Artifacts => 9.0,
        SignalGroup.Documentation => 1.8,
        SignalGroup.Comments => 2.0,
        SignalGroup.Naming => 1.5,
        SignalGroup.ModernSyntax => 1.6,
        SignalGroup.Defensive => 1.2,
        SignalGroup.Async => 2.0,
        SignalGroup.Logging => 1.4,
        SignalGroup.Exceptions => 1.2,
        SignalGroup.Structure => 1.2,
        SignalGroup.HumanHabits => 1.6,
        SignalGroup.Formatting => 1.3,
        SignalGroup.Tests => 1.8,
        SignalGroup.MrShape => 2.2,
        SignalGroup.Process => 2.0,
        SignalGroup.ProjectShift => 1.5,
        _ => 1.5,
    };

    /// <summary>Устойчивость группы к маскировке стиля (для пояснения в отчёте).</summary>
    public static bool IsMimicryResistant(SignalGroup g) =>
        g is SignalGroup.Async or SignalGroup.Logging or SignalGroup.MrShape or SignalGroup.Process or SignalGroup.Tests;
}

/// <summary>Результат вычисления одного признака.</summary>
public sealed class SignalResult
{
    public required string Id { get; init; }

    public required SignalGroup Group { get; init; }

    /// <summary>Формулировка признака (зависит от направления: «за ИИ» или «за человека»).</summary>
    public required string Title { get; init; }

    /// <summary>Свидетельство: −1 — типично для человека, +1 — типично для Claude.</summary>
    public double Evidence { get; init; }

    /// <summary>Надёжность 0..1 — зависит от числа наблюдений.</summary>
    public double Reliability { get; init; }

    /// <summary>Вес признака в логитах.</summary>
    public double Weight { get; init; }

    /// <summary>Пояснение с цифрами: «11 из 12 async-методов…».</summary>
    public string Detail { get; init; } = "";

    public IReadOnlyList<CodeExample> Examples { get; init; } = [];

    /// <summary>Значение признака (для калибровки и отладки).</summary>
    public double? RawValue { get; init; }

    public double Contribution => Weight * Evidence * Reliability;
}

public sealed class GroupScore
{
    public required SignalGroup Group { get; init; }

    public double RawSum { get; init; }

    public double Capped { get; init; }

    public List<SignalResult> Signals { get; init; } = [];
}
