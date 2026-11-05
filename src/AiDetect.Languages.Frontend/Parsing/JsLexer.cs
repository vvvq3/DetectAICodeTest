using System.Text;

namespace AiDetect.Languages.Frontend.Parsing;

public enum TokenKind
{
    Identifier,
    Keyword,
    Number,
    String,

    /// <summary>Шаблонная строка `...`; выражения ${...} идут отдельными токенами следом за ней.</summary>
    Template,
    Regex,
    Punct,
    LineComment,
    BlockComment,

    /// <summary>Открывающий JSX-тег: Text — имя элемента («» для фрагмента).</summary>
    JsxOpen,

    /// <summary>Закрывающий JSX-тег или конец самозакрывающегося.</summary>
    JsxClose,

    /// <summary>Атрибут JSX: Text — имя, Value — строковое значение (если задано строкой).</summary>
    JsxAttr,
    JsxText,
    JsxExprStart,
    JsxExprEnd,
}

/// <summary>Токен JS/TS. Line/EndLine — 1-based номера строк начала и конца.</summary>
public sealed class Token
{
    public TokenKind Kind { get; init; }
    public string Text { get; set; } = "";
    public int Line { get; set; }
    public int EndLine { get; set; }
    public int Start { get; init; }
    public int End { get; set; }

    /// <summary>Значение атрибута JSX (строка) или null.</summary>
    public string? Value { get; set; }

    /// <summary>Атрибут JSX задан выражением {…}.</summary>
    public bool ExprValue { get; set; }

    /// <summary>Комментарий внутри JSX: {/* … */}.</summary>
    public bool InJsx { get; set; }

    /// <summary>Шаблонная строка с подстановками ${…}.</summary>
    public bool HasSubstitutions { get; set; }

    /// <summary>JSX-тег самозакрывающийся (&lt;X /&gt;).</summary>
    public bool SelfClosing { get; set; }

    /// <summary>Перед токеном был перевод строки (для ASI и форматирования).</summary>
    public bool NewLineBefore { get; set; }

    public bool Is(string punct) => Kind == TokenKind.Punct && Text == punct;

    public bool IsKw(string kw) => (Kind == TokenKind.Keyword || Kind == TokenKind.Identifier) && Text == kw;

    public bool IsComment => Kind is TokenKind.LineComment or TokenKind.BlockComment;

    public override string ToString() => $"{Kind}:{Text}@{Line}";
}

/// <summary>
/// Лексер JavaScript/TypeScript/JSX/TSX без внешних зависимостей. Устойчив к обрывкам и ошибкам: никогда не бросает исключений,
/// незакрытые конструкции заканчиваются концом файла. Регулярные выражения и JSX отличаются от деления и сравнения по контексту.
/// </summary>
public sealed class JsLexer
{
    private static readonly HashSet<string> Keywords = new(StringComparer.Ordinal)
    {
        "break", "case", "catch", "class", "const", "continue", "debugger", "default", "delete", "do", "else", "enum", "export", "extends",
        "false", "finally", "for", "function", "if", "import", "in", "instanceof", "new", "null", "return", "super", "switch", "this",
        "throw", "true", "try", "typeof", "var", "void", "while", "with", "yield", "let", "await", "undefined",
    };

    /// <summary>Ключевые слова, после которых может начинаться выражение (значит, «/» — регулярное выражение, «&lt;» — JSX).</summary>
    private static readonly HashSet<string> ExpressionKeywords = new(StringComparer.Ordinal)
    {
        "return", "typeof", "instanceof", "in", "of", "new", "delete", "void", "throw", "case", "do", "else", "yield", "await", "default",
        "extends", "export",
    };

    private static readonly string[] Puncts =
    [
        ">>>=", "...", "===", "!==", "**=", "<<=", ">>=", ">>>", "&&=", "||=", "??=",
        "=>", "==", "!=", "<=", ">=", "&&", "||", "??", "?.", "++", "--", "+=", "-=", "*=", "/=", "%=", "&=", "|=", "^=", "**", "<<", ">>",
    ];

    private const int MaxDepth = 150;

    private readonly string _s;
    private readonly bool _jsx;
    private readonly bool _typeScript;
    private readonly List<Token> _tokens = [];
    private readonly int[] _lineStarts;
    private int _pos;
    private int _depth;
    private bool _newLine = true;

    private JsLexer(string source, bool jsx, bool typeScript)
    {
        _s = source;
        _jsx = jsx;
        _typeScript = typeScript;
        var starts = new List<int> { 0 };
        for (int i = 0; i < source.Length; i++)
        {
            if (source[i] == '\n')
            {
                starts.Add(i + 1);
            }
        }

        _lineStarts = starts.ToArray();
    }

    /// <summary>Разбивает исходник на токены. <paramref name="jsx"/> — разрешить JSX (.jsx, .tsx, .js); <paramref name="typeScript"/> — TS-синтаксис.</summary>
    public static List<Token> Tokenize(string source, bool jsx, bool typeScript, int lineOffset = 0)
    {
        var lexer = new JsLexer(source, jsx, typeScript);
        if (source.StartsWith("#!", StringComparison.Ordinal))
        {
            while (lexer._pos < source.Length && source[lexer._pos] != '\n')
            {
                lexer._pos++;
            }
        }

        lexer.LexTokens(stopAtBrace: false);
        if (lineOffset != 0)
        {
            foreach (var t in lexer._tokens)
            {
                t.Line += lineOffset;
                t.EndLine += lineOffset;
            }
        }

        return lexer._tokens;
    }

    /// <summary>Как <see cref="Tokenize"/>, по расширению файла.</summary>
    public static List<Token> TokenizeFile(string path, string source, int lineOffset = 0)
    {
        var ext = Path.GetExtension(path).ToLowerInvariant();
        bool ts = ext is ".ts" or ".tsx" or ".mts" or ".cts";
        bool jsx = ext is ".tsx" or ".jsx" or ".js" or ".mjs" or ".cjs" or ".vue";
        return Tokenize(source, jsx, ts, lineOffset);
    }

    public int LineOf(int offset)
    {
        int idx = Array.BinarySearch(_lineStarts, offset);
        return (idx >= 0 ? idx : ~idx - 1) + 1;
    }

    private char Cur => _pos < _s.Length ? _s[_pos] : '\0';

    private char Peek(int n = 1) => _pos + n < _s.Length ? _s[_pos + n] : '\0';

    private Token Emit(TokenKind kind, int start, string text)
    {
        var t = new Token
        {
            Kind = kind, Text = text, Start = start, End = _pos, Line = LineOf(start), EndLine = LineOf(Math.Max(start, _pos - 1)),
            NewLineBefore = _newLine,
        };
        _newLine = false;
        _tokens.Add(t);
        return t;
    }

    private Token? LastSignificant()
    {
        for (int i = _tokens.Count - 1; i >= 0; i--)
        {
            if (!_tokens[i].IsComment)
            {
                return _tokens[i];
            }
        }

        return null;
    }

    /// <summary>Может ли в текущей позиции начинаться выражение (по предыдущему значимому токену).</summary>
    private bool ExpressionAllowed()
    {
        var prev = LastSignificant();
        if (prev is null)
        {
            return true;
        }

        return prev.Kind switch
        {
            TokenKind.Punct => prev.Text is not (")" or "]" or "}" or "++" or "--"),
            TokenKind.Keyword => ExpressionKeywords.Contains(prev.Text),
            TokenKind.Identifier => prev.Text is "await" or "yield" or "of",
            TokenKind.JsxExprStart => true,
            TokenKind.JsxAttr => true,
            _ => false,
        };
    }

    private void SkipWhitespace()
    {
        while (_pos < _s.Length)
        {
            char c = _s[_pos];
            if (c == '\n')
            {
                _newLine = true;
                _pos++;
            }
            else if (char.IsWhiteSpace(c) || c == '﻿')
            {
                _pos++;
            }
            else
            {
                break;
            }
        }
    }

    /// <summary>Основной цикл. stopAtBrace — остановиться на непарной «}» (выражение в ${…} или {…} JSX), не поглощая её.</summary>
    private void LexTokens(bool stopAtBrace)
    {
        int braces = 0;
        while (true)
        {
            SkipWhitespace();
            if (_pos >= _s.Length)
            {
                return;
            }

            char c = _s[_pos];
            int start = _pos;
            if (c == '/' && Peek() == '/')
            {
                while (_pos < _s.Length && _s[_pos] != '\n')
                {
                    _pos++;
                }

                Emit(TokenKind.LineComment, start, _s[start.._pos]);
                continue;
            }

            if (c == '/' && Peek() == '*')
            {
                int end = _s.IndexOf("*/", _pos + 2, StringComparison.Ordinal);
                _pos = end < 0 ? _s.Length : end + 2;
                Emit(TokenKind.BlockComment, start, _s[start.._pos]);
                continue;
            }

            if (c == '}' && stopAtBrace && braces == 0)
            {
                return;
            }

            if (IsIdentStart(c) || c == '#' && _pos + 1 < _s.Length && IsIdentStart(_s[_pos + 1]))
            {
                _pos++;
                while (_pos < _s.Length && IsIdentPart(_s[_pos]))
                {
                    _pos++;
                }

                var word = _s[start.._pos];
                Emit(Keywords.Contains(word) ? TokenKind.Keyword : TokenKind.Identifier, start, word);
                continue;
            }

            if (char.IsDigit(c) || c == '.' && char.IsDigit(Peek()))
            {
                LexNumber(start);
                continue;
            }

            if (c is '"' or '\'')
            {
                LexString(c, start);
                continue;
            }

            if (c == '`')
            {
                LexTemplate(start);
                continue;
            }

            if (c == '/' && ExpressionAllowed())
            {
                if (LexRegex(start))
                {
                    continue;
                }
            }

            if (c == '<' && _jsx && _depth < MaxDepth && LooksLikeJsx())
            {
                int savedPos = _pos;
                int savedCount = _tokens.Count;
                bool savedNewLine = _newLine;
                if (LexJsxElement())
                {
                    continue;
                }

                // Не JSX (например, generic в .tsx) — откатываемся и читаем «<» как оператор.
                _pos = savedPos;
                _tokens.RemoveRange(savedCount, _tokens.Count - savedCount);
                _newLine = savedNewLine;
            }

            // Пунктуация.
            string? p = null;
            foreach (var cand in Puncts)
            {
                if (string.CompareOrdinal(_s, _pos, cand, 0, cand.Length) == 0)
                {
                    if (cand == "?." && char.IsDigit(Peek(2)))
                    {
                        continue;
                    }

                    p = cand;
                    break;
                }
            }

            p ??= c.ToString();
            _pos += p.Length;
            Emit(TokenKind.Punct, start, p);
            if (p == "{")
            {
                braces++;
            }
            else if (p == "}")
            {
                braces--;
            }
        }
    }

    private static bool IsIdentStart(char c) => char.IsLetter(c) || c is '_' or '$';

    private static bool IsIdentPart(char c) => char.IsLetterOrDigit(c) || c is '_' or '$' or '‌' or '‍';

    private void LexNumber(int start)
    {
        if (_s[_pos] == '0' && (Peek() is 'x' or 'X' or 'b' or 'B' or 'o' or 'O'))
        {
            _pos += 2;
            while (_pos < _s.Length && (char.IsAsciiHexDigit(_s[_pos]) || _s[_pos] == '_'))
            {
                _pos++;
            }
        }
        else
        {
            while (_pos < _s.Length && (char.IsDigit(_s[_pos]) || _s[_pos] is '_' or '.'))
            {
                if (_s[_pos] == '.' && (Peek() == '.' || !char.IsDigit(Peek()) && _pos > start && !char.IsDigit(_s[_pos - 1])))
                {
                    break;
                }

                _pos++;
            }

            if (_pos < _s.Length && _s[_pos] is 'e' or 'E' && (char.IsDigit(Peek()) || Peek() is '+' or '-' && char.IsDigit(Peek(2))))
            {
                _pos += 2;
                while (_pos < _s.Length && char.IsDigit(_s[_pos]))
                {
                    _pos++;
                }
            }
        }

        if (_pos < _s.Length && _s[_pos] == 'n')
        {
            _pos++;
        }

        Emit(TokenKind.Number, start, _s[start.._pos]);
    }

    private void LexString(char quote, int start)
    {
        _pos++;
        var sb = new StringBuilder();
        while (_pos < _s.Length)
        {
            char c = _s[_pos];
            if (c == '\\' && _pos + 1 < _s.Length)
            {
                sb.Append(_s[_pos + 1]);
                _pos += 2;
                continue;
            }

            if (c == quote)
            {
                _pos++;
                break;
            }

            if (c == '\n')
            {
                break;
            }

            sb.Append(c);
            _pos++;
        }

        var t = Emit(TokenKind.String, start, sb.ToString());
        t.Value = _s[start.._pos];
    }

    private void LexTemplate(int start)
    {
        _pos++;
        var token = Emit(TokenKind.Template, start, "");
        var sb = new StringBuilder();
        while (_pos < _s.Length)
        {
            char c = _s[_pos];
            if (c == '\\' && _pos + 1 < _s.Length)
            {
                sb.Append(_s[_pos + 1]);
                _pos += 2;
                continue;
            }

            if (c == '`')
            {
                _pos++;
                break;
            }

            if (c == '$' && Peek() == '{')
            {
                token.HasSubstitutions = true;
                sb.Append("${}");
                _pos += 2;
                _depth++;
                if (_depth < MaxDepth)
                {
                    LexTokens(stopAtBrace: true);
                }

                _depth--;
                if (Cur == '}')
                {
                    _pos++;
                }

                continue;
            }

            sb.Append(c);
            _pos++;
        }

        token.Text = sb.ToString();
        token.End = _pos;
        token.EndLine = LineOf(Math.Max(start, _pos - 1));
    }

    private bool LexRegex(int start)
    {
        int i = _pos + 1;
        bool inClass = false;
        while (i < _s.Length)
        {
            char c = _s[i];
            if (c == '\n')
            {
                return false;
            }

            if (c == '\\')
            {
                i += 2;
                continue;
            }

            if (c == '[')
            {
                inClass = true;
            }
            else if (c == ']')
            {
                inClass = false;
            }
            else if (c == '/' && !inClass)
            {
                i++;
                while (i < _s.Length && char.IsLetter(_s[i]))
                {
                    i++;
                }

                _pos = i;
                Emit(TokenKind.Regex, start, _s[start.._pos]);
                return true;
            }

            i++;
        }

        return false;
    }

    // ------------------------------------------------------------------ JSX

    private bool LooksLikeJsx()
    {
        char next = Peek();
        if (next == '>')
        {
            return ExpressionAllowed();
        }

        if (!IsIdentStart(next) || !ExpressionAllowed())
        {
            return false;
        }

        if (_typeScript)
        {
            // <T,>(…) => и <T extends X>(…) — generic-стрелка, а не JSX.
            int i = _pos + 1;
            while (i < _s.Length && IsIdentPart(_s[i]))
            {
                i++;
            }

            while (i < _s.Length && _s[i] == ' ')
            {
                i++;
            }

            if (i < _s.Length && _s[i] == ',')
            {
                return false;
            }

            if (string.CompareOrdinal(_s, i, "extends ", 0, 8) == 0)
            {
                return false;
            }

            if (i < _s.Length && _s[i] == '>' && i + 1 < _s.Length && _s[i + 1] == '(')
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>Разбор JSX-элемента начиная с «&lt;». false — не удалось (не JSX), вызывающий откатывается.</summary>
    private bool LexJsxElement()
    {
        _depth++;
        try
        {
            if (_depth >= MaxDepth)
            {
                return false;
            }

            int start = _pos;
            _pos++;
            SkipWhitespace();
            string name = "";
            if (Cur != '>')
            {
                int ns = _pos;
                while (_pos < _s.Length && (IsIdentPart(_s[_pos]) || _s[_pos] is '.' or ':' or '-'))
                {
                    _pos++;
                }

                name = _s[ns.._pos];
                if (name.Length == 0)
                {
                    return false;
                }
            }

            var open = Emit(TokenKind.JsxOpen, start, name);

            // Атрибуты.
            while (true)
            {
                SkipWhitespace();
                if (_pos >= _s.Length)
                {
                    return false;
                }

                char c = _s[_pos];
                if (c == '/' && Peek() == '>')
                {
                    _pos += 2;
                    open.SelfClosing = true;
                    var close = Emit(TokenKind.JsxClose, _pos - 2, name);
                    close.SelfClosing = true;
                    return true;
                }

                if (c == '>')
                {
                    _pos++;
                    return LexJsxChildren(name);
                }

                if (c == '/' && Peek() == '*')
                {
                    int end = _s.IndexOf("*/", _pos + 2, StringComparison.Ordinal);
                    int cs = _pos;
                    _pos = end < 0 ? _s.Length : end + 2;
                    Emit(TokenKind.BlockComment, cs, _s[cs.._pos]).InJsx = true;
                    continue;
                }

                if (c == '{')
                {
                    // {...props} или комментарий {/* */}
                    if (!LexJsxExpression())
                    {
                        return false;
                    }

                    continue;
                }

                if (IsIdentStart(c))
                {
                    int an = _pos;
                    while (_pos < _s.Length && (IsIdentPart(_s[_pos]) || _s[_pos] is '-' or ':' or '.'))
                    {
                        _pos++;
                    }

                    var attr = Emit(TokenKind.JsxAttr, an, _s[an.._pos]);
                    SkipWhitespace();
                    if (Cur != '=')
                    {
                        continue;
                    }

                    _pos++;
                    SkipWhitespace();
                    char v = Cur;
                    if (v is '"' or '\'')
                    {
                        int vs = ++_pos;
                        while (_pos < _s.Length && _s[_pos] != v)
                        {
                            _pos++;
                        }

                        attr.Value = _s[vs..Math.Min(_pos, _s.Length)];
                        _pos = Math.Min(_pos + 1, _s.Length);
                        attr.End = _pos;
                        attr.EndLine = LineOf(_pos - 1);
                    }
                    else if (v == '{')
                    {
                        attr.ExprValue = true;
                        if (!LexJsxExpression())
                        {
                            return false;
                        }
                    }
                    else if (v == '<')
                    {
                        if (!LexJsxElement())
                        {
                            return false;
                        }
                    }
                    else
                    {
                        return false;
                    }

                    continue;
                }

                return false;
            }
        }
        finally
        {
            _depth--;
        }
    }

    /// <summary>{ выражение } внутри JSX. Комментарий {/* */} помечается InJsx.</summary>
    private bool LexJsxExpression()
    {
        int start = _pos;
        _pos++;
        int before = _tokens.Count;
        Emit(TokenKind.JsxExprStart, start, "{");
        LexTokens(stopAtBrace: true);
        if (Cur != '}')
        {
            return false;
        }

        for (int i = before + 1; i < _tokens.Count; i++)
        {
            if (_tokens[i].IsComment)
            {
                _tokens[i].InJsx = true;
            }
        }

        int end = _pos;
        _pos++;
        Emit(TokenKind.JsxExprEnd, end, "}");
        return true;
    }

    private bool LexJsxChildren(string name)
    {
        while (true)
        {
            if (_pos >= _s.Length)
            {
                return false;
            }

            char c = _s[_pos];
            if (c == '{')
            {
                if (!LexJsxExpression())
                {
                    return false;
                }

                continue;
            }

            if (c == '<')
            {
                int i = _pos + 1;
                while (i < _s.Length && char.IsWhiteSpace(_s[i]))
                {
                    i++;
                }

                if (i < _s.Length && _s[i] == '/')
                {
                    int start = _pos;
                    int end = _s.IndexOf('>', i);
                    if (end < 0)
                    {
                        return false;
                    }

                    var closing = _s[(i + 1)..end].Trim();
                    _pos = end + 1;
                    Emit(TokenKind.JsxClose, start, closing);

                    // Несовпадающие имена тегов допускаем: важно не потерять синхронизацию на ошибках разметки.
                    return true;
                }

                if (!LexJsxElement())
                {
                    return false;
                }

                continue;
            }

            int ts = _pos;
            while (_pos < _s.Length && _s[_pos] is not ('<' or '{'))
            {
                if (_s[_pos] == '\n')
                {
                    _newLine = true;
                }

                _pos++;
            }

            var text = _s[ts.._pos];
            if (text.Trim().Length > 0)
            {
                int lead = text.Length - text.TrimStart().Length;
                var t = new Token
                {
                    Kind = TokenKind.JsxText, Text = text.Trim(), Start = ts + lead, End = _pos, Line = LineOf(ts + lead),
                    EndLine = LineOf(Math.Max(ts, _pos - 1)),
                };
                _tokens.Add(t);
            }
        }
    }
}
