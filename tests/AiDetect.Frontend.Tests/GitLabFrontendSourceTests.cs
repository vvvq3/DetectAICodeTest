using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using AiDetect.Core.Analysis;
using AiDetect.Core.Languages;
using AiDetect.Core.Sources;
using AiDetect.Languages.CSharp;
using AiDetect.Languages.Frontend;

namespace AiDetect.Frontend.Tests;

/// <summary>GitLab-клиент на поддельном сервере: фронтенд-файлы, package.json пакета, соседние файлы, статистика коммитов по языкам.</summary>
public class GitLabFrontendSourceTests
{
    private const string Url = "https://gitlab.test/team/app/-/merge_requests/3";
    private const string Api = "https://gitlab.test/api/v4/projects/team%2Fapp";

    private sealed class Fake : HttpMessageHandler
    {
        public Dictionary<string, string> Routes { get; } = new(StringComparer.Ordinal);

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var url = request.RequestUri!.AbsoluteUri;
            var page = Regex.Match(url, @"[?&]page=(\d+)");
            if (page.Success && page.Groups[1].Value != "1")
            {
                return Task.FromResult(Ok("[]"));
            }

            var key = Regex.Replace(url, @"[?&]per_page=\d+&page=\d+$", "");
            if (Routes.TryGetValue(key, out var body))
            {
                return Task.FromResult(Ok(body));
            }

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound) { Content = new StringContent("{\"message\":\"404 Not Found\"}") });
        }

        private static HttpResponseMessage Ok(string body) => new(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
    }

    private static string Esc(string s) => Uri.EscapeDataString(s);

    private const string Card = "export function UserCard({ name }: UserCardProps) {\n  return <div className=\"card\">{name}</div>;\n}\n";

    private static Fake Server()
    {
        var f = new Fake();
        f.Routes[$"{Api}/merge_requests/3"] = """
            {"title":"User card","description":"","author":{"name":"Anna","username":"anna"},"source_branch":"feature/card","target_branch":"main",
             "diff_refs":{"base_sha":"base1","head_sha":"head1","start_sha":"base1"}}
            """;
        f.Routes[$"{Api}/merge_requests/3/commits"] = """
            [{"id":"c2","title":"tests","message":"tests","author_name":"Anna","author_email":"a@x","authored_date":"2026-01-10T10:20:00Z","committed_date":"2026-01-10T10:20:00Z"},
             {"id":"c1","title":"card","message":"card","author_name":"Anna","author_email":"a@x","authored_date":"2026-01-10T10:00:00Z","committed_date":"2026-01-10T10:00:00Z"}]
            """;
        f.Routes[$"{Api}/merge_requests/3/notes?sort=asc&order_by=created_at"] = "[]";
        f.Routes[$"{Api}/merge_requests/3/diffs"] = """
            [{"old_path":"web/src/components/UserCard.tsx","new_path":"web/src/components/UserCard.tsx","new_file":true,"renamed_file":false,"deleted_file":false,"diff":"@@ -0,0 +1,3 @@\n+export function UserCard({ name }: UserCardProps) {\n+  return <div className=\"card\">{name}</div>;\n+}\n"},
             {"old_path":"src/Api/Users.cs","new_path":"src/Api/Users.cs","new_file":false,"renamed_file":false,"deleted_file":false,"diff":"@@ -1,1 +1,2 @@\n public class Users {}\n+// note\n"}]
            """;
        f.Routes[$"{Api}/repository/files/{Esc("web/src/components/UserCard.tsx")}/raw?ref=head1"] = Card;
        f.Routes[$"{Api}/repository/files/{Esc("src/Api/Users.cs")}/raw?ref=head1"] = "public class Users {}\n// note\n";
        f.Routes[$"{Api}/repository/files/{Esc("src/Api/Users.cs")}/raw?ref=base1"] = "public class Users {}\n";
        f.Routes[$"{Api}/repository/commits/c1/diff"] = """
            [{"new_path":"web/src/components/UserCard.tsx","diff":"@@ -0,0 +1,3 @@\n+a\n+b\n+c\n"},{"new_path":"src/Api/Users.cs","diff":"@@ -1,1 +1,2 @@\n x\n+y\n"}]
            """;
        f.Routes[$"{Api}/repository/commits/c2/diff"] = """[{"new_path":"web/src/components/UserCard.test.tsx","diff":"@@ -0,0 +1,2 @@\n+a\n+b\n"}]""";
        f.Routes[$"{Api}/repository/tree?ref=head1"] = """
            [{"name":".eslintrc.json","type":"blob","path":".eslintrc.json"},{"name":"web","type":"tree","path":"web"},{"name":"src","type":"tree","path":"src"}]
            """;
        f.Routes[$"{Api}/repository/files/{Esc(".eslintrc.json")}/raw?ref=head1"] = """{ "extends": ["airbnb"] }""";
        f.Routes[$"{Api}/repository/tree?ref=base1&path={Esc("web/src/components")}"] = """
            [{"name":"Button.tsx","type":"blob","path":"web/src/components/Button.tsx"}]
            """;
        f.Routes[$"{Api}/repository/files/{Esc("web/src/components/Button.tsx")}/raw?ref=base1"] = "export const Button = () => <button>ok</button>;\n";
        f.Routes[$"{Api}/repository/tree?ref=base1&path={Esc("web")}"] = """
            [{"name":"package.json","type":"blob","path":"web/package.json"},{"name":"tsconfig.json","type":"blob","path":"web/tsconfig.json"},{"name":"src","type":"tree","path":"web/src"}]
            """;
        f.Routes[$"{Api}/repository/files/{Esc("web/package.json")}/raw?ref=base1"] = """{ "dependencies": { "react": "^18.2.0" }, "devDependencies": { "typescript": "^5.4.0" } }""";
        f.Routes[$"{Api}/repository/files/{Esc("web/tsconfig.json")}/raw?ref=base1"] = """{ "compilerOptions": { "strict": true } }""";
        return f;
    }

    [Fact]
    public async Task LoadsFrontendFilesPackageConfigsAndPerLanguageStats()
    {
        var registry = new LanguageRegistry([new CSharpLanguage(), new FrontendLanguage()]);
        using var source = new GitLabMergeRequestSource(Url, "t", registry.Files, Server());
        var mr = await source.LoadAsync();

        Assert.Equal(Card, mr.Changes.First(c => c.Path.EndsWith(".tsx", StringComparison.Ordinal)).NewContent);
        Assert.Contains(mr.Repository.BaselineFiles, f => f.Path == "web/src/components/Button.tsx");
        Assert.Contains(mr.Repository.ConfigFiles, f => f.Path == "web/package.json");
        Assert.Contains(mr.Repository.ConfigFiles, f => f.Path == "web/tsconfig.json");
        Assert.Contains(mr.Repository.ConfigFiles, f => f.Path == ".eslintrc.json");

        var c1 = mr.Commits.First(c => c.Sha == "c1");
        Assert.Equal(4, c1.Additions);
        Assert.Contains(c1.FileStats!, s => s.Path.EndsWith(".tsx", StringComparison.Ordinal) && s.Added == 3);

        var report = new MergeRequestAnalyzer(registry).Analyze(mr, new AnalyzerOptions { Parameters = new ModelParameters() });
        Assert.Equal("frontend", report.LanguageId);
        Assert.StartsWith("TypeScript 5 · React 18", report.Stack, StringComparison.Ordinal);
        Assert.Contains(report.Warnings, w => w.Contains("ESLint", StringComparison.Ordinal) || w.Contains("строгий TypeScript", StringComparison.Ordinal));

        // Тот же MR как C#: в скорости учитываются только строки C#.
        var cs = MergeRequestAnalyzer.BuildContext(mr, new AnalyzerOptions { Language = "csharp" }, registry.Get("csharp"));
        Assert.Equal(1, cs.CommitAdditions(c1));
    }
}
