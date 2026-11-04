using System.Text;
using AiDetect.Cli;
using AiDetect.Core.Analysis;
using AiDetect.Core.Model;
using AiDetect.Core.Reporting;
using AiDetect.Core.Sources;

Console.OutputEncoding = Encoding.UTF8;
var opts = CliOptions.Parse(args);
if (opts.Help || (opts.Command is "analyze" or "fetch" or "batch" && opts.Target is null && opts.LoadPath is null && opts.GitRepo is null))
{
    Console.WriteLine(CliOptions.HelpText);
    return opts.Help ? 0 : 1;
}

try
{
    return opts.Command switch
    {
        "web" => await WebServer.RunAsync(opts),
        "eval" => await Evaluation.RunEvalAsync(opts),
        "calibrate" => await Evaluation.RunCalibrateAsync(opts),
        "fetch" => await FetchAsync(opts),
        "batch" => await BatchAsync(opts),
        _ => await AnalyzeAsync(opts),
    };
}
catch (SourceException ex)
{
    Console.Error.WriteLine("Ошибка: " + ex.Message);
    return 2;
}

static async Task<int> BatchAsync(CliOptions o)
{
    if (o.Target is null || !File.Exists(o.Target))
    {
        Console.Error.WriteLine("Укажите файл со ссылками на MR (по одной в строке): aidetect batch mrs.txt");
        return 1;
    }

    var urls = File.ReadAllLines(o.Target).Select(l => l.Trim()).Where(l => l.Length > 0 && !l.StartsWith('#')).ToList();
    var rows = new List<(string Url, AnalysisReport? Report, string? Error)>();
    var htmlDir = o.HtmlPath;
    if (htmlDir is not null)
    {
        Directory.CreateDirectory(htmlDir);
    }

    int i = 0;
    foreach (var url in urls)
    {
        i++;
        Console.Error.WriteLine($"[{i}/{urls.Count}] {url}");
        try
        {
            var data = await LoadAsync(o, url, _ => { });
            var report = new MergeRequestAnalyzer().Analyze(data, o.ToAnalyzerOptions());
            rows.Add((url, report, null));
            if (htmlDir is not null)
            {
                var name = string.Concat(url.Split('/').TakeLast(3)).Replace("-", "") + ".html";
                await File.WriteAllTextAsync(Path.Combine(htmlDir, name), HtmlReportWriter.RenderPage(report));
            }
        }
        catch (Exception ex) when (ex is SourceException or HttpRequestException or TaskCanceledException)
        {
            rows.Add((url, null, ex.Message));
        }
    }

    Console.WriteLine();
    Console.WriteLine($"{"P(ИИ)",6}  {"Вердикт",-46} {"Строк",6}  MR");
    foreach (var (url, report, error) in rows)
    {
        if (report is null)
        {
            Console.WriteLine($"{"—",6}  {"ошибка: " + error,-46}");
            continue;
        }

        Console.WriteLine($"{report.Probability * 100,5:0}%  {report.Verdict,-46} {report.AddedCodeLines + report.AddedTestLines,6}  {url}");
    }

    if (htmlDir is not null)
    {
        Console.WriteLine($"HTML-отчёты: {Path.GetFullPath(htmlDir)}");
    }

    return 0;
}

static async Task<int> FetchAsync(CliOptions o)
{
    var data = await LoadAsync(o, o.Target, msg => { if (!o.Quiet) Console.Error.WriteLine("  " + msg); });
    var path = o.SavePath ?? o.OutPath ?? "mr.json";
    await JsonMergeRequestStore.SaveAsync(data, path);
    Console.WriteLine($"MR сохранён в {path}: {data.Changes.Count} файлов, {data.Commits.Count} коммитов");
    return 0;
}

static async Task<int> AnalyzeAsync(CliOptions o)
{
    var data = await LoadAsync(o, o.Target, msg => { if (!o.Json && !o.Quiet) Console.Error.WriteLine("  " + msg); });
    if (o.SavePath is not null)
    {
        await JsonMergeRequestStore.SaveAsync(data, o.SavePath);
        if (!o.Json)
        {
            Console.Error.WriteLine($"  MR сохранён в {o.SavePath}");
        }
    }

    var analyzerOptions = o.ToAnalyzerOptions();
    var report = new MergeRequestAnalyzer().Analyze(data, analyzerOptions);
    if (o.Json)
    {
        Console.WriteLine(JsonReportWriter.Render(report));
    }
    else
    {
        bool color = !o.NoColor && !Console.IsOutputRedirected && Environment.GetEnvironmentVariable("NO_COLOR") is null;
        Console.WriteLine();
        Console.WriteLine(new ConsoleReportWriter(color, o.Verbose).Render(report));
    }

    if (o.HtmlPath is not null)
    {
        await File.WriteAllTextAsync(o.HtmlPath, HtmlReportWriter.RenderPage(report));
        if (!o.Json)
        {
            Console.WriteLine($"  HTML-отчёт: {Path.GetFullPath(o.HtmlPath)}");
        }
    }

    return 0;
}

static async Task<MergeRequestData> LoadAsync(CliOptions o, string? target, Action<string> log)
{
    if (o.LoadPath is not null)
    {
        return await new JsonMergeRequestStore(o.LoadPath).LoadAsync();
    }

    if (o.GitRepo is not null)
    {
        var source = new LocalGitSource(o.GitRepo, o.GitBase ?? "main", o.GitHead ?? "HEAD")
        {
            Title = o.Title,
            Description = o.Description,
            Branch = o.Branch,
        };
        return await source.LoadAsync();
    }

    if (target is not null && target.EndsWith(".json", StringComparison.OrdinalIgnoreCase) && File.Exists(target))
    {
        return await new JsonMergeRequestStore(target).LoadAsync();
    }

    using var gitlab = new GitLabMergeRequestSource(target!, o.Token, o.Insecure, log: log, apiBaseOverride: o.ApiUrl);
    return await gitlab.LoadAsync();
}
