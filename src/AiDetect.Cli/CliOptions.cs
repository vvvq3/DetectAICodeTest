using AiDetect.Core.Analysis;

namespace AiDetect.Cli;

public sealed class CliOptions
{
    public string Command { get; private set; } = "analyze";
    public string? Target { get; private set; }
    public string? Token { get; private set; } = Environment.GetEnvironmentVariable("GITLAB_TOKEN");
    public string? ApiUrl { get; private set; } = Environment.GetEnvironmentVariable("GITLAB_API_URL");
    public bool Insecure { get; private set; } = Environment.GetEnvironmentVariable("GITLAB_INSECURE") is "1" or "true";
    public bool Json { get; private set; }
    public bool Verbose { get; private set; }
    public bool Quiet { get; private set; }
    public bool NoColor { get; private set; }
    public bool NoArtifacts { get; private set; }
    public bool Help { get; private set; }
    public string? HtmlPath { get; private set; }
    public string? SavePath { get; private set; }
    public string? LoadPath { get; private set; }
    public string? WeightsPath { get; private set; }
    public string? OutPath { get; private set; }
    public string? GitRepo { get; private set; }
    public string? GitBase { get; private set; }
    public string? GitHead { get; private set; }
    public string? Title { get; private set; }
    public string? Description { get; private set; }
    public string? Branch { get; private set; }
    public int Port { get; private set; } = 5057;
    public double Lambda { get; private set; } = 4.0;
    public bool Balanced { get; private set; }
    public bool OpenBrowser { get; private set; } = true;

    public static CliOptions Parse(string[] args)
    {
        var o = new CliOptions();
        int i = 0;
        if (args.Length > 0 && args[0] is "web" or "eval" or "calibrate" or "analyze" or "fetch" or "batch")
        {
            o.Command = args[0];
            i = 1;
        }

        for (; i < args.Length; i++)
        {
            var a = args[i];
            string Next() => i + 1 < args.Length ? args[++i] : throw new ArgumentException($"После {a} нужно значение");
            switch (a)
            {
                case "-h" or "--help" or "help":
                    o.Help = true;
                    break;
                case "--token":
                    o.Token = Next();
                    break;
                case "--api-url":
                    o.ApiUrl = Next();
                    break;
                case "--insecure" or "-k":
                    o.Insecure = true;
                    break;
                case "--json":
                    o.Json = true;
                    break;
                case "--verbose" or "-v":
                    o.Verbose = true;
                    break;
                case "--quiet" or "-q":
                    o.Quiet = true;
                    break;
                case "--no-color":
                    o.NoColor = true;
                    break;
                case "--no-artifacts":
                    o.NoArtifacts = true;
                    break;
                case "--html":
                    o.HtmlPath = Next();
                    break;
                case "--save":
                    o.SavePath = Next();
                    break;
                case "--load":
                    o.LoadPath = Next();
                    break;
                case "--weights":
                    o.WeightsPath = Next();
                    break;
                case "--out" or "-o":
                    o.OutPath = Next();
                    break;
                case "--git":
                    o.GitRepo = Next();
                    break;
                case "--base":
                    o.GitBase = Next();
                    break;
                case "--head":
                    o.GitHead = Next();
                    break;
                case "--title":
                    o.Title = Next();
                    break;
                case "--description":
                    o.Description = Next();
                    break;
                case "--branch":
                    o.Branch = Next();
                    break;
                case "--port":
                    o.Port = int.Parse(Next());
                    break;
                case "--lambda":
                    o.Lambda = double.Parse(Next(), System.Globalization.CultureInfo.InvariantCulture);
                    break;
                case "--balanced":
                    o.Balanced = true;
                    break;
                case "--no-browser":
                    o.OpenBrowser = false;
                    break;
                default:
                    if (a.StartsWith('-'))
                    {
                        throw new ArgumentException($"Неизвестный параметр {a}. См. --help");
                    }

                    o.Target ??= a;
                    break;
            }
        }

        return o;
    }

    public AnalyzerOptions ToAnalyzerOptions() => new()
    {
        IgnoreArtifacts = NoArtifacts,
        Parameters = WeightsPath is not null ? ModelParameters.Load(WeightsPath) : null,
    };

    public const string HelpText = """
        aidetect — определяет, написан ли C#-мердж-реквест человеком или Claude Code. Работает локально.

        Использование:
          aidetect <ссылка на MR>                      анализ MR из GitLab
          aidetect web [--port 5057]                   локальная веб-страница с полем для ссылки
          aidetect batch mrs.txt [--html отчёты/]      несколько MR из файла (по ссылке в строке) — сводная таблица
          aidetect --git <репо> --base <ветка> --head <ветка>   анализ диапазона коммитов локального git
          aidetect --load mr.json                      анализ сохранённого MR
          aidetect fetch <ссылка> --save mr.json       только скачать MR в JSON (без анализа)
          aidetect eval <каталог корпуса>              точность на размеченном корпусе (human/, ai/)
          aidetect calibrate <каталог корпуса> -o weights.json   подбор весов на своих размеченных MR

        Параметры:
          --token <токен>     GitLab Personal Access Token (read_api). По умолчанию из GITLAB_TOKEN
          --insecure, -k      не проверять TLS-сертификат GitLab (самоподписанный)
          --api-url <url>     явный адрес API, если GitLab установлен в подкаталоге (https://host/gitlab/api/v4)
          --html <файл>       сохранить HTML-отчёт
          --json              вывести отчёт в JSON
          --save <файл>       сохранить загруженный MR в JSON (для повторного анализа без сети)
          --no-artifacts      не учитывать прямые следы (трейлеры, CLAUDE.md) — проверка только по коду
          --weights <файл>    свои веса модели (после calibrate)
          --verbose, -v       показать все сработавшие признаки
          --no-color          без цветов

        Пример:
          export GITLAB_TOKEN=glpat-xxxxxxxx
          aidetect https://gitlab.company.com/team/project/-/merge_requests/123
        """;
}
