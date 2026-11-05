using System.Diagnostics;
using System.Globalization;
using AiDetect.Core.Analysis;
using AiDetect.Core.Reporting;
using AiDetect.Core.Sources;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace AiDetect.Cli;

/// <summary>
/// Локальный веб-интерфейс: одна страница с полем для ссылки на MR. Слушает только 127.0.0.1.
/// Анализ запускается фоновым запросом, поэтому токен и галочка остаются на странице между проверками.
/// </summary>
public static class WebServer
{
    public static async Task<int> RunAsync(CliOptions o)
    {
        var builder = WebApplication.CreateSlimBuilder();
        builder.Logging.SetMinimumLevel(LogLevel.Warning);
        builder.WebHost.UseUrls($"http://127.0.0.1:{o.Port}");
        var app = builder.Build();

        bool serverHasToken = !string.IsNullOrEmpty(o.Token);
        app.MapGet("/", () => Results.Content(Page(serverHasToken), "text/html; charset=utf-8"));
        app.MapPost("/api/analyze", async (HttpContext http) =>
        {
            var form = await http.Request.ReadFormAsync();
            string url = form["url"].ToString().Trim();
            string? token = form["token"].ToString().Trim() is { Length: > 0 } t ? t : o.Token;
            bool noArtifacts = form["noArtifacts"].ToString() == "on";
            try
            {
                using var source = new GitLabMergeRequestSource(url, token, o.Insecure, apiBaseOverride: o.ApiUrl);
                var data = await source.LoadAsync(http.RequestAborted);
                var options = o.ToAnalyzerOptions();
                options.IgnoreArtifacts = noArtifacts;
                var report = new MergeRequestAnalyzer().Analyze(data, options);

                // Краткие сведения для списка проверенных MR (кириллица — в URL-кодировке).
                http.Response.Headers["X-Probability"] = Math.Round(report.Probability * 100).ToString(CultureInfo.InvariantCulture);
                http.Response.Headers["X-Verdict"] = Uri.EscapeDataString(report.Verdict);
                http.Response.Headers["X-Title"] = Uri.EscapeDataString(report.Title);
                return Results.Content(HtmlReportWriter.RenderBody(report), "text/html; charset=utf-8");
            }
            catch (Exception ex) when (ex is not OperationCanceledException || !http.RequestAborted.IsCancellationRequested)
            {
                var message = ex is SourceException or HttpRequestException or TaskCanceledException
                    ? ex.Message
                    : $"{ex.GetType().Name}: {ex.Message}";
                return Results.Text(message, "text/plain; charset=utf-8", statusCode: StatusCodes.Status400BadRequest);
            }
        });

        var address = $"http://127.0.0.1:{o.Port}/";
        Console.WriteLine($"Веб-интерфейс детектора: {address}  (Ctrl+C — остановить)");
        if (!serverHasToken)
        {
            Console.WriteLine("Токен GitLab введите один раз на странице (или задайте GITLAB_TOKEN до запуска).");
        }

        if (o.OpenBrowser)
        {
            TryOpenBrowser(address);
        }

        await app.RunAsync();
        return 0;
    }

    private const string ExtraStyles = """
        .row{display:flex;gap:8px;flex-wrap:wrap;align-items:center}
        button:disabled{opacity:.6;cursor:progress}
        .busy{display:none;align-items:center;gap:14px}
        .busy.on{display:flex}
        .spinner{width:26px;height:26px;border:3px solid var(--line);border-top-color:var(--accent);border-radius:50%;animation:spin .8s linear infinite;flex:none}
        @keyframes spin{to{transform:rotate(360deg)}}
        #result.dim{opacity:.35;pointer-events:none;transition:opacity .2s}
        .hist{list-style:none;margin:0;padding:0}
        .hist li{display:flex;gap:10px;align-items:baseline;padding:6px 0;border-top:1px solid var(--line);cursor:pointer}
        .hist li:first-child{border-top:0}
        .hist li.active .t{font-weight:600}
        .hist .p{min-width:44px;font-weight:700;font-variant-numeric:tabular-nums}
        .hist .u{color:var(--muted);font-size:13px;word-break:break-all}
        .tokenok{font-size:13px;color:var(--human)}
        """;

    private static string Page(bool serverHasToken)
    {
        string tokenPlaceholder = serverHasToken ? "токен уже задан через GITLAB_TOKEN" : "GitLab токен (read_api), вводится один раз";
        return "<!doctype html><html lang=\"ru\"><head><meta charset=\"utf-8\"><meta name=\"viewport\" content=\"width=device-width,initial-scale=1\">" +
               "<title>Детектор ИИ-кода</title><style>" + HtmlReportWriter.Styles + ExtraStyles + "</style></head><body><main>" +
               $$"""
               <div class="card"><h1>Детектор ИИ-кода в C# MR</h1>
               <div class="muted" style="margin-bottom:10px">Вставьте ссылку на мердж-реквест GitLab и нажмите «Проверить». Токен и настройки сохраняются, пока открыта вкладка. Анализ выполняется локально, код никуда не отправляется.</div>
               <form id="f" autocomplete="off">
                 <div class="row">
                   <input type="text" id="url" name="url" placeholder="https://gitlab.company.com/group/project/-/merge_requests/123" required autofocus style="flex:3 1 420px">
                   <input type="password" id="token" name="token" placeholder="{{tokenPlaceholder}}" style="flex:1 1 200px;max-width:280px" autocomplete="off">
                   <button type="submit" id="go">Проверить</button>
                 </div>
                 <div class="row" style="margin-top:8px;justify-content:space-between">
                   <label><input type="checkbox" id="noArtifacts" name="noArtifacts"> оценивать только по коду и процессу (без трейлеров, CLAUDE.md и подписей)</label>
                   <span id="tokenState" class="tokenok"></span>
                 </div>
               </form>
               </div>
               <div class="card busy" id="busy"><div class="spinner"></div><div><b>Анализирую MR…</b> <span id="elapsed" class="muted"></span><div class="muted" id="busyUrl" style="word-break:break-all"></div><div class="muted" style="font-size:13px">Загружаю MR, коммиты и файлы из GitLab, затем разбираю код. Обычно это занимает от нескольких секунд до минуты.</div></div></div>
               <div class="card" id="histCard" style="display:none"><div class="muted" style="margin-bottom:6px">Проверено в этой сессии (нажмите, чтобы открыть отчёт):</div><ul class="hist" id="hist"></ul></div>
               <div id="result"></div>
               """ +
               "<script>" + Script + "</script></main></body></html>";
    }

    private const string Script = """
        (() => {
          const $ = id => document.getElementById(id);
          const form = $('f'), url = $('url'), token = $('token'), cb = $('noArtifacts'), go = $('go');
          const busy = $('busy'), elapsed = $('elapsed'), result = $('result'), hist = $('hist'), histCard = $('histCard');
          const history = [];
          const store = (kind, key, value) => { try { kind.setItem(key, value); } catch (e) { } };
          const load = (kind, key) => { try { return kind.getItem(key); } catch (e) { return null; } };
          const esc = s => s.replace(/[&<>"']/g, c => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' }[c]));
          const dec = s => { try { return decodeURIComponent(s || ''); } catch (e) { return s || ''; } };

          // Токен живёт, пока открыта вкладка (sessionStorage), галочка запоминается (localStorage).
          token.value = load(sessionStorage, 'aidetect.token') || '';
          cb.checked = load(localStorage, 'aidetect.noArtifacts') === '1';
          const showTokenState = () => { $('tokenState').textContent = token.value ? '✓ токен сохранён до закрытия вкладки' : ''; };
          showTokenState();
          token.addEventListener('input', () => { store(sessionStorage, 'aidetect.token', token.value.trim()); showTokenState(); });
          cb.addEventListener('change', () => store(localStorage, 'aidetect.noArtifacts', cb.checked ? '1' : '0'));

          let timer = null;
          const setBusy = (on, mr) => {
            go.disabled = on;
            go.textContent = on ? 'Проверяю…' : 'Проверить';
            busy.classList.toggle('on', on);
            result.classList.toggle('dim', on);
            clearInterval(timer);
            if (on) {
              const started = Date.now();
              $('busyUrl').textContent = mr;
              elapsed.textContent = '0 с';
              timer = setInterval(() => { elapsed.textContent = Math.round((Date.now() - started) / 1000) + ' с'; }, 500);
            }
          };

          const renderHistory = active => {
            histCard.style.display = history.length ? '' : 'none';
            hist.innerHTML = history.map((h, i) =>
              `<li data-i="${i}" class="${i === active ? 'active' : ''}"><span class="p" style="color:${h.p >= 65 ? 'var(--ai)' : h.p > 35 ? 'var(--mid)' : 'var(--human)'}">${h.p}%</span>` +
              `<span><span class="t">${esc(h.title || h.url)}</span> <span class="muted">— ${esc(h.verdict)}</span><div class="u">${esc(h.url)}</div></span></li>`).join('');
          };
          hist.addEventListener('click', e => {
            const li = e.target.closest('li');
            if (!li) return;
            const i = Number(li.dataset.i);
            result.innerHTML = history[i].html;
            renderHistory(i);
          });

          form.addEventListener('submit', async e => {
            e.preventDefault();
            const mr = url.value.trim();
            if (!mr || go.disabled) return;
            setBusy(true, mr);
            try {
              const body = new URLSearchParams({ url: mr, token: token.value.trim(), noArtifacts: cb.checked ? 'on' : '' });
              const resp = await fetch('/api/analyze', { method: 'POST', body });
              const text = await resp.text();
              if (resp.ok) {
                result.innerHTML = text;
                history.unshift({ url: mr, html: text, p: Number(resp.headers.get('X-Probability')), verdict: dec(resp.headers.get('X-Verdict')), title: dec(resp.headers.get('X-Title')) });
                renderHistory(0);
                url.value = '';
              } else {
                result.innerHTML = '<div class="card warn">Ошибка: ' + esc(text) + '</div>';
              }
            } catch (err) {
              result.innerHTML = '<div class="card warn">Нет связи с программой: возможно, она остановлена в терминале. Запустите её снова и обновите страницу.</div>';
            } finally {
              setBusy(false);
              url.focus();
            }
          });
        })();
        """;

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
