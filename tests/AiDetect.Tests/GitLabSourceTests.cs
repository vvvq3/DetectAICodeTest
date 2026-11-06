using System.Net;
using System.Text;
using AiDetect.Core.Sources;
using AiDetect.Languages.CSharp;

namespace AiDetect.Tests;

public class GitLabSourceTests
{
    private const string Url = "https://gitlab.test/team/shop/-/merge_requests/7";
    private const string Api = "https://gitlab.test/api/v4/projects/team%2Fshop";

    private sealed class FakeGitLab : HttpMessageHandler
    {
        public Dictionary<string, Func<HttpResponseMessage>> Routes { get; } = new(StringComparer.Ordinal);
        public List<string> Requests { get; } = [];
        public int TooManyRequestsLeft { get; set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var url = request.RequestUri!.ToString();
            Requests.Add(url);
            if (TooManyRequestsLeft > 0)
            {
                TooManyRequestsLeft--;
                var r = new HttpResponseMessage((HttpStatusCode)429);
                r.Headers.RetryAfter = new System.Net.Http.Headers.RetryConditionHeaderValue(TimeSpan.FromMilliseconds(10));
                return Task.FromResult(r);
            }

            var path = url.Split('?')[0];
            if (Routes.TryGetValue(path, out var exact))
            {
                return Task.FromResult(exact());
            }

            foreach (var (prefix, factory) in Routes.Where(r => r.Key.Contains('?') || r.Key.EndsWith('/')).OrderByDescending(r => r.Key.Length))
            {
                if (url.StartsWith(prefix, StringComparison.Ordinal))
                {
                    return Task.FromResult(factory());
                }
            }

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound) { Content = new StringContent("{\"message\":\"404 Not Found\"}") });
        }
    }

    private static Func<HttpResponseMessage> Json(string json) => () => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(json, Encoding.UTF8, "application/json") };

    private static Func<HttpResponseMessage> Raw(string text) => () => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(text, Encoding.UTF8) };

    private static FakeGitLab CreateServer(bool supportsDiffs)
    {
        var fake = new FakeGitLab();
        fake.Routes[$"{Api}/merge_requests/7"] = Json("""
            {"title":"Add discounts","description":"## Summary\n- add","author":{"name":"Ivan","username":"ivan"},
             "source_branch":"feature/discounts","target_branch":"main","created_at":"2026-01-10T10:00:00Z",
             "diff_refs":{"base_sha":"base1","head_sha":"head1","start_sha":"base1"}}
            """);
        fake.Routes[$"{Api}/merge_requests/7/commits"] = Json("""
            [{"id":"c2","title":"tests","message":"tests","author_name":"Ivan","author_email":"i@x","authored_date":"2026-01-10T10:20:00Z","committed_date":"2026-01-10T10:20:00Z"},
             {"id":"c1","title":"Add discounts","message":"Add discounts\n\nCo-Authored-By: Claude <noreply@anthropic.com>","author_name":"Ivan","author_email":"i@x","authored_date":"2026-01-10T10:00:00Z","committed_date":"2026-01-10T10:00:00Z"}]
            """);
        const string diffs = """
            [{"old_path":"src/Discount.cs","new_path":"src/Discount.cs","new_file":true,"renamed_file":false,"deleted_file":false,"diff":"@@ -0,0 +1,3 @@\n+public sealed class Discount\n+{\n+}\n"}]
            """;
        if (supportsDiffs)
        {
            fake.Routes[$"{Api}/merge_requests/7/diffs"] = Json(diffs);
        }
        else
        {
            fake.Routes[$"{Api}/merge_requests/7/changes"] = Json("{\"changes\":" + diffs + "}");
        }

        fake.Routes[$"{Api}/merge_requests/7/notes"] = Json("[]");
        fake.Routes[$"{Api}/repository/files/src%2FDiscount.cs/raw?ref=head1"] = Raw("public sealed class Discount\n{\n}\n");
        fake.Routes[$"{Api}/repository/commits/"] = Json("[{\"new_path\":\"src/Discount.cs\",\"diff\":\"@@ -0,0 +1,1 @@\\n+x\\n\"}]");
        fake.Routes[$"{Api}/repository/tree?ref=head1"] = Json("[{\"name\":\"CLAUDE.md\",\"type\":\"blob\",\"path\":\"CLAUDE.md\"}]");
        fake.Routes[$"{Api}/repository/tree?ref=base1"] = Json("[{\"name\":\"Order.cs\",\"type\":\"blob\",\"path\":\"src/Order.cs\"}]");
        fake.Routes[$"{Api}/repository/files/src%2FOrder.cs/raw?ref=base1"] = Raw("namespace Shop { public class Order { } }\n");
        return fake;
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task LoadAsync_ReadsMrCommitsChangesAndRepositoryContext(bool supportsDiffs)
    {
        var fake = CreateServer(supportsDiffs);
        using var source = new GitLabMergeRequestSource(Url, "token", TestLanguages.Registry.Files, fake);

        var mr = await source.LoadAsync();

        Assert.Equal("Add discounts", mr.Title);
        Assert.Equal("ivan", mr.AuthorUsername);
        Assert.Equal(2, mr.Commits.Count);
        Assert.Equal("c1", mr.Commits[0].Sha);
        var change = Assert.Single(mr.Changes);
        Assert.True(change.IsNew);
        Assert.Contains("sealed class Discount", change.NewContent);
        Assert.Contains("CLAUDE.md", mr.Repository.AgentFilesAtHead);
        Assert.Contains(mr.Repository.BaselineFiles, f => f.Path == "src/Order.cs");
    }

    [Fact]
    public async Task LoadAsync_RetriesOnTooManyRequests()
    {
        var fake = CreateServer(supportsDiffs: true);
        fake.TooManyRequestsLeft = 2;
        using var source = new GitLabMergeRequestSource(Url, "token", TestLanguages.Registry.Files, fake);

        var mr = await source.LoadAsync();

        Assert.Equal("Add discounts", mr.Title);
    }

    [Fact]
    public async Task LoadAsync_UnknownMr_GivesClearError()
    {
        var fake = new FakeGitLab();
        using var source = new GitLabMergeRequestSource(Url, "token", TestLanguages.Registry.Files, fake);

        var ex = await Assert.ThrowsAsync<SourceException>(() => source.LoadAsync());

        Assert.Contains("404", ex.Message);
    }

    [Fact]
    public async Task LoadAsync_Unauthorized_SuggestsToken()
    {
        var fake = new FakeGitLab();
        fake.Routes[$"{Api}/merge_requests/7"] = () => new HttpResponseMessage(HttpStatusCode.Unauthorized);
        using var source = new GitLabMergeRequestSource(Url, null, TestLanguages.Registry.Files, fake);

        var ex = await Assert.ThrowsAsync<SourceException>(() => source.LoadAsync());

        Assert.Contains("GITLAB_TOKEN", ex.Message);
    }

    [Fact]
    public async Task LoadAsync_ProxyForbiddenPage_SaysNetworkNotToken()
    {
        var fake = new FakeGitLab();
        fake.Routes[$"{Api}/merge_requests/7"] = () => new HttpResponseMessage(HttpStatusCode.Forbidden)
        {
            Content = new StringContent("<html><head><title>403 Forbidden</title></head><body><center>nginx</center></body></html>", Encoding.UTF8, "text/html"),
        };
        using var source = new GitLabMergeRequestSource(Url, "token", TestLanguages.Registry.Files, fake);

        var ex = await Assert.ThrowsAsync<SourceException>(() => source.LoadAsync());

        Assert.Contains("403", ex.Message);
        Assert.Contains("VPN", ex.Message);
        Assert.Contains("gitlab.test", ex.Message);
    }

    [Theory]
    [InlineData("{\"error\":\"insufficient_scope\"}", "read_api")]
    [InlineData("{\"message\":\"403 Forbidden\"}", "нет доступа к проекту")]
    public async Task LoadAsync_GitLabForbidden_BlamesToken(string json, string expected)
    {
        var fake = new FakeGitLab();
        fake.Routes[$"{Api}/merge_requests/7"] = () => new HttpResponseMessage(HttpStatusCode.Forbidden)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json"),
        };
        using var source = new GitLabMergeRequestSource(Url, "token", TestLanguages.Registry.Files, fake);

        var ex = await Assert.ThrowsAsync<SourceException>(() => source.LoadAsync());

        Assert.Contains(expected, ex.Message);
        Assert.DoesNotContain("VPN", ex.Message);
    }
}
