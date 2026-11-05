using AiDetect.Core.Analysis;
using AiDetect.Core.Languages;
using AiDetect.Core.Model;
using AiDetect.Core.Preprocessing;

namespace AiDetect.Core.Sources;

/// <summary>
/// Какие файлы источник MR (GitLab, git) загружает: объединение правил всех зарегистрированных языков.
/// Анализ потом берёт из загруженного только файлы выбранного языка.
/// </summary>
public sealed class SourceFilePolicy(IReadOnlyList<ILanguageAnalyzer> languages)
{
    public IReadOnlyList<ILanguageAnalyzer> Languages => languages;

    /// <summary>Нужна новая версия файла: код любого языка или Markdown.</summary>
    public bool NeedsContent(string path) => CommonFiles.IsMarkdown(path) || languages.Any(l => l.IsSourceFile(path));

    /// <summary>Нужна старая версия файла (для базовой линии): код любого языка.</summary>
    public bool NeedsOldContent(string path) => languages.Any(l => l.IsSourceFile(path));

    /// <summary>Файл учитывается в статистике коммитов (скорость написания кода).</summary>
    public bool IsCodeForSpeed(string path) => languages.Any(l => l.IsSourceFile(path) && !l.IsGenerated(path, null));

    public bool IsRootConfigFile(string fileName) =>
        ProjectConventions.IsCommonRootConfigFile(fileName) || languages.Any(l => l.IsRootConfigFile(fileName));

    public bool IsProjectFile(string fileName) => languages.Any(l => l.IsProjectFile(fileName));

    /// <summary>Каталоги для базовой линии по каждому языку: каталоги изменённых файлов языка и их родители, ближайшие первыми.</summary>
    public List<(ILanguageAnalyzer Language, List<string> Directories)> BaselineDirectories(MergeRequestData mr) =>
        languages.Select(l => (l, CandidateDirectories(mr.Changes.Where(c => l.IsSourceFile(c.Path)).Select(c => c.Path))))
            .Where(x => x.Item2.Count > 0)
            .ToList();

    public static bool IsBaselineCandidate(ILanguageAnalyzer language, string path) =>
        language.IsSourceFile(path) && !language.IsGenerated(path, null);

    /// <summary>Каталоги изменённых файлов и их родители (до двух уровней), сначала самые глубокие.</summary>
    public static List<string> CandidateDirectories(IEnumerable<string> paths)
    {
        var dirs = new List<string>();
        foreach (var path in paths)
        {
            var dir = Path.GetDirectoryName(path)?.Replace('\\', '/') ?? "";
            for (int depth = 0; depth < 2; depth++)
            {
                if (!dirs.Contains(dir))
                {
                    dirs.Add(dir);
                }

                if (dir.Length == 0)
                {
                    break;
                }

                dir = Path.GetDirectoryName(dir)?.Replace('\\', '/') ?? "";
            }
        }

        return dirs.OrderByDescending(d => d.Count(ch => ch == '/')).ThenByDescending(d => d.Length).ToList();
    }

    /// <summary>Каталоги-предки изменённых файлов языков с маркером пакета (package.json), без корня, ближайшие первыми.</summary>
    public List<(ILanguageAnalyzer Language, List<string> Ancestors)> PackageSearchPaths(MergeRequestData mr)
    {
        var result = new List<(ILanguageAnalyzer, List<string>)>();
        foreach (var lang in languages.Where(l => l.PackageMarker is not null))
        {
            var ancestors = new List<string>();
            foreach (var path in mr.Changes.Where(c => !c.IsDeleted && lang.IsSourceFile(c.Path)).Select(c => c.Path))
            {
                var dir = Path.GetDirectoryName(path)?.Replace('\\', '/') ?? "";
                while (dir.Length > 0)
                {
                    if (!ancestors.Contains(dir))
                    {
                        ancestors.Add(dir);
                    }

                    dir = Path.GetDirectoryName(dir)?.Replace('\\', '/') ?? "";
                }
            }

            if (ancestors.Count > 0)
            {
                result.Add((lang, ancestors.OrderByDescending(d => d.Count(ch => ch == '/')).ToList()));
            }
        }

        return result;
    }
}
