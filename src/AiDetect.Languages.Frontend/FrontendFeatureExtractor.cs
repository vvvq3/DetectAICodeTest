using AiDetect.Core.Features;
using AiDetect.Languages.Frontend.Parsing;

namespace AiDetect.Languages.Frontend;

/// <summary>Вид фронтенд-файла.</summary>
public enum FrontendFileKind
{
    None,
    Script,
    Vue,
    Template,
    Style,
}

/// <summary>Контекст извлечения признаков одного файла: счётчики, область анализа (добавленные строки), примеры.</summary>
internal sealed class Ctx(CodeStats stats, string file, string content, Func<int, bool> scope, bool isTest)
{
    private readonly string[] _lines = content.Split('\n');

    public CodeStats S { get; } = stats;

    public string File { get; } = file;

    public bool IsTest { get; } = isTest;

    public string Content { get; } = content;

    public int LineCount => _lines.Length;

    public bool In(int line) => line >= 1 && scope(line);

    public string LineText(int line) => line >= 1 && line <= _lines.Length ? _lines[line - 1].TrimEnd('\r') : "";

    /// <summary>Счётчик с примером (если строка в области анализа).</summary>
    public void Hit(string key, int line, double value = 1)
    {
        if (!In(line))
        {
            return;
        }

        S.Add(key, value);
        S.AddExample(key, File, line, LineText(line));
    }

    /// <summary>Счётчик без примера (знаменатели).</summary>
    public void Count(string key, int line, double value = 1)
    {
        if (In(line))
        {
            S.Add(key, value);
        }
    }
}

/// <summary>
/// Извлечение признаков фронтенд-кода: JS/TS/JSX/TSX (свой лексер и мини-парсер), Vue SFC, шаблоны Angular, CSS/SCSS/LESS.
/// Считаются только строки в области анализа (добавленные в MR; для нового файла — все).
/// </summary>
public static class FrontendFeatureExtractor
{
    public static FrontendFileKind KindOf(string path)
    {
        var ext = Path.GetExtension(path).ToLowerInvariant();
        return ext switch
        {
            ".ts" or ".tsx" or ".js" or ".jsx" or ".mjs" or ".cjs" or ".mts" or ".cts" => FrontendFileKind.Script,
            ".vue" => FrontendFileKind.Vue,
            ".html" or ".htm" => FrontendFileKind.Template,
            ".css" or ".scss" or ".sass" or ".less" => FrontendFileKind.Style,
            _ => FrontendFileKind.None,
        };
    }

    public static CodeStats Extract(string path, string content, Func<int, bool> inScope, bool isTestFile, bool isNewFile)
    {
        var stats = new CodeStats();
        stats.Add(FK.Files);
        if (isNewFile)
        {
            stats.Add(FK.NewFiles);
        }

        content = content.Replace("\r\n", "\n");
        var ctx = new Ctx(stats, path, content, inScope, isTestFile);
        var ext = Path.GetExtension(path).ToLowerInvariant();
        switch (KindOf(path))
        {
            case FrontendFileKind.Script:
                {
                    bool ts = ext is ".ts" or ".tsx" or ".mts" or ".cts";
                    bool jsx = ext is not (".ts" or ".mts" or ".cts");
                    new ScriptAnalyzer(ctx, JsLexer.Tokenize(content, jsx, ts), ts, 0, ScriptHost.File).Run();
                    break;
                }

            case FrontendFileKind.Vue:
                ExtractVue(ctx, content);
                break;
            case FrontendFileKind.Template:
                new MarkupAnalyzer(ctx).Template(HtmlScanner.Scan(content), isAngular: true);
                break;
            case FrontendFileKind.Style:
                new MarkupAnalyzer(ctx).Style(CssScanner.Scan(content, 0, lineComments: ext is ".scss" or ".less" or ".sass"), content, 0);
                break;
        }

        stats.Add(FK.TotalLines, Enumerable.Range(1, ctx.LineCount).Count(ctx.In));
        return stats;
    }

    private static void ExtractVue(Ctx ctx, string content)
    {
        var blocks = VueSfc.Parse(content);
        bool any = false;
        foreach (var block in blocks)
        {
            switch (block.Kind)
            {
                case "script":
                    {
                        bool ts = block.Lang is "ts" or "tsx";
                        bool jsx = block.Lang is "tsx" or "jsx";
                        int line = block.LineOffset + 1;
                        if (block.Has("setup"))
                        {
                            ctx.Hit(FK.VueScriptSetup, line);
                        }

                        if (ts)
                        {
                            ctx.Hit(FK.VueLangTs, line);
                        }

                        new ScriptAnalyzer(ctx, JsLexer.Tokenize(block.Content, jsx, ts, block.LineOffset), ts, block.LineOffset, ScriptHost.Vue).Run();
                        any = true;
                        break;
                    }

                case "template":
                    new MarkupAnalyzer(ctx).Template(HtmlScanner.Scan(block.Content, block.LineOffset), isAngular: false);
                    any = true;
                    break;
                case "style":
                    {
                        if (block.Has("scoped") || block.Has("module"))
                        {
                            ctx.Count(FK.VueScopedStyles, block.LineOffset + 1);
                        }

                        new MarkupAnalyzer(ctx).Style(CssScanner.Scan(block.Content, block.LineOffset, lineComments: block.Lang is "scss" or "less" or "sass"),
                            block.Content, block.LineOffset);
                        any = true;
                        break;
                    }
            }
        }

        if (any)
        {
            ctx.Count(FK.VueComponents, 1, ctx.In(1) || Enumerable.Range(1, ctx.LineCount).Any(ctx.In) ? 1 : 0);
        }
    }
}
