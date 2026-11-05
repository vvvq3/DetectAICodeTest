using System.Diagnostics;
using System.Text;
using AiDetect.Core.Diff;
using AiDetect.Core.Model;

namespace AiDetect.Core.Sources;

/// <summary>
/// MR из локального git-репозитория: диапазон base..head (как ветка MR относительно целевой).
/// Нужен для офлайн-проверок и тестов на публичных репозиториях без GitLab.
/// </summary>
public sealed class LocalGitSource(string repoPath, string baseRef, string headRef, SourceFilePolicy files, SourceLimits? limits = null) : IMergeRequestSource
{
    private const int MaxConfigFiles = 40;
    private readonly SourceLimits _limits = limits ?? new SourceLimits();

    public string? Title { get; init; }
    public string? Description { get; init; }
    public string? Branch { get; init; }

    public Task<MergeRequestData> LoadAsync(CancellationToken cancellationToken = default)
    {
        var head = Git("rev-parse", headRef).Trim();
        var mergeBase = Git("merge-base", baseRef, head).Trim();
        if (mergeBase.Length == 0)
        {
            mergeBase = Git("rev-parse", baseRef).Trim();
        }

        var data = new MergeRequestData
        {
            Source = $"{Path.GetFullPath(repoPath)} {baseRef}..{headRef}",
            BaseSha = mergeBase,
            HeadSha = head,
            SourceBranch = Branch ?? (headRef.Contains('/') || !headRef.All(char.IsLetterOrDigit) ? headRef : ""),
            TargetBranch = baseRef,
            HasMrMetadata = Description is not null,
        };

        data.Commits = LoadCommits(mergeBase, head);
        var last = data.Commits.LastOrDefault();
        data.Title = Title ?? last?.Title ?? "";
        data.Description = Description ?? "";
        data.AuthorName = last?.AuthorName ?? "";
        data.CreatedAt = data.Commits.FirstOrDefault()?.AuthoredDate;

        foreach (var line in Git("diff", "--name-status", "-M", mergeBase, head).Split('\n', StringSplitOptions.RemoveEmptyEntries).Take(_limits.MaxFiles))
        {
            var parts = line.Split('\t');
            var status = parts[0];
            var change = new FileChange();
            if (status.StartsWith('R') && parts.Length >= 3)
            {
                change.OldPath = parts[1];
                change.NewPath = parts[2];
                change.IsRenamed = true;
            }
            else
            {
                change.OldPath = change.NewPath = parts[^1];
                change.IsNew = status == "A";
                change.IsDeleted = status == "D";
            }

            change.Diff = StripHeaders(Git("diff", "-M", "-U3", mergeBase, head, "--", change.IsRenamed ? change.OldPath : change.NewPath, change.NewPath));
            bool interesting = files.NeedsContent(change.Path);
            if (interesting && !change.IsDeleted)
            {
                change.NewContent = Show(head, change.NewPath);
            }

            if (interesting && !change.IsNew && files.NeedsOldContent(change.OldPath))
            {
                change.OldContent = Show(mergeBase, change.OldPath);
            }

            data.Changes.Add(change);
        }

        foreach (var name in Git("ls-tree", "--name-only", head).Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            if (name is "CLAUDE.md" or "CLAUDE.local.md" or ".claude" or "AGENTS.md" or ".mcp.json")
            {
                data.Repository.AgentFilesAtHead.Add(name);
            }

            if (files.IsRootConfigFile(name))
            {
                AddConfig(data, head, name);
            }
        }

        foreach (var name in Run(["ls-tree", "--name-only", head, ".gitlab/merge_request_templates/"]).Output.Split('\n', StringSplitOptions.RemoveEmptyEntries).Take(5))
        {
            AddConfig(data, head, name);
        }

        data.Repository.GitIgnore = Show(head, ".gitignore");
        LoadBaseline(data, mergeBase);
        LoadPackageConfigs(data, mergeBase, head);
        return Task.FromResult(data);
    }

    private List<CommitInfo> LoadCommits(string from, string to)
    {
        const char Sep = '\u001f';
        const char RecordSep = '\u001e';
        var raw = Git("log", "--reverse", $"--format=%H{Sep}%an{Sep}%ae{Sep}%aI{Sep}%cI{Sep}%s{Sep}%B{RecordSep}", $"{from}..{to}");
        var commits = new List<CommitInfo>();
        foreach (var rec in raw.Split(RecordSep, StringSplitOptions.RemoveEmptyEntries))
        {
            var f = rec.TrimStart('\n').Split(Sep);
            if (f.Length < 7)
            {
                continue;
            }

            commits.Add(new CommitInfo
            {
                Sha = f[0],
                AuthorName = f[1],
                AuthorEmail = f[2],
                AuthoredDate = DateTimeOffset.TryParse(f[3], out var a) ? a : null,
                CommittedDate = DateTimeOffset.TryParse(f[4], out var c) ? c : null,
                Title = f[5],
                Message = f[6].Trim(),
            });
        }

        foreach (var commit in commits.Take(_limits.MaxCommitsWithStats))
        {
            int added = 0;
            int removed = 0;
            commit.FileStats = [];
            foreach (var line in Git("show", "--numstat", "--format=", commit.Sha).Split('\n', StringSplitOptions.RemoveEmptyEntries))
            {
                var p = line.Split('\t');
                if (p.Length < 3)
                {
                    continue;
                }

                commit.ChangedPaths.Add(p[2]);
                if (files.IsCodeForSpeed(p[2]) && int.TryParse(p[0], out var ad) && int.TryParse(p[1], out var rm))
                {
                    added += ad;
                    removed += rm;
                    commit.FileStats.Add(new CommitFileStat { Path = p[2], Added = ad, Removed = rm });
                }
            }

            commit.Additions = added;
            commit.Deletions = removed;
        }

        return commits;
    }

    private void LoadBaseline(MergeRequestData data, string baseSha)
    {
        var changed = data.Changes.Select(c => c.Path).ToHashSet(StringComparer.Ordinal);
        foreach (var (language, dirs) in files.BaselineDirectories(data))
        {
            var candidates = new List<string>();
            foreach (var dir in dirs)
            {
                var listing = Git("ls-tree", "--name-only", baseSha, dir.Length == 0 ? "." : dir + "/");
                foreach (var path in listing.Split('\n', StringSplitOptions.RemoveEmptyEntries))
                {
                    if (language.IsProjectFile(Path.GetFileName(path)))
                    {
                        AddConfig(data, baseSha, path);
                    }

                    if (SourceFilePolicy.IsBaselineCandidate(language, path) && !changed.Contains(path) && !candidates.Contains(path))
                    {
                        candidates.Add(path);
                    }
                }

                if (candidates.Count >= _limits.MaxBaselineFiles * 3)
                {
                    break;
                }
            }

            int taken = 0;
            foreach (var path in candidates)
            {
                if (taken >= _limits.MaxBaselineFiles)
                {
                    break;
                }

                var content = Show(baseSha, path);
                if (content is not null && content.Length <= _limits.MaxBaselineFileBytes)
                {
                    data.Repository.BaselineFiles.Add(new SourceFile { Path = path, Content = content });
                    taken++;
                }
            }
        }
    }

    /// <summary>Ближайший к изменённым файлам корень пакета (каталог с package.json) и его конфиги (tsconfig, ESLint, Prettier…).</summary>
    private void LoadPackageConfigs(MergeRequestData data, string baseSha, string head)
    {
        var packageDirs = new List<string>();
        foreach (var (language, ancestors) in files.PackageSearchPaths(data))
        {
            foreach (var dir in ancestors)
            {
                if (packageDirs.Count >= 3)
                {
                    break;
                }

                if (packageDirs.Contains(dir) || packageDirs.Any(p => dir.StartsWith(p + "/", StringComparison.Ordinal)))
                {
                    continue;
                }

                var rev = Run(["cat-file", "-e", $"{baseSha}:{dir}/{language.PackageMarker}"]).Code == 0 ? baseSha
                    : Run(["cat-file", "-e", $"{head}:{dir}/{language.PackageMarker}"]).Code == 0 ? head : null;
                if (rev is null)
                {
                    continue;
                }

                packageDirs.Add(dir);
                foreach (var path in Git("ls-tree", "--name-only", rev, dir + "/").Split('\n', StringSplitOptions.RemoveEmptyEntries))
                {
                    if (language.IsProjectFile(Path.GetFileName(path)) || language.IsRootConfigFile(Path.GetFileName(path)))
                    {
                        AddConfig(data, rev, path);
                    }
                }
            }
        }
    }

    private void AddConfig(MergeRequestData data, string rev, string path)
    {
        if (data.Repository.ConfigFiles.Count < MaxConfigFiles && data.Repository.ConfigFiles.All(f => f.Path != path) && Show(rev, path) is { } content)
        {
            data.Repository.ConfigFiles.Add(new SourceFile { Path = path, Content = content });
        }
    }

    private static string StripHeaders(string diff)
    {
        int idx = diff.IndexOf("\n@@", StringComparison.Ordinal);
        if (diff.StartsWith("@@", StringComparison.Ordinal))
        {
            return diff;
        }

        return idx >= 0 ? diff[(idx + 1)..] : "";
    }

    private string? Show(string rev, string path)
    {
        var (code, output) = Run(["show", $"{rev}:{path}"]);
        if (code != 0 || output.Length > _limits.MaxFileBytes)
        {
            return null;
        }

        return output.TrimStart('﻿');
    }

    private string Git(params string[] args)
    {
        var (code, output) = Run(args);
        if (code != 0)
        {
            throw new SourceException($"git {string.Join(' ', args)} завершился с кодом {code}");
        }

        return output;
    }

    private (int Code, string Output) Run(string[] args)
    {
        var psi = new ProcessStartInfo("git")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            StandardOutputEncoding = Encoding.UTF8,
        };
        psi.ArgumentList.Add("-C");
        psi.ArgumentList.Add(repoPath);
        psi.ArgumentList.Add("-c");
        psi.ArgumentList.Add("core.quotepath=off");
        foreach (var a in args)
        {
            psi.ArgumentList.Add(a);
        }

        using var p = Process.Start(psi) ?? throw new SourceException("Не удалось запустить git. Установлен ли git?");
        var stdout = p.StandardOutput.ReadToEndAsync();
        var stderr = p.StandardError.ReadToEndAsync();
        p.WaitForExit();
        return (p.ExitCode, stdout.Result);
    }
}
