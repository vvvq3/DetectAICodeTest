using System.Text.RegularExpressions;
using AiDetect.Core.Text;
using AiDetect.Languages.Frontend.Parsing;

namespace AiDetect.Languages.Frontend;

internal sealed partial class ScriptAnalyzer
{
    private static readonly HashSet<string> LifecycleNames = new(StringComparer.Ordinal)
    {
        "constructor", "ngOnInit", "ngOnDestroy", "ngOnChanges", "ngAfterViewInit", "ngAfterContentInit", "ngDoCheck", "ngAfterViewChecked",
        "ngAfterContentChecked", "render", "componentDidMount", "componentWillUnmount", "componentDidUpdate", "shouldComponentUpdate",
        "getDerivedStateFromProps", "componentDidCatch", "data", "setup", "mounted", "created", "beforeDestroy", "destroyed", "beforeUnmount",
        "unmounted", "beforeMount", "updated", "beforeCreate", "activated", "deactivated",
    };

    private static readonly HashSet<string> ArrayMethods = new(StringComparer.Ordinal)
    {
        "map", "filter", "find", "some", "every", "forEach", "findIndex", "flatMap", "findLast", "sort",
    };

    private static readonly HashSet<string> UtilityTypeNames = new(StringComparer.Ordinal)
    {
        "Record", "Partial", "Pick", "Omit", "ReturnType", "Parameters", "Awaited", "NonNullable", "Readonly", "Required", "Extract", "Exclude",
        "InstanceType", "ReadonlyArray",
    };

    private static readonly HashSet<string> NewApiNames = new(StringComparer.Ordinal)
    {
        "structuredClone", "randomUUID", "hasOwn", "toSorted", "toReversed", "toSpliced", "findLast", "findLastIndex", "replaceAll", "groupBy",
    };

    // ------------------------------------------------------------------ строки

    private void Lines()
    {
        var code = new HashSet<int>();
        var comments = new HashSet<int>();
        foreach (var t in _all)
        {
            var set = t.IsComment ? comments : code;
            for (int l = t.Line; l <= Math.Max(t.Line, t.EndLine); l++)
            {
                set.Add(l);
            }
        }

        foreach (var l in code)
        {
            if (_c.In(l))
            {
                _c.S.Add(FK.CodeLines);
                _c.S.Add(FK.ScriptLines);
                if (_ts)
                {
                    _c.S.Add(FK.TsLines);
                }
            }
        }

        foreach (var l in comments.Where(l => !code.Contains(l)))
        {
            _c.Count(FK.CommentLines, l);
        }

        if (code.Any(_c.In))
        {
            _c.S.Add(FK.ScriptFiles);
        }
    }

    // ------------------------------------------------------------------ комментарии

    private static string CommentBody(Token t)
    {
        var s = t.Text;
        if (s.StartsWith("//", StringComparison.Ordinal))
        {
            return s[2..].Trim();
        }

        s = s.StartsWith("/**", StringComparison.Ordinal) ? s[3..] : s.Length >= 2 ? s[2..] : s;
        if (s.EndsWith("*/", StringComparison.Ordinal))
        {
            s = s[..^2];
        }

        return string.Join(" ", s.Split('\n').Select(l => l.Trim().TrimStart('*').Trim()).Where(l => l.Length > 0));
    }

    private void Comments()
    {
        Token? firstToken = _all.FirstOrDefault();
        foreach (var t in _all.Where(t => t.IsComment))
        {
            int line = t.Line;
            if (!_c.In(line))
            {
                continue;
            }

            var raw = t.Text;
            var body = CommentBody(t);
            if (raw.Contains("eslint-disable", StringComparison.Ordinal))
            {
                _c.Hit(FK.EslintDisable, line);
                if (raw.Contains("exhaustive-deps", StringComparison.Ordinal))
                {
                    _c.Hit(FK.DepsDisable, line);
                }

                continue;
            }

            if (raw.Contains("@ts-ignore", StringComparison.Ordinal) || raw.Contains("@ts-nocheck", StringComparison.Ordinal))
            {
                _c.Hit(FK.TsIgnore, line);
                continue;
            }

            if (raw.Contains("@ts-expect-error", StringComparison.Ordinal))
            {
                _c.Hit(FK.TsExpectError, line);
                continue;
            }

            if (raw.Contains("prettier-ignore", StringComparison.Ordinal) || raw.Contains("istanbul ignore", StringComparison.Ordinal) ||
                raw.StartsWith("///", StringComparison.Ordinal) || raw.Contains("@license", StringComparison.OrdinalIgnoreCase) ||
                raw.Contains("Copyright", StringComparison.OrdinalIgnoreCase) || raw.Contains("webpackChunkName", StringComparison.Ordinal) ||
                raw.StartsWith("#region", StringComparison.Ordinal) || raw.Contains("tslint:", StringComparison.Ordinal) ||
                raw.Contains("jshint", StringComparison.Ordinal) || raw.Contains("global ", StringComparison.Ordinal) && raw.StartsWith("/*", StringComparison.Ordinal))
            {
                continue;
            }

            if (t == firstToken && (Regex.IsMatch(body, @"^[\w./@-]+\.(tsx?|jsx?|vue)$") || body.Contains("@fileoverview", StringComparison.Ordinal) ||
                                    body.StartsWith("This file", StringComparison.OrdinalIgnoreCase)))
            {
                _c.Hit(FK.FileHeader, line);
            }

            bool jsdoc = raw.StartsWith("/**", StringComparison.Ordinal) && !t.InJsx;
            if (jsdoc)
            {
                _c.Hit(FK.JsDocBlocks, line);
                var first = body.Split('@')[0].Trim();
                if (FrontendText.DocTemplate().IsMatch(first))
                {
                    _c.Hit(FK.JsDocTemplate, line);
                }

                int paramTags = Regex.Matches(raw, @"@param\b").Count;
                if (paramTags > 0)
                {
                    _c.Count(FK.JsDocParams, line, paramTags);
                    if (_ts && Regex.IsMatch(raw, @"@param\s*\{"))
                    {
                        _c.Hit(FK.JsDocTypedParams, line);
                    }
                }

                if (Regex.IsMatch(raw, @"@(example|throws|default|see|remarks)\b"))
                {
                    _c.Hit(FK.JsDocExtraTags, line);
                }

                if (TextUtil.ContainsEmoji(body))
                {
                    _c.Hit(FK.EmojiInCode, line);
                }

                continue;
            }

            _c.Count(FK.LineComments, line);
            if (FrontendText.Todo().IsMatch(body))
            {
                _c.Hit(FK.Todo, line);
            }

            if (FrontendText.Ticket().IsMatch(body))
            {
                _c.Hit(FK.TicketRefs, line);
            }

            if (_c.IsTest && FrontendText.AaaMarker().IsMatch(body))
            {
                _c.Hit(FK.AaaComments, line);
                continue;
            }

            if (FrontendText.IsCommentedCode(body))
            {
                _c.Hit(FK.CommentedOutCode, line);
                continue;
            }

            if (TextUtil.ContainsEmoji(body))
            {
                _c.Hit(FK.EmojiInCode, line);
            }

            if (FrontendText.ChangeNote().IsMatch(body))
            {
                _c.Hit(FK.ChangeNote, line);
            }

            bool cyr = FrontendText.Cyrillic().IsMatch(body);
            if (cyr)
            {
                _c.Hit(FK.CyrillicComments, line);
            }
            else if (FrontendText.Latin().IsMatch(body))
            {
                _c.Count(FK.LatinComments, line);
            }

            var firstLetter = body.FirstOrDefault(char.IsLetter);
            bool capitalized = firstLetter != default && char.IsUpper(firstLetter);
            bool lower = firstLetter != default && char.IsLower(firstLetter) && !body.StartsWith("http", StringComparison.OrdinalIgnoreCase) &&
                         !Regex.IsMatch(body, @"^[a-z]+[A-Z(.]");
            if (lower || raw.StartsWith("//", StringComparison.Ordinal) && raw.Length > 2 && char.IsLetter(raw[2]) || body.Contains("???", StringComparison.Ordinal) ||
                body.Contains("!!!", StringComparison.Ordinal) || cyr && FrontendText.Slang().IsMatch(body))
            {
                _c.Hit(FK.Casual, line);
            }

            if (cyr && capitalized && !FrontendText.Slang().IsMatch(body))
            {
                _c.Count(FK.CyrillicPolished, line);
            }

            var words = FrontendText.Word().Matches(body).Select(m => m.Value).ToList();
            if (t.InJsx && capitalized && words.Count is >= 1 and <= 4 && !body.EndsWith('.'))
            {
                _c.Hit(FK.JsxSection, line);
                continue;
            }

            if (capitalized && words.Count is >= 1 and <= 10 && (FrontendText.NarrativeVerbs.Contains(words[0]) || FrontendText.RussianNarrative().IsMatch(words[0])) &&
                !FrontendText.Todo().IsMatch(body))
            {
                _c.Hit(FK.Narrative, line);
            }

            var next = capitalized ? NextCodeLine(t.EndLine) : null;
            if (next is not null)
            {
                var content = words.Where(w => w.Length >= 3 && !FrontendText.StopWords.Contains(w)).Select(w => w.ToLowerInvariant()).Distinct().ToList();
                if (content.Count >= 2)
                {
                    var idWords = FrontendText.SplitIdentifiers(next);
                    int overlap = content.Count(w => idWords.Any(iw => iw.StartsWith(w.Length > 4 ? w[..4] : w, StringComparison.Ordinal)));
                    if (overlap >= 2 && overlap >= content.Count * 0.6)
                    {
                        _c.Hit(FK.Restating, line);
                    }
                }
            }
        }
    }

    private string? NextCodeLine(int afterLine)
    {
        for (int l = afterLine + 1; l <= Math.Min(_c.LineCount, afterLine + 3); l++)
        {
            var t = _c.LineText(l).Trim();
            if (t.Length == 0 || t.StartsWith("//", StringComparison.Ordinal) || t.StartsWith("*", StringComparison.Ordinal) || t.StartsWith("/*", StringComparison.Ordinal))
            {
                continue;
            }

            return t;
        }

        return null;
    }

    // ------------------------------------------------------------------ документация

    /// <summary>JSDoc непосредственно перед токеном (между ними — только декораторы и модификаторы).</summary>
    private bool HasJsDocBefore(Token target)
    {
        int idx = _all.IndexOf(target);
        for (int j = idx - 1; j >= 0 && j >= idx - 40; j--)
        {
            var t = _all[j];
            if (t.IsComment)
            {
                return t.Text.StartsWith("/**", StringComparison.Ordinal) && t.EndLine >= target.Line - 30;
            }

            if (t.Kind == TokenKind.Identifier && Modifiers.Contains(t.Text) || t.Text is "export" or "default" or "@" or "async" ||
                t.Kind == TokenKind.Punct && t.Text is "(" or ")" or "{" or "}" or "," or ":" or "[" or "]" && InDecorator(j) ||
                t.Kind is TokenKind.String or TokenKind.Identifier or TokenKind.Number && InDecorator(j))
            {
                continue;
            }

            return false;
        }

        return false;
    }

    /// <summary>Грубая проверка: токен внутри декоратора @X(…) перед объявлением.</summary>
    private bool InDecorator(int allIndex)
    {
        for (int j = allIndex; j >= 0 && j >= allIndex - 30; j--)
        {
            var t = _all[j];
            if (t.Kind == TokenKind.Punct && t.Text == "@")
            {
                return true;
            }

            if (t.Kind == TokenKind.Punct && t.Text is ";" || t.IsComment)
            {
                return false;
            }
        }

        return false;
    }

    private void Documentation()
    {
        for (int i = 0; i < _t.Length; i++)
        {
            if (!IsId(i, "export") || _depth[i] != 0)
            {
                continue;
            }

            int j = i + 1;
            if (IsId(j, "default"))
            {
                j++;
            }

            if (At(j) is { } decl && decl.Text is "function" or "class" or "const" or "let" or "interface" or "type" or "enum" or "async" or "abstract")
            {
                _c.Count(FK.DocTargets, _t[i].Line);
                if (HasJsDocBefore(_t[i]))
                {
                    _c.Hit(FK.DocDocumented, _t[i].Line);
                }
            }
        }

        foreach (var f in _funcs.Where(f => f.IsMethod && !f.IsPrivate && !LifecycleNames.Contains(f.Name) && ClassAt(f.StartTok) is not null))
        {
            _c.Count(FK.DocTargets, f.StartLine);
            if (HasJsDocBefore(FirstModifierToken(f.StartTok)))
            {
                _c.Hit(FK.DocDocumented, f.StartLine);
            }
        }

        // Члены интерфейсов пропсов/опций с /** */ над ними.
        for (int i = 0; i < _t.Length; i++)
        {
            if (!(IsId(i, "interface") || IsId(i, "type") && IsP(i + 2, "=")) || !IsIdent(i + 1))
            {
                continue;
            }

            var name = _t[i + 1].Text;
            if (!Regex.IsMatch(name, "(Props|Options|Config|Settings|State)$"))
            {
                continue;
            }

            int open = -1;
            for (int j = i + 2; j < _t.Length && j < i + 30; j++)
            {
                if (IsP(j, "{"))
                {
                    open = j;
                    break;
                }
            }

            if (open < 0 || _match[open] < 0)
            {
                continue;
            }

            for (int j = open + 1; j < _match[open]; j++)
            {
                if (_depth[j] == _depth[open] + 1 && IsIdent(j) && (IsP(j + 1, ":") || IsP(j + 1, "?") && IsP(j + 2, ":")) &&
                    (IsP(j - 1, "{") || IsP(j - 1, ";") || IsP(j - 1, ",") || _t[j].NewLineBefore || IsId(j - 1, "readonly")))
                {
                    _c.Count(FK.PropsMembers, _t[j].Line);
                    if (HasJsDocBefore(IsId(j - 1, "readonly") ? _t[j - 1] : _t[j]))
                    {
                        _c.Hit(FK.PropsMembersDocumented, _t[j].Line);
                    }
                }
            }
        }
    }

    private Token FirstModifierToken(int i)
    {
        int j = i;
        while (j - 1 >= 0 && At(j - 1) is { } m && Modifiers.Contains(m.Text))
        {
            j--;
        }

        return _t[j];
    }

    // ------------------------------------------------------------------ именование

    private void Naming()
    {
        for (int i = 0; i < _t.Length; i++)
        {
            var t = _t[i];
            if (!_c.In(t.Line))
            {
                continue;
            }

            // catch (error) / .catch((err) => …)
            if (IsId(i, "catch") && IsP(i + 1, "(") && IsIdent(i + 2))
            {
                CatchParam(_t[i + 2].Text, t.Line);
            }
            else if (IsId(i, "catch") && IsP(i - 1, ".") && IsP(i + 1, "("))
            {
                int p = i + 2;
                if (IsP(p, "("))
                {
                    p++;
                }

                if (IsIdent(p) && (IsP(p + 1, "=>") || IsP(p + 1, ")") || IsP(p + 1, ":")))
                {
                    CatchParam(_t[p].Text, t.Line);
                }
            }

            // users.map((user) => …)
            if (t.Kind == TokenKind.Identifier && ArrayMethods.Contains(t.Text) && IsP(i - 1, ".") && IsP(i + 1, "("))
            {
                int p = i + 2;
                bool paren = IsP(p, "(");
                if (paren)
                {
                    p++;
                }

                if (IsIdent(p) && (paren && (IsP(p + 1, ")") || IsP(p + 1, ",") || IsP(p + 1, ":")) || !paren && IsP(p + 1, "=>")))
                {
                    var name = _t[p].Text;
                    _c.Count(FK.ArrayCallbacks, t.Line);
                    if (name.Length == 1 && name != "_")
                    {
                        _c.Hit(FK.ArrayCallbackSingleLetter, t.Line);
                    }
                    else if (name.Length >= 3 && !FrontendText.Abbreviations.Contains(name))
                    {
                        _c.Count(FK.ArrayCallbackDescriptive, t.Line);
                    }
                }
            }

            // Объявления: const/let/var name | { a, b } | [a, setA]
            if (t.Kind == TokenKind.Keyword && t.Text is "const" or "let" or "var" && !IsP(i - 1, "."))
            {
                foreach (var (name, line) in DeclaredNames(i + 1))
                {
                    LocalName(name, line);
                    if (t.Text == "const" && _depth[i] == 0 && FrontendText.IsScreaming(name) && IsP(i + 2, "=") || IsP(i + 2, ":") && FrontendText.IsScreaming(name) && _depth[i] == 0)
                    {
                        _c.Hit(FK.ScreamingConsts, line);
                    }
                }
            }

            if (IsId(i, "interface") && IsIdent(i + 1))
            {
                _c.Count(FK.Interfaces, t.Line);
                var n = _t[i + 1].Text;
                if (n.Length > 2 && n[0] == 'I' && char.IsUpper(n[1]) && char.IsLower(n[2]))
                {
                    _c.Hit(FK.InterfacesIPrefix, t.Line);
                }
            }

            if (t.Kind == TokenKind.Identifier && t.Text is "private" or "protected" && IsIdent(i + 1) && _t[i + 1].Text.StartsWith('_') ||
                t.Kind == TokenKind.Identifier && t.Text is "private" or "protected" && IsId(i + 1, "readonly") && IsIdent(i + 2) && _t[i + 2].Text.StartsWith('_'))
            {
                _c.Hit(FK.UnderscorePrivate, t.Line);
            }

            if (t.Kind == TokenKind.Identifier && FrontendText.Cyrillic().IsMatch(t.Text))
            {
                _c.Hit(FK.CyrillicIdentifiers, t.Line);
            }
        }

        foreach (var f in _funcs.Where(f => _c.In(f.StartLine)))
        {
            foreach (var p in ParamNames(f))
            {
                LocalName(p, f.StartLine);
            }
        }
    }

    private void CatchParam(string name, int line)
    {
        _c.Count(FK.CatchParams, line);
        if (name is "error")
        {
            _c.Count(FK.CatchParamError, line);
        }
        else if (name is "e" or "err" or "ex" or "er" or "e1" or "error1")
        {
            _c.Hit(FK.CatchParamShort, line);
        }
    }

    private void LocalName(string name, int line)
    {
        if (name.Length == 0 || name == "_")
        {
            return;
        }

        _c.S.LocalNames.Add(name);
        _c.Count(FK.LocalNames, line);
        if (FrontendText.Abbreviations.Contains(name) && !FrontendText.ShortNameExceptions.Contains(name) || Regex.IsMatch(name, @"^[a-z]+\d$"))
        {
            _c.Hit(FK.Abbreviations, line);
        }

        if (FrontendText.GenericNames.Contains(name) || FrontendText.GenericPrefixes.Any(p => name.StartsWith(p, StringComparison.Ordinal) && name.Length > p.Length &&
                                                                                           char.IsUpper(name[p.Length])))
        {
            _c.Count(FK.GenericNames, line);
        }

        if (FrontendText.HasTranslit(name))
        {
            _c.Hit(FK.Translit, line);
        }
    }

    /// <summary>Имена в объявлении после const/let/var: идентификатор или деструктуризация {a, b: c} / [a, setA].</summary>
    private IEnumerable<(string Name, int Line)> DeclaredNames(int i)
    {
        if (IsIdent(i))
        {
            yield return (_t[i].Text, _t[i].Line);
            yield break;
        }

        if ((IsP(i, "{") || IsP(i, "[")) && _match[i] > i)
        {
            for (int j = i + 1; j < _match[i]; j++)
            {
                if (_match[j] > j && j != i)
                {
                    // вложенная деструктуризация: берём имена внутри
                    continue;
                }

                if (IsIdent(j) && (IsP(j + 1, ",") || IsP(j + 1, "}") || IsP(j + 1, "]") || IsP(j + 1, "=")) && !IsP(j - 1, "."))
                {
                    yield return (_t[j].Text, _t[j].Line);
                }
            }
        }
    }

    private IEnumerable<string> ParamNames(FuncInfo f)
    {
        if (f.ParamOpen == f.ParamClose)
        {
            if (IsIdent(f.ParamOpen))
            {
                yield return _t[f.ParamOpen].Text;
            }

            yield break;
        }

        int baseDepth = 0;
        for (int j = f.ParamOpen + 1; j < f.ParamClose; j++)
        {
            if (IsP(j, "<"))
            {
                baseDepth++;
            }
            else if (IsP(j, ">"))
            {
                baseDepth--;
            }

            if (IsIdent(j) && !Modifiers.Contains(_t[j].Text) && (IsP(j + 1, ",") || IsP(j + 1, ":") || IsP(j + 1, "=") || IsP(j + 1, "?") ||
                                                                    j + 1 == f.ParamClose || IsP(j + 1, "}") || IsP(j + 1, "]")) &&
                !IsP(j - 1, ":") && !IsP(j - 1, ".") && !IsP(j - 1, "<") && !IsP(j - 1, "|"))
            {
                yield return _t[j].Text;
            }
        }
    }

    // ------------------------------------------------------------------ типизация

    private void Typing()
    {
        bool inImport = false;
        for (int i = 0; i < _t.Length; i++)
        {
            var t = _t[i];
            if (IsId(i, "import") || IsId(i, "export") && IsP(i + 1, "{"))
            {
                inImport = true;
            }
            else if (IsP(i, ";") || t.NewLineBefore && t.Kind == TokenKind.Keyword && t.Text is not ("from"))
            {
                inImport = inImport && !(IsP(i, ";") || t.NewLineBefore && !IsP(i - 1, ","));
            }

            if (!_c.In(t.Line))
            {
                continue;
            }

            if (IsId(i, "import"))
            {
                _c.Count(FK.Imports, t.Line);
                if (IsId(i + 1, "type") && !IsP(i + 2, "(") || IsP(i + 1, "{") && _match[i + 1] > 0 &&
                    Enumerable.Range(i + 2, Math.Max(0, _match[i + 1] - i - 2)).Any(j => IsId(j, "type") && IsIdent(j + 1)))
                {
                    _c.Hit(FK.ImportTypes, t.Line);
                }
            }

            if (t.Kind != TokenKind.Identifier && !(t.Kind == TokenKind.Punct && t.Text == "!"))
            {
                continue;
            }

            switch (t.Text)
            {
                case "any" when IsTypePosition(i):
                    _c.Hit(FK.AnyTypes, t.Line);
                    break;
                case "unknown" when IsTypePosition(i):
                    _c.Hit(FK.UnknownTypes, t.Line);
                    if (IsId(i - 1, "as") && IsId(i + 1, "as"))
                    {
                        _c.Hit(FK.DoubleCasts, t.Line);
                    }

                    break;
                case "as" when !inImport && i > 0 && !IsP(i - 1, "*") && !IsP(i - 1, "{") && !IsP(i - 1, ","):
                    if (IsId(i + 1, "const"))
                    {
                        _c.Hit(FK.AsConst, t.Line);
                    }
                    else if (At(i + 1) is { Kind: TokenKind.Identifier } && !IsId(i + 1, "unknown"))
                    {
                        _c.Hit(FK.AsCasts, t.Line);
                    }

                    break;
                case "readonly":
                    _c.Count(FK.ReadonlyMods, t.Line);
                    break;
                case "is" when IsIdent(i - 1) && IsP(i - 2, ":") && IsP(i - 3, ")"):
                    _c.Hit(FK.TypeGuards, t.Line);
                    break;
                case "!":
                    if (i > 0 && (IsIdent(i - 1) || IsP(i - 1, ")") || IsP(i - 1, "]")) && _t[i - 1].End == t.Start &&
                        (IsP(i + 1, ".") || IsP(i + 1, ")") || IsP(i + 1, ";") || IsP(i + 1, ",") || IsP(i + 1, "[") || IsP(i + 1, "]")))
                    {
                        _c.Hit(FK.NonNull, t.Line);
                    }

                    break;
                default:
                    if (UtilityTypeNames.Contains(t.Text) && IsP(i + 1, "<"))
                    {
                        _c.Hit(FK.UtilityTypes, t.Line);
                    }

                    break;
            }

            if (IsId(i, "type") && IsIdent(i + 1) && (IsP(i + 2, "=") || IsP(i + 2, "<")))
            {
                int j = i + 2;
                if (IsP(j, "<"))
                {
                    j = SkipAngle(j);
                }

                if (j > 0 && IsP(j, "="))
                {
                    int k = IsP(j + 1, "|") ? j + 2 : j + 1;
                    if (At(k) is { Kind: TokenKind.String } && IsP(k + 1, "|"))
                    {
                        _c.Hit(FK.LiteralUnions, t.Line);
                    }
                }
            }

            if (IsId(i, "enum") || IsId(i, "const") && IsId(i + 1, "enum"))
            {
                _c.Hit(FK.Enums, t.Line);
            }

            if (t.Text is "useState" or "useRef" or "ref" or "shallowRef" or "signal" && !IsP(i - 1, "."))
            {
                bool generic = IsP(i + 1, "<");
                int paren = CallParen(i);
                if (paren > 0 && _match[paren] > paren)
                {
                    bool empty = _match[paren] == paren + 1 || IsId(paren + 1, "null") || IsId(paren + 1, "undefined") ||
                                 IsP(paren + 1, "[") && IsP(paren + 2, "]") && _match[paren] == paren + 3;
                    if (empty)
                    {
                        _c.Count(FK.StateHooks, t.Line);
                        if (generic)
                        {
                            _c.Hit(FK.StateHooksGeneric, t.Line);
                        }
                    }
                }
            }
        }

        foreach (var f in _funcs.Where(f => _c.In(f.StartLine) && !f.IsComponent && f.BlockBody &&
                                            (f.Exported || f.IsMethod && !f.IsPrivate && !LifecycleNames.Contains(f.Name) && ClassAt(f.StartTok) is not null)))
        {
            _c.Count(FK.ExportedFunctions, f.StartLine);
            if (f.HasReturnType)
            {
                _c.Count(FK.ExportedFunctionsTyped, f.StartLine);
            }
        }
    }

    private bool IsTypePosition(int i)
    {
        if (IsP(i + 1, "(") || IsP(i + 1, ".") || IsP(i + 1, "=") && !IsP(i - 1, ":"))
        {
            return false;
        }

        return IsP(i - 1, ":") || IsId(i - 1, "as") || IsP(i - 1, "<") || IsP(i - 1, "|") || IsP(i - 1, "&") ||
               IsP(i - 1, ",") && (IsP(i + 1, ">") || IsP(i + 1, ",")) || IsP(i + 1, "[") && IsP(i + 2, "]");
    }

    // ------------------------------------------------------------------ синтаксис

    private void Syntax()
    {
        for (int i = 0; i < _t.Length; i++)
        {
            var t = _t[i];
            if (!_c.In(t.Line))
            {
                continue;
            }

            if (t.Kind == TokenKind.Punct)
            {
                switch (t.Text)
                {
                    case "??":
                        _c.Hit(FK.Nullish, t.Line);
                        break;
                    case "||":
                        if (At(i + 1) is { Kind: TokenKind.String or TokenKind.Number } || IsId(i + 1, "null") || IsId(i + 1, "false") ||
                            IsId(i + 1, "undefined") || (IsP(i + 1, "[") || IsP(i + 1, "{")) && _match[i + 1] == i + 2)
                        {
                            _c.Hit(FK.OrDefault, t.Line);
                        }

                        break;
                    case "?.":
                        _c.Count(FK.OptionalChain, t.Line);
                        if (IsP(i + 1, "("))
                        {
                            _c.Hit(FK.OptionalCall, t.Line);
                        }

                        break;
                    case "&&":
                        if (IsIdent(i - 1) && IsIdent(i + 1) && _t[i - 1].Text == _t[i + 1].Text && IsP(i + 2, "."))
                        {
                            _c.Hit(FK.AndChain, t.Line);
                        }

                        break;
                    case "===" or "!==":
                        _c.Count(FK.StrictEq, t.Line);
                        break;
                    case "==" or "!=":
                        if (!IsId(i + 1, "null") && !IsId(i - 1, "null"))
                        {
                            _c.Hit(FK.LooseEq, t.Line);
                        }

                        break;
                    case "+":
                        if (At(i - 1) is { Kind: TokenKind.String } s1 && s1.Text.Length > 0 || At(i + 1) is { Kind: TokenKind.String } s2 && s2.Text.Length > 0)
                        {
                            _c.Hit(FK.Concat, t.Line);
                        }

                        break;
                    case "=>":
                        _c.Count(FK.ArrowFunctions, t.Line);
                        break;
                    case "??=" or "||=" or "&&=":
                        _c.Hit(FK.NewApis, t.Line);
                        break;
                }

                continue;
            }

            if (t.Kind == TokenKind.Template && t.HasSubstitutions)
            {
                _c.Count(FK.TemplateSubst, t.Line);
                continue;
            }

            if (t.Kind == TokenKind.Keyword)
            {
                switch (t.Text)
                {
                    case "const":
                        _c.Count(FK.ConstDecl, t.Line);
                        break;
                    case "let":
                        _c.Count(FK.LetDecl, t.Line);
                        break;
                    case "var":
                        _c.Hit(FK.VarDecl, t.Line);
                        break;
                    case "await":
                        _c.Count(FK.AwaitExpr, t.Line);
                        break;
                    case "function" when IsP(i - 1, "=") || IsP(i - 1, "(") || IsP(i - 1, ",") || IsP(i - 1, ":"):
                        _c.Hit(FK.FunctionExpressions, t.Line);
                        break;
                    case "for" when IsP(i + 1, "("):
                        if (At(i + 2) is { } d && d.Text is "let" or "var" && IsIdent(i + 3) && IsP(i + 4, "=") && At(i + 5) is { Kind: TokenKind.Number } &&
                            IsP(i + 6, ";"))
                        {
                            _c.Hit(FK.IndexLoops, t.Line);
                        }
                        else if (_match[i + 1] > 0 && Enumerable.Range(i + 2, Math.Min(8, _match[i + 1] - i - 2)).Any(j => IsId(j, "of")))
                        {
                            _c.Count(FK.ForOfLoops, t.Line);
                        }

                        break;
                }

                continue;
            }

            if (t.Kind != TokenKind.Identifier)
            {
                continue;
            }

            if (t.Text == "then" && IsP(i - 1, ".") && IsP(i + 1, "("))
            {
                _c.Hit(FK.ThenCalls, t.Line);
            }
            else if (t.Text == "parseInt" && IsP(i + 1, "(") && _match[i + 1] > 0 &&
                     !Enumerable.Range(i + 2, _match[i + 1] - i - 2).Any(j => IsP(j, ",") && _depth[j] == _depth[i + 1]))
            {
                _c.Hit(FK.ParseIntNoRadix, t.Line);
            }
            else if (Regex.IsMatch(t.Text, "^set[A-Z]") && IsP(i + 1, "(") && !IsP(i - 1, ".") && !IsP(i - 1, "function"))
            {
                _c.Count(FK.SetterCalls, t.Line);
                if (IsIdent(i + 2) && IsP(i + 3, "=>") || IsP(i + 2, "(") && IsIdent(i + 3) && IsP(i + 4, ")") && IsP(i + 5, "=>"))
                {
                    _c.Hit(FK.FunctionalUpdates, t.Line);
                }
            }
            else if (NewApiNames.Contains(t.Text) && IsP(i + 1, "(") || t.Text == "at" && IsP(i - 1, ".") && IsP(i + 1, "(") && IsP(i + 2, "-"))
            {
                _c.Hit(FK.NewApis, t.Line);
            }
            else if (t.Text == "async" && (IsId(i + 1, "function") || IsP(i + 1, "(") || IsIdent(i + 1) && IsP(i + 2, "=>")))
            {
                _c.Count(FK.AsyncFunctions, t.Line);
            }
        }

        foreach (var f in _funcs.Where(f => f.IsMethod && f.Async && _c.In(f.StartLine)))
        {
            _c.Count(FK.AsyncFunctions, f.StartLine);
        }
    }
}
