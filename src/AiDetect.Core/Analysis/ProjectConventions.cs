using System.Text.RegularExpressions;
using AiDetect.Core.Model;

namespace AiDetect.Core.Analysis;

/// <summary>
/// Конвенции проекта, которые делают «ИИ-подобные» признаки обязательными для всех (и людей тоже):
/// StyleCop / обязательный XML-doc, правила .editorconfig, Conventional Commits, шаблоны описания MR.
/// Найденные конвенции ослабляют соответствующие признаки — это снижает ложные срабатывания на дисциплинированных командах.
/// </summary>
public sealed partial class ProjectConventions
{
    public bool StyleCop { get; private init; }
    public bool XmlDocRequired { get; private init; }
    public bool EditorConfigStyleRules { get; private init; }
    public bool FileScopedNamespacesEnforced { get; private init; }
    public bool ConventionalCommits { get; private init; }
    public List<string> MrTemplateHeadings { get; private init; } = [];
    public List<string> Notes { get; private init; } = [];

    public bool Any => StyleCop || XmlDocRequired || EditorConfigStyleRules || ConventionalCommits || MrTemplateHeadings.Count > 0;

    [GeneratedRegex(@"^#{1,4}\s+(.+?)\s*$", RegexOptions.Multiline)]
    private static partial Regex HeadingRegex();

    public static ProjectConventions Detect(RepositoryContext repo)
    {
        bool styleCop = false, xmlDoc = false, editorStyle = false, fileScoped = false, conventional = false;
        var headings = new List<string>();
        var notes = new List<string>();
        foreach (var f in repo.ConfigFiles)
        {
            var name = Path.GetFileName(f.Path).ToLowerInvariant();
            var c = f.Content;
            if (name.EndsWith(".csproj") || name.EndsWith(".props") || name.EndsWith(".targets") || name == "stylecop.json")
            {
                if (c.Contains("StyleCop.Analyzers", StringComparison.OrdinalIgnoreCase) || name == "stylecop.json")
                {
                    styleCop = true;
                }

                if (Regex.IsMatch(c, @"<GenerateDocumentationFile>\s*true", RegexOptions.IgnoreCase) ||
                    Regex.IsMatch(c, @"<DocumentationFile>", RegexOptions.IgnoreCase))
                {
                    xmlDoc = true;
                }
            }
            else if (name == ".editorconfig")
            {
                if (Regex.IsMatch(c, @"^\s*(dotnet_style_|csharp_style_|csharp_prefer_|dotnet_diagnostic\.)", RegexOptions.Multiline))
                {
                    editorStyle = true;
                }

                if (Regex.IsMatch(c, @"csharp_style_namespace_declarations\s*=\s*file_scoped", RegexOptions.IgnoreCase))
                {
                    fileScoped = true;
                }

                if (Regex.IsMatch(c, @"dotnet_diagnostic\.(SA1600|SA1611|SA1615|CS1591)\.severity\s*=\s*(warning|error)", RegexOptions.IgnoreCase))
                {
                    xmlDoc = true;
                }
            }
            else if (name.StartsWith(".commitlintrc") || name.StartsWith("commitlint.config") || name == ".czrc" || name == ".versionrc" ||
                     name == ".releaserc" || name.StartsWith(".releaserc"))
            {
                conventional = true;
            }
            else if (f.Path.Replace('\\', '/').Contains("merge_request_templates/", StringComparison.OrdinalIgnoreCase) ||
                     f.Path.Replace('\\', '/').Contains("pull_request_template", StringComparison.OrdinalIgnoreCase))
            {
                headings.AddRange(HeadingRegex().Matches(c).Select(m => Normalize(m.Groups[1].Value)));
            }
        }

        if (styleCop)
        {
            notes.Add("StyleCop");
        }

        if (xmlDoc)
        {
            notes.Add("обязательный XML-doc");
        }

        if (editorStyle)
        {
            notes.Add(".editorconfig со стилевыми правилами");
        }

        if (conventional)
        {
            notes.Add("Conventional Commits (commitlint)");
        }

        if (headings.Count > 0)
        {
            notes.Add("шаблон описания MR");
        }

        return new ProjectConventions
        {
            StyleCop = styleCop,
            XmlDocRequired = xmlDoc || styleCop,
            EditorConfigStyleRules = editorStyle,
            FileScopedNamespacesEnforced = fileScoped,
            ConventionalCommits = conventional,
            MrTemplateHeadings = headings.Distinct().ToList(),
            Notes = notes,
        };
    }

    public static string Normalize(string heading) =>
        Regex.Replace(heading.ToLowerInvariant(), @"[^\p{L}\p{N} ]", "").Trim();

    /// <summary>Пути конфигурационных файлов, которые стоит загрузить из корня репозитория.</summary>
    public static bool IsRootConfigFile(string name)
    {
        var n = name.ToLowerInvariant();
        return n is "directory.build.props" or "directory.packages.props" or "directory.build.targets" or ".editorconfig" or "stylecop.json"
                   or ".czrc" or ".versionrc" or ".releaserc" ||
               n.StartsWith(".commitlintrc", StringComparison.Ordinal) || n.StartsWith("commitlint.config", StringComparison.Ordinal) ||
               n.StartsWith(".releaserc", StringComparison.Ordinal);
    }
}
