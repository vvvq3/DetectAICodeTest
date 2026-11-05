using AiDetect.Languages.Frontend.Parsing;

namespace AiDetect.Languages.Frontend;

/// <summary>Где находится скрипт: отдельный файл или блок &lt;script&gt; Vue.</summary>
internal enum ScriptHost
{
    File,
    Vue,
}

/// <summary>Функция, метод или стрелочная функция, найденная мини-парсером.</summary>
internal sealed record FuncInfo(
    string Name,
    int StartTok,
    int ParamOpen,
    int ParamClose,
    int BodyOpen,
    int BodyClose,
    bool Arrow,
    bool BlockBody,
    bool Async,
    bool Exported,
    bool HasReturnType,
    bool IsMethod,
    bool IsPrivate,
    int StartLine,
    int EndLine)
{
    public bool IsComponent { get; set; }

    public bool IsHook => FrontendText.IsHookName(Name);
}

/// <summary>Класс с декораторами (Angular) и границами тела.</summary>
internal sealed record ClassInfo(string Name, List<string> Decorators, string? Extends, List<string> Implements, int StartTok, int BodyOpen, int BodyClose, int Line);

/// <summary>
/// Мини-парсер JS/TS поверх токенов: парные скобки, глубина, функции и классы. Признаки — в ScriptAnalyzer.Features.cs.
/// </summary>
internal sealed partial class ScriptAnalyzer
{
    private static readonly HashSet<string> NotMethodNames = new(StringComparer.Ordinal)
    {
        "if", "for", "while", "switch", "catch", "with", "function", "return", "typeof", "await", "new", "delete", "void", "yield", "super",
        "import", "export", "do", "else", "try", "finally", "throw", "in", "of", "instanceof",
    };

    private static readonly HashSet<string> Modifiers = new(StringComparer.Ordinal)
    {
        "public", "private", "protected", "static", "async", "get", "set", "readonly", "override", "abstract", "declare",
    };

    private readonly Ctx _c;
    private readonly List<Token> _all;
    private readonly Token[] _t;
    private readonly int[] _match;
    private readonly int[] _depth;
    private readonly bool _ts;
    private readonly int _lineOffset;
    private readonly ScriptHost _host;
    private readonly List<FuncInfo> _funcs = [];
    private readonly List<ClassInfo> _classes = [];

    public ScriptAnalyzer(Ctx ctx, List<Token> tokens, bool typeScript, int lineOffset, ScriptHost host)
    {
        _c = ctx;
        _all = tokens;
        _t = tokens.Where(t => !t.IsComment).ToArray();
        _ts = typeScript;
        _lineOffset = lineOffset;
        _host = host;
        _match = new int[_t.Length];
        _depth = new int[_t.Length];
        Array.Fill(_match, -1);
        var stack = new Stack<int>();
        int depth = 0;
        for (int i = 0; i < _t.Length; i++)
        {
            var k = _t[i];
            bool open = k.Kind == TokenKind.JsxExprStart || k.Kind == TokenKind.Punct && k.Text is "(" or "[" or "{";
            bool close = k.Kind == TokenKind.JsxExprEnd || k.Kind == TokenKind.Punct && k.Text is ")" or "]" or "}";
            if (close)
            {
                if (stack.Count > 0)
                {
                    int o = stack.Pop();
                    _match[o] = i;
                    _match[i] = o;
                    if (_t[o].Text is "{" || _t[o].Kind == TokenKind.JsxExprStart)
                    {
                        depth = Math.Max(0, depth - 1);
                    }
                }
            }

            _depth[i] = depth;
            if (open)
            {
                stack.Push(i);
                if (k.Text == "{" || k.Kind == TokenKind.JsxExprStart)
                {
                    depth++;
                }
            }
        }
    }

    public void Run()
    {
        FindStructure();
        Lines();
        Comments();
        Documentation();
        Naming();
        if (_ts || _host == ScriptHost.Vue)
        {
            Typing();
        }

        Syntax();
        Errors();
        Async();
        Logging();
        React();
        Angular();
        Vue();
        Jsx();
        if (_c.IsTest)
        {
            Tests();
        }

        Habits();
        Formatting();
        Functions();
    }

    // ------------------------------------------------------------------ утилиты токенов

    private Token? At(int i) => i >= 0 && i < _t.Length ? _t[i] : null;

    private bool IsP(int i, string p) => At(i) is { } t && t.Kind == TokenKind.Punct && t.Text == p;

    private bool IsId(int i, string name) => At(i) is { } t && (t.Kind is TokenKind.Identifier or TokenKind.Keyword) && t.Text == name;

    private bool IsIdent(int i) => At(i) is { Kind: TokenKind.Identifier };

    private int Line(int i) => At(i)?.Line ?? 0;

    /// <summary>Вызов вида «name(» (не объявление метода): возвращает индекс «(».</summary>
    private int CallParen(int i)
    {
        if (IsP(i + 1, "("))
        {
            return i + 1;
        }

        // name<T>(
        if (IsP(i + 1, "<"))
        {
            int j = SkipAngle(i + 1);
            if (j > 0 && IsP(j, "("))
            {
                return j;
            }
        }

        return -1;
    }

    /// <summary>Пропуск «&lt;…&gt;» дженерика: индекс токена после закрывающей «&gt;» или −1.</summary>
    private int SkipAngle(int i)
    {
        int d = 0;
        for (int j = i; j < _t.Length && j < i + 60; j++)
        {
            var t = _t[j];
            if (t.Kind != TokenKind.Punct)
            {
                continue;
            }

            switch (t.Text)
            {
                case "<":
                    d++;
                    break;
                case ">":
                    d--;
                    break;
                case ">>":
                    d -= 2;
                    break;
                case ">>>":
                    d -= 3;
                    break;
                case ";" or "{" or "}" or "=>" or "=":
                    return -1;
            }

            if (d <= 0)
            {
                return j + 1;
            }
        }

        return -1;
    }

    /// <summary>Пропуск аннотации возвращаемого типа после «)»: индекс первого токена после типа (ожидается «{» или «=>»).</summary>
    private int SkipReturnType(int colon)
    {
        int angle = 0;
        for (int j = colon + 1; j < _t.Length && j < colon + 80; j++)
        {
            var t = _t[j];
            if (t.Kind != TokenKind.Punct)
            {
                continue;
            }

            switch (t.Text)
            {
                case "<":
                    angle++;
                    break;
                case ">":
                    angle--;
                    break;
                case ">>":
                    angle -= 2;
                    break;
                case "(" or "[":
                    if (_match[j] > j)
                    {
                        j = _match[j];
                    }

                    break;
                case "{":
                    if (angle > 0 || j == colon + 1 || IsP(j - 1, "|") || IsP(j - 1, "&") || IsP(j - 1, "<") || IsP(j - 1, ","))
                    {
                        // Объектный тип { a: string } — пропускаем целиком.
                        if (_match[j] > j)
                        {
                            j = _match[j];
                        }

                        break;
                    }

                    return j;
                case "=>":
                    if (angle <= 0)
                    {
                        return j;
                    }

                    break;
                case ";" or "}" or "=":
                    return -1;
            }
        }

        return -1;
    }

    // ------------------------------------------------------------------ структура

    private void FindStructure()
    {
        for (int i = 0; i < _t.Length; i++)
        {
            var t = _t[i];
            if (t.Kind == TokenKind.Keyword && t.Text == "function")
            {
                TryFunctionKeyword(i);
            }
            else if (t.Kind == TokenKind.Punct && t.Text == "=>")
            {
                TryArrow(i);
            }
            else if (t.Kind == TokenKind.Keyword && t.Text == "class")
            {
                TryClass(i);
            }
            else if ((t.Kind == TokenKind.Identifier || t.Kind == TokenKind.Keyword && t.Text is "constructor" or "delete" or "get" or "set") &&
                     !NotMethodNames.Contains(t.Text))
            {
                TryMethod(i);
            }
        }

        foreach (var f in _funcs)
        {
            f.IsComponent = FrontendText.IsPascal(f.Name) && ContainsJsx(f.BlockBody ? f.BodyOpen : f.ParamClose, f.BodyClose);
        }
    }

    private bool ContainsJsx(int from, int to)
    {
        for (int j = Math.Max(0, from); j <= to && j < _t.Length; j++)
        {
            if (_t[j].Kind == TokenKind.JsxOpen)
            {
                return true;
            }
        }

        return false;
    }

    private bool ExportedBefore(int i)
    {
        for (int j = i - 1; j >= 0 && j >= i - 4; j--)
        {
            if (IsId(j, "export"))
            {
                return true;
            }

            if (!(At(j) is { } t && (t.Text is "default" or "async" or "const" or "let" or "var" or "abstract" or "declare")))
            {
                return false;
            }
        }

        return false;
    }

    /// <summary>Имя функции-выражения по контексту: «const name = …», «name: …», «name = …».</summary>
    private (string Name, bool Exported) NameFromContext(int open)
    {
        int j = open - 1;
        if (IsId(j, "async"))
        {
            j--;
        }

        // useCallback(…) / memo(…) / forwardRef(…) — имя из присваивания снаружи.
        if (IsP(j, "(") && At(j - 1) is { Kind: TokenKind.Identifier } wrapper &&
            wrapper.Text is "useCallback" or "useMemo" or "memo" or "forwardRef" or "observer" or "computed" or "watch" or "defineComponent")
        {
            j -= 2;
            if (IsP(j, ".") )
            {
                j -= 2;
            }
        }

        if (IsP(j, "=") || IsP(j, ":"))
        {
            bool colon = IsP(j, ":");
            // «const Foo: React.FC<Props> = (» — идём назад до объявления.
            for (int k = j - 1; k >= 0 && k >= j - 25; k--)
            {
                if (At(k) is { Kind: TokenKind.Keyword } kw && kw.Text is "const" or "let" or "var")
                {
                    if (IsIdent(k + 1))
                    {
                        return (_t[k + 1].Text, ExportedBefore(k));
                    }

                    break;
                }

                if (IsP(k, ";") || IsP(k, "{") || IsP(k, "}") || IsP(k, ","))
                {
                    break;
                }
            }

            if (IsIdent(j - 1) || At(j - 1) is { Kind: TokenKind.String })
            {
                return (At(j - 1)!.Text, false);
            }

            if (colon && IsP(j - 1, ")"))
            {
                return ("", false);
            }
        }

        return ("", false);
    }

    private void TryFunctionKeyword(int i)
    {
        int j = i + 1;
        if (IsP(j, "*"))
        {
            j++;
        }

        string name = "";
        if (IsIdent(j))
        {
            name = _t[j].Text;
            j++;
        }

        if (IsP(j, "<"))
        {
            j = SkipAngle(j);
            if (j < 0)
            {
                return;
            }
        }

        if (!IsP(j, "(") || _match[j] < 0)
        {
            return;
        }

        int open = j;
        int close = _match[j];
        int k = close + 1;
        bool rt = false;
        if (IsP(k, ":"))
        {
            rt = true;
            k = SkipReturnType(k);
            if (k < 0)
            {
                return;
            }
        }

        if (!IsP(k, "{") || _match[k] < 0)
        {
            return;
        }

        bool exported = ExportedBefore(IsId(i - 1, "async") ? i - 1 : i);
        if (name.Length == 0)
        {
            (name, var ex) = NameFromContext(IsId(i - 1, "async") ? i - 1 : i);
            exported |= ex;
        }

        _funcs.Add(new FuncInfo(name, i, open, close, k, _match[k], false, true, IsId(i - 1, "async"), exported, rt, false, false,
            _t[i].Line, _t[_match[k]].Line));
    }

    private void TryArrow(int i)
    {
        int p = i - 1;
        int open, close;
        bool rt = false;
        if (IsP(p, ")") && _match[p] >= 0)
        {
            close = p;
            open = _match[p];
        }
        else if (IsIdent(p) && !(IsP(p - 1, ":") && _ts))
        {
            open = close = p;
        }
        else
        {
            // (a: number): string => — тип возврата перед стрелкой.
            int q = -1;
            for (int k = p; k >= 0 && k >= i - 50; k--)
            {
                if (IsP(k, ";") || IsP(k, "{") || IsP(k, "}") || IsP(k, "=>") || IsP(k, "="))
                {
                    break;
                }

                if (IsP(k, ")") && IsP(k + 1, ":") && _match[k] >= 0)
                {
                    q = k;
                    break;
                }
            }

            if (q < 0)
            {
                return;
            }

            close = q;
            open = _match[q];
            rt = true;
        }

        if (IsP(close + 1, ":") && close + 1 < i)
        {
            rt = true;
        }

        bool async = IsId(open - 1, "async") || IsP(open - 1, ">") && IsId(open - 2 - 0, "async");
        int bodyOpen = i + 1;
        bool block = IsP(bodyOpen, "{") && _match[bodyOpen] > bodyOpen;
        int bodyClose;
        if (block)
        {
            bodyClose = _match[bodyOpen];
        }
        else
        {
            bodyClose = ExpressionEnd(bodyOpen);
        }

        var (name, exported) = NameFromContext(open);
        _funcs.Add(new FuncInfo(name, open, open, close, bodyOpen, bodyClose, true, block, async, exported, rt, false, false,
            _t[open].Line, _t[Math.Min(bodyClose, _t.Length - 1)].Line));
    }

    /// <summary>Конец выражения-тела стрелки: до «,», «;», «)», «]», «}» на нулевой глубине.</summary>
    private int ExpressionEnd(int from)
    {
        int j = from;
        while (j < _t.Length)
        {
            var t = _t[j];
            if (_match[j] > j)
            {
                j = _match[j] + 1;
                continue;
            }

            if (t.Kind == TokenKind.JsxOpen)
            {
                // Пропуск JSX до парного закрытия.
                int d = 0;
                while (j < _t.Length)
                {
                    if (_t[j].Kind == TokenKind.JsxOpen && !_t[j].SelfClosing)
                    {
                        d++;
                    }
                    else if (_t[j].Kind == TokenKind.JsxClose && !_t[j].SelfClosing)
                    {
                        d--;
                    }

                    if (d <= 0 && (_t[j].Kind == TokenKind.JsxClose || _t[j].SelfClosing && _t[j].Kind == TokenKind.JsxOpen))
                    {
                        break;
                    }

                    j++;
                }

                j++;
                continue;
            }

            if (t.Kind == TokenKind.JsxExprEnd || t.Kind == TokenKind.Punct && t.Text is "," or ";" or ")" or "]" or "}")
            {
                return Math.Max(from, j - 1);
            }

            if (j > from && t.NewLineBefore && t.Kind == TokenKind.Keyword && t.Text is "const" or "let" or "var" or "return" or "if" or "export" or "import")
            {
                return Math.Max(from, j - 1);
            }

            j++;
        }

        return _t.Length - 1;
    }

    private void TryMethod(int i)
    {
        int paren = CallParen(i);
        if (paren < 0 || _match[paren] < 0)
        {
            return;
        }

        var prev = At(i - 1);
        if (prev is not null && (prev.Kind == TokenKind.Punct && prev.Text is "." or "?." or "=" or "(" or "!" or "+" or "-" or "&&" or "||" or "??" or "?" or ":" or "=>" or "return"
                                 || prev.Kind == TokenKind.Keyword && prev.Text is "function" or "new" or "return" or "await" or "typeof" or "throw" or "case"
                                 || prev.Kind is TokenKind.JsxExprStart or TokenKind.String or TokenKind.Number))
        {
            return;
        }

        // Допустимые соседи слева: начало, «{», «}», «;», «,», модификаторы, «*», конец декоратора.
        if (prev is not null && !(prev.Kind == TokenKind.Punct && prev.Text is "{" or "}" or ";" or "," or "*" or ")" ||
                                  prev.Kind is TokenKind.Identifier or TokenKind.Keyword && Modifiers.Contains(prev.Text)))
        {
            return;
        }

        int close = _match[paren];
        int k = close + 1;
        bool rt = false;
        if (IsP(k, ":"))
        {
            rt = true;
            k = SkipReturnType(k);
            if (k < 0)
            {
                return;
            }
        }

        if (!IsP(k, "{") || _match[k] < 0)
        {
            return;
        }

        bool async = false;
        bool priv = _t[i].Text.StartsWith('#') || _t[i].Text.StartsWith('_');
        for (int j = i - 1; j >= 0 && j >= i - 4 && At(j) is { } m && Modifiers.Contains(m.Text); j--)
        {
            async |= m.Text == "async";
            priv |= m.Text is "private" or "protected";
        }

        _funcs.Add(new FuncInfo(_t[i].Text, i, paren, close, k, _match[k], false, true, async, false, rt, true, priv, _t[i].Line, _t[_match[k]].Line));
    }

    private void TryClass(int i)
    {
        int j = i + 1;
        string name = IsIdent(j) ? _t[j++].Text : "";
        if (IsP(j, "<"))
        {
            j = SkipAngle(j);
            if (j < 0)
            {
                return;
            }
        }

        string? ext = null;
        var impl = new List<string>();
        while (j < _t.Length && !IsP(j, "{"))
        {
            if (IsId(j, "extends") && At(j + 1) is { } e)
            {
                ext = e.Text;
                if (IsP(j + 2, ".") && At(j + 3) is { } e2)
                {
                    ext = e.Text + "." + e2.Text;
                }
            }

            if (IsId(j, "implements"))
            {
                for (int k = j + 1; k < _t.Length && !IsP(k, "{"); k++)
                {
                    if (IsIdent(k) && (IsP(k - 1, ",") || k == j + 1))
                    {
                        impl.Add(_t[k].Text);
                    }
                }
            }

            if (IsP(j, ";") || j > i + 60)
            {
                return;
            }

            j++;
        }

        if (!IsP(j, "{") || _match[j] < 0)
        {
            return;
        }

        // Декораторы перед классом: @Component({…}) export class …
        var decorators = new List<string>();
        int d = i - 1;
        while (d >= 0 && At(d) is { } dt && dt.Text is "export" or "default" or "abstract")
        {
            d--;
        }

        while (d >= 0)
        {
            if (IsP(d, ")") && _match[d] > 0 && IsIdent(_match[d] - 1) && IsP(_match[d] - 2, "@"))
            {
                decorators.Add(_t[_match[d] - 1].Text);
                d = _match[d] - 3;
            }
            else if (IsIdent(d) && IsP(d - 1, "@"))
            {
                decorators.Add(_t[d].Text);
                d -= 2;
            }
            else
            {
                break;
            }
        }

        _classes.Add(new ClassInfo(name, decorators, ext, impl, i, j, _match[j], _t[i].Line));
    }

    private ClassInfo? ClassAt(int tokenIndex) =>
        _classes.Where(c => c.BodyOpen < tokenIndex && tokenIndex < c.BodyClose).OrderByDescending(c => c.BodyOpen).FirstOrDefault();

    private bool IsAngularUiClass(ClassInfo? c) => c is not null && c.Decorators.Any(d => d is "Component" or "Directive");
}
