namespace AiDetect.Core.Languages;

/// <summary>Стек проекта для отчёта: «TypeScript 4.1 · React 17 · Redux · Jest».</summary>
public sealed class StackInfo
{
    /// <summary>Элементы стека в порядке важности: язык, фреймворк, библиотеки, тесты.</summary>
    public List<string> Items { get; set; } = [];

    /// <summary>Машинные метки (react, angular, vue, typescript, aspnetcore…) — для разбивки метрик по фреймворкам.</summary>
    public List<string> Tags { get; set; } = [];

    /// <summary>Откуда взят стек: «package.json», «*.csproj», «импорты в коде».</summary>
    public string Source { get; set; } = "";

    public bool IsEmpty => Items.Count == 0;

    public void Add(string item, string? tag = null)
    {
        if (!Items.Contains(item, StringComparer.OrdinalIgnoreCase))
        {
            Items.Add(item);
        }

        if (tag is not null && !Tags.Contains(tag, StringComparer.OrdinalIgnoreCase))
        {
            Tags.Add(tag);
        }
    }

    public bool Has(string tag) => Tags.Contains(tag, StringComparer.OrdinalIgnoreCase);

    public override string ToString() => string.Join(" · ", Items);
}
