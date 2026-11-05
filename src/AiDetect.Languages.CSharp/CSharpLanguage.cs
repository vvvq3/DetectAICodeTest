using System.Text.RegularExpressions;
using AiDetect.Core.Analysis;
using AiDetect.Core.Features;
using AiDetect.Core.Languages;
using AiDetect.Core.Model;
using AiDetect.Core.Preprocessing;
using AiDetect.Core.Signals;

namespace AiDetect.Languages.CSharp;

/// <summary>Детектор C# (.NET): разбор Roslyn, ~90 признаков, конвенции StyleCop/XML-doc/.editorconfig, стек из *.csproj.</summary>
public sealed partial class CSharpLanguage : ILanguageAnalyzer
{
    public const string LanguageId = "csharp";

    private static readonly Layer[] Meaningful =
    [
        Layer.Controller, Layer.Service, Layer.Handler, Layer.Repository, Layer.Domain, Layer.Contracts, Layer.DependencyInjection,
        Layer.Validation, Layer.Options, Layer.Mapping, Layer.Middleware, Layer.Migration, Layer.Tests, Layer.Documentation,
    ];

    public string Id => LanguageId;

    public string Title => "C# (.NET)";

    public string CodeNoun => "C#";

    public IReadOnlyList<string> Aliases { get; } = ["cs", "c#", "csharp", "dotnet", ".net", "net"];

    public bool IsSourceFile(string path) => CSharpFileClassifier.IsCSharp(path);

    public bool IsGenerated(string path, string? content) => CSharpFileClassifier.IsGenerated(path, content);

    public bool IsTestFile(string path, string? content) => CSharpFileClassifier.IsTestFile(path, content);

    public Layer ClassifyLayer(string path, string? content) => CSharpFileClassifier.ClassifyLayer(path, content);

    public IReadOnlyCollection<Layer> MeaningfulLayers => Meaningful;

    public bool IsBoilerplateLayer(Layer layer) => layer is Layer.Contracts or Layer.Options or Layer.DependencyInjection or Layer.Migration;

    public CodeStats Extract(string path, string content, Func<int, bool> inScope, bool isTestFile, bool isNewFile) =>
        CSharpFeatureExtractor.Extract(path, content, inScope, isTestFile, isNewFile);

    public void EvaluateCodeSignals(AnalysisContext ctx, SignalBuilder b) => new CSharpCodeSignals(ctx, b).Evaluate();

    public void EvaluateShapeSignals(AnalysisContext ctx, SignalBuilder b) => new CSharpShapeSignals(ctx, b).Evaluate();

    public ProjectConventions DetectConventions(RepositoryContext repository) => CSharpConventions.Detect(repository);

    public bool IsRootConfigFile(string fileName) => CSharpConventions.IsRootConfigFile(fileName);

    public bool IsProjectFile(string fileName) => fileName.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase);

    public string? PackageMarker => null;

    public string? GroupTitle(SignalGroup group) => null;

    // ------------------------------------------------------------------ стек

    private static readonly (string Prefix, string Title, string Tag)[] KnownPackages =
    [
        ("Microsoft.AspNetCore.Components", "Blazor", "blazor"),
        ("Microsoft.EntityFrameworkCore", "EF Core", "efcore"),
        ("EntityFramework", "Entity Framework", "ef"),
        ("Dapper", "Dapper", "dapper"),
        ("MediatR", "MediatR", "mediatr"),
        ("AutoMapper", "AutoMapper", "automapper"),
        ("FluentValidation", "FluentValidation", "fluentvalidation"),
        ("MassTransit", "MassTransit", "masstransit"),
        ("Grpc.AspNetCore", "gRPC", "grpc"),
        ("Microsoft.AspNetCore.SignalR", "SignalR", "signalr"),
        ("Hangfire", "Hangfire", "hangfire"),
        ("Quartz", "Quartz", "quartz"),
        ("Polly", "Polly", "polly"),
        ("Serilog", "Serilog", "serilog"),
        ("NLog", "NLog", "nlog"),
        ("Swashbuckle", "Swagger", "swagger"),
        ("NSwag", "Swagger", "swagger"),
        ("IdentityServer4", "IdentityServer", "identityserver"),
        ("Duende.IdentityServer", "IdentityServer", "identityserver"),
        ("Npgsql", "PostgreSQL", "postgres"),
        ("Microsoft.Data.SqlClient", "SQL Server", "sqlserver"),
        ("System.Data.SqlClient", "SQL Server", "sqlserver"),
        ("MongoDB.Driver", "MongoDB", "mongodb"),
        ("StackExchange.Redis", "Redis", "redis"),
        ("RabbitMQ.Client", "RabbitMQ", "rabbitmq"),
        ("Autofac", "Autofac", "autofac"),
        ("Newtonsoft.Json", "Newtonsoft.Json", "newtonsoft"),
    ];

    private static readonly (string Prefix, string Title, string Tag)[] TestPackages =
    [
        ("xunit", "xUnit", "xunit"),
        ("NUnit", "NUnit", "nunit"),
        ("MSTest", "MSTest", "mstest"),
        ("Moq", "Moq", "moq"),
        ("NSubstitute", "NSubstitute", "nsubstitute"),
        ("FakeItEasy", "FakeItEasy", "fakeiteasy"),
        ("FluentAssertions", "FluentAssertions", "fluentassertions"),
        ("Shouldly", "Shouldly", "shouldly"),
    ];

    [GeneratedRegex(@"<TargetFrameworks?>\s*([^<]+?)\s*</TargetFrameworks?>", RegexOptions.IgnoreCase)]
    private static partial Regex TargetFrameworkRegex();

    [GeneratedRegex(@"<TargetFrameworkVersion>\s*v?([\d.]+)\s*<", RegexOptions.IgnoreCase)]
    private static partial Regex LegacyFrameworkRegex();

    [GeneratedRegex(@"<Project\s+Sdk=""([^""]+)""", RegexOptions.IgnoreCase)]
    private static partial Regex SdkRegex();

    [GeneratedRegex(@"<(?:PackageReference|PackageVersion)\s+Include=""([^""]+)""", RegexOptions.IgnoreCase)]
    private static partial Regex PackageRegex();

    [GeneratedRegex(@"<Reference\s+Include=""([^"",]+)", RegexOptions.IgnoreCase)]
    private static partial Regex LegacyReferenceRegex();

    [GeneratedRegex(@"^\s*using\s+(?:static\s+)?([A-Za-z0-9_.]+)\s*;", RegexOptions.Multiline)]
    private static partial Regex UsingRegex();

    public StackInfo DetectStack(MergeRequestData mr)
    {
        var stack = new StackInfo();
        stack.Add("C#", "csharp");
        var projects = mr.Repository.ConfigFiles
            .Where(f => f.Path.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase) || f.Path.EndsWith(".props", StringComparison.OrdinalIgnoreCase))
            .ToList();
        var frameworks = new List<string>();
        var sdks = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var packages = new List<string>();
        foreach (var p in projects)
        {
            foreach (Match m in TargetFrameworkRegex().Matches(p.Content))
            {
                frameworks.AddRange(m.Groups[1].Value.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
            }

            foreach (Match m in LegacyFrameworkRegex().Matches(p.Content))
            {
                frameworks.Add("net" + m.Groups[1].Value.Replace(".", ""));
            }

            foreach (Match m in SdkRegex().Matches(p.Content))
            {
                sdks.Add(m.Groups[1].Value);
            }

            packages.AddRange(PackageRegex().Matches(p.Content).Select(m => m.Groups[1].Value));
            packages.AddRange(LegacyReferenceRegex().Matches(p.Content).Select(m => m.Groups[1].Value));
        }

        // Если проектных файлов нет — подсказки из using в изменённых файлах.
        var usings = mr.Changes.Where(c => IsSourceFile(c.Path) && c.NewContent is not null)
            .SelectMany(c => UsingRegex().Matches(c.NewContent!).Select(m => m.Groups[1].Value))
            .ToHashSet(StringComparer.Ordinal);

        var dotnet = frameworks.Select(DescribeFramework).Where(x => x is not null).Select(x => x!)
            .OrderByDescending(x => x.Rank).FirstOrDefault();
        if (dotnet is not null)
        {
            stack.Add(dotnet.Title, "dotnet");
        }

        bool web = sdks.Any(s => s.Contains(".Web", StringComparison.OrdinalIgnoreCase)) ||
                   packages.Any(p => p.StartsWith("Microsoft.AspNetCore", StringComparison.OrdinalIgnoreCase)) ||
                   usings.Any(u => u.StartsWith("Microsoft.AspNetCore", StringComparison.Ordinal));
        if (sdks.Any(s => s.Contains("BlazorWebAssembly", StringComparison.OrdinalIgnoreCase)))
        {
            stack.Add("Blazor WebAssembly", "blazor");
        }
        else if (web)
        {
            stack.Add(dotnet?.Title.StartsWith(".NET Framework", StringComparison.Ordinal) == true ? "ASP.NET" : "ASP.NET Core", "aspnetcore");
        }
        else if (sdks.Any(s => s.Contains("Worker", StringComparison.OrdinalIgnoreCase)))
        {
            stack.Add("Worker Service", "worker");
        }

        bool Has(string prefix) =>
            packages.Any(p => p.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) ||
            usings.Any(u => u.StartsWith(prefix, StringComparison.Ordinal));

        foreach (var (prefix, title, tag) in KnownPackages)
        {
            if (Has(prefix))
            {
                stack.Add(title, tag);
            }
        }

        var tests = TestPackages.Where(t => Has(t.Prefix)).ToList();
        foreach (var (_, title, tag) in tests)
        {
            stack.Add(title, tag);
        }

        stack.Source = projects.Count > 0 ? "*.csproj" : usings.Count > 0 ? "using в коде" : "";
        return stack;
    }

    private sealed record FrameworkInfo(string Title, double Rank);

    /// <summary>«netcoreapp3.1» → «.NET Core 3.1», «net8.0» → «.NET 8», «net48» → «.NET Framework 4.8».</summary>
    private static FrameworkInfo? DescribeFramework(string tfm)
    {
        var t = tfm.Trim().ToLowerInvariant();
        if (t.StartsWith("$(", StringComparison.Ordinal))
        {
            return null;
        }

        var core = Regex.Match(t, @"^netcoreapp(\d+\.\d+)");
        if (core.Success)
        {
            return new FrameworkInfo(".NET Core " + core.Groups[1].Value, 3 + double.Parse(core.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture) / 10);
        }

        var standard = Regex.Match(t, @"^netstandard(\d+\.\d+)");
        if (standard.Success)
        {
            return new FrameworkInfo(".NET Standard " + standard.Groups[1].Value, 1);
        }

        var modern = Regex.Match(t, @"^net(\d+)\.(\d+)");
        if (modern.Success)
        {
            return new FrameworkInfo(".NET " + modern.Groups[1].Value, 5 + int.Parse(modern.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture));
        }

        var legacy = Regex.Match(t, @"^net(\d)(\d)(\d?)$");
        if (legacy.Success)
        {
            var v = legacy.Groups[1].Value + "." + legacy.Groups[2].Value + (legacy.Groups[3].Value.Length > 0 ? "." + legacy.Groups[3].Value : "");
            return new FrameworkInfo(".NET Framework " + v, 0.5);
        }

        return null;
    }
}
