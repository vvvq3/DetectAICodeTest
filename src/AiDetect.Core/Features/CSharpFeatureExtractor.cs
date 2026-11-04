using System.Text.RegularExpressions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

namespace AiDetect.Core.Features;

/// <summary>
/// Извлекает стилометрические признаки C#-кода через Roslyn.
/// Считаются только узлы, «якорная» строка которых входит в область анализа (добавленные строки МР
/// или весь файл для базовой линии).
/// </summary>
public static partial class CSharpFeatureExtractor
{
    private static readonly CSharpParseOptions ParseOptions = new(LanguageVersion.Preview, DocumentationMode.Parse);

    public static CodeStats Extract(string path, string content, Func<int, bool> inScope, bool isTestFile, bool isNewFile)
    {
        var stats = new CodeStats();
        var tree = CSharpSyntaxTree.ParseText(SourceText.From(content), ParseOptions, path);
        var root = tree.GetCompilationUnitRoot();
        var text = tree.GetText();
        var ctx = new Ctx(stats, path, text, inScope, isTestFile);

        stats.Add(K.Files);
        if (isNewFile)
        {
            stats.Add(K.NewFiles);
            if (root.Members.OfType<FileScopedNamespaceDeclarationSyntax>().Any())
            {
                stats.Add(K.FileScopedNs);
            }
            else if (root.Members.OfType<NamespaceDeclarationSyntax>().Any())
            {
                stats.Add(K.BlockNs);
            }
        }

        AnalyzeLines(ctx);
        AnalyzeComments(ctx, root);
        new Walker(ctx).Visit(root);
        return stats;
    }

    internal sealed class Ctx(CodeStats stats, string path, SourceText text, Func<int, bool> inScope, bool isTest)
    {
        public CodeStats S { get; } = stats;
        public string Path { get; } = path;
        public SourceText Text { get; } = text;
        public Func<int, bool> InScope { get; } = inScope;
        public bool IsTest { get; } = isTest;

        public int LineOf(SyntaxNode node) => Text.Lines.GetLineFromPosition(node.SpanStart).LineNumber + 1;

        public int LineOf(SyntaxToken token) => Text.Lines.GetLineFromPosition(token.SpanStart).LineNumber + 1;

        public int LineOf(SyntaxTrivia trivia) => Text.Lines.GetLineFromPosition(trivia.SpanStart).LineNumber + 1;

        public bool In(SyntaxNode node) => InScope(LineOf(node));

        public bool In(SyntaxToken token) => InScope(LineOf(token));

        public string LineText(int line) => line >= 1 && line <= Text.Lines.Count ? Text.Lines[line - 1].ToString() : "";

        public void Hit(string key, int line) => S.Hit(key, Path, line, LineText(line));

        public void Hit(string key, SyntaxNode node) => Hit(key, LineOf(node));
    }

    // ------------------------------------------------------------------ строки и форматирование

    private static void AnalyzeLines(Ctx ctx)
    {
        var lines = ctx.Text.Lines;
        int blankRun = 0;
        bool inBlockComment = false;
        for (int i = 0; i < lines.Count; i++)
        {
            int lineNo = i + 1;
            var line = lines[i].ToString();
            var trimmed = line.Trim();
            if (!ctx.InScope(lineNo))
            {
                blankRun = 0;
                continue;
            }

            ctx.S.Add(K.TotalLines);
            if (trimmed.Length == 0)
            {
                blankRun++;
                if (blankRun == 2)
                {
                    ctx.Hit(K.DoubleBlankLines, lineNo);
                }

                continue;
            }

            blankRun = 0;
            if (inBlockComment)
            {
                ctx.S.Add(K.CommentLines);
                if (trimmed.Contains("*/"))
                {
                    inBlockComment = false;
                }

                continue;
            }

            if (trimmed.StartsWith("//", StringComparison.Ordinal))
            {
                ctx.S.Add(K.CommentLines);
                CheckTypos(ctx, line, lineNo);
                continue;
            }

            if (trimmed.StartsWith("/*", StringComparison.Ordinal))
            {
                ctx.S.Add(K.CommentLines);
                inBlockComment = !trimmed.Contains("*/");
                continue;
            }

            ctx.S.Add(K.CodeLines);
            CheckTypos(ctx, line, lineNo);

            if (line.Length > 0 && (line.EndsWith(' ') || line.EndsWith('\t')))
            {
                ctx.Hit(K.TrailingWhitespace, lineNo);
            }

            if (line.StartsWith('\t'))
            {
                ctx.S.Add(K.TabIndent);
            }

            if (line.Length > 160)
            {
                ctx.Hit(K.LongLines, lineNo);
            }

            var code = StripStringsAndComments(trimmed);
            if (KeywordNoSpaceRegex().IsMatch(code))
            {
                ctx.Hit(K.KeywordNoSpace, lineNo);
            }

            if (code.Contains("){") || BraceNoSpaceRegex().IsMatch(code))
            {
                ctx.Hit(K.BraceSameLineNoSpace, lineNo);
            }

            if (CuddledElseRegex().IsMatch(code))
            {
                ctx.Hit(K.CuddledElse, lineNo);
            }

            if (CommaNoSpaceRegex().IsMatch(code))
            {
                ctx.Hit(K.CommaNoSpace, lineNo);
            }

            if (OperatorNoSpaceRegex().IsMatch(code))
            {
                ctx.Hit(K.OperatorNoSpace, lineNo);
            }

            if (code.EndsWith('{') && code.Length > 1 && !code.StartsWith('{') && !code.EndsWith("=> {") &&
                !code.EndsWith("= {") && !code.EndsWith("new {") && !code.EndsWith("new() {") && !code.EndsWith("$\"{") &&
                (code.StartsWith("if") || code.StartsWith("for") || code.StartsWith("while") || code.StartsWith("public ") ||
                 code.StartsWith("private ") || code.StartsWith("else") || code.StartsWith("try") || code.StartsWith("catch") ||
                 code.StartsWith("internal ") || code.StartsWith("protected ") || code.StartsWith("switch") || code.StartsWith("using")))
            {
                ctx.Hit(K.KAndRBraces, lineNo);
            }
        }
    }

    private static readonly HashSet<string> Misspellings = new(StringComparer.Ordinal)
    {
        "recieve", "recieved", "reciever", "seperate", "seperated", "occured", "occurence", "lenght", "adress", "adresses", "sucess", "succesful",
        "successfull", "sucessful", "retrive", "retreive", "paramter", "paramters", "responce", "reponse", "neccessary", "necesary", "existance",
        "calender", "comming", "begining", "untill", "wich", "teh", "acces", "accesible", "proccess", "proccessing", "procces", "aditional",
        "additonal", "adition", "allready", "alredy", "arguement", "assigment", "atribute", "auth0rize", "beggining", "buisness", "bussiness",
        "cancelation", "comparsion", "compatable", "completly", "conection", "consistant", "containg", "contructor", "convertion", "corect",
        "curent", "currenty", "defualt", "definately", "dependancy", "dependancies", "desription", "destory", "diffrent", "dissable", "doesnt",
        "dont", "enviroment", "eqauls", "exeption", "exeptions", "existant", "expection", "failded", "fucntion", "funtion", "garantee", "gurantee",
        "hanlder", "heigth", "identifer", "immediatly", "implmentation", "implemenation", "independant", "infomation", "initalize", "initialise",
        "intial", "intergration", "lenth", "maximun", "mesage", "messege", "occuring", "overriden", "paremeter", "perfomance", "permision",
        "posible", "prefered", "privilage", "probaly", "propery", "proprety", "quantitiy", "recieves", "recursivly", "refered", "refernce",
        "registred", "relevent", "remaing", "repositry", "requred", "resouce", "resourse", "respone", "responsse", "seach", "sendign", "sepcific",
        "settigns", "similiar", "singelton", "specfic", "statment", "stauts", "strign", "succeded", "sucess", "suport", "temporay", "threshhold",
        "transction", "trasaction", "udpate", "updat", "usefull", "usuall", "valdiate", "validaton", "valus", "verfiy", "wether", "whith", "writting",
        "accout", "ammount", "amout", "cutomer", "custmer", "costumer", "oder", "ordes", "prodcut", "pruduct", "quanity", "quantiy", "summery",
    };

    private static void CheckTypos(Ctx ctx, string line, int lineNo)
    {
        foreach (Match m in IdentifierRegex().Matches(line))
        {
            foreach (Match part in CamelPartRegex().Matches(m.Value))
            {
                if (part.Value.Length >= 4 && Misspellings.Contains(part.Value.ToLowerInvariant()))
                {
                    ctx.Hit(K.Typos, lineNo);
                    return;
                }
            }
        }
    }

    /// <summary>Убирает содержимое строковых литералов и хвостовой комментарий, чтобы регулярки по форматированию не ошибались.</summary>
    internal static string StripStringsAndComments(string code)
    {
        var sb = new System.Text.StringBuilder(code.Length);
        bool inString = false;
        bool verbatim = false;
        char quote = '"';
        for (int i = 0; i < code.Length; i++)
        {
            char c = code[i];
            if (!inString)
            {
                if (c == '/' && i + 1 < code.Length && code[i + 1] == '/')
                {
                    break;
                }

                if (c == '"' || c == '\'')
                {
                    inString = true;
                    quote = c;
                    verbatim = c == '"' && i > 0 && (code[i - 1] == '@' || (i > 1 && code[i - 2] == '@'));
                    sb.Append(c);
                    continue;
                }

                sb.Append(c);
            }
            else
            {
                if (c == '\\' && !verbatim && i + 1 < code.Length)
                {
                    i++;
                    continue;
                }

                if (c == quote)
                {
                    inString = false;
                    sb.Append(c);
                }
            }
        }

        return sb.ToString();
    }

    [GeneratedRegex(@"\b(if|for|foreach|while|switch|catch|using|lock)\(")]
    private static partial Regex KeywordNoSpaceRegex();

    [GeneratedRegex(@"\b(else|try|finally)\{")]
    private static partial Regex BraceNoSpaceRegex();

    [GeneratedRegex(@"\}\s*else\b")]
    private static partial Regex CuddledElseRegex();

    [GeneratedRegex(@",[A-Za-z0-9_""(]")]
    private static partial Regex CommaNoSpaceRegex();

    [GeneratedRegex(@"[A-Za-z0-9_\)\]](==|!=|<=|>=|&&|\|\||\+=|-=|=)[A-Za-z0-9_\(""]|[A-Za-z0-9_\)\]] (==|!=|&&|\|\||=)[A-Za-z0-9_\(""]|[A-Za-z0-9_\)\]](==|!=|&&|\|\||=) [A-Za-z0-9_\(""]")]
    private static partial Regex OperatorNoSpaceRegex();

    // ------------------------------------------------------------------ комментарии

    private static readonly HashSet<string> NarrativeVerbs = new(StringComparer.OrdinalIgnoreCase)
    {
        "validate", "check", "create", "map", "ensure", "get", "retrieve", "calculate", "build", "return", "initialize",
        "update", "add", "remove", "set", "load", "save", "handle", "process", "convert", "parse", "apply", "filter",
        "log", "throw", "verify", "fetch", "send", "register", "configure", "compute", "find", "determine", "prepare",
        "store", "persist", "publish", "notify", "generate", "normalize", "extract", "resolve", "attempt", "try",
        "skip", "use", "call", "invoke", "wait", "mark", "append", "clear", "reset", "sort", "group", "merge", "assign",
        "fall", "default", "only", "first", "then", "finally", "next", "now", "otherwise", "if", "early", "guard", "cap", "limit",
        "clamp", "round", "wrap", "dispose", "cleanup", "release", "emit", "raise", "dispatch", "schedule", "seed", "copy", "match",
        "lookup", "look", "collect", "aggregate", "calculate", "recalculate", "refresh", "sync", "trim", "strip", "split", "combine", "keep",
    };

    private static readonly HashSet<string> CommentStopWords = new(StringComparer.OrdinalIgnoreCase)
    {
        "the", "a", "an", "to", "of", "for", "and", "or", "in", "on", "by", "with", "from", "is", "it", "its", "this",
        "that", "be", "as", "at", "if", "we", "our", "new", "into", "all", "any", "not", "no",
    };

    private static void AnalyzeComments(Ctx ctx, CompilationUnitSyntax root)
    {
        foreach (var trivia in root.DescendantTrivia(descendIntoTrivia: false))
        {
            if (trivia.IsKind(SyntaxKind.RegionDirectiveTrivia))
            {
                var regionLine = ctx.LineOf(trivia);
                if (ctx.InScope(regionLine))
                {
                    ctx.Hit(K.Regions, regionLine);
                }

                continue;
            }

            if (!trivia.IsKind(SyntaxKind.SingleLineCommentTrivia) && !trivia.IsKind(SyntaxKind.MultiLineCommentTrivia))
            {
                continue;
            }

            int line = ctx.LineOf(trivia);
            if (!ctx.InScope(line))
            {
                continue;
            }

            var raw = trivia.ToString();
            var body = trivia.IsKind(SyntaxKind.SingleLineCommentTrivia)
                ? raw.TrimStart('/').Trim()
                : raw.Trim('/', '*').Trim();
            if (body.Length == 0)
            {
                continue;
            }

            if (AaaMarkerRegex().IsMatch(body))
            {
                // Маркеры Arrange/Act/Assert учитываются в признаках тестов, а не как обычные комментарии.
                continue;
            }

            ctx.S.Add(K.LineComments);

            if (TodoRegex().IsMatch(body))
            {
                ctx.Hit(K.TodoComments, line);
            }

            if (TicketRegex().IsMatch(body))
            {
                ctx.Hit(K.TicketRefs, line);
            }

            if (LooksLikeCode(body))
            {
                ctx.Hit(K.CommentedOutCode, line);
                continue;
            }

            if (CyrillicRegex().IsMatch(body))
            {
                ctx.Hit(K.CyrillicComments, line);
            }
            else if (LatinRegex().IsMatch(body))
            {
                ctx.S.Add(K.LatinComments);
            }

            var firstLetter = body.FirstOrDefault(char.IsLetter);
            if (firstLetter != default)
            {
                if (char.IsUpper(firstLetter) && (body.EndsWith('.') || body.EndsWith(')')) && body.Split(' ').Length >= 4)
                {
                    ctx.S.Add(K.SentenceComments);
                }

                if (char.IsLower(firstLetter) && !body.StartsWith("http", StringComparison.OrdinalIgnoreCase))
                {
                    ctx.Hit(K.LowercaseComments, line);
                }
            }

            if (StepRegex().IsMatch(body))
            {
                ctx.Hit(K.StepComments, line);
            }

            var words = WordRegex().Matches(body).Select(m => m.Value).ToList();
            if (words.Count is >= 1 and <= 12 && (NarrativeVerbs.Contains(words[0]) || RussianNarrativeRegex().IsMatch(words[0])) &&
                !body.Contains("TODO", StringComparison.OrdinalIgnoreCase))
            {
                ctx.Hit(K.NarrativeComments, line);
            }

            if (CyrillicRegex().IsMatch(body) && char.IsUpper(firstLetter) && !SlangRegex().IsMatch(body))
            {
                ctx.S.Add(K.CyrillicPolishedComments);
            }

            // «Пересказывающий» комментарий: слова комментария почти все встречаются в идентификаторах следующей строки кода.
            var next = NextCodeLine(ctx, line);
            if (next is not null)
            {
                var content = words.Where(w => w.Length >= 3 && !CommentStopWords.Contains(w))
                    .Select(w => w.ToLowerInvariant()).Distinct().ToList();
                if (content.Count >= 2)
                {
                    var idWords = SplitIdentifiers(next);
                    int overlap = content.Count(w => idWords.Any(iw => iw.StartsWith(w.Length > 4 ? w[..4] : w, StringComparison.Ordinal)));
                    if (overlap >= 2 && overlap >= content.Count * 0.6)
                    {
                        ctx.Hit(K.RestatingComments, line);
                    }
                }
            }
        }
    }

    private static string? NextCodeLine(Ctx ctx, int commentLine)
    {
        for (int l = commentLine + 1; l <= Math.Min(ctx.Text.Lines.Count, commentLine + 3); l++)
        {
            var t = ctx.LineText(l).Trim();
            if (t.Length == 0 || t.StartsWith("//", StringComparison.Ordinal))
            {
                continue;
            }

            return t;
        }

        return null;
    }

    internal static HashSet<string> SplitIdentifiers(string code)
    {
        var result = new HashSet<string>(StringComparer.Ordinal);
        foreach (Match m in IdentifierRegex().Matches(code))
        {
            foreach (Match part in CamelPartRegex().Matches(m.Value))
            {
                if (part.Value.Length >= 2)
                {
                    result.Add(part.Value.ToLowerInvariant());
                }
            }
        }

        return result;
    }

    internal static bool LooksLikeCode(string body)
    {
        var b = body.Trim();
        if (b.Length < 3)
        {
            return false;
        }

        if (b.EndsWith(';') || b == "{" || b == "}" || b.EndsWith("{", StringComparison.Ordinal) && b.Contains('('))
        {
            return true;
        }

        return CodeLikeRegex().IsMatch(b);
    }

    [GeneratedRegex(@"^(Arrange|Act|Assert|Act\s*&\s*Assert|Given|When|Then|Setup|Verify)\b[\s:.]*$", RegexOptions.IgnoreCase)]
    private static partial Regex AaaMarkerRegex();

    [GeneratedRegex(@"\b(TODO|FIXME|HACK|XXX|КОСТЫЛЬ|ToDo|todo)\b")]
    private static partial Regex TodoRegex();

    [GeneratedRegex(@"\b[A-Z][A-Z0-9]{1,9}-\d{1,6}\b|#\d{2,6}\b|\b(task|задача|тикет|issue|bug)\s*#?\d+", RegexOptions.IgnoreCase)]
    private static partial Regex TicketRegex();

    [GeneratedRegex(@"[А-Яа-яЁё]")]
    private static partial Regex CyrillicRegex();

    /// <summary>Русский глагол 1-го лица мн. числа в начале комментария: «Получаем…», «Проверяем…», «Создаём…».</summary>
    [GeneratedRegex(@"^[А-ЯЁа-яё][а-яё]{2,}(ем|ём|им|ам|ям)$")]
    private static partial Regex RussianNarrativeRegex();

    /// <summary>Разговорные/сленговые маркеры в русских комментариях — признак «ручного» стиля.</summary>
    [GeneratedRegex(@"(костыл|дерга|дёрга|хз|пока так|временно|потом|переделать|кривов|херн|фигн|почему-то|зачем-то|!!|\?\?|\)\)|\bвроде\b|\bщас\b|\bнадо\b)", RegexOptions.IgnoreCase)]
    private static partial Regex SlangRegex();

    [GeneratedRegex(@"[A-Za-z]{3,}")]
    private static partial Regex LatinRegex();

    [GeneratedRegex(@"^(step\s*\d+|\d+[\.\)]\s)", RegexOptions.IgnoreCase)]
    private static partial Regex StepRegex();

    [GeneratedRegex(@"[A-Za-zА-Яа-яЁё']+")]
    private static partial Regex WordRegex();

    [GeneratedRegex(@"[A-Za-z_][A-Za-z0-9_]*")]
    private static partial Regex IdentifierRegex();

    [GeneratedRegex(@"[A-Z]?[a-z0-9]+|[A-Z]+(?![a-z])")]
    private static partial Regex CamelPartRegex();

    [GeneratedRegex(@"^(var |await |return |using |if\s*\(|foreach\s*\(|for\s*\(|while\s*\(|public |private |protected |internal |_[a-z]\w*\.|this\.|new [A-Z]|[A-Za-z_][\w\.]*\s*=\s*[^=].*;?$|[A-Za-z_][\w\.]*\(.*\)\s*;?$)")]
    private static partial Regex CodeLikeRegex();

    // ------------------------------------------------------------------ синтаксический обход

    private static readonly Regex LogMethodRegex = new(@"^Log(Trace|Debug|Information|Warning|Error|Critical)$", RegexOptions.Compiled);

    private static readonly Regex TemplatePlaceholderRegex = new(@"\{[A-Za-z_@][A-Za-z0-9_]*(:[^}]*)?\}", RegexOptions.Compiled);

    private static readonly HashSet<string> TestAttributes = new(StringComparer.Ordinal)
    {
        "Fact", "Theory", "Test", "TestCase", "TestMethod", "DataTestMethod", "TestCaseSource",
    };

    private static readonly HashSet<string> TemplateDocPhrases = new(StringComparer.OrdinalIgnoreCase)
    {
        "Gets or sets", "Initializes a new instance", "The unique identifier", "A token to cancel", "The cancellation token",
        "cancellation token", "Represents a", "Represents the", "Provides ", "if found; otherwise", "A task that represents",
        "The asynchronous operation", "Creates a new", "Retrieves ", "Gets the ", "Gets a ", "Defines ", "Contains ",
        "Handles ", "Service for", "Service responsible", "Repository for", "Data transfer object", "DTO for", "Configuration options",
    };

    private sealed class Walker(Ctx ctx) : CSharpSyntaxWalker(SyntaxWalkerDepth.Node)
    {
        private readonly CodeStats _s = ctx.S;

        public override void VisitClassDeclaration(ClassDeclarationSyntax node)
        {
            if (ctx.In(node.Identifier))
            {
                CheckDoc(node, node.Modifiers, node.Identifier);
                bool isStatic = node.Modifiers.Any(SyntaxKind.StaticKeyword);
                bool isAbstract = node.Modifiers.Any(SyntaxKind.AbstractKeyword);
                bool isPartial = node.Modifiers.Any(SyntaxKind.PartialKeyword);
                if (!isStatic && !isAbstract && !isPartial && !ctx.IsTest)
                {
                    _s.Add(K.ConcreteClasses);
                    if (node.Modifiers.Any(SyntaxKind.SealedKeyword))
                    {
                        ctx.Hit(K.SealedClasses, ctx.LineOf(node.Identifier));
                    }
                }

                if (node.ParameterList is not null)
                {
                    ctx.Hit(K.PrimaryCtorClasses, node);
                }

                CheckMemberOrder(node);
            }

            base.VisitClassDeclaration(node);
        }

        public override void VisitRecordDeclaration(RecordDeclarationSyntax node)
        {
            if (ctx.In(node.Identifier))
            {
                CheckDoc(node, node.Modifiers, node.Identifier);
                ctx.Hit(K.Records, node);
            }

            base.VisitRecordDeclaration(node);
        }

        public override void VisitInterfaceDeclaration(InterfaceDeclarationSyntax node)
        {
            if (ctx.In(node.Identifier))
            {
                CheckDoc(node, node.Modifiers, node.Identifier);
            }

            base.VisitInterfaceDeclaration(node);
        }

        public override void VisitStructDeclaration(StructDeclarationSyntax node)
        {
            if (ctx.In(node.Identifier))
            {
                CheckDoc(node, node.Modifiers, node.Identifier);
            }

            base.VisitStructDeclaration(node);
        }

        public override void VisitEnumDeclaration(EnumDeclarationSyntax node)
        {
            if (ctx.In(node.Identifier))
            {
                CheckDoc(node, node.Modifiers, node.Identifier);
            }

            base.VisitEnumDeclaration(node);
        }

        public override void VisitConstructorDeclaration(ConstructorDeclarationSyntax node)
        {
            if (ctx.In(node.Identifier))
            {
                if (!node.Modifiers.Any(SyntaxKind.StaticKeyword))
                {
                    CheckAccess(node, node.Modifiers);
                }

                CheckDoc(node, node.Modifiers, node.Identifier, node.ParameterList);
                CheckParameters(node.ParameterList);
                CheckGuards(node, node.Modifiers, node.ParameterList, node.Body, node.ExpressionBody);
            }

            base.VisitConstructorDeclaration(node);
        }

        public override void VisitMethodDeclaration(MethodDeclarationSyntax node)
        {
            if (ctx.In(node.Identifier))
            {
                AnalyzeMethod(node);
            }

            base.VisitMethodDeclaration(node);
        }

        public override void VisitLocalFunctionStatement(LocalFunctionStatementSyntax node)
        {
            if (ctx.In(node.Identifier))
            {
                CheckParameters(node.ParameterList);
            }

            base.VisitLocalFunctionStatement(node);
        }

        public override void VisitPropertyDeclaration(PropertyDeclarationSyntax node)
        {
            if (ctx.In(node.Identifier))
            {
                CheckAccess(node, node.Modifiers);
                CheckDoc(node, node.Modifiers, node.Identifier);
                if (node.Modifiers.Any(SyntaxKind.RequiredKeyword))
                {
                    ctx.Hit(K.RequiredMembers, node);
                }

                if (node.ExpressionBody is not null)
                {
                    _s.Add(K.ExpressionBodied);
                }

                var typeName = node.Type.ToString();
                bool isAuto = node.AccessorList is not null && node.AccessorList.Accessors.All(a => a.Body is null && a.ExpressionBody is null);
                if (isAuto && (typeName == "string" || typeName == "String"))
                {
                    _s.Add(K.StringAutoProps);
                    var init = node.Initializer?.Value.ToString();
                    if (init is "string.Empty" or "String.Empty" or "\"\"")
                    {
                        ctx.Hit(K.StringEmptyInitializers, node);
                    }
                }

                if (node.Initializer?.Value is PostfixUnaryExpressionSyntax { RawKind: (int)SyntaxKind.SuppressNullableWarningExpression })
                {
                    ctx.Hit(K.NullForgivingInit, node);
                }

                CountCollectionTypes(node.Type, node);
            }

            base.VisitPropertyDeclaration(node);
        }

        public override void VisitAccessorDeclaration(AccessorDeclarationSyntax node)
        {
            if (node.IsKind(SyntaxKind.InitAccessorDeclaration) && ctx.In(node))
            {
                _s.Add(K.InitAccessors);
            }

            base.VisitAccessorDeclaration(node);
        }

        public override void VisitFieldDeclaration(FieldDeclarationSyntax node)
        {
            if (ctx.In(node))
            {
                CheckAccess(node, node.Modifiers);
                var mods = node.Modifiers;
                bool isConst = mods.Any(SyntaxKind.ConstKeyword);
                bool isPublic = mods.Any(SyntaxKind.PublicKeyword) || mods.Any(SyntaxKind.InternalKeyword) || mods.Any(SyntaxKind.ProtectedKeyword);
                bool isPrivate = !isPublic;
                if (isConst)
                {
                    _s.Add(K.ConstDeclarations);
                    foreach (var v in node.Declaration.Variables)
                    {
                        if (v.Identifier.Text == "SectionName")
                        {
                            ctx.Hit(K.OptionsSectionName, node);
                        }
                    }
                }
                else if (isPrivate && node.Parent is ClassDeclarationSyntax or RecordDeclarationSyntax or StructDeclarationSyntax)
                {
                    foreach (var v in node.Declaration.Variables)
                    {
                        _s.Add(K.PrivateFields);
                        if (mods.Any(SyntaxKind.ReadOnlyKeyword))
                        {
                            _s.Add(K.ReadonlyFields);
                        }

                        if (v.Identifier.Text.StartsWith('_'))
                        {
                            _s.Add(K.UnderscoreFields);
                        }
                    }
                }
                else if (mods.Any(SyntaxKind.PublicKeyword) && !mods.Any(SyntaxKind.StaticKeyword) && !mods.Any(SyntaxKind.ReadOnlyKeyword))
                {
                    ctx.Hit(K.PublicFields, node);
                }
            }

            base.VisitFieldDeclaration(node);
        }

        public override void VisitLocalDeclarationStatement(LocalDeclarationStatementSyntax node)
        {
            if (ctx.In(node) && node.Parent is not ForStatementSyntax)
            {
                if (node.Declaration.Type.IsVar)
                {
                    _s.Add(K.VarDeclarations);
                }
                else
                {
                    _s.Add(K.ExplicitDeclarations);
                }

                foreach (var v in node.Declaration.Variables)
                {
                    AddLocalName(v.Identifier.Text);
                }
            }

            base.VisitLocalDeclarationStatement(node);
        }

        public override void VisitForEachStatement(ForEachStatementSyntax node)
        {
            if (ctx.In(node))
            {
                AddLocalName(node.Identifier.Text);
            }

            base.VisitForEachStatement(node);
        }

        public override void VisitDeclarationExpression(DeclarationExpressionSyntax node)
        {
            if (ctx.In(node) && node.Designation is SingleVariableDesignationSyntax d)
            {
                AddLocalName(d.Identifier.Text);
            }

            base.VisitDeclarationExpression(node);
        }

        public override void VisitSimpleLambdaExpression(SimpleLambdaExpressionSyntax node)
        {
            if (ctx.In(node))
            {
                _s.LambdaParameterNames.Add(node.Parameter.Identifier.Text);
            }

            base.VisitSimpleLambdaExpression(node);
        }

        public override void VisitParenthesizedLambdaExpression(ParenthesizedLambdaExpressionSyntax node)
        {
            if (ctx.In(node))
            {
                foreach (var p in node.ParameterList.Parameters)
                {
                    _s.LambdaParameterNames.Add(p.Identifier.Text);
                }
            }

            base.VisitParenthesizedLambdaExpression(node);
        }

        public override void VisitFileScopedNamespaceDeclaration(FileScopedNamespaceDeclarationSyntax node) =>
            base.VisitFileScopedNamespaceDeclaration(node);

        public override void VisitBinaryExpression(BinaryExpressionSyntax node)
        {
            if (ctx.In(node))
            {
                if ((node.IsKind(SyntaxKind.EqualsExpression) || node.IsKind(SyntaxKind.NotEqualsExpression)) &&
                    (node.Right.IsKind(SyntaxKind.NullLiteralExpression) || node.Left.IsKind(SyntaxKind.NullLiteralExpression)))
                {
                    ctx.Hit(K.NullEqualityChecks, node);
                }
                else if (node.IsKind(SyntaxKind.AddExpression) && node.Parent is not BinaryExpressionSyntax { RawKind: (int)SyntaxKind.AddExpression } &&
                         IsStringConcat(node))
                {
                    ctx.Hit(K.StringConcat, node);
                }
            }

            base.VisitBinaryExpression(node);
        }

        public override void VisitIsPatternExpression(IsPatternExpressionSyntax node)
        {
            if (ctx.In(node))
            {
                var pattern = node.Pattern is UnaryPatternSyntax u ? u.Pattern : node.Pattern;
                if (pattern is ConstantPatternSyntax { Expression: LiteralExpressionSyntax lit } && lit.IsKind(SyntaxKind.NullLiteralExpression))
                {
                    ctx.Hit(K.NullPatternChecks, node);
                }
                else if (pattern is RecursivePatternSyntax)
                {
                    _s.Add(K.PropertyPatterns);
                }
            }

            base.VisitIsPatternExpression(node);
        }

        public override void VisitImplicitObjectCreationExpression(ImplicitObjectCreationExpressionSyntax node)
        {
            if (ctx.In(node))
            {
                _s.Add(K.ImplicitNew);
            }

            base.VisitImplicitObjectCreationExpression(node);
        }

        public override void VisitObjectCreationExpression(ObjectCreationExpressionSyntax node)
        {
            if (ctx.In(node))
            {
                _s.Add(K.ExplicitNew);
                var typeName = node.Type.ToString();
                if ((typeName.StartsWith("List<", StringComparison.Ordinal) || typeName.StartsWith("Dictionary<", StringComparison.Ordinal) ||
                     typeName.StartsWith("HashSet<", StringComparison.Ordinal)) &&
                    (node.ArgumentList is null || node.ArgumentList.Arguments.Count == 0) && node.Initializer is null)
                {
                    ctx.Hit(K.NewEmptyCollection, node);
                }
            }

            base.VisitObjectCreationExpression(node);
        }

        public override void VisitCollectionExpression(CollectionExpressionSyntax node)
        {
            if (ctx.In(node))
            {
                ctx.Hit(K.CollectionExpressions, node);
            }

            base.VisitCollectionExpression(node);
        }

        public override void VisitSwitchExpression(SwitchExpressionSyntax node)
        {
            if (ctx.In(node))
            {
                ctx.Hit(K.SwitchExpressions, node);
            }

            base.VisitSwitchExpression(node);
        }

        public override void VisitSwitchStatement(SwitchStatementSyntax node)
        {
            if (ctx.In(node))
            {
                _s.Add(K.SwitchStatements);
            }

            base.VisitSwitchStatement(node);
        }

        public override void VisitLiteralExpression(LiteralExpressionSyntax node)
        {
            if (ctx.In(node))
            {
                if (node.IsKind(SyntaxKind.StringLiteralExpression))
                {
                    var token = node.Token;
                    if (token.IsKind(SyntaxKind.SingleLineRawStringLiteralToken) || token.IsKind(SyntaxKind.MultiLineRawStringLiteralToken))
                    {
                        _s.Add(K.RawStrings);
                    }

                    var value = token.ValueText;
                    if (value.Length == 0 && node.Parent is not AttributeArgumentSyntax)
                    {
                        ctx.Hit(K.EmptyStringLiteral, node);
                    }

                    CheckStringContent(value, node);
                }
                else if (node.IsKind(SyntaxKind.NumericLiteralExpression) && IsMagicNumber(node))
                {
                    _s.Add(K.MagicNumbers);
                }
            }

            base.VisitLiteralExpression(node);
        }

        public override void VisitInterpolatedStringExpression(InterpolatedStringExpressionSyntax node)
        {
            if (ctx.In(node))
            {
                CheckStringContent(InterpolatedText(node), node);
            }

            base.VisitInterpolatedStringExpression(node);
        }

        public override void VisitMemberAccessExpression(MemberAccessExpressionSyntax node)
        {
            if (ctx.In(node))
            {
                var name = node.Name.Identifier.Text;
                var target = node.Expression.ToString();
                switch (name)
                {
                    case "Empty" when target is "string" or "String":
                        ctx.Hit(K.StringEmpty, node);
                        break;
                    case "UtcNow" when target is "DateTime" or "DateTimeOffset":
                        ctx.Hit(K.UtcNow, node);
                        break;
                    case "Now" when target is "DateTime" or "DateTimeOffset":
                        ctx.Hit(K.LocalNow, node);
                        break;
                    case "Result" when node.Parent is not InvocationExpressionSyntax && LooksLikeTaskResult(node):
                        ctx.Hit(K.SyncOverAsync, node);
                        break;
                    case "OrdinalIgnoreCase" or "Ordinal" or "InvariantCultureIgnoreCase" or "CurrentCultureIgnoreCase" when target == "StringComparison":
                        _s.Add(K.StringComparisonUse);
                        break;
                }
            }

            base.VisitMemberAccessExpression(node);
        }

        public override void VisitInvocationExpression(InvocationExpressionSyntax node)
        {
            if (ctx.In(node))
            {
                AnalyzeInvocation(node);
            }

            base.VisitInvocationExpression(node);
        }

        public override void VisitThrowStatement(ThrowStatementSyntax node)
        {
            if (ctx.In(node))
            {
                if (node.Expression is null)
                {
                    _s.Add(K.Rethrow);
                }
                else if (node.Expression is IdentifierNameSyntax)
                {
                    ctx.Hit(K.ThrowEx, node);
                }
                else
                {
                    AnalyzeThrown(node.Expression);
                }
            }

            base.VisitThrowStatement(node);
        }

        public override void VisitThrowExpression(ThrowExpressionSyntax node)
        {
            if (ctx.In(node))
            {
                AnalyzeThrown(node.Expression);
            }

            base.VisitThrowExpression(node);
        }

        public override void VisitCatchClause(CatchClauseSyntax node)
        {
            if (ctx.In(node))
            {
                _s.Add(K.CatchClauses);
                var name = node.Declaration?.Identifier.Text;
                if (name == "e")
                {
                    ctx.Hit(K.CatchVarE, node);
                }
                else if (name == "ex")
                {
                    _s.Add(K.CatchVarEx);
                }

                if (node.Block.Statements.Count == 0)
                {
                    ctx.Hit(K.EmptyCatch, node);
                }
            }

            base.VisitCatchClause(node);
        }

        public override void VisitArgument(ArgumentSyntax node)
        {
            if (node.NameColon is not null && ctx.In(node))
            {
                _s.Add(K.NamedArguments);
            }

            base.VisitArgument(node);
        }

        public override void VisitReturnStatement(ReturnStatementSyntax node)
        {
            if (ctx.In(node) && node.Parent is BlockSyntax block && block.Statements.IndexOf(node) > 0)
            {
                _s.Add(K.ReturnsChecked);
                int line = ctx.LineOf(node);
                if (ctx.LineText(line - 1).Trim().Length == 0)
                {
                    _s.Add(K.ReturnsWithBlankLine);
                }
            }

            base.VisitReturnStatement(node);
        }

        public override void VisitQueryExpression(QueryExpressionSyntax node)
        {
            if (ctx.In(node))
            {
                ctx.Hit(K.LinqQuerySyntax, node);
            }

            base.VisitQueryExpression(node);
        }

        public override void VisitAttribute(AttributeSyntax node)
        {
            if (ctx.In(node) && node.Name.ToString() is "ProducesResponseType" or "ProducesResponseTypeAttribute")
            {
                ctx.Hit(K.ProducesResponseType, node);
            }

            base.VisitAttribute(node);
        }

        // -------------------------------------------------------------- методы

        private void AnalyzeMethod(MethodDeclarationSyntax node)
        {
            _s.Add(K.Methods);
            if (node.ExplicitInterfaceSpecifier is null)
            {
                CheckAccess(node, node.Modifiers);
            }
            CheckDoc(node, node.Modifiers, node.Identifier, node.ParameterList, node.ReturnType);
            CheckParameters(node.ParameterList);
            CountCollectionTypes(node.ReturnType, node);

            if (node.ExpressionBody is not null)
            {
                _s.Add(K.ExpressionBodied);
            }

            var body = (SyntaxNode?)node.Body ?? node.ExpressionBody;
            if (body is not null)
            {
                int start = ctx.LineOf(node.Identifier);
                int end = ctx.Text.Lines.GetLineFromPosition(body.Span.End).LineNumber + 1;
                _s.MethodLengths.Add(end - start + 1);
                _s.MethodComplexities.Add(1 + body.DescendantNodes().Count(IsBranch));
                if (MaxNesting(body) >= 4)
                {
                    ctx.Hit(K.DeepNestedMethods, ctx.LineOf(node.Identifier));
                }
            }

            bool isTestMethod = node.AttributeLists.SelectMany(a => a.Attributes).Any(a => TestAttributes.Contains(a.Name.ToString()));
            if (isTestMethod)
            {
                AnalyzeTestMethod(node);
                return;
            }

            if (node.Parent is ClassDeclarationSyntax cls && cls.Identifier.Text.EndsWith("Controller", StringComparison.Ordinal) &&
                node.Modifiers.Any(SyntaxKind.PublicKeyword))
            {
                _s.Add(K.ControllerActions);
            }

            CheckGuards(node, node.Modifiers, node.ParameterList, node.Body, node.ExpressionBody);

            bool isAsync = node.Modifiers.Any(SyntaxKind.AsyncKeyword);
            var returnType = node.ReturnType.ToString();
            bool returnsTask = returnType is "Task" or "ValueTask" || returnType.StartsWith("Task<", StringComparison.Ordinal) ||
                               returnType.StartsWith("ValueTask<", StringComparison.Ordinal) || returnType.StartsWith("IAsyncEnumerable<", StringComparison.Ordinal);

            if (isAsync && returnType == "void" && !IsEventHandler(node.ParameterList))
            {
                ctx.Hit(K.AsyncVoid, ctx.LineOf(node.Identifier));
            }

            if (ctx.IsTest || node.Modifiers.Any(SyntaxKind.OverrideKeyword) || node.ExplicitInterfaceSpecifier is not null)
            {
                return;
            }

            var name = node.Identifier.Text;
            bool isMiddlewareInvoke = name is "InvokeAsync" or "Invoke" && node.ParameterList.Parameters.Any(p => p.Type?.ToString() == "HttpContext");
            if ((isAsync || returnsTask) && name != "Main" && !isMiddlewareInvoke && !IsEventHandler(node.ParameterList))
            {
                _s.Add(K.AsyncMethods);
                var ctParam = node.ParameterList.Parameters.FirstOrDefault(p => p.Type?.ToString() is "CancellationToken" or "CancellationToken?");
                if (ctParam is not null)
                {
                    _s.Add(K.AsyncWithCt);
                }
                else
                {
                    _s.AddExample(K.AsyncWithCt + ".missing", ctx.Path, ctx.LineOf(node.Identifier), ctx.LineText(ctx.LineOf(node.Identifier)));
                }

                bool isControllerAction = node.Parent is ClassDeclarationSyntax owner && owner.Identifier.Text.EndsWith("Controller", StringComparison.Ordinal);
                if (name.EndsWith("Async", StringComparison.Ordinal) || isControllerAction)
                {
                    _s.Add(K.AsyncSuffix);
                }
                else
                {
                    _s.AddExample(K.AsyncSuffix + ".missing", ctx.Path, ctx.LineOf(node.Identifier), ctx.LineText(ctx.LineOf(node.Identifier)));
                }

                if (ctParam is not null && body is not null)
                {
                    var ctName = ctParam.Identifier.Text;
                    foreach (var aw in body.DescendantNodes().OfType<AwaitExpressionSyntax>())
                    {
                        if (aw.Expression is not InvocationExpressionSyntax && aw.Expression is not MemberAccessExpressionSyntax)
                        {
                            continue;
                        }

                        var inv = aw.Expression.ToString();
                        if (inv.StartsWith("Task.WhenAll", StringComparison.Ordinal) || inv.StartsWith("Task.WhenAny", StringComparison.Ordinal))
                        {
                            continue;
                        }

                        _s.Add(K.AwaitsInCtMethods);
                        if (aw.DescendantNodes().OfType<IdentifierNameSyntax>().Any(id => id.Identifier.Text == ctName))
                        {
                            _s.Add(K.AwaitsPassingCt);
                        }
                    }
                }
            }
        }

        private void AnalyzeTestMethod(MethodDeclarationSyntax node)
        {
            _s.Add(K.TestMethods);
            var name = node.Identifier.Text;
            int underscores = name.Count(c => c == '_');
            if (underscores >= 2)
            {
                ctx.Hit(K.TestNamePattern, ctx.LineOf(node.Identifier));
            }
            else if (name.Contains("Should", StringComparison.Ordinal))
            {
                _s.Add(K.TestNameShould);
            }

            if (char.IsLower(name[0]))
            {
                _s.Add(K.TestNameLower);
            }

            var attrs = node.AttributeLists.SelectMany(a => a.Attributes).Select(a => a.Name.ToString()).ToList();
            if (attrs.Any(a => a is "Theory" or "TestCase" or "DataTestMethod" or "TestCaseSource"))
            {
                _s.Add(K.TestTheory);
                _s.Add(K.TestInlineData, attrs.Count(a => a is "InlineData" or "TestCase" or "DataRow"));
            }

            var full = node.ToFullString();
            if (AaaRegex().IsMatch(full))
            {
                ctx.Hit(K.TestAaa, ctx.LineOf(node.Identifier));
            }
        }

        private void AnalyzeInvocation(InvocationExpressionSyntax node)
        {
            string? name = node.Expression switch
            {
                MemberAccessExpressionSyntax m => m.Name.Identifier.Text,
                IdentifierNameSyntax id => id.Identifier.Text,
                GenericNameSyntax g => g.Identifier.Text,
                _ => null,
            };
            if (name is null)
            {
                return;
            }

            var target = node.Expression is MemberAccessExpressionSyntax ma ? ma.Expression.ToString() : "";

            if (name == "nameof")
            {
                _s.Add(K.Nameof);
                return;
            }

            switch (name)
            {
                case "IsNullOrWhiteSpace" when target is "string" or "String":
                    _s.Add(K.IsNullOrWhiteSpace);
                    return;
                case "IsNullOrEmpty" when target is "string" or "String":
                    ctx.Hit(K.IsNullOrEmpty, node);
                    return;
                case "ToLowerInvariant" or "ToUpperInvariant":
                    _s.Add(K.ToLowerInvariant);
                    return;
                case "ToLower" or "ToUpper" when node.ArgumentList.Arguments.Count == 0:
                    ctx.Hit(K.ToLowerPlain, node);
                    return;
                case "ConfigureAwait" when node.ArgumentList.Arguments.FirstOrDefault()?.Expression.IsKind(SyntaxKind.FalseLiteralExpression) == true:
                    _s.Add(K.ConfigureAwaitFalse);
                    return;
                case "Wait" when node.ArgumentList.Arguments.Count == 0 && node.Parent is ExpressionStatementSyntax:
                case "GetResult" when target.EndsWith("GetAwaiter()", StringComparison.Ordinal):
                    ctx.Hit(K.SyncOverAsync, node);
                    return;
                case "WriteLine" or "Write" when target == "Console":
                    ctx.Hit(K.ConsoleWrites, node);
                    return;
                case "Format" when target is "string" or "String":
                    ctx.Hit(K.StringFormat, node);
                    return;
                case "Sleep" when target == "Thread":
                    ctx.Hit(K.ThreadSleep, node);
                    return;
                case "AsNoTracking":
                    _s.Add(K.AsNoTracking);
                    return;
                case "ThrowIfNull" or "ThrowIfNullOrEmpty" or "ThrowIfNullOrWhiteSpace" or "ThrowIfNegative" or "ThrowIfNegativeOrZero"
                    or "ThrowIfZero" or "ThrowIfGreaterThan" or "ThrowIfLessThan" or "ThrowIfEqual" or "ThrowIfNotEqual"
                    when target.EndsWith("Exception", StringComparison.Ordinal):
                    ctx.Hit(K.GuardThrowIfHelpers, node);
                    return;
            }

            if (LogMethodRegex.IsMatch(name) || (name == "Log" && node.ArgumentList.Arguments.Count >= 2 && target.Contains("ogger", StringComparison.OrdinalIgnoreCase)))
            {
                AnalyzeLogCall(node);
            }
        }

        private void AnalyzeLogCall(InvocationExpressionSyntax node)
        {
            var args = node.ArgumentList.Arguments;
            for (int i = 0; i < args.Count; i++)
            {
                var expr = args[i].Expression;
                switch (expr)
                {
                    case InterpolatedStringExpressionSyntax:
                        _s.Add(K.LogCalls);
                        ctx.Hit(K.LogInterpolated, node);
                        return;
                    case BinaryExpressionSyntax b when b.IsKind(SyntaxKind.AddExpression) && IsStringConcat(b):
                        _s.Add(K.LogCalls);
                        ctx.Hit(K.LogInterpolated, node);
                        return;
                    case InvocationExpressionSyntax inv when inv.Expression.ToString() is "string.Format" or "String.Format":
                        _s.Add(K.LogCalls);
                        ctx.Hit(K.LogInterpolated, node);
                        return;
                    case LiteralExpressionSyntax lit when lit.IsKind(SyntaxKind.StringLiteralExpression):
                        _s.Add(K.LogCalls);
                        if (TemplatePlaceholderRegex.IsMatch(lit.Token.ValueText) && args.Count > i + 1)
                        {
                            ctx.Hit(K.LogTemplated, node);
                        }
                        else
                        {
                            _s.Add(K.LogPlain);
                        }

                        return;
                }
            }
        }

        private void AnalyzeThrown(ExpressionSyntax expr)
        {
            if (expr is not BaseObjectCreationExpressionSyntax creation)
            {
                return;
            }

            var typeName = creation is ObjectCreationExpressionSyntax oc ? oc.Type.ToString() : "";
            if (typeName is "Exception" or "System.Exception" or "ApplicationException" or "SystemException")
            {
                ctx.Hit(K.GenericExceptionThrows, expr);
            }
            else if (typeName.EndsWith("Exception", StringComparison.Ordinal))
            {
                _s.Add(K.SpecificExceptionThrows);
            }

            var args = creation.ArgumentList?.Arguments;
            if (args is null || args.Value.Count == 0)
            {
                return;
            }

            if (args.Value.Any(a => a.Expression is InvocationExpressionSyntax { Expression: IdentifierNameSyntax { Identifier.Text: "nameof" } }))
            {
                _s.Add(K.ThrowWithNameof);
            }

            string? message = args.Value[0].Expression switch
            {
                LiteralExpressionSyntax lit when lit.IsKind(SyntaxKind.StringLiteralExpression) => lit.Token.ValueText,
                InterpolatedStringExpressionSyntax interp => InterpolatedText(interp),
                _ => null,
            };
            if (message is null || (typeName.StartsWith("Argument", StringComparison.Ordinal) && args.Value.Count == 1 && message.Length < 25))
            {
                return;
            }

            _s.Add(K.ThrowMessages);
            var trimmed = message.Trim();
            if (trimmed.Length == 0)
            {
                return;
            }

            if (CyrillicRegex().IsMatch(trimmed))
            {
                ctx.Hit(K.ThrowMessageCyrillic, expr);
            }

            var first = trimmed.FirstOrDefault(char.IsLetter);
            if (first != default && char.IsUpper(first) && trimmed.EndsWith('.') && trimmed.Split(' ').Length >= 3)
            {
                ctx.Hit(K.ThrowMessageSentence, expr);
            }
            else if (first != default && char.IsLower(first))
            {
                ctx.Hit(K.ThrowMessageLowercase, expr);
            }

            if (trimmed.Contains("'{", StringComparison.Ordinal))
            {
                ctx.Hit(K.ThrowMessageQuoted, expr);
            }
        }

        // -------------------------------------------------------------- вспомогательные проверки

        private void CheckDoc(SyntaxNode node, SyntaxTokenList modifiers, SyntaxToken identifier, ParameterListSyntax? parameters = null, TypeSyntax? returnType = null)
        {
            bool inInterface = node.Parent is InterfaceDeclarationSyntax;
            bool isPublicApi = modifiers.Any(SyntaxKind.PublicKeyword) || modifiers.Any(SyntaxKind.ProtectedKeyword) || inInterface;
            if (!isPublicApi || ctx.IsTest)
            {
                return;
            }

            _s.Add(K.DocTargets);
            var doc = node.GetLeadingTrivia()
                .Select(t => t.GetStructure())
                .OfType<DocumentationCommentTriviaSyntax>()
                .FirstOrDefault();
            if (doc is null)
            {
                return;
            }

            _s.Add(K.DocTargetsDocumented);
            _s.Add(K.DocBlocks);
            var docText = doc.ToFullString();
            if (docText.Contains("inheritdoc", StringComparison.Ordinal))
            {
                _s.Add(K.InheritDoc);
                return;
            }

            if (docText.Contains("<exception cref", StringComparison.Ordinal))
            {
                _s.Add(K.DocExceptionTags);
            }

            if (TemplateDocPhrases.Any(p => docText.Contains(p, StringComparison.OrdinalIgnoreCase)))
            {
                ctx.Hit(K.DocTemplatePhrase, ctx.LineOf(doc));
            }

            if (parameters is { Parameters.Count: > 0 })
            {
                _s.Add(K.DocMethodsWithParams);
                var elements = doc.Content.OfType<XmlElementSyntax>().Select(e => e.StartTag.Name.LocalName.Text).ToList();
                int paramDocs = elements.Count(e => e == "param");
                bool needsReturns = returnType is not null && returnType.ToString() is not "void" and not "Task" and not "ValueTask";
                bool hasReturns = elements.Contains("returns");
                if (paramDocs >= parameters.Parameters.Count && (!needsReturns || hasReturns))
                {
                    ctx.Hit(K.DocMethodsComplete, ctx.LineOf(identifier));
                }
            }
        }

        private void CheckParameters(ParameterListSyntax list)
        {
            foreach (var p in list.Parameters)
            {
                var name = p.Identifier.Text;
                var type = p.Type?.ToString() ?? "";
                if (type is "CancellationToken" or "CancellationToken?")
                {
                    _s.Add(K.CtParams);
                    if (name == "cancellationToken")
                    {
                        _s.Add(K.CtFullName);
                    }
                    else
                    {
                        ctx.Hit(K.CtShortName, p);
                    }

                    if (p.Default is not null)
                    {
                        _s.Add(K.CtDefault);
                    }

                    continue;
                }

                if (name.Length > 0 && name != "_")
                {
                    _s.ParameterNames.Add(name);
                }
            }
        }

        private void CheckGuards(SyntaxNode member, SyntaxTokenList modifiers, ParameterListSyntax parameters, BlockSyntax? body, ArrowExpressionClauseSyntax? arrow)
        {
            if (ctx.IsTest || !modifiers.Any(SyntaxKind.PublicKeyword) || body is null)
            {
                return;
            }

            var refParams = parameters.Parameters
                .Where(p => p.Type is not null && IsLikelyReferenceType(p.Type.ToString()))
                .Select(p => p.Identifier.Text)
                .ToHashSet(StringComparer.Ordinal);
            if (refParams.Count == 0)
            {
                return;
            }

            _s.Add(K.GuardableMembers);
            bool guarded = false;
            foreach (var stmt in body.Statements.Take(Math.Max(3, refParams.Count + 1)))
            {
                var text = stmt.ToString();
                if (text.Contains("ThrowIf", StringComparison.Ordinal) || text.Contains("Guard.Against.", StringComparison.Ordinal) ||
                    text.Contains("Guard.NotNull", StringComparison.Ordinal) || text.Contains("Check.NotNull", StringComparison.Ordinal) ||
                    text.Contains("Ensure.", StringComparison.Ordinal))
                {
                    guarded = true;
                }
                else if (stmt is IfStatementSyntax ifs && ifs.Statement.ToString().Contains("throw new Argument", StringComparison.Ordinal))
                {
                    guarded = true;
                    ctx.Hit(K.GuardIfThrow, stmt);
                }
                else if (text.Contains("?? throw new ArgumentNullException", StringComparison.Ordinal))
                {
                    guarded = true;
                    ctx.Hit(K.GuardCoalesceThrow, stmt);
                }
            }

            if (!guarded)
            {
                // Конструкторы часто пишут проверки в присваиваниях полей ниже первых строк.
                foreach (var coalesce in body.DescendantNodes().OfType<ThrowExpressionSyntax>())
                {
                    if (coalesce.Expression.ToString().StartsWith("new ArgumentNullException", StringComparison.Ordinal))
                    {
                        guarded = true;
                        ctx.Hit(K.GuardCoalesceThrow, coalesce);
                        break;
                    }
                }
            }

            if (guarded)
            {
                _s.Add(K.GuardedMembers);
            }
        }

        private void CheckAccess(MemberDeclarationSyntax member, SyntaxTokenList modifiers)
        {
            if (member.Parent is not (ClassDeclarationSyntax or StructDeclarationSyntax or RecordDeclarationSyntax))
            {
                return;
            }

            _s.Add(K.MembersChecked);
            if (modifiers.Any(SyntaxKind.PublicKeyword) || modifiers.Any(SyntaxKind.PrivateKeyword) || modifiers.Any(SyntaxKind.ProtectedKeyword) ||
                modifiers.Any(SyntaxKind.InternalKeyword))
            {
                _s.Add(K.ExplicitAccess);
            }
            else
            {
                _s.AddExample(K.ExplicitAccess + ".missing", ctx.Path, ctx.LineOf(member), ctx.LineText(ctx.LineOf(member)));
            }
        }

        private void CheckMemberOrder(TypeDeclarationSyntax type)
        {
            if (type.Members.Count < 4)
            {
                return;
            }

            _s.Add(K.TypesOrderChecked);
            int maxRank = -1;
            int inversions = 0;
            foreach (var m in type.Members)
            {
                int rank = m switch
                {
                    FieldDeclarationSyntax f when f.Modifiers.Any(SyntaxKind.ConstKeyword) => 0,
                    FieldDeclarationSyntax f when f.Modifiers.Any(SyntaxKind.StaticKeyword) => 1,
                    FieldDeclarationSyntax => 2,
                    ConstructorDeclarationSyntax => 3,
                    PropertyDeclarationSyntax => 4,
                    MethodDeclarationSyntax md when md.Modifiers.Any(SyntaxKind.PublicKeyword) => 5,
                    MethodDeclarationSyntax => 6,
                    _ => -1,
                };
                if (rank < 0)
                {
                    continue;
                }

                if (rank < maxRank && !(rank == 5 && maxRank == 6))
                {
                    inversions++;
                }

                maxRank = Math.Max(maxRank, rank);
            }

            if (inversions == 0)
            {
                _s.Add(K.TypesOrdered);
            }
        }

        private void CountCollectionTypes(TypeSyntax type, SyntaxNode at)
        {
            var t = type.ToString();
            if (t.StartsWith("IReadOnlyList<", StringComparison.Ordinal) || t.StartsWith("IReadOnlyCollection<", StringComparison.Ordinal) ||
                t.StartsWith("IReadOnlyDictionary<", StringComparison.Ordinal) || t.Contains("<IReadOnlyList<", StringComparison.Ordinal) ||
                t.Contains("<IReadOnlyCollection<", StringComparison.Ordinal))
            {
                _s.Add(K.ReadOnlyCollections);
            }
            else if (t.StartsWith("List<", StringComparison.Ordinal) || t.Contains("<List<", StringComparison.Ordinal) ||
                     t.StartsWith("Dictionary<", StringComparison.Ordinal) || t.EndsWith("[]", StringComparison.Ordinal))
            {
                _s.Add(K.MutableCollectionsInSignatures);
            }
        }

        private void AddLocalName(string name)
        {
            if (name.Length > 0 && name != "_")
            {
                _s.LocalNames.Add(name);
            }
        }

        private void CheckStringContent(string value, SyntaxNode node)
        {
            if (ContainsEmoji(value))
            {
                ctx.Hit(K.EmojiInStrings, node);
            }

            if (ctx.IsTest && (value.Contains("example.com", StringComparison.OrdinalIgnoreCase) || value.Contains("John Doe", StringComparison.Ordinal) ||
                               value.Contains("test@", StringComparison.OrdinalIgnoreCase) || value.Contains("Test User", StringComparison.Ordinal)))
            {
                ctx.Hit(K.TestExampleData, node);
            }
        }
    }

    // ------------------------------------------------------------------ утилиты

    [GeneratedRegex(@"//\s*(Arrange|Act|Assert|Given|When|Then)\b", RegexOptions.IgnoreCase)]
    private static partial Regex AaaRegex();

    private static bool IsBranch(SyntaxNode n) => n.Kind() is SyntaxKind.IfStatement or SyntaxKind.ConditionalExpression or SyntaxKind.CaseSwitchLabel
        or SyntaxKind.CasePatternSwitchLabel or SyntaxKind.SwitchExpressionArm or SyntaxKind.ForStatement or SyntaxKind.ForEachStatement
        or SyntaxKind.WhileStatement or SyntaxKind.DoStatement or SyntaxKind.CatchClause or SyntaxKind.LogicalAndExpression
        or SyntaxKind.LogicalOrExpression or SyntaxKind.CoalesceExpression;

    /// <summary>Максимальная глубина вложенности управляющих конструкций (else-if не увеличивает глубину).</summary>
    internal static int MaxNesting(SyntaxNode body)
    {
        int max = 0;
        foreach (var n in body.DescendantNodes().Where(IsNestingStatement))
        {
            int depth = 0;
            for (var cur = n; cur is not null && cur != body; cur = cur.Parent)
            {
                if (IsNestingStatement(cur) && !(cur is IfStatementSyntax && cur.Parent is ElseClauseSyntax))
                {
                    depth++;
                }
            }

            max = Math.Max(max, depth);
        }

        return max;
    }

    private static bool IsNestingStatement(SyntaxNode n) => n.Kind() is SyntaxKind.IfStatement or SyntaxKind.ForStatement or SyntaxKind.ForEachStatement
        or SyntaxKind.WhileStatement or SyntaxKind.DoStatement or SyntaxKind.SwitchStatement or SyntaxKind.TryStatement or SyntaxKind.UsingStatement
        or SyntaxKind.LockStatement;

    private static bool IsEventHandler(ParameterListSyntax list) =>
        list.Parameters.Count == 2 && list.Parameters[0].Type?.ToString() is "object" or "object?";

    private static bool IsStringConcat(BinaryExpressionSyntax node)
    {
        bool hasLiteral = false;
        bool hasNonLiteral = false;
        void Walk(ExpressionSyntax e)
        {
            if (e is BinaryExpressionSyntax b && b.IsKind(SyntaxKind.AddExpression))
            {
                Walk(b.Left);
                Walk(b.Right);
            }
            else if (e is LiteralExpressionSyntax l && l.IsKind(SyntaxKind.StringLiteralExpression))
            {
                hasLiteral = true;
            }
            else
            {
                hasNonLiteral = true;
            }
        }

        Walk(node);
        return hasLiteral && hasNonLiteral;
    }

    private static bool LooksLikeTaskResult(MemberAccessExpressionSyntax node)
    {
        var target = node.Expression.ToString();
        return target.EndsWith("Async()", StringComparison.Ordinal) || target.EndsWith("Task", StringComparison.Ordinal) ||
               target.EndsWith("task", StringComparison.Ordinal) || (node.Expression is InvocationExpressionSyntax inv && inv.ToString().Contains("Async(", StringComparison.Ordinal));
    }

    private static bool IsMagicNumber(LiteralExpressionSyntax node)
    {
        var v = node.Token.ValueText;
        if (v is "0" or "1" or "-1" or "2" or "100")
        {
            return false;
        }

        return node.Parent is BinaryExpressionSyntax or ArgumentSyntax && node.Ancestors().All(a => a is not FieldDeclarationSyntax && a is not AttributeSyntax);
    }

    internal static bool IsLikelyReferenceType(string type)
    {
        if (type.EndsWith('?'))
        {
            return false;
        }

        return type switch
        {
            "int" or "long" or "short" or "byte" or "bool" or "double" or "float" or "decimal" or "char" or "uint" or "ulong" or "Guid"
                or "DateTime" or "DateTimeOffset" or "TimeSpan" or "CancellationToken" or "DateOnly" or "TimeOnly" => false,
            _ => !type.StartsWith("ref ", StringComparison.Ordinal) && !type.StartsWith("out ", StringComparison.Ordinal),
        };
    }

    private static string InterpolatedText(InterpolatedStringExpressionSyntax node)
    {
        var sb = new System.Text.StringBuilder();
        foreach (var c in node.Contents)
        {
            if (c is InterpolatedStringTextSyntax t)
            {
                sb.Append(t.TextToken.ValueText);
            }
            else if (c is InterpolationSyntax i)
            {
                sb.Append('{').Append(i.Expression.ToString()).Append('}');
            }
        }

        return sb.ToString();
    }

    internal static bool ContainsEmoji(string s)
    {
        for (int i = 0; i < s.Length; i++)
        {
            int cp = char.IsSurrogatePair(s, i) ? char.ConvertToUtf32(s, i) : s[i];
            if (cp is >= 0x1F300 and <= 0x1FAFF or >= 0x2600 and <= 0x27BF or 0x2B50 or 0x2B55 or 0x2705 or 0x274C or 0x2714 or 0x2716)
            {
                return true;
            }

            if (char.IsSurrogatePair(s, i))
            {
                i++;
            }
        }

        return false;
    }
}
