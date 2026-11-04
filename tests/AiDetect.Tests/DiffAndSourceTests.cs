using AiDetect.Core.Diff;
using AiDetect.Core.Preprocessing;
using AiDetect.Core.Sources;

namespace AiDetect.Tests;

public class DiffAndSourceTests
{
    [Fact]
    public void Parse_ReturnsAddedLineNumbersOfNewFile()
    {
        const string diff = "@@ -1,3 +1,4 @@\n line1\n-old\n+new2\n+new3\n line4\n";

        var stats = UnifiedDiff.Parse(diff);

        Assert.Equal(new[] { 2, 3 }, stats.AddedLines.OrderBy(x => x));
        Assert.Equal(1, stats.Removed);
    }

    [Fact]
    public void Parse_HandlesMultipleHunks()
    {
        const string diff = "@@ -1,2 +1,2 @@\n a\n+b\n@@ -10,2 +10,3 @@\n x\n+y\n+z\n";

        var stats = UnifiedDiff.Parse(diff);

        Assert.Equal(new[] { 2, 11, 12 }, stats.AddedLines.OrderBy(x => x));
    }

    [Fact]
    public void LineDiff_BuildsDiffThatParsesBackToAddedLines()
    {
        var oldText = "a\nb\nc\nd\n";
        var newText = "a\nb\nX\nc\nd\nY\n";

        var diff = LineDiff.BuildUnifiedDiff(oldText, newText);
        var stats = UnifiedDiff.Parse(diff);

        Assert.Equal(new[] { 3, 6 }, stats.AddedLines.OrderBy(x => x));
    }

    [Theory]
    [InlineData("https://gitlab.company.com/group/sub/project/-/merge_requests/42", "https://gitlab.company.com", "group/sub/project", 42)]
    [InlineData("http://10.0.0.5/team/api/merge_requests/7/diffs", "http://10.0.0.5", "team/api", 7)]
    public void ParseUrl_ExtractsHostProjectAndIid(string url, string baseUrl, string project, int iid)
    {
        var parsed = GitLabMergeRequestSource.ParseUrl(url);

        Assert.Equal(baseUrl, parsed.BaseUrl);
        Assert.Equal(project, parsed.ProjectPath);
        Assert.Equal(iid, parsed.Iid);
    }

    [Fact]
    public void ParseUrl_RejectsNonMergeRequestLinks() =>
        Assert.Throws<SourceException>(() => GitLabMergeRequestSource.ParseUrl("https://gitlab.com/group/project/-/issues/1"));

    [Theory]
    [InlineData("src/App/Migrations/20240101_Init.cs", true)]
    [InlineData("src/App/Forms/Main.Designer.cs", true)]
    [InlineData("src/App/obj/Debug/App.AssemblyInfo.cs", true)]
    [InlineData("src/App/Services/OrderService.cs", false)]
    public void IsGenerated_DetectsGeneratedFiles(string path, bool expected) =>
        Assert.Equal(expected, FileClassifier.IsGenerated(path, null));

    [Theory]
    [InlineData("src/Shop.Api/Controllers/OrdersController.cs", Layer.Controller)]
    [InlineData("src/Shop.Api/Services/OrderService.cs", Layer.Service)]
    [InlineData("tests/Shop.Tests/OrderServiceTests.cs", Layer.Tests)]
    [InlineData("docs/readme.md", Layer.Documentation)]
    [InlineData("src/Shop.Api/Validators/CreateOrderValidator.cs", Layer.Validation)]
    public void ClassifyLayer_RecognisesCommonLayers(string path, Layer expected) =>
        Assert.Equal(expected, FileClassifier.ClassifyLayer(path, null));
}
