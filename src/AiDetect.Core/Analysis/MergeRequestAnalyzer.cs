using System.Collections.Concurrent;
using AiDetect.Core.Diff;
using AiDetect.Core.Features;
using AiDetect.Core.Languages;
using AiDetect.Core.Model;
using AiDetect.Core.Signals;

namespace AiDetect.Core.Analysis;

/// <summary>
/// Главный конвейер: выбор языка → предобработка файлов MR → признаки по добавленному коду и по коду проекта (детектор языка) →
/// сигналы кода (язык) и MR (общие) по группам → итоговая вероятность и объяснение.
/// </summary>
public sealed class MergeRequestAnalyzer(LanguageRegistry registry)
{
    private const int MaxBaselineLines = 12000;

    private static readonly ConcurrentDictionary<string, ModelParameters> DefaultParameters = new(StringComparer.Ordinal);

    public LanguageRegistry Registry => registry;

    /// <summary>Веса языка по умолчанию (weights.{язык}.json рядом с программой), загружаются один раз.</summary>
    public static ModelParameters ParametersFor(ILanguageAnalyzer language) =>
        DefaultParameters.GetOrAdd(language.Id, id => ModelParameters.LoadDefault(id));

    public AnalysisReport Analyze(MergeRequestData mr, AnalyzerOptions? options = null)
    {
        options ??= new AnalyzerOptions();
        var language = registry.Resolve(options.Language, mr);
        var parameters = options.Parameters ?? ParametersFor(language);
        var ctx = BuildContext(mr, options, language);

        var signals = EvaluateSignals(ctx, parameters);
        var (groups, logit) = Scorer.Combine(signals, parameters);
        var (_, logitNoArtifacts) = Scorer.Combine(signals, parameters, includeArtifacts: false);
        double p = Scorer.Probability(logit, parameters);
        bool artifacts = signals.Any(s => s.Group == SignalGroup.Artifacts && s.Contribution >= 2.5);

        int codeChars = ctx.AnalyzedFiles.Sum(f => f.AddedLines * 40);
        var warnings = new List<string>();
        int added = ctx.AddedProdLines + ctx.AddedTestLines;
        if (added < 60)
        {
            warnings.Add($"Мало добавленного {language.CodeNoun}-кода (< 60 строк): по исследованию надёжность текстового анализа низкая.");
        }

        if (ctx.Baseline is null)
        {
            warnings.Add("Нет базовой линии проекта (не удалось получить существующий код) — «конвенциональные» признаки не скорректированы на стиль команды.");
        }

        if (mr.Commits.Count == 0)
        {
            warnings.Add("Нет информации о коммитах — процессные сигналы не учтены.");
        }

        var (confidence, note) = ConfidenceOf(added, signals, artifacts, ctx.Baseline is not null);
        if (ctx.Conventions.Any)
        {
            warnings.Insert(0, "Учтены конвенции проекта (соответствующие признаки ослаблены): " + string.Join(", ", ctx.Conventions.Notes) + ".");
        }

        var linesByLanguage = registry.AddedLinesByLanguage(mr);
        foreach (var other in registry.Languages.Where(l => l != language && linesByLanguage[l.Id] >= 30))
        {
            warnings.Add($"В MR есть также +{linesByLanguage[other.Id]} строк {other.CodeNoun}-кода — они не анализировались. " +
                         $"Чтобы проверить их, выберите язык «{other.Title}».");
        }

        var stack = DetectStack(language, mr);
        var titles = groups.Select(g => g.Group).Concat(signals.Select(s => s.Group)).Distinct()
            .ToDictionary(g => g, g => language.GroupTitle(g) ?? GroupInfo.Title(g));
        var fileScores = ScoreFiles(ctx, parameters);
        var summary = Explain(p, groups, signals, artifacts, titles);

        return new AnalysisReport
        {
            Summary = summary,
            FileScores = fileScores,
            Source = mr.Source,
            Title = mr.Title,
            Author = string.IsNullOrEmpty(mr.AuthorUsername) ? mr.AuthorName : $"{mr.AuthorName} (@{mr.AuthorUsername})",
            SourceBranch = mr.SourceBranch,
            Probability = p,
            Logit = logit,
            ProbabilityWithoutArtifacts = Scorer.Probability(logitNoArtifacts, parameters),
            ArtifactsFound = artifacts,
            Verdict = added < 30 && !artifacts ? $"Недостаточно {language.CodeNoun}-кода для вывода" : Scorer.Verdict(p),
            Confidence = confidence,
            ConfidenceNote = note,
            Groups = groups,
            Signals = signals,
            FilesChanged = ctx.Files.Count,
            FilesAnalyzed = ctx.AnalyzedFiles.Count(),
            ExcludedFiles = ctx.Files.Where(f => f.Excluded && f.IsSource).Select(f => $"{f.Path} — {f.ExcludeReason}").ToList(),
            AddedCodeLines = ctx.AddedProdLines,
            AddedTestLines = ctx.AddedTestLines,
            CodeChars = codeChars,
            Commits = mr.Commits.Count,
            BaselineFiles = ctx.BaselineFiles,
            Warnings = warnings,
            LanguageId = language.Id,
            LanguageTitle = language.Title,
            CodeNoun = language.CodeNoun,
            LanguageAuto = LanguageRegistry.IsAuto(options.Language),
            Stack = stack.ToString(),
            StackTags = stack.Tags,
            GroupTitles = titles,
        };
    }

    private static StackInfo DetectStack(ILanguageAnalyzer language, MergeRequestData mr)
    {
        try
        {
            return language.DetectStack(mr);
        }
        catch (Exception ex) when (ex is FormatException or InvalidOperationException or System.Text.Json.JsonException or ArgumentException)
        {
            // Стек — справочная строка отчёта, ошибки разбора конфигов не должны мешать анализу.
            return new StackInfo();
        }
    }

    /// <summary>Оценка каждого файла с ≥ 25 добавленными строками только по признакам кода.</summary>
    private static List<FileScore> ScoreFiles(AnalysisContext ctx, ModelParameters parameters)
    {
        var result = new List<FileScore>();
        foreach (var f in ctx.AnalyzedFiles.Where(f => f.Stats is not null && f.AddedLines >= 25))
        {
            var fileCtx = new AnalysisContext
            {
                Mr = ctx.Mr,
                Language = ctx.Language,
                Files = [f],
                Added = f.Stats!,
                Baseline = ctx.Baseline,
                BaselineFiles = ctx.BaselineFiles,
                Options = ctx.Options,
                Conventions = ctx.Conventions,
            };
            var builder = new SignalBuilder(fileCtx, parameters);
            ctx.Language.EvaluateCodeSignals(fileCtx, builder);
            var (_, logit) = Scorer.Combine(builder.Results, parameters, includeArtifacts: false);
            var top = builder.Results.Where(s => s.Contribution > 0.15).OrderByDescending(s => s.Contribution).Take(2).Select(s => s.Title).ToList();
            result.Add(new FileScore(f.Path, f.AddedLines, Scorer.Probability(logit, parameters), top));
        }

        return result.OrderByDescending(r => r.Probability).ToList();
    }

    /// <summary>Короткое объяснение решения: какие группы сигналов перевесили.</summary>
    private static string Explain(double p, List<GroupScore> groups, List<SignalResult> signals, bool artifacts, Dictionary<SignalGroup, string> titles)
    {
        string Title(SignalGroup g) => LowerFirst(titles.TryGetValue(g, out var t) ? t : GroupInfo.Title(g));
        var forAi = groups.Where(g => g.Capped > 0.3).OrderByDescending(g => g.Capped).Take(3).Select(g => Title(g.Group)).ToList();
        var forHuman = groups.Where(g => g.Capped < -0.3).OrderBy(g => g.Capped).Take(3).Select(g => Title(g.Group)).ToList();
        var resistant = groups.Where(g => GroupInfo.IsMimicryResistant(g.Group) && g.Capped > 0.3).Select(g => Title(g.Group)).ToList();
        var parts = new List<string>();
        if (artifacts)
        {
            parts.Add("в MR есть прямые следы Claude Code (подписи/трейлеры/служебные файлы) — это почти доказательство");
        }

        if (forAi.Count > 0)
        {
            parts.Add("за ИИ говорят: " + string.Join(", ", forAi));
        }

        if (forHuman.Count > 0)
        {
            parts.Add("за человека: " + string.Join(", ", forHuman));
        }

        if (resistant.Count > 0 && !artifacts)
        {
            parts.Add("устойчивые к маскировке стиля группы за ИИ: " + string.Join(", ", resistant));
        }

        string lead = p switch
        {
            >= 0.65 => "Код похож на работу Claude Code",
            > 0.35 => "Однозначного вывода нет",
            _ => "Код похож на написанный человеком",
        };
        return parts.Count == 0 ? lead + "." : lead + ": " + string.Join("; ", parts) + ".";
    }

    private static string LowerFirst(string s) =>
        s.Length > 1 && char.IsUpper(s[0]) && !char.IsUpper(s[1]) ? char.ToLowerInvariant(s[0]) + s[1..] : s;

    public static List<SignalResult> EvaluateSignals(AnalysisContext ctx, ModelParameters parameters)
    {
        var builder = new SignalBuilder(ctx, parameters);
        ctx.Language.EvaluateCodeSignals(ctx, builder);
        new MrSignals(ctx, builder).Evaluate();
        return builder.Results;
    }

    public static AnalysisContext BuildContext(MergeRequestData mr, AnalyzerOptions options, ILanguageAnalyzer language)
    {
        var files = new List<FileAnalysis>();
        var added = new CodeStats();
        foreach (var change in mr.Changes)
        {
            var path = change.Path;
            var diff = UnifiedDiff.Parse(change.Diff);
            bool isSource = language.IsSourceFile(path);
            var content = change.NewContent;
            if (content is null && isSource && !change.IsDeleted && !string.IsNullOrEmpty(change.Diff))
            {
                content = UnifiedDiff.ReconstructNewSide(change.Diff);
            }

            int addedLines = diff.Added;
            if (change.IsNew && addedLines == 0 && content is not null)
            {
                addedLines = content.Split('\n').Length;
            }

            var layer = language.ClassifyLayer(path, content);
            if (change.IsDeleted || !isSource)
            {
                files.Add(new FileAnalysis
                {
                    Path = path, Layer = layer, IsSource = isSource, IsDeleted = change.IsDeleted, IsNew = change.IsNew,
                    AddedLines = addedLines, RemovedLines = diff.Removed,
                });
                continue;
            }

            bool generated = language.IsGenerated(path, content);
            if (generated || content is null)
            {
                files.Add(new FileAnalysis
                {
                    Path = path, Layer = layer, IsSource = true, IsNew = change.IsNew, Excluded = true,
                    ExcludeReason = generated ? "сгенерированный файл/миграция" : "нет содержимого", AddedLines = addedLines, RemovedLines = diff.Removed,
                });
                continue;
            }

            bool isTest = language.IsTestFile(path, content);
            bool wholeFile = change.IsNew || (diff.Added == 0 && string.IsNullOrEmpty(change.Diff));
            var addedSet = diff.AddedLines;
            Func<int, bool> scope = wholeFile ? _ => true : addedSet.Contains;
            var stats = language.Extract(path, content, scope, isTest, change.IsNew);
            double weight = language.IsBoilerplateLayer(layer) ? options.BoilerplateWeight : 1.0;
            added.Merge(stats, weight);
            files.Add(new FileAnalysis
            {
                Path = path, Layer = layer, IsSource = true, IsTest = isTest, IsNew = change.IsNew, AddedLines = addedLines,
                RemovedLines = diff.Removed, Weight = weight, Stats = stats,
            });
        }

        var (baseline, baselineFiles) = BuildBaseline(mr, language);
        return new AnalysisContext
        {
            Mr = mr,
            Language = language,
            Files = files,
            Added = added,
            Baseline = baseline,
            BaselineFiles = baselineFiles,
            Options = options,
            Conventions = language.DetectConventions(mr.Repository),
        };
    }

    /// <summary>Норма проекта: старые версии изменённых файлов и соседние файлы каталогов на момент base.</summary>
    private static (CodeStats? Stats, int Files) BuildBaseline(MergeRequestData mr, ILanguageAnalyzer language)
    {
        var stats = new CodeStats();
        int files = 0;
        int lines = 0;
        var changedPaths = mr.Changes.Select(c => c.Path).ToHashSet(StringComparer.Ordinal);

        IEnumerable<(string Path, string Content)> Candidates()
        {
            foreach (var c in mr.Changes)
            {
                if (!c.IsNew && c.OldContent is not null && language.IsSourceFile(c.OldPath))
                {
                    yield return (c.OldPath, c.OldContent);
                }
            }

            foreach (var f in mr.Repository.BaselineFiles)
            {
                if (!changedPaths.Contains(f.Path) && language.IsSourceFile(f.Path))
                {
                    yield return (f.Path, f.Content);
                }
            }
        }

        foreach (var (path, content) in Candidates())
        {
            if (lines >= MaxBaselineLines || language.IsGenerated(path, content))
            {
                continue;
            }

            bool isTest = language.IsTestFile(path, content);
            var s = language.Extract(path, content, _ => true, isTest, isNewFile: true);
            stats.Merge(s);
            files++;
            lines += (int)s[StatKeys.CodeLines];
        }

        return files == 0 || stats[StatKeys.CodeLines] < 30 ? (null, files) : (stats, files);
    }

    private static (string Level, string Note) ConfidenceOf(int addedLines, List<SignalResult> signals, bool artifacts, bool hasBaseline)
    {
        if (artifacts)
        {
            return ("высокая", "найдены прямые следы Claude Code");
        }

        int groups = signals.Where(s => Math.Abs(s.Contribution) > 0.1).Select(s => s.Group).Distinct().Count();
        if (addedLines >= 150 && groups >= 6)
        {
            return ("средняя–высокая", $"{addedLines} строк кода, {groups} независимых групп сигналов" + (hasBaseline ? ", есть сравнение с кодом проекта" : ""));
        }

        if (addedLines >= 60 && groups >= 4)
        {
            return ("средняя", $"{addedLines} строк кода, {groups} групп сигналов");
        }

        return ("низкая", $"мало данных: {addedLines} строк кода, {groups} групп сигналов");
    }
}
