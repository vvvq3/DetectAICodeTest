using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.RegularExpressions;
using AiDetect.Core.Diff;
using AiDetect.Core.Model;
using AiDetect.Core.Preprocessing;

namespace AiDetect.Core.Sources;

public sealed record GitLabMrUrl(string BaseUrl, string ProjectPath, int Iid)
{
    public string ApiBase => BaseUrl.TrimEnd('/') + "/api/v4";

    public string EncodedProject => Uri.EscapeDataString(ProjectPath);
}

/// <summary>
/// Загрузка MR через GitLab REST API v4: метаданные, коммиты (со статистикой строк), диффы,
/// содержимое файлов до/после, служебные файлы агента в репозитории и соседние файлы для базовой линии.
/// Работает с любым GitLab (собственный сервер или gitlab.com). Токен — заголовок PRIVATE-TOKEN (право read_api).
/// </summary>
public sealed partial class GitLabMergeRequestSource : IMergeRequestSource, IDisposable
{
    private readonly GitLabMrUrl _url;
    private readonly HttpClient _http;
    private readonly SourceLimits _limits;
    private readonly Action<string>? _log;

    public GitLabMergeRequestSource(string mrUrl, string? token, bool insecure = false, SourceLimits? limits = null, Action<string>? log = null, string? apiBaseOverride = null)
    {
        _url = ParseUrl(mrUrl);
        if (!string.IsNullOrWhiteSpace(apiBaseOverride))
        {
            _url = _url with { BaseUrl = apiBaseOverride.TrimEnd('/').Replace("/api/v4", "", StringComparison.Ordinal) };
        }

        _limits = limits ?? new SourceLimits();
        _log = log;
        var handler = new HttpClientHandler { AutomaticDecompression = DecompressionMethods.All };
        if (insecure)
        {
            handler.ServerCertificateCustomValidationCallback = HttpClientHandler.DangerousAcceptAnyServerCertificateValidator;
        }

        _http = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(60) };
        _http.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("aidetect", "0.1"));
        if (!string.IsNullOrWhiteSpace(token))
        {
            _http.DefaultRequestHeaders.Add("PRIVATE-TOKEN", token);
        }
    }

    [GeneratedRegex(@"^(?<base>https?://[^/]+)/(?<project>.+?)/(?:-/)?merge_requests/(?<iid>\d+)", RegexOptions.IgnoreCase)]
    private static partial Regex MrUrlRegex();

    public static GitLabMrUrl ParseUrl(string url)
    {
        var m = MrUrlRegex().Match(url.Trim());
        if (!m.Success)
        {
            throw new SourceException($"Не похоже на ссылку на MR GitLab: {url}. Ожидается вида https://gitlab.example.com/group/project/-/merge_requests/123");
        }

        return new GitLabMrUrl(m.Groups["base"].Value, m.Groups["project"].Value.Trim('/'), int.Parse(m.Groups["iid"].Value));
    }

    public async Task<MergeRequestData> LoadAsync(CancellationToken cancellationToken = default)
    {
        var mrPath = $"/projects/{_url.EncodedProject}/merge_requests/{_url.Iid}";
        _log?.Invoke($"Загружаю MR !{_url.Iid} из {_url.ProjectPath}…");
        using var mrDoc = await GetJsonAsync(mrPath, cancellationToken);
        var mrJson = mrDoc.RootElement;

        var data = new MergeRequestData
        {
            Source = $"{_url.BaseUrl}/{_url.ProjectPath}/-/merge_requests/{_url.Iid}",
            Title = Str(mrJson, "title"),
            Description = Str(mrJson, "description"),
            AuthorName = mrJson.TryGetProperty("author", out var author) && author.ValueKind == JsonValueKind.Object ? Str(author, "name") : "",
            AuthorUsername = author.ValueKind == JsonValueKind.Object ? Str(author, "username") : "",
            SourceBranch = Str(mrJson, "source_branch"),
            TargetBranch = Str(mrJson, "target_branch"),
            CreatedAt = Date(mrJson, "created_at"),
            MergedAt = Date(mrJson, "merged_at"),
        };
        if (mrJson.TryGetProperty("diff_refs", out var refs) && refs.ValueKind == JsonValueKind.Object)
        {
            data.BaseSha = Str(refs, "base_sha");
            data.HeadSha = Str(refs, "head_sha");
        }

        data.HeadSha ??= Str(mrJson, "sha");

        _log?.Invoke("Загружаю коммиты…");
        data.Commits = await LoadCommitsAsync(mrPath, cancellationToken);

        _log?.Invoke("Загружаю изменения…");
        data.Changes = await LoadChangesAsync(mrPath, cancellationToken);
        await LoadContentsAsync(data, cancellationToken);
        await LoadCommitStatsAsync(data, cancellationToken);
        await LoadRepositoryContextAsync(data, cancellationToken);
        return data;
    }

    private async Task<List<CommitInfo>> LoadCommitsAsync(string mrPath, CancellationToken ct)
    {
        var commits = new List<CommitInfo>();
        foreach (var c in await GetPagedAsync($"{mrPath}/commits", ct))
        {
            commits.Add(new CommitInfo
            {
                Sha = Str(c, "id"),
                Title = Str(c, "title"),
                Message = Str(c, "message"),
                AuthorName = Str(c, "author_name"),
                AuthorEmail = Str(c, "author_email"),
                AuthoredDate = Date(c, "authored_date"),
                CommittedDate = Date(c, "committed_date"),
            });
        }

        // GitLab отдаёт коммиты от новых к старым.
        commits.Reverse();
        return commits;
    }

    private async Task<List<FileChange>> LoadChangesAsync(string mrPath, CancellationToken ct)
    {
        List<JsonElement> items;
        try
        {
            items = await GetPagedAsync($"{mrPath}/diffs", ct);
        }
        catch (SourceException ex) when (ex.Message.Contains("404", StringComparison.Ordinal))
        {
            // Старые версии GitLab (< 15.7): /changes.
            using var doc = await GetJsonAsync($"{mrPath}/changes?access_raw_diffs=true", ct);
            items = doc.RootElement.GetProperty("changes").EnumerateArray().Select(e => e.Clone()).ToList();
        }

        return items.Take(_limits.MaxFiles).Select(e => new FileChange
        {
            OldPath = Str(e, "old_path"),
            NewPath = Str(e, "new_path"),
            IsNew = Bool(e, "new_file"),
            IsDeleted = Bool(e, "deleted_file"),
            IsRenamed = Bool(e, "renamed_file"),
            Diff = Str(e, "diff"),
        }).ToList();
    }

    private const int Parallelism = 8;

    private async Task LoadContentsAsync(MergeRequestData data, CancellationToken ct)
    {
        int i = 0;
        var relevant = data.Changes.Where(c => FileClassifier.IsCSharp(c.Path) || c.Path.EndsWith(".md", StringComparison.OrdinalIgnoreCase)).ToList();
        _log?.Invoke($"Загружаю содержимое {relevant.Count} файлов…");
        await Parallel.ForEachAsync(relevant, new ParallelOptions { MaxDegreeOfParallelism = Parallelism, CancellationToken = ct }, async (change, token) =>
        {
            int n = Interlocked.Increment(ref i);
            if (n % 20 == 0)
            {
                _log?.Invoke($"  файлы {n}/{relevant.Count}");
            }

            await LoadChangeContentAsync(data, change, token);
        });
    }

    private async Task LoadChangeContentAsync(MergeRequestData data, FileChange change, CancellationToken ct)
    {
        {
            if (!change.IsDeleted && data.HeadSha is not null)
            {
                change.NewContent = await GetRawFileAsync(change.NewPath, data.HeadSha, ct);
            }

            if (!change.IsNew && data.BaseSha is not null && FileClassifier.IsCSharp(change.OldPath))
            {
                change.OldContent = await GetRawFileAsync(change.OldPath, data.BaseSha, ct);
            }

            if (string.IsNullOrEmpty(change.Diff) && change.NewContent is not null)
            {
                // Слишком большой дифф GitLab не отдаёт — считаем сами.
                change.Diff = LineDiff.BuildUnifiedDiff(change.IsNew ? "" : change.OldContent, change.NewContent);
            }
        }
    }

    private async Task LoadCommitStatsAsync(MergeRequestData data, CancellationToken ct)
    {
        var commits = data.Commits.Take(_limits.MaxCommitsWithStats).ToList();
        if (commits.Count < 2)
        {
            // Для одного коммита скорость не считается — экономим запросы.
            return;
        }

        _log?.Invoke($"Статистика по {commits.Count} коммитам…");
        await Parallel.ForEachAsync(commits, new ParallelOptions { MaxDegreeOfParallelism = Parallelism, CancellationToken = ct }, async (commit, token) =>
        {
            await LoadCommitStatAsync(commit, token);
        });
    }

    private async Task LoadCommitStatAsync(CommitInfo commit, CancellationToken ct)
    {
        {
            try
            {
                var diffs = await GetPagedAsync($"/projects/{_url.EncodedProject}/repository/commits/{commit.Sha}/diff", ct);
                int added = 0;
                int removed = 0;
                foreach (var d in diffs)
                {
                    var path = Str(d, "new_path");
                    commit.ChangedPaths.Add(path);
                    if (!IsCodeForSpeed(path))
                    {
                        continue;
                    }

                    var stats = UnifiedDiff.Parse(Str(d, "diff"));
                    added += stats.AddedText.Count(l => l.Trim().Length > 0);
                    removed += stats.Removed;
                }

                commit.Additions = added;
                commit.Deletions = removed;
            }
            catch (SourceException)
            {
                // Статистика коммита необязательна.
            }
        }
    }

    internal static bool IsCodeForSpeed(string path) =>
        FileClassifier.IsCSharp(path) && !FileClassifier.IsGenerated(path, null);

    private async Task LoadRepositoryContextAsync(MergeRequestData data, CancellationToken ct)
    {
        _log?.Invoke("Контекст репозитория и базовая линия стиля…");
        var head = data.HeadSha;
        if (head is not null)
        {
            try
            {
                foreach (var e in await GetPagedAsync($"/projects/{_url.EncodedProject}/repository/tree?ref={Uri.EscapeDataString(head)}", ct, maxPages: 3))
                {
                    var name = Str(e, "name");
                    if (name is "CLAUDE.md" or "CLAUDE.local.md" or ".claude" or "AGENTS.md" or ".mcp.json")
                    {
                        data.Repository.AgentFilesAtHead.Add(name);
                    }
                }

                data.Repository.GitIgnore = await GetRawFileAsync(".gitignore", head, ct);
            }
            catch (SourceException)
            {
                // Нет доступа к дереву — не критично.
            }
        }

        var baseRef = data.BaseSha;
        if (baseRef is null)
        {
            return;
        }

        var changed = data.Changes.Select(c => c.Path).ToHashSet(StringComparer.Ordinal);
        var candidates = new List<string>();
        foreach (var dir in BaselineSelection.CandidateDirectories(data))
        {
            if (candidates.Count >= _limits.MaxBaselineFiles * 3)
            {
                break;
            }

            try
            {
                var query = $"/projects/{_url.EncodedProject}/repository/tree?ref={Uri.EscapeDataString(baseRef)}" +
                            (dir.Length > 0 ? $"&path={Uri.EscapeDataString(dir)}" : "");
                foreach (var e in await GetPagedAsync(query, ct, maxPages: 2))
                {
                    var path = Str(e, "path");
                    if (Str(e, "type") == "blob" && BaselineSelection.IsBaselineCandidate(path) && !changed.Contains(path) && !candidates.Contains(path))
                    {
                        candidates.Add(path);
                    }
                }
            }
            catch (SourceException)
            {
                // Каталога могло не быть на base (новый модуль) — пробуем следующий.
            }
        }

        // Ближайшие к изменённым файлам каталоги идут первыми; загружаем параллельно и сохраняем исходный порядок.
        var picked = candidates.Take(_limits.MaxBaselineFiles + 4).ToList();
        var loaded = new string?[picked.Count];
        await Parallel.ForEachAsync(Enumerable.Range(0, picked.Count), new ParallelOptions { MaxDegreeOfParallelism = Parallelism, CancellationToken = ct }, async (idx, token) =>
        {
            loaded[idx] = await GetRawFileAsync(picked[idx], baseRef, token);
        });

        for (int idx = 0; idx < picked.Count && data.Repository.BaselineFiles.Count < _limits.MaxBaselineFiles; idx++)
        {
            if (loaded[idx] is { } content && content.Length <= _limits.MaxBaselineFileBytes)
            {
                data.Repository.BaselineFiles.Add(new SourceFile { Path = picked[idx], Content = content });
            }
        }
    }

    // ------------------------------------------------------------------ HTTP

    private async Task<string?> GetRawFileAsync(string path, string reference, CancellationToken ct)
    {
        var url = $"{_url.ApiBase}/projects/{_url.EncodedProject}/repository/files/{Uri.EscapeDataString(path)}/raw?ref={Uri.EscapeDataString(reference)}";
        using var resp = await SendAsync(url, ct);
        if (resp.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }

        if (!resp.IsSuccessStatusCode)
        {
            return null;
        }

        var bytes = await resp.Content.ReadAsByteArrayAsync(ct);
        if (bytes.Length > _limits.MaxFileBytes)
        {
            return null;
        }

        return System.Text.Encoding.UTF8.GetString(bytes).TrimStart('﻿');
    }

    private async Task<JsonDocument> GetJsonAsync(string relative, CancellationToken ct)
    {
        var url = _url.ApiBase + relative;
        using var resp = await SendAsync(url, ct);
        await EnsureSuccessAsync(resp, relative, ct);
        var stream = await resp.Content.ReadAsStreamAsync(ct);
        return await JsonDocument.ParseAsync(stream, cancellationToken: ct);
    }

    private async Task<List<JsonElement>> GetPagedAsync(string relative, CancellationToken ct, int maxPages = 20)
    {
        var result = new List<JsonElement>();
        for (int page = 1; page <= maxPages; page++)
        {
            var sep = relative.Contains('?') ? '&' : '?';
            var url = $"{_url.ApiBase}{relative}{sep}per_page=100&page={page}";
            using var resp = await SendAsync(url, ct);
            await EnsureSuccessAsync(resp, relative, ct);
            using var doc = await JsonDocument.ParseAsync(await resp.Content.ReadAsStreamAsync(ct), cancellationToken: ct);
            if (doc.RootElement.ValueKind != JsonValueKind.Array)
            {
                break;
            }

            int count = 0;
            foreach (var e in doc.RootElement.EnumerateArray())
            {
                result.Add(e.Clone());
                count++;
            }

            var next = resp.Headers.TryGetValues("X-Next-Page", out var values) ? values.FirstOrDefault() : null;
            if (count < 100 && string.IsNullOrEmpty(next))
            {
                break;
            }

            if (next is not null && next.Length == 0)
            {
                break;
            }
        }

        return result;
    }

    private async Task<HttpResponseMessage> SendAsync(string url, CancellationToken ct)
    {
        for (int attempt = 1; ; attempt++)
        {
            try
            {
                var resp = await _http.GetAsync(url, ct);
                if ((int)resp.StatusCode == 429 || (int)resp.StatusCode >= 500 && attempt < 3)
                {
                    resp.Dispose();
                    await Task.Delay(TimeSpan.FromSeconds(attempt * 2), ct);
                    continue;
                }

                return resp;
            }
            catch (HttpRequestException ex) when (attempt < 3)
            {
                _log?.Invoke($"Сетевая ошибка ({ex.Message}), повтор…");
                await Task.Delay(TimeSpan.FromSeconds(attempt * 2), ct);
            }
            catch (HttpRequestException ex)
            {
                throw new SourceException($"Не удалось подключиться к {_url.BaseUrl}: {ex.Message}. Проверьте адрес, VPN, сертификат (флаг --insecure для самоподписанного).", ex);
            }
        }
    }

    private static async Task EnsureSuccessAsync(HttpResponseMessage resp, string what, CancellationToken ct)
    {
        if (resp.IsSuccessStatusCode)
        {
            return;
        }

        var body = await resp.Content.ReadAsStringAsync(ct);
        if (body.Length > 300)
        {
            body = body[..300];
        }

        throw resp.StatusCode switch
        {
            HttpStatusCode.Unauthorized => new SourceException("GitLab ответил 401: нужен токен. Задайте GITLAB_TOKEN (Personal Access Token с правом read_api) или --token."),
            HttpStatusCode.Forbidden => new SourceException($"GitLab ответил 403 на {what}: у токена нет прав (нужно read_api) или нет доступа к проекту."),
            HttpStatusCode.NotFound => new SourceException($"GitLab ответил 404 на {what}: MR/проект не найден или нет доступа (у приватных проектов без токена тоже 404)."),
            _ => new SourceException($"GitLab ответил {(int)resp.StatusCode} на {what}: {body}"),
        };
    }

    private static string Str(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : "";

    private static bool Bool(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.True;

    private static DateTimeOffset? Date(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String && DateTimeOffset.TryParse(v.GetString(), out var d) ? d : null;

    public void Dispose() => _http.Dispose();
}
