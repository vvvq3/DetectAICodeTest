using AiDetect.Core.Analysis;
using AiDetect.Core.Model;
using AiDetect.Core.Signals;

namespace AiDetect.Tests;

public class AnalyzerTests
{
    private static string DemoDir()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !Directory.Exists(Path.Combine(dir.FullName, "samples", "demo")))
        {
            dir = dir.Parent;
        }

        return Path.Combine(dir!.FullName, "samples", "demo");
    }

    private static MergeRequestData BuildMr(string variant, string commitTitle, string commitBody)
    {
        var root = DemoDir();
        var mr = new MergeRequestData { Title = commitTitle, HasMrMetadata = false };
        foreach (var file in Directory.GetFiles(Path.Combine(root, variant), "*", SearchOption.AllDirectories))
        {
            var rel = Path.GetRelativePath(Path.Combine(root, variant), file).Replace('\\', '/');
            var content = File.ReadAllText(file);
            var basePath = Path.Combine(root, "base", rel);
            var old = File.Exists(basePath) ? File.ReadAllText(basePath) : null;
            mr.Changes.Add(new FileChange
            {
                OldPath = rel,
                NewPath = rel,
                IsNew = old is null,
                NewContent = content,
                OldContent = old,
                Diff = AiDetect.Core.Diff.LineDiff.BuildUnifiedDiff(old ?? "", content),
            });
        }

        foreach (var file in Directory.GetFiles(Path.Combine(root, "base"), "*.cs", SearchOption.AllDirectories))
        {
            var rel = Path.GetRelativePath(Path.Combine(root, "base"), file).Replace('\\', '/');
            mr.Repository.BaselineFiles.Add(new SourceFile { Path = rel, Content = File.ReadAllText(file) });
        }

        mr.Commits.Add(new CommitInfo { Sha = "abc123", Title = commitTitle, Message = commitTitle + "\n\n" + commitBody, AuthorName = "Dev" });
        return mr;
    }

    [Fact]
    public void ClaudeStyleFeature_ScoresHigh()
    {
        var mr = BuildMr("ai", "feat(discounts): add discount code support", "- Add entity\n- Add service\n- Add tests");

        var report = new MergeRequestAnalyzer().Analyze(mr, new AnalyzerOptions { Parameters = new ModelParameters() });

        Assert.True(report.Probability > 0.85, $"P = {report.Probability:0.00}");
        Assert.Contains(report.TopAiSignals(10), s => s.Group == SignalGroup.Async);
    }

    [Fact]
    public void HumanStyleFeature_ScoresLow()
    {
        var mr = BuildMr("human", "добавил скидки", "");

        var report = new MergeRequestAnalyzer().Analyze(mr, new AnalyzerOptions { Parameters = new ModelParameters() });

        Assert.True(report.Probability < 0.15, $"P = {report.Probability:0.00}");
        Assert.Contains(report.TopHumanSignals(10), s => s.Group == SignalGroup.HumanHabits);
    }

    [Fact]
    public void CoAuthoredTrailer_IsDecisive_ButCanBeIgnored()
    {
        var mr = BuildMr("human", "добавил скидки", "Co-Authored-By: Claude <noreply@anthropic.com>");

        var withArtifacts = new MergeRequestAnalyzer().Analyze(mr, new AnalyzerOptions { Parameters = new ModelParameters() });
        var codeOnly = new MergeRequestAnalyzer().Analyze(mr, new AnalyzerOptions { Parameters = new ModelParameters(), IgnoreArtifacts = true });

        Assert.True(withArtifacts.ArtifactsFound);
        Assert.True(withArtifacts.Probability > codeOnly.Probability + 0.3);
        Assert.DoesNotContain(codeOnly.Signals, s => s.Group == SignalGroup.Artifacts);
    }

    [Fact]
    public void GroupContribution_IsCapped()
    {
        var signals = Enumerable.Range(0, 10).Select(i => new SignalResult
        {
            Id = "x" + i, Group = SignalGroup.Naming, Title = "t", Evidence = 1, Reliability = 1, Weight = 1,
        }).ToList();

        var (groups, logit) = Scorer.Combine(signals, new ModelParameters());

        Assert.Equal(GroupInfo.Cap(SignalGroup.Naming), groups.Single().Capped);
        Assert.Equal(GroupInfo.Cap(SignalGroup.Naming) + new ModelParameters().Bias, logit, 6);
    }

    [Fact]
    public void FileGrowth_NewFilesCreatedInSingleCommits_AreReportedAsAiLike()
    {
        var mr = BuildMr("ai", "feat: discounts", "- a\n- b");
        mr.Commits.Clear();
        var newFiles = mr.Changes.Where(c => c.IsNew && c.Path.EndsWith(".cs")).Select(c => c.Path).ToList();
        mr.Commits.Add(new CommitInfo { Sha = "1", Title = "feat: code", Message = "feat: code", AuthorName = "Dev", ChangedPaths = newFiles.Take(3).ToList() });
        mr.Commits.Add(new CommitInfo { Sha = "2", Title = "test: tests", Message = "test: tests", AuthorName = "Dev", ChangedPaths = newFiles.Skip(3).ToList() });

        var report = new MergeRequestAnalyzer().Analyze(mr, new AnalyzerOptions { Parameters = new ModelParameters() });

        var growth = Assert.Single(report.Signals, s => s.Id == "proc.fileGrowth");
        Assert.True(growth.Evidence > 0.5);
    }
}
