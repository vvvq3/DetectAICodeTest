using System.Text.RegularExpressions;

namespace AiDetect.Languages.Frontend.Parsing;

/// <summary>Блок однофайлового компонента Vue: template / script / style.</summary>
public sealed record SfcBlock(string Kind, string Content, int LineOffset, IReadOnlyDictionary<string, string> Attrs)
{
    public string? Lang => Attrs.TryGetValue("lang", out var l) ? l.ToLowerInvariant() : null;

    public bool Has(string attr) => Attrs.ContainsKey(attr);
}

/// <summary>Разбор .vue на блоки верхнего уровня (вложенные &lt;template&gt; внутри шаблона учитываются).</summary>
public static partial class VueSfc
{
    [GeneratedRegex(@"<(template|script|style)\b([^>]*)>", RegexOptions.IgnoreCase)]
    private static partial Regex BlockOpen();

    [GeneratedRegex(@"</?template\b[^>]*>", RegexOptions.IgnoreCase)]
    private static partial Regex TemplateTag();

    [GeneratedRegex(@"([:@#\w-]+)(?:\s*=\s*(?:""([^""]*)""|'([^']*)'|([^\s>]+)))?")]
    private static partial Regex AttrRegex();

    public static List<SfcBlock> Parse(string source)
    {
        var blocks = new List<SfcBlock>();
        int pos = 0;
        while (pos < source.Length)
        {
            var m = BlockOpen().Match(source, pos);
            if (!m.Success)
            {
                break;
            }

            var kind = m.Groups[1].Value.ToLowerInvariant();
            var attrs = ParseAttrs(m.Groups[2].Value);
            int contentStart = m.Index + m.Length;
            int contentEnd;
            int next;
            if (m.Groups[2].Value.TrimEnd().EndsWith('/'))
            {
                pos = contentStart;
                continue;
            }

            if (kind == "template")
            {
                int depth = 1;
                var t = TemplateTag().Match(source, contentStart);
                contentEnd = source.Length;
                next = source.Length;
                while (t.Success)
                {
                    if (t.Value.StartsWith("</", StringComparison.Ordinal))
                    {
                        depth--;
                    }
                    else if (!t.Value.EndsWith("/>", StringComparison.Ordinal))
                    {
                        depth++;
                    }

                    if (depth == 0)
                    {
                        contentEnd = t.Index;
                        next = t.Index + t.Length;
                        break;
                    }

                    t = t.NextMatch();
                }
            }
            else
            {
                int end = source.IndexOf("</" + kind, contentStart, StringComparison.OrdinalIgnoreCase);
                contentEnd = end < 0 ? source.Length : end;
                int gt = end < 0 ? -1 : source.IndexOf('>', end);
                next = gt < 0 ? source.Length : gt + 1;
            }

            int lineOffset = CountLines(source, contentStart);
            blocks.Add(new SfcBlock(kind, source[contentStart..contentEnd], lineOffset, attrs));
            pos = next;
        }

        return blocks;
    }

    /// <summary>Сколько переводов строк до позиции (смещение номеров строк блока).</summary>
    public static int CountLines(string s, int end)
    {
        int n = 0;
        for (int i = 0; i < end && i < s.Length; i++)
        {
            if (s[i] == '\n')
            {
                n++;
            }
        }

        return n;
    }

    public static Dictionary<string, string> ParseAttrs(string text)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (Match a in AttrRegex().Matches(text))
        {
            var name = a.Groups[1].Value;
            var value = a.Groups[2].Success ? a.Groups[2].Value : a.Groups[3].Success ? a.Groups[3].Value : a.Groups[4].Success ? a.Groups[4].Value : "";
            result.TryAdd(name, value);
        }

        return result;
    }
}

public sealed record HtmlAttr(string Name, string? Value, int Line);

public sealed record HtmlElement(string Name, int Line, IReadOnlyList<HtmlAttr> Attrs)
{
    public HtmlAttr? Attr(string name) => Attrs.FirstOrDefault(a => a.Name.Equals(name, StringComparison.OrdinalIgnoreCase));

    public bool HasAttr(string name) => Attr(name) is not null;
}

public sealed record TextItem(string Text, int Line);

/// <summary>Результат сканирования HTML-шаблона (Angular .html, Vue &lt;template&gt;).</summary>
public sealed class HtmlScan
{
    public List<HtmlElement> Elements { get; } = [];
    public List<TextItem> Comments { get; } = [];
    public List<TextItem> Texts { get; } = [];
    public List<TextItem> Interpolations { get; } = [];

    /// <summary>Блоки управления Angular 17+: @if, @for, @switch, @defer.</summary>
    public List<TextItem> ControlFlow { get; } = [];
}

/// <summary>Лёгкий сканер HTML-шаблонов: элементы с атрибутами, комментарии, текст, интерполяции {{ }}.</summary>
public static partial class HtmlScanner
{
    [GeneratedRegex(@"@(if|for|switch|defer|else|case|empty)\b")]
    private static partial Regex ControlFlowRegex();

    public static HtmlScan Scan(string s, int lineOffset = 0)
    {
        var result = new HtmlScan();
        int line = 1 + lineOffset;
        int i = 0;
        int textStart = 0;
        int textLine = line;

        void FlushText(int end)
        {
            if (end <= textStart)
            {
                return;
            }

            var text = s[textStart..end];
            int tl = textLine;
            foreach (var raw in text.Split('\n'))
            {
                var t = raw.Trim();
                if (t.Length > 0)
                {
                    int p = 0;
                    while ((p = t.IndexOf("{{", p, StringComparison.Ordinal)) >= 0)
                    {
                        int e = t.IndexOf("}}", p + 2, StringComparison.Ordinal);
                        if (e < 0)
                        {
                            break;
                        }

                        result.Interpolations.Add(new TextItem(t[(p + 2)..e].Trim(), tl));
                        p = e + 2;
                    }

                    var plain = Regex.Replace(t, @"\{\{.*?\}\}", " ").Trim();
                    if (plain.Length > 0)
                    {
                        result.Texts.Add(new TextItem(plain, tl));
                    }

                    foreach (Match m in ControlFlowRegex().Matches(t))
                    {
                        result.ControlFlow.Add(new TextItem(m.Value, tl));
                    }
                }

                tl++;
            }
        }

        while (i < s.Length)
        {
            char c = s[i];
            if (c == '<' && string.CompareOrdinal(s, i, "<!--", 0, 4) == 0)
            {
                FlushText(i);
                int end = s.IndexOf("-->", i + 4, StringComparison.Ordinal);
                end = end < 0 ? s.Length : end + 3;
                result.Comments.Add(new TextItem(s[(i + 4)..Math.Max(i + 4, end - 3)].Trim(), line));
                line += CountNewLines(s, i, end);
                i = end;
                textStart = i;
                textLine = line;
                continue;
            }

            if (c == '<' && i + 1 < s.Length && (char.IsLetter(s[i + 1]) || s[i + 1] == '/'))
            {
                FlushText(i);
                int tagLine = line;
                int j = i + 1;
                bool closing = s[j] == '/';
                if (closing)
                {
                    j++;
                }

                int ns = j;
                while (j < s.Length && (char.IsLetterOrDigit(s[j]) || s[j] is '-' or ':' or '.' or '_'))
                {
                    j++;
                }

                var name = s[ns..j];
                var attrs = new List<HtmlAttr>();
                int attrLine = line;
                while (j < s.Length && s[j] != '>')
                {
                    char a = s[j];
                    if (a == '\n')
                    {
                        attrLine++;
                        j++;
                        continue;
                    }

                    if (char.IsWhiteSpace(a) || a == '/')
                    {
                        j++;
                        continue;
                    }

                    int an = j;
                    while (j < s.Length && !char.IsWhiteSpace(s[j]) && s[j] is not ('=' or '>') && !(s[j] == '/' && j + 1 < s.Length && s[j + 1] == '>'))
                    {
                        j++;
                    }

                    var attrName = s[an..j];
                    string? value = null;
                    int k = j;
                    while (k < s.Length && s[k] is ' ' or '\t')
                    {
                        k++;
                    }

                    if (k < s.Length && s[k] == '=')
                    {
                        k++;
                        while (k < s.Length && char.IsWhiteSpace(s[k]))
                        {
                            if (s[k] == '\n')
                            {
                                attrLine++;
                            }

                            k++;
                        }

                        if (k < s.Length && s[k] is '"' or '\'')
                        {
                            char q = s[k];
                            int vs = k + 1;
                            int ve = s.IndexOf(q, vs);
                            ve = ve < 0 ? s.Length : ve;
                            value = s[vs..ve];
                            int startLine = attrLine;
                            attrLine += CountNewLines(s, vs, ve);
                            attrs.Add(new HtmlAttr(attrName, value, startLine));
                            j = Math.Min(ve + 1, s.Length);
                            continue;
                        }

                        int us = k;
                        while (k < s.Length && !char.IsWhiteSpace(s[k]) && s[k] != '>')
                        {
                            k++;
                        }

                        value = s[us..k];
                        j = k;
                    }

                    if (attrName.Length > 0)
                    {
                        attrs.Add(new HtmlAttr(attrName, value, attrLine));
                    }
                    else
                    {
                        j++;
                    }
                }

                line = attrLine;
                if (!closing && name.Length > 0)
                {
                    result.Elements.Add(new HtmlElement(name, tagLine, attrs));
                }

                i = Math.Min(j + 1, s.Length);

                // Содержимое script/style не разбираем как разметку.
                if (!closing && name is "script" or "style")
                {
                    int end = s.IndexOf("</" + name, i, StringComparison.OrdinalIgnoreCase);
                    end = end < 0 ? s.Length : end;
                    line += CountNewLines(s, i, end);
                    i = end;
                }

                textStart = i;
                textLine = line;
                continue;
            }

            if (c == '\n')
            {
                line++;
            }

            i++;
        }

        FlushText(s.Length);
        return result;
    }

    private static int CountNewLines(string s, int from, int to)
    {
        int n = 0;
        for (int i = from; i < to && i < s.Length; i++)
        {
            if (s[i] == '\n')
            {
                n++;
            }
        }

        return n;
    }
}

public sealed record CssDeclaration(string Property, string Value, int Line, int Depth);

public sealed record CssRule(string Selector, int Line, int Depth);

/// <summary>Результат сканирования CSS/SCSS/LESS.</summary>
public sealed class CssScan
{
    public List<CssDeclaration> Declarations { get; } = [];
    public List<CssRule> Rules { get; } = [];
    public List<TextItem> Comments { get; } = [];
    public List<TextItem> AtRules { get; } = [];
    public int MaxDepth { get; set; }
}

/// <summary>Лёгкий сканер CSS/SCSS/LESS: селекторы, объявления, at-правила, комментарии, вложенность.</summary>
public static class CssScanner
{
    public static CssScan Scan(string s, int lineOffset = 0, bool lineComments = true)
    {
        var result = new CssScan();
        int line = 1 + lineOffset;
        int depth = 0;
        int i = 0;
        var buf = new System.Text.StringBuilder();
        int bufLine = line;
        int parens = 0;
        char quote = '\0';

        void Flush(bool asSelector)
        {
            var text = buf.ToString().Trim();
            buf.Clear();
            if (text.Length == 0)
            {
                return;
            }

            if (asSelector)
            {
                if (text.StartsWith('@'))
                {
                    result.AtRules.Add(new TextItem(text, bufLine));
                }
                else
                {
                    result.Rules.Add(new CssRule(text, bufLine, depth));
                }

                return;
            }

            if (text.StartsWith('@'))
            {
                result.AtRules.Add(new TextItem(text, bufLine));
                return;
            }

            int colon = text.IndexOf(':');
            if (colon > 0)
            {
                result.Declarations.Add(new CssDeclaration(text[..colon].Trim(), text[(colon + 1)..].Trim(), bufLine, depth));
            }
        }

        while (i < s.Length)
        {
            char c = s[i];
            if (quote != '\0')
            {
                buf.Append(c);
                if (c == '\\' && i + 1 < s.Length)
                {
                    buf.Append(s[i + 1]);
                    i += 2;
                    continue;
                }

                if (c == quote || c == '\n')
                {
                    quote = '\0';
                }

                if (c == '\n')
                {
                    line++;
                }

                i++;
                continue;
            }

            if (c == '/' && i + 1 < s.Length && s[i + 1] == '*')
            {
                int end = s.IndexOf("*/", i + 2, StringComparison.Ordinal);
                end = end < 0 ? s.Length : end + 2;
                result.Comments.Add(new TextItem(s[(i + 2)..Math.Max(i + 2, end - 2)].Trim(), line));
                for (int k = i; k < end; k++)
                {
                    if (s[k] == '\n')
                    {
                        line++;
                    }
                }

                i = end;
                continue;
            }

            if (lineComments && c == '/' && i + 1 < s.Length && s[i + 1] == '/' && parens == 0 && (i == 0 || char.IsWhiteSpace(s[i - 1]) || s[i - 1] is ';' or '{' or '}'))
            {
                int end = s.IndexOf('\n', i);
                end = end < 0 ? s.Length : end;
                result.Comments.Add(new TextItem(s[(i + 2)..end].Trim(), line));
                i = end;
                continue;
            }

            if (buf.Length == 0 && !char.IsWhiteSpace(c))
            {
                bufLine = line;
            }

            switch (c)
            {
                case '\n':
                    line++;
                    if (buf.Length > 0)
                    {
                        buf.Append(' ');
                    }

                    break;
                case ' ' or '\t' or '\r' when buf.Length == 0:
                    break;
                case '"' or '\'':
                    quote = c;
                    buf.Append(c);
                    break;
                case '(':
                    parens++;
                    buf.Append(c);
                    break;
                case ')':
                    parens = Math.Max(0, parens - 1);
                    buf.Append(c);
                    break;
                case '{' when parens == 0:
                    if (i > 0 && s[i - 1] == '#')
                    {
                        // SCSS-интерполяция #{...}
                        int end = s.IndexOf('}', i);
                        end = end < 0 ? s.Length : end + 1;
                        buf.Append(s[i..end]);
                        i = end;
                        continue;
                    }

                    Flush(asSelector: true);
                    depth++;
                    result.MaxDepth = Math.Max(result.MaxDepth, depth);
                    break;
                case '}' when parens == 0:
                    Flush(asSelector: false);
                    depth = Math.Max(0, depth - 1);
                    break;
                case ';' when parens == 0:
                    Flush(asSelector: false);
                    break;
                default:
                    buf.Append(c);
                    break;
            }

            i++;
        }

        Flush(asSelector: false);
        return result;
    }
}
