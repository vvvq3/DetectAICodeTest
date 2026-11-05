using System.Text.RegularExpressions;
using AiDetect.Core.Analysis;
using AiDetect.Core.Model;

namespace AiDetect.Languages.CSharp;

/// <summary>
/// Конвенции C#-проекта: StyleCop / обязательный XML-doc, правила .editorconfig (плюс общие: Conventional Commits, шаблоны MR).
/// </summary>
public sealed class CSharpConventions : ProjectConventions
{
    public bool StyleCop { get; private init; }
    public bool XmlDocRequired { get; private init; }
    public bool EditorConfigStyleRules { get; private init; }
    public bool FileScopedNamespacesEnforced { get; private init; }

    public override bool Any => StyleCop || XmlDocRequired || EditorConfigStyleRules || ConventionalCommits || MrTemplateHeadings.Count > 0;

    public static new CSharpConventions Detect(RepositoryContext repo)
    {
        bool styleCop = false, xmlDoc = false, editorStyle = false, fileScoped = false;
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
        }

        var (conventional, headings) = DetectCommon(repo);
        var notes = new List<string>();
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

        AddCommonNotes(notes, conventional, headings);
        return new CSharpConventions
        {
            StyleCop = styleCop,
            XmlDocRequired = xmlDoc || styleCop,
            EditorConfigStyleRules = editorStyle,
            FileScopedNamespacesEnforced = fileScoped,
            ConventionalCommits = conventional,
            MrTemplateHeadings = headings,
            Notes = notes,
        };
    }

    /// <summary>Ослабление признаков, которые в проекте обязательны по конвенциям (StyleCop, .editorconfig, commitlint).</summary>
    public override double Factor(string id)
    {
        double f = 1.0;
        if (XmlDocRequired && id.StartsWith("doc.", StringComparison.Ordinal))
        {
            f *= 0.2;
        }

        if (StyleCop && id is "ord.members" or "acc.explicit" or "fmt.blankBeforeReturn" or "cmt.case" or "fld.readonly" or "fmt.irregular")
        {
            f *= id is "fld.readonly" or "fmt.irregular" ? 0.6 : 0.3;
        }

        if (EditorConfigStyleRules && id is "syn.nullPattern" or "syn.modern" or "api.stringEmpty" or "syn.fileScopedNs" or "fmt.irregular"
                or "prop.stringEmptyInit" or "acc.explicit" or "cls.sealed")
        {
            f *= 0.6;
        }

        if (FileScopedNamespacesEnforced && id == "syn.fileScopedNs")
        {
            f = 0;
        }

        if (ConventionalCommits && id == "proc.commitStyle")
        {
            f *= 0.4;
        }

        return f;
    }

    /// <summary>Пути конфигурационных файлов C#, которые стоит загрузить из корня репозитория.</summary>
    public static bool IsRootConfigFile(string name)
    {
        var n = name.ToLowerInvariant();
        return n is "directory.build.props" or "directory.packages.props" or "directory.build.targets" or "stylecop.json" ||
               n.EndsWith(".csproj", StringComparison.Ordinal);
    }
}
