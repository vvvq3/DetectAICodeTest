using AiDetect.Core.Diff;
using AiDetect.Core.Model;
using AiDetect.Core.Sources;

namespace AiDetect.Core.Languages;

/// <summary>Список поддерживаемых языков: выбор по --lang / на странице и автоопределение по файлам MR.</summary>
public sealed class LanguageRegistry
{
    public const string Auto = "auto";

    public LanguageRegistry(IEnumerable<ILanguageAnalyzer> languages)
    {
        Languages = languages.ToList();
        if (Languages.Count == 0)
        {
            throw new ArgumentException("Нужен хотя бы один язык.", nameof(languages));
        }

        Files = new SourceFilePolicy(Languages);
    }

    public IReadOnlyList<ILanguageAnalyzer> Languages { get; }

    /// <summary>Какие файлы MR загружать источникам (объединение правил всех языков).</summary>
    public SourceFilePolicy Files { get; }

    public static bool IsAuto(string? id) => string.IsNullOrWhiteSpace(id) || id.Trim().Equals(Auto, StringComparison.OrdinalIgnoreCase) ||
                                             id.Trim().Equals("авто", StringComparison.OrdinalIgnoreCase);

    public ILanguageAnalyzer? Find(string? idOrAlias)
    {
        if (string.IsNullOrWhiteSpace(idOrAlias))
        {
            return null;
        }

        var key = idOrAlias.Trim();
        return Languages.FirstOrDefault(l => l.Id.Equals(key, StringComparison.OrdinalIgnoreCase) ||
                                             l.Aliases.Any(a => a.Equals(key, StringComparison.OrdinalIgnoreCase)));
    }

    public ILanguageAnalyzer Get(string idOrAlias) =>
        Find(idOrAlias) ?? throw new ArgumentException(
            $"Неизвестный язык «{idOrAlias}». Доступны: {Auto}, " + string.Join(", ", Languages.Select(l => l.Id)) + ".");

    /// <summary>Добавленные строки кода каждого языка в MR (без сгенерированных файлов).</summary>
    public Dictionary<string, int> AddedLinesByLanguage(MergeRequestData mr)
    {
        var result = Languages.ToDictionary(l => l.Id, _ => 0, StringComparer.Ordinal);
        foreach (var change in mr.Changes.Where(c => !c.IsDeleted))
        {
            int? added = null;
            foreach (var lang in Languages)
            {
                if (!lang.IsSourceFile(change.Path) || lang.IsGenerated(change.Path, null))
                {
                    continue;
                }

                added ??= CountAdded(change);
                result[lang.Id] += added.Value;
            }
        }

        return result;
    }

    /// <summary>Язык MR: больше всего добавленных строк; при равенстве — первый в списке.</summary>
    public ILanguageAnalyzer Detect(MergeRequestData mr)
    {
        var lines = AddedLinesByLanguage(mr);
        var best = Languages[0];
        foreach (var lang in Languages.Skip(1))
        {
            if (lines[lang.Id] > lines[best.Id])
            {
                best = lang;
            }
        }

        return best;
    }

    /// <summary>Выбранный язык или автоопределение.</summary>
    public ILanguageAnalyzer Resolve(string? idOrAuto, MergeRequestData mr) => IsAuto(idOrAuto) ? Detect(mr) : Get(idOrAuto!);

    internal static int CountAdded(FileChange change)
    {
        int added = UnifiedDiff.Parse(change.Diff).Added;
        if (added == 0 && change.IsNew && change.NewContent is not null)
        {
            added = change.NewContent.Split('\n').Length;
        }

        return added;
    }
}
