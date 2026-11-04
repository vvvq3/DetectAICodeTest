using AiDetect.Core.Analysis;
using AiDetect.Core.Features;
using AiDetect.Core.Model;

namespace AiDetect.Tests;

public class ConventionsAndNewFeaturesTests
{
    [Fact]
    public void Detect_FindsStyleCopXmlDocEditorConfigAndCommitlint()
    {
        var repo = new RepositoryContext
        {
            ConfigFiles =
            [
                new SourceFile { Path = "Directory.Build.props", Content = "<Project><ItemGroup><PackageReference Include=\"StyleCop.Analyzers\" /></ItemGroup></Project>" },
                new SourceFile { Path = "src/App/App.csproj", Content = "<Project><PropertyGroup><GenerateDocumentationFile>true</GenerateDocumentationFile></PropertyGroup></Project>" },
                new SourceFile { Path = ".editorconfig", Content = "[*.cs]\ncsharp_style_namespace_declarations = file_scoped:warning\n" },
                new SourceFile { Path = ".commitlintrc.json", Content = "{}" },
                new SourceFile { Path = ".gitlab/merge_request_templates/Default.md", Content = "## Что сделано\n\n## Как проверить\n" },
            ],
        };

        var c = ProjectConventions.Detect(repo);

        Assert.True(c.StyleCop);
        Assert.True(c.XmlDocRequired);
        Assert.True(c.EditorConfigStyleRules);
        Assert.True(c.FileScopedNamespacesEnforced);
        Assert.True(c.ConventionalCommits);
        Assert.Contains("что сделано", c.MrTemplateHeadings);
    }

    [Fact]
    public void Detect_EmptyRepository_HasNoConventions() =>
        Assert.False(ProjectConventions.Detect(new RepositoryContext()).Any);

    [Fact]
    public void Extract_CountsExplicitAccessGenericExceptionsAndNesting()
    {
        const string code = """
            public class A
            {
                int _count;
                private readonly string _name = "";

                public void Run(int[] items)
                {
                    foreach (var i in items)
                    {
                        if (i > 0)
                        {
                            try
                            {
                                if (i > 10)
                                {
                                    throw new Exception("too big");
                                }
                            }
                            catch (InvalidOperationException)
                            {
                                throw new ArgumentException("bad", nameof(items));
                            }
                        }
                    }
                }
            }
            """;

        var s = CSharpFeatureExtractor.Extract("A.cs", code, _ => true, false, true);

        Assert.Equal(3, s[K.MembersChecked]);
        Assert.Equal(2, s[K.ExplicitAccess]);
        Assert.Equal(1, s[K.GenericExceptionThrows]);
        Assert.Equal(1, s[K.SpecificExceptionThrows]);
        Assert.Equal(1, s[K.DeepNestedMethods]);
    }

    [Fact]
    public void Extract_RussianNarrativeComments_AreDetected()
    {
        const string code = """
            class A
            {
                void M()
                {
                    // Получаем заказы покупателя
                    var x = 1;
                    // костыль, потом переделать
                    var y = 2;
                }
            }
            """;

        var s = CSharpFeatureExtractor.Extract("A.cs", code, _ => true, false, true);

        Assert.Equal(1, s[K.NarrativeComments]);
        Assert.Equal(2, s[K.CyrillicComments]);
        Assert.Equal(1, s[K.CyrillicPolishedComments]);
    }
}
