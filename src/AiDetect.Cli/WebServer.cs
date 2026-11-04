using System.Diagnostics;
using System.Net;
using AiDetect.Core.Analysis;
using AiDetect.Core.Reporting;
using AiDetect.Core.Sources;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace AiDetect.Cli;

/// <summary>Локальный веб-интерфейс: страница с полем для ссылки на MR. Слушает только 127.0.0.1.</summary>
public static class WebServer
{
    public static async Task<int> RunAsync(CliOptions o)
    {
        var builder = WebApplication.CreateSlimBuilder();
        builder.Logging.SetMinimumLevel(LogLevel.Warning);
        builder.WebHost.UseUrls($"http://127.0.0.1:{o.Port}");
        var app = builder.Build();

        app.MapGet("/", () => Results.Content(Page(Form("", false), null), "text/html; charset=utf-8"));
        app.MapPost("/analyze", async (HttpRequest request) =>
        {
            var form = await request.ReadFormAsync();
            string url = form["url"].ToString().Trim();
            string? token = form["token"].ToString() is { Length: > 0 } t ? t : o.Token;
            bool noArtifacts = form["noArtifacts"].ToString() == "on";
            string formHtml = Form(url, noArtifacts);
            try
            {
                using var source = new GitLabMergeRequestSource(url, token, o.Insecure, apiBaseOverride: o.ApiUrl);
                var data = await source.LoadAsync(request.HttpContext.RequestAborted);
                var options = o.ToAnalyzerOptions();
                options.IgnoreArtifacts = noArtifacts;
                var report = new MergeRequestAnalyzer().Analyze(data, options);
                return Results.Content(Page(formHtml, HtmlReportWriter.RenderBody(report)), "text/html; charset=utf-8");
            }
            catch (Exception ex) when (ex is SourceException or HttpRequestException or TaskCanceledException)
            {
                var error = $"<div class=\"card warn\">Ошибка: {HtmlReportWriter.E(ex.Message)}</div>";
                return Results.Content(Page(formHtml, error), "text/html; charset=utf-8");
            }
        });

        var address = $"http://127.0.0.1:{o.Port}/";
        Console.WriteLine($"Веб-интерфейс детектора: {address}  (Ctrl+C — остановить)");
        if (string.IsNullOrEmpty(o.Token))
        {
            Console.WriteLine("Подсказка: задайте GITLAB_TOKEN до запуска или вводите токен в форме.");
        }

        if (o.OpenBrowser)
        {
            TryOpenBrowser(address);
        }

        await app.RunAsync();
        return 0;
    }

    private static string Form(string url, bool noArtifacts) => $"""
        <div class="card"><h1>Детектор ИИ-кода в C# MR</h1>
        <div class="muted" style="margin-bottom:10px">Вставьте ссылку на мердж-реквест GitLab. Анализ выполняется локально, код никуда не отправляется.</div>
        <form method="post" action="/analyze">
          <input type="text" name="url" placeholder="https://gitlab.company.com/group/project/-/merge_requests/123" value="{WebUtility.HtmlEncode(url)}" required>
          <input type="password" name="token" placeholder="GitLab токен (если не задан GITLAB_TOKEN)" style="max-width:260px">
          <button type="submit">Проверить</button>
          <label style="flex-basis:100%"><input type="checkbox" name="noArtifacts" {(noArtifacts ? "checked" : "")}> оценивать только по коду и процессу (без трейлеров, CLAUDE.md и подписей)</label>
        </form>
        </div>
        """;

    private static string Page(string form, string? body) =>
        "<!doctype html><html lang=\"ru\"><head><meta charset=\"utf-8\"><meta name=\"viewport\" content=\"width=device-width,initial-scale=1\">" +
        "<title>Детектор ИИ-кода</title><style>" + HtmlReportWriter.Styles + "</style></head><body><main>" + form + (body ?? "") +
        "</main></body></html>";

    private static void TryOpenBrowser(string url)
    {
        try
        {
            if (OperatingSystem.IsMacOS())
            {
                Process.Start("open", url);
            }
            else if (OperatingSystem.IsWindows())
            {
                Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
            }
        }
        catch (Exception)
        {
            // Браузер не открылся — адрес напечатан в консоли.
        }
    }
}
