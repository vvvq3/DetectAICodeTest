using AiDetect.Core.Diff;
using AiDetect.Core.Features;
using AiDetect.Core.Model;
using AiDetect.Core.Preprocessing;
using AiDetect.Core.Signals;

namespace AiDetect.Core.Analysis;

/// <summary>
/// Главный конвейер: предобработка файлов MR → признаки Roslyn по добавленному коду и по коду проекта →
/// сигналы по группам → итоговая вероятность и объяснение.
/// </summary>
public sealed class MergeRequestAnalyzer
{
    private const int MaxBaselineLines = 12000;

    public AnalysisReport Analyze(MergeRequestData mr, AnalyzerOptions? options = null)
    {
        options ??= new AnalyzerOptions();
        var parameters = options.Parameters ?? ModelParameters.LoadDefault();
        var ctx = BuildContext(mr, options);

        var signals = EvaluateSignals(ctx, parameters);
        var (groups, logit) = Scorer.Combine(signals, parameters);
        var (_, logitNoArtifacts) = Scorer.Combine(signals, parameters, includeArtifacts: false);
        double p = Scorer.Probability(logit, parameters);
        bool artifacts = signals.Any(s => s.Group == SignalGroup.Artifacts && s.Contribution >= 2.5);

        int codeChars = ctx.AnalyzedCs.Sum(f => f.AddedLines * 40);
        var warnings = new List<string>();
        int added = ctx.AddedProdLines + ctx.AddedTestLines;
        if (added < 60)
        {
            warnings.Add("Мало добавленного C#-кода (< 60 строк): по исследованию надёжность текстового анализа низкая.");
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

        return new AnalysisReport
        {
            Source = mr.Source,
            Title = mr.Title,
            Author = string.IsNullOrEmpty(mr.AuthorUsername) ? mr.AuthorName : $"{mr.AuthorName} (@{mr.AuthorUsername})",
            SourceBranch = mr.SourceBranch,
            Probability = p,
            Logit = logit,
            ProbabilityWithoutArtifacts = Scorer.Probability(logitNoArtifacts, parameters),
            ArtifactsFound = artifacts,
            Verdict = Scorer.Verdict(p),
            Confidence = confidence,
            ConfidenceNote = note,
            Groups = groups,
            Signals = signals,
            FilesChanged = ctx.Files.Count,
            FilesAnalyzed = ctx.AnalyzedCs.Count(),
            ExcludedFiles = ctx.Files.Where(f => f.Excluded && f.IsCSharp).Select(f => $"{f.Path} — {f.ExcludeReason}").ToList(),
            AddedCodeLines = ctx.AddedProdLines,
            AddedTestLines = ctx.AddedTestLines,
            CodeChars = codeChars,
            Commits = mr.Commits.Count,
            BaselineFiles = ctx.BaselineFiles,
            Warnings = warnings,
        };
    }

    public static List<SignalResult> EvaluateSignals(AnalysisContext ctx, ModelParameters parameters)
    {
        var builder = new SignalBuilder(ctx, parameters);
        new CodeSignals(ctx, builder).Evaluate();
        new MrSignals(ctx, builder).Evaluate();
        return builder.Results;
    }

    public static AnalysisContext BuildContext(MergeRequestData mr, AnalyzerOptions options)
    {
        var files = new List<FileAnalysis>();
        var added = new CodeStats();
        foreach (var change in mr.Changes)
        {
            var path = change.Path;
            var diff = UnifiedDiff.Parse(change.Diff);
            bool isCs = FileClassifier.IsCSharp(path);
            var content = change.NewContent;
            if (content is null && isCs && !change.IsDeleted && !string.IsNullOrEmpty(change.Diff))
            {
                content = UnifiedDiff.ReconstructNewSide(change.Diff);
            }

            int addedLines = diff.Added;
            if (change.IsNew && addedLines == 0 && content is not null)
            {
                addedLines = content.Split('\n').Length;
            }

            var layer = FileClassifier.ClassifyLayer(path, content);
            if (change.IsDeleted || !isCs)
            {
                files.Add(new FileAnalysis
                {
                    Path = path, Layer = layer, IsCSharp = isCs, IsDeleted = change.IsDeleted, IsNew = change.IsNew,
                    AddedLines = addedLines, RemovedLines = diff.Removed,
                });
                continue;
            }

            bool generated = FileClassifier.IsGenerated(path, content);
            if (generated || content is null)
            {
                files.Add(new FileAnalysis
                {
                    Path = path, Layer = layer, IsCSharp = true, IsNew = change.IsNew, Excluded = true,
                    ExcludeReason = generated ? "сгенерированный файл/миграция" : "нет содержимого", AddedLines = addedLines, RemovedLines = diff.Removed,
                });
                continue;
            }

            bool isTest = FileClassifier.IsTestFile(path, content);
            bool wholeFile = change.IsNew || (diff.Added == 0 && string.IsNullOrEmpty(change.Diff));
            var addedSet = diff.AddedLines;
            Func<int, bool> scope = wholeFile ? _ => true : addedSet.Contains;
            var stats = CSharpFeatureExtractor.Extract(path, content, scope, isTest, change.IsNew);
            double weight = layer is Layer.Contracts or Layer.Options or Layer.DependencyInjection or Layer.Migration ? options.BoilerplateWeight : 1.0;
            added.Merge(stats, weight);
            files.Add(new FileAnalysis
            {
                Path = path, Layer = layer, IsCSharp = true, IsTest = isTest, IsNew = change.IsNew, AddedLines = addedLines,
                RemovedLines = diff.Removed, Weight = weight, Stats = stats,
            });
        }

        var (baseline, baselineFiles) = BuildBaseline(mr);
        return new AnalysisContext
        {
            Mr = mr,
            Files = files,
            Added = added,
            Baseline = baseline,
            BaselineFiles = baselineFiles,
            Options = options,
            Conventions = ProjectConventions.Detect(mr.Repository),
        };
    }

    /// <summary>Норма проекта: старые версии изменённых файлов и соседние файлы каталогов на момент base.</summary>
    private static (CodeStats? Stats, int Files) BuildBaseline(MergeRequestData mr)
    {
        var stats = new CodeStats();
        int files = 0;
        int lines = 0;
        var changedPaths = mr.Changes.Select(c => c.Path).ToHashSet(StringComparer.Ordinal);

        IEnumerable<(string Path, string Content)> Candidates()
        {
            foreach (var c in mr.Changes)
            {
                if (!c.IsNew && c.OldContent is not null && FileClassifier.IsCSharp(c.OldPath))
                {
                    yield return (c.OldPath, c.OldContent);
                }
            }

            foreach (var f in mr.Repository.BaselineFiles)
            {
                if (!changedPaths.Contains(f.Path) && FileClassifier.IsCSharp(f.Path))
                {
                    yield return (f.Path, f.Content);
                }
            }
        }

        foreach (var (path, content) in Candidates())
        {
            if (lines >= MaxBaselineLines || FileClassifier.IsGenerated(path, content))
            {
                continue;
            }

            bool isTest = FileClassifier.IsTestFile(path, content);
            var s = CSharpFeatureExtractor.Extract(path, content, _ => true, isTest, isNewFile: true);
            stats.Merge(s);
            files++;
            lines += (int)s[K.CodeLines];
        }

        return files == 0 || stats[K.CodeLines] < 30 ? (null, files) : (stats, files);
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
