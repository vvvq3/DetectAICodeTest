using System.Text.RegularExpressions;
using AiDetect.Core.Model;

namespace AiDetect.Core.Analysis;

/// <summary>
/// Конвенции проекта, которые делают «ИИ-подобные» признаки обязательными для всех (и людей тоже).
/// Общая часть: Conventional Commits (commitlint) и шаблоны описания MR. Языки добавляют свои конвенции
/// (StyleCop и XML-doc для C#, ESLint/Prettier/tsconfig для фронтенда) и свои множители признаков.
/// Найденные конвенции ослабляют соответствующие признаки — это снижает ложные срабатывания на дисциплинированных командах.
/// </summary>
public partial class ProjectConventions
{
    public bool ConventionalCommits { get; init; }

    public List<string> MrTemplateHeadings { get; init; } = [];

    /// <summary>Найденные конвенции для отчёта («StyleCop», «Prettier»…).</summary>
    public List<string> Notes { get; init; } = [];

    public virtual bool Any => ConventionalCommits || MrTemplateHeadings.Count > 0 || Notes.Count > 0;

    /// <summary>Множитель веса признака (1 — без поправки, 0 — признак выключен конвенцией).</summary>
    public virtual double Factor(string signalId) => ConventionalCommits && signalId == "proc.commitStyle" ? 0.4 : 1.0;

    [GeneratedRegex(@"^#{1,4}\s+(.+?)\s*$", RegexOptions.Multiline)]
    private static partial Regex HeadingRegex();

    /// <summary>Только общие конвенции (без языковых).</summary>
    public static ProjectConventions Detect(RepositoryContext repo)
    {
        var (conventional, headings) = DetectCommon(repo);
        var notes = new List<string>();
        AddCommonNotes(notes, conventional, headings);
        return new ProjectConventions { ConventionalCommits = conventional, MrTemplateHeadings = headings, Notes = notes };
    }

    /// <summary>Conventional Commits (commitlint, commitizen, semantic-release) и заголовки шаблонов описания MR.</summary>
    public static (bool Conventional, List<string> Headings) DetectCommon(RepositoryContext repo)
    {
        bool conventional = false;
        var headings = new List<string>();
        foreach (var f in repo.ConfigFiles)
        {
            var name = Path.GetFileName(f.Path).ToLowerInvariant();
            var normalized = f.Path.Replace('\\', '/');
            if (IsCommitConventionFile(name))
            {
                conventional = true;
            }
            else if (normalized.Contains("merge_request_templates/", StringComparison.OrdinalIgnoreCase) ||
                     normalized.Contains("pull_request_template", StringComparison.OrdinalIgnoreCase))
            {
                headings.AddRange(HeadingRegex().Matches(f.Content).Select(m => Normalize(m.Groups[1].Value)));
            }
        }

        return (conventional, headings.Distinct().ToList());
    }

    public static void AddCommonNotes(List<string> notes, bool conventional, List<string> headings)
    {
        if (conventional)
        {
            notes.Add("Conventional Commits (commitlint)");
        }

        if (headings.Count > 0)
        {
            notes.Add("шаблон описания MR");
        }
    }

    private static bool IsCommitConventionFile(string name) =>
        name.StartsWith(".commitlintrc", StringComparison.Ordinal) || name.StartsWith("commitlint.config", StringComparison.Ordinal) ||
        name == ".czrc" || name == ".versionrc" || name.StartsWith(".releaserc", StringComparison.Ordinal);

    public static string Normalize(string heading) =>
        Regex.Replace(heading.ToLowerInvariant(), @"[^\p{L}\p{N} ]", "").Trim();

    /// <summary>Общие для всех языков файлы конфигурации в корне репозитория.</summary>
    public static bool IsCommonRootConfigFile(string name)
    {
        var n = name.ToLowerInvariant();
        return n is ".editorconfig" || IsCommitConventionFile(n);
    }
}
