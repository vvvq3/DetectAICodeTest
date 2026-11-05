using System.Text.RegularExpressions;
using AiDetect.Languages.Frontend.Parsing;

namespace AiDetect.Languages.Frontend;

internal sealed partial class ScriptAnalyzer
{
    private static readonly Regex ResourceRegex = new(@"^(addEventListener|setInterval|setTimeout|subscribe|observe|WebSocket|EventSource|IntersectionObserver|ResizeObserver|MutationObserver|fetch|axios|requestAnimationFrame|on)$", RegexOptions.Compiled);

    private static readonly Regex CleanupRegex = new(@"^(removeEventListener|clearInterval|clearTimeout|unsubscribe|disconnect|close|abort|cancelAnimationFrame|off|unobserve|destroy)$", RegexOptions.Compiled);

    // ------------------------------------------------------------------ ошибки

    private void Errors()
    {
        bool hasFinally = false, loading = false, error = false, asyncWork = false, state = false;
        for (int i = 0; i < _t.Length; i++)
        {
            var t = _t[i];
            if (t.Kind == TokenKind.Identifier)
            {
                if (Regex.IsMatch(t.Text, "^(set)?(is)?[Ll]oading$"))
                {
                    loading = true;
                }

                if (Regex.IsMatch(t.Text, "^(set)?[Ee]rror(Message)?$") && (IsP(i + 1, "(") || IsP(i + 1, "=") || IsP(i - 1, "[") || IsP(i - 1, ",")))
                {
                    error = true;
                }

                if (t.Text is "fetch" or "axios" or "subscribe" or "then" or "useQuery" or "useSWR")
                {
                    asyncWork = true;
                }

                if (t.Text is "useState" or "ref" or "reactive" or "signal" or "BehaviorSubject")
                {
                    state = true;
                }
            }

            if (IsId(i, "finally"))
            {
                hasFinally = true;
            }

            if (IsId(i, "await"))
            {
                asyncWork = true;
            }

            if (!_c.In(t.Line))
            {
                continue;
            }

            if (IsId(i, "catch") && !IsP(i - 1, "."))
            {
                _c.Count(FK.CatchBlocks, t.Line);
                int body = IsP(i + 1, "(") && _match[i + 1] > 0 ? _match[i + 1] + 1 : i + 1;
                if (IsP(body, "{") && _match[body] > 0)
                {
                    int end = _match[body];
                    if (end == body + 1)
                    {
                        _c.Hit(FK.EmptyCatch, t.Line);
                    }
                    else if (IsId(body + 1, "console") && IsId(body + 3, "log") && IsP(body + 4, "(") && _match[body + 4] > 0 &&
                             (_match[body + 4] + 1 == end || IsP(_match[body + 4] + 1, ";") && _match[body + 4] + 2 == end))
                    {
                        _c.Hit(FK.CatchLogOnly, t.Line);
                    }
                }
            }

            // promise.catch(e => console.log(e)) — обработчик только с console.log.
            if (IsId(i, "catch") && IsP(i - 1, ".") && IsP(i + 1, "(") && _match[i + 1] > 0)
            {
                _c.Count(FK.CatchBlocks, t.Line);
                int p = i + 2;
                if (IsP(p, "(") && _match[p] > 0)
                {
                    p = _match[p];
                }

                if ((IsIdent(p) || IsP(p, ")")) && IsP(p + 1, "=>"))
                {
                    int b = p + 2;
                    bool block = IsP(b, "{");
                    int k = block ? b + 1 : b;
                    if (IsId(k, "console") && IsId(k + 2, "log") && IsP(k + 3, "(") && _match[k + 3] > 0 &&
                        (!block || _match[b] == _match[k + 3] + 1 || IsP(_match[k + 3] + 1, ";") && _match[b] == _match[k + 3] + 2))
                    {
                        _c.Hit(FK.CatchLogOnly, t.Line);
                    }
                }
            }

            if (IsId(i, "instanceof") && IsId(i + 1, "Error") && (IsP(i + 2, "?") || IsP(i + 2, "&&")))
            {
                _c.Hit(FK.InstanceofMessage, t.Line);
            }

            if (IsId(i, "as") && IsId(i + 1, "Error") && IsP(i + 2, ")") && IsP(i + 3, ".") ||
                At(i) is { Kind: TokenKind.Identifier } m && m.Text is "e" or "err" or "ex" && IsP(i + 1, ".") && IsId(i + 2, "message"))
            {
                _c.Hit(FK.ErrorMessageCast, t.Line);
            }

            if (IsId(i, "fetch") && IsP(i + 1, "(") && !IsP(i - 1, "."))
            {
                _c.Count(FK.FetchCalls, t.Line);
            }

            if (IsP(i, "!") && IsIdent(i + 1) && IsP(i + 2, ".") && IsId(i + 3, "ok"))
            {
                _c.Hit(FK.ResponseOk, t.Line);
            }

            if (t.Kind is TokenKind.String or TokenKind.Template)
            {
                if (FrontendText.FailedMessage().IsMatch(t.Text))
                {
                    _c.Hit(FK.FailedMessages, t.Line);
                }

                if (FrontendText.HttpErrorPhrase().IsMatch(t.Text))
                {
                    _c.Hit(FK.HttpErrorPhrase, t.Line);
                }

                if (t.Text.Contains("must be used within", StringComparison.OrdinalIgnoreCase))
                {
                    _c.Hit(FK.ContextGuard, t.Line);
                }

                if (t.Text.Contains("AbortError", StringComparison.Ordinal))
                {
                    _c.Hit(FK.AbortErrorCheck, t.Line);
                }

                if (IsErrorSink(i))
                {
                    _c.Count(FK.ErrorStrings, t.Line);
                }
            }

            if (IsId(i, "throw"))
            {
                if (IsId(i + 1, "new") && At(i + 2) is { } en && en.Text.EndsWith("Error", StringComparison.Ordinal))
                {
                    _c.Count(FK.ThrowNewError, t.Line);
                }
                else if (At(i + 1) is { Kind: TokenKind.String or TokenKind.Template } || IsP(i + 1, "{"))
                {
                    _c.Hit(FK.ThrowLiteral, t.Line);
                }
            }
        }

        int firstLine = _t.Length > 0 ? _t[0].Line : 1;
        int scoped = _t.Select(x => x.Line).FirstOrDefault(_c.In);
        if (scoped > 0 && asyncWork && state)
        {
            _c.Count(FK.StatefulComponents, scoped);
            if (loading && error && hasFinally)
            {
                _c.Hit(FK.LoadingTriad, _t.First(x => x.Kind == TokenKind.Keyword && x.Text == "finally" || x == _t[^1]).Line is var fl && _c.In(fl) ? fl : scoped);
            }
        }
    }

    /// <summary>Строка — сообщение об ошибке: аргумент throw new Error(…), console.error(…), setError(…), toast.error(…).</summary>
    private bool IsErrorSink(int i)
    {
        int j = i - 1;
        if (!IsP(j, "("))
        {
            return false;
        }

        var callee = At(j - 1);
        if (callee is null)
        {
            return false;
        }

        if (callee.Text.EndsWith("Error", StringComparison.Ordinal) && IsId(j - 2, "new"))
        {
            return true;
        }

        if (callee.Text is "error" or "warn" && IsP(j - 2, ".") && (IsId(j - 3, "console") || IsId(j - 3, "toast") || IsId(j - 3, "message") || IsId(j - 3, "notification")))
        {
            return true;
        }

        return callee.Text is "setError" or "setErrorMessage" or "showError" or "notifyError";
    }

    // ------------------------------------------------------------------ async, отмена, отписка

    private void Async()
    {
        for (int i = 0; i < _t.Length; i++)
        {
            var t = _t[i];
            if (!_c.In(t.Line) || t.Kind is not (TokenKind.Identifier or TokenKind.Keyword))
            {
                continue;
            }

            switch (t.Text)
            {
                case "useEffect" or "useLayoutEffect" when IsP(i + 1, "("):
                    Effect(i);
                    break;
                case "AbortController" when IsId(i - 1, "new"):
                    _c.Hit(FK.AbortControllers, t.Line);
                    break;
                case "let" when IsIdent(i + 1) && IsP(i + 2, "=") && IsId(i + 3, "false") &&
                                Regex.IsMatch(_t[i + 1].Text, "^(ignore|cancell?ed|isCancell?ed|didCancel|isActive|active|aborted|stale|isSubscribed|subscribed)$"):
                    _c.Hit(FK.CancelFlags, t.Line);
                    break;
                case "isMounted" or "_isMounted" or "mounted" or "isComponentMounted" when IsP(i + 1, "=") || IsP(i - 1, "."):
                    if (_host == ScriptHost.File)
                    {
                        _c.Hit(FK.IsMountedFlags, t.Line);
                    }

                    break;
                case "subscribe" when IsP(i - 1, ".") && IsP(i + 1, "("):
                    Subscribe(i);
                    break;
                case "Subject" or "ReplaySubject" when IsId(i - 1, "new") && LooksLikeDestroySubject(i):
                    _c.Hit(FK.DestroySubjects, t.Line);
                    break;
                case "onUnmounted" or "onBeforeUnmount" or "beforeDestroy" or "beforeUnmount" or "destroyed" when IsP(i + 1, "("):
                    _c.Hit(FK.VueUnmountHooks, t.Line);
                    break;
                case "all" or "allSettled" when IsP(i - 1, ".") && IsId(i - 2, "Promise"):
                    _c.Hit(FK.PromiseAll, t.Line);
                    break;
            }
        }
    }

    private bool LooksLikeDestroySubject(int i)
    {
        for (int j = i - 1; j >= 0 && j >= i - 8; j--)
        {
            if (IsIdent(j) && Regex.IsMatch(_t[j].Text, "(?i)(destroy|unsubscribe|ngUnsubscribe|onDestroy|alive|componentDestroyed)"))
            {
                return true;
            }
        }

        return false;
    }

    private void Effect(int i)
    {
        int paren = i + 1;
        int close = _match[paren];
        if (close < 0)
        {
            return;
        }

        int line = _t[i].Line;
        _c.Count(FK.Effects, line);
        int k = paren + 1;
        if (IsId(k, "async"))
        {
            _c.Hit(FK.AsyncEffect, line);
            k++;
        }

        // () => { … } или function () { … }
        int bodyOpen = -1;
        if (IsP(k, "(") && _match[k] > 0 && IsP(_match[k] + 1, "=>") && IsP(_match[k] + 2, "{"))
        {
            bodyOpen = _match[k] + 2;
        }
        else if (IsId(k, "function") && IsP(k + 1, "(") && _match[k + 1] > 0 && IsP(_match[k + 1] + 1, "{"))
        {
            bodyOpen = _match[k + 1] + 1;
        }

        if (bodyOpen < 0 || _match[bodyOpen] < 0)
        {
            return;
        }

        int bodyClose = _match[bodyOpen];
        bool resource = false, cleanup = false, cleaned = false;
        int statements = 0;
        bool onlySetter = true;
        for (int j = bodyOpen + 1; j < bodyClose; j++)
        {
            if (_depth[j] == _depth[bodyOpen] + 1 && (IsP(j, ";") || _t[j].NewLineBefore && j > bodyOpen + 1 && !IsP(j - 1, ",") && !IsP(j, ".")))
            {
                statements++;
            }

            if (IsIdent(j) && ResourceRegex.IsMatch(_t[j].Text) && (IsP(j + 1, "(") || IsId(j - 1, "new")))
            {
                resource = true;
            }

            if (IsId(j, "return") && _depth[j] == _depth[bodyOpen] + 1 &&
                (IsP(j + 1, "(") || IsIdent(j + 1) && !IsP(j + 2, "(") || IsId(j + 1, "function") || IsId(j + 1, "async")))
            {
                cleanup = true;
                int end = IsP(j + 1, "(") && _match[j + 1] > 0 ? _match[j + 1] + 4 : j + 30;
                for (int q = j + 1; q < bodyClose && q < end + 40; q++)
                {
                    if (IsIdent(q) && (CleanupRegex.IsMatch(_t[q].Text) || Regex.IsMatch(_t[q].Text, "^(ignore|cancell?ed|isCancell?ed|didCancel|active|isActive|aborted|stale|isSubscribed|subscribed)$") && IsP(q + 1, "=")))
                    {
                        cleaned = true;
                    }
                }
            }

            if (_depth[j] == _depth[bodyOpen] + 1 && IsIdent(j) && !Regex.IsMatch(_t[j].Text, "^set[A-Z]") && (j == bodyOpen + 1 || _t[j].NewLineBefore))
            {
                onlySetter = false;
            }
        }

        if (cleanup)
        {
            _c.Hit(FK.EffectsWithCleanup, line);
        }

        if (resource)
        {
            _c.Count(FK.EffectsWithResource, line);
            if (cleanup && cleaned)
            {
                _c.Hit(FK.EffectsResourceCleaned, line);
            }
        }

        if (!resource && onlySetter && statements <= 1 && bodyClose > bodyOpen + 2 && Regex.IsMatch(_t[bodyOpen + 1].Text, "^set[A-Z]"))
        {
            _c.Hit(FK.DerivedStateEffect, line);
        }
    }

    private void Subscribe(int i)
    {
        int line = _t[i].Line;
        int paren = i + 1;
        _c.Count(FK.SubscribeCalls, line);
        if (IsP(paren + 1, "{"))
        {
            _c.Hit(FK.NgObserverSubscribe, line);
        }

        // Вложенный subscribe: внутри аргументов другого subscribe.
        for (int j = i - 1; j >= 0 && j >= i - 300; j--)
        {
            if (IsId(j, "subscribe") && IsP(j + 1, "(") && _match[j + 1] > i)
            {
                _c.Hit(FK.NgNestedSubscribe, line);
                break;
            }
        }

        var cls = ClassAt(i);
        if (!IsAngularUiClass(cls))
        {
            return;
        }

        // Начало цепочки: назад до начала выражения.
        int start = i;
        while (start > 0 && !(IsP(start - 1, ";") || IsP(start - 1, "{") || IsP(start - 1, "}") || _t[start].NewLineBefore && IsId(start, "this") && _depth[start] == _depth[i]))
        {
            start--;
            if (i - start > 120)
            {
                break;
            }
        }

        bool http = false, torn = false;
        for (int j = start; j < i; j++)
        {
            if (IsIdent(j) && (_t[j].Text is "http" or "httpClient" or "HttpClient" || _t[j].Text.EndsWith("Service", StringComparison.Ordinal) && IsP(j + 1, ".") &&
                                   At(j + 2) is { } m && Regex.IsMatch(m.Text, "^(get|post|put|patch|delete|load|fetch|save|create|update|remove)")))
            {
                http = true;
            }

            if (IsIdent(j) && _t[j].Text is "takeUntil" or "takeUntilDestroyed" or "untilDestroyed" or "first" or "take" or "takeWhile")
            {
                torn = true;
            }

            if (IsP(j, "=") || IsId(j, "add") && IsP(j - 1, ".") || IsId(j, "push") && IsP(j - 1, "."))
            {
                // Подписка сохранена в поле/коллекцию — отписка в ngOnDestroy.
                torn |= ClassHasUnsubscribe(cls!);
            }
        }

        if (http && !torn)
        {
            return;
        }

        _c.Count(FK.Subscribes, line);
        if (torn)
        {
            _c.Count(FK.SubscribesTornDown, line);
        }
        else
        {
            _c.S.AddExample(FK.Subscribes, _c.File, line, _c.LineText(line));
        }
    }

    private bool ClassHasUnsubscribe(ClassInfo cls)
    {
        for (int j = cls.BodyOpen; j < cls.BodyClose; j++)
        {
            if (IsId(j, "unsubscribe") && IsP(j - 1, "."))
            {
                return true;
            }
        }

        return false;
    }

    // ------------------------------------------------------------------ логирование

    private void Logging()
    {
        for (int i = 0; i < _t.Length; i++)
        {
            if (!IsId(i, "console") || !IsP(i + 1, ".") || !IsP(i + 3, "(") || !_c.In(_t[i].Line))
            {
                continue;
            }

            int line = _t[i].Line;
            var method = _t[i + 2].Text;
            if (method is "error" or "warn")
            {
                _c.Count(FK.ConsoleErrors, line);
                if (At(i + 4) is { Kind: TokenKind.String or TokenKind.Template } s && FrontendText.LogPrefix().IsMatch(s.Text) && IsP(i + 5, ",") &&
                    (IsIdent(i + 6) || At(i + 6) is { Kind: TokenKind.Keyword }))
                {
                    _c.Hit(FK.ConsoleErrorTemplated, line);
                }
            }
            else if (method is "log" or "debug" or "info" or "trace" or "table" or "dir" && !_c.IsTest)
            {
                _c.Hit(FK.ConsoleLogs, line);
            }
        }
    }

    // ------------------------------------------------------------------ React

    private void React()
    {
        foreach (var f in _funcs.Where(f => f.IsComponent && _c.In(f.StartLine)))
        {
            _c.Count(FK.Components, f.StartLine);
            bool typed = false, named = false;
            int from = f.ParamOpen;
            int to = f.ParamClose;
            for (int j = from; j <= to; j++)
            {
                if (IsP(j, ":") && IsIdent(j + 1) && _t[j + 1].Text.EndsWith("Props", StringComparison.Ordinal) && _depth[j] == _depth[from])
                {
                    typed = true;
                    named |= _t[j + 1].Text == f.Name + "Props";
                }
            }

            // const Foo: FC<FooProps> = … / React.FC<FooProps>
            for (int j = f.StartTok - 1; j >= 0 && j >= f.StartTok - 12; j--)
            {
                if (IsIdent(j) && _t[j].Text.EndsWith("Props", StringComparison.Ordinal) && IsP(j - 1, "<"))
                {
                    typed = true;
                    named |= _t[j].Text == f.Name + "Props";
                }

                if (IsP(j, ";") || IsP(j, "}"))
                {
                    break;
                }
            }

            if (typed)
            {
                _c.Count(FK.PropsTyped, f.StartLine);
            }

            if (named)
            {
                _c.Count(FK.PropsTypeNamed, f.StartLine);
            }
        }

        foreach (var f in _funcs.Where(f => f.IsHook && _c.In(f.StartLine) && f.Name.Length > 0))
        {
            _c.Hit(FK.CustomHooks, f.StartLine);
        }

        foreach (var c in _classes.Where(c => c.Extends is "Component" or "PureComponent" or "React.Component" or "React.PureComponent" && _c.In(c.Line)))
        {
            _c.Hit(FK.ClassComponents, c.Line);
            _c.Count(FK.Components, c.Line);
        }

        for (int i = 0; i < _t.Length; i++)
        {
            var t = _t[i];
            if (!_c.In(t.Line) || t.Kind != TokenKind.Identifier)
            {
                continue;
            }

            switch (t.Text)
            {
                case "useCallback" or "useMemo" when IsP(i + 1, "(") || IsP(i + 1, "<"):
                    _c.Count(FK.MemoHooks, t.Line);
                    break;
                case "memo" when IsP(i + 1, "(") && (IsP(i - 1, ".") && IsId(i - 2, "React") || !IsP(i - 1, ".")):
                    _c.Count(FK.MemoHooks, t.Line);
                    break;
                case "FC" or "FunctionComponent" or "VFC" or "SFC" when IsP(i + 1, "<") || IsP(i + 1, "=") || IsP(i - 1, ".") && IsId(i - 2, "React"):
                    _c.Hit(FK.FcType, t.Line);
                    break;
                case "propTypes" or "PropTypes" when !IsId(i - 1, "import"):
                    _c.Hit(FK.PropTypes, t.Line);
                    break;
                case "displayName" when IsP(i - 1, ".") && IsP(i + 1, "="):
                    _c.Hit(FK.DisplayName, t.Line);
                    break;
            }
        }
    }

    // ------------------------------------------------------------------ Angular

    private void Angular()
    {
        foreach (var c in _classes.Where(c => c.Decorators.Count > 0 && _c.In(c.Line)))
        {
            if (c.Decorators.Contains("Component"))
            {
                _c.Count(FK.NgComponents, c.Line);
            }

            // constructor(private readonly http: HttpClient)
            for (int j = c.BodyOpen; j < c.BodyClose; j++)
            {
                if (IsId(j, "constructor") && IsP(j + 1, "(") && _match[j + 1] > 0)
                {
                    for (int k = j + 2; k < _match[j + 1]; k++)
                    {
                        if (At(k) is { Kind: TokenKind.Identifier } m && m.Text is "private" or "public" or "protected" or "readonly" &&
                            (IsP(k - 1, "(") || IsP(k - 1, ",") || IsP(k - 1, ")")))
                        {
                            _c.Count(FK.NgCtorParams, _t[k].Line);
                            if (m.Text == "readonly" || IsId(k + 1, "readonly"))
                            {
                                _c.Count(FK.NgCtorReadonly, _t[k].Line);
                            }
                        }
                    }

                    break;
                }
            }
        }

        bool angular = _classes.Any(c => c.Decorators.Count > 0) || _t.Any(t => t.Kind == TokenKind.String && t.Text.StartsWith("@angular/", StringComparison.Ordinal));
        for (int i = 0; i < _t.Length; i++)
        {
            var t = _t[i];
            if (!_c.In(t.Line) || t.Kind != TokenKind.Identifier)
            {
                continue;
            }

            if (t.Text is "get" or "post" or "put" or "patch" or "delete" && IsP(i - 1, ".") && At(i - 2) is { } h &&
                h.Text is "http" or "httpClient" or "_http" && (IsP(i + 1, "(") || IsP(i + 1, "<")))
            {
                _c.Count(FK.NgHttpCalls, t.Line);
                if (IsP(i + 1, "<") && !IsId(i + 2, "any"))
                {
                    _c.Count(FK.NgHttpTyped, t.Line);
                }
            }
            else if (t.Text == "OnPush" && IsP(i - 1, "."))
            {
                _c.Hit(FK.NgOnPush, t.Line);
            }
            else if (angular && t.Text == "inject" && IsP(i + 1, "(") && !IsP(i - 1, "."))
            {
                _c.Count(FK.NgInject, t.Line);
            }
            else if (angular && t.Text is "signal" or "computed" or "effect" or "toSignal" or "input" or "output" or "model" or "linkedSignal" && IsP(i + 1, "(") &&
                     !IsP(i - 1, "."))
            {
                _c.Count(FK.NgSignals, t.Line);
            }
            else if (t.Text is "catchError" or "finalize" or "debounceTime" or "distinctUntilChanged" or "switchMap" or "shareReplay" && IsP(i + 1, "("))
            {
                _c.Count(FK.NgRxOperators, t.Line);
            }
            else if (t.Text is "getElementById" or "querySelector" or "querySelectorAll" or "getElementsByClassName" && IsP(i - 1, ".") && IsId(i - 2, "document") ||
                     t.Text == "style" && IsP(i - 1, ".") && IsId(i - 2, "nativeElement"))
            {
                _c.Hit(FK.NgManualDom, t.Line);
            }
        }
    }

    // ------------------------------------------------------------------ Vue

    private void Vue()
    {
        bool vueFile = _host == ScriptHost.Vue || _t.Any(t => t.Kind == TokenKind.String && t.Text is "vue" or "vue-property-decorator" or "vue-class-component");
        if (!vueFile)
        {
            return;
        }

        for (int i = 0; i < _t.Length; i++)
        {
            var t = _t[i];
            if (!_c.In(t.Line) || t.Kind != TokenKind.Identifier)
            {
                continue;
            }

            switch (t.Text)
            {
                case "defineProps":
                    _c.Count(FK.VueProps, t.Line);
                    if (IsP(i + 1, "<"))
                    {
                        _c.Hit(FK.VuePropsTyped, t.Line);
                    }

                    break;
                case "props" when IsP(i + 1, ":") && _depth[i] <= 1 && (IsP(i + 2, "{") || IsP(i + 2, "[")):
                    _c.Count(FK.VueProps, t.Line);
                    break;
                case "withDefaults" when IsP(i + 1, "("):
                    _c.Hit(FK.VueWithDefaults, t.Line);
                    break;
                case "defineEmits":
                    _c.Count(FK.VueEmits, t.Line);
                    if (IsP(i + 1, "<"))
                    {
                        _c.Hit(FK.VueEmitsTyped, t.Line);
                    }

                    break;
                case "emits" when IsP(i + 1, ":") && _depth[i] <= 1:
                    _c.Count(FK.VueEmits, t.Line);
                    break;
                case "methods" or "computed" or "watch" when IsP(i + 1, ":") && _depth[i] <= 1:
                case "data" when IsP(i + 1, "(") && _depth[i] <= 1 && IsP(_match[i + 1] + 1, "{"):
                    _c.Hit(FK.VueOptionsApi, t.Line);
                    break;
                case "$refs" or "$parent" or "$forceUpdate" or "$set" or "$children" when IsP(i - 1, "."):
                    _c.Hit(FK.VueOptionsApi, t.Line);
                    break;
            }
        }
    }

    // ------------------------------------------------------------------ JSX: разметка и доступность

    private void Jsx()
    {
        for (int i = 0; i < _t.Length; i++)
        {
            var t = _t[i];
            if (t.Kind == TokenKind.JsxText && _c.In(t.Line) && t.Text.Any(char.IsLetter) && t.Text.Length >= 3)
            {
                _c.Count(FK.HardcodedTexts, t.Line);
                continue;
            }

            if (t.Kind != TokenKind.JsxOpen || !_c.In(t.Line) || t.Text.Length == 0)
            {
                continue;
            }

            var attrs = new List<(Token Attr, int ValueStart)>();
            int j = i + 1;
            while (j < _t.Length)
            {
                if (_t[j].Kind == TokenKind.JsxAttr)
                {
                    attrs.Add((_t[j], j + 1));
                    if (_t[j].ExprValue && _t[j + 1].Kind == TokenKind.JsxExprStart && _match[j + 1] > j)
                    {
                        j = _match[j + 1] + 1;
                        continue;
                    }

                    j++;
                    continue;
                }

                if (_t[j].Kind == TokenKind.JsxExprStart && IsP(j + 1, "...") && _match[j] > j)
                {
                    j = _match[j] + 1;
                    continue;
                }

                break;
            }

            var name = t.Text;
            bool Has(string a) => attrs.Any(x => x.Attr.Text == a);
            _c.Count(FK.Elements, t.Line);
            if (name == "button")
            {
                _c.Count(FK.Buttons, t.Line);
                if (Has("type"))
                {
                    _c.Hit(FK.ButtonsTyped, t.Line);
                }
            }

            if (name is "img")
            {
                _c.Count(FK.Images, t.Line);
                if (attrs.Any(a => a.Attr.Text == "alt" && (a.Attr.ExprValue || !string.IsNullOrEmpty(a.Attr.Value))))
                {
                    _c.Count(FK.ImagesAlt, t.Line);
                }
            }

            if (name is "input" or "select" or "textarea")
            {
                _c.Count(FK.Inputs, t.Line);
            }

            if (name == "label" && (Has("htmlFor") || Has("for")))
            {
                _c.Hit(FK.LabelsFor, t.Line);
            }

            if (name is "div" or "span" or "li" or "td" && attrs.Any(a => a.Attr.Text == "onClick") && !Has("role") && !Has("onKeyDown") && !Has("onKeyPress"))
            {
                _c.Hit(FK.ClickOnDiv, t.Line);
            }

            foreach (var (a, valueStart) in attrs)
            {
                var an = a.Text;
                if (an.StartsWith("aria-", StringComparison.Ordinal))
                {
                    _c.Hit(FK.AriaAttrs, a.Line);
                }
                else if (an == "role")
                {
                    _c.Count(FK.RoleAttrs, a.Line);
                }
                else if (an is "data-testid" or "data-test" or "data-cy" or "data-test-id" or "data-qa")
                {
                    _c.Hit(FK.TestIds, a.Line);
                }
                else if (an == "key")
                {
                    _c.Count(FK.KeyAttrs, a.Line);
                    if (a.ExprValue && At(valueStart + 1) is { Kind: TokenKind.Identifier } kv && kv.Text is "index" or "i" or "idx" or "j" or "ind" &&
                        At(valueStart + 2) is { Kind: TokenKind.JsxExprEnd } ||
                        a.ExprValue && _match[valueStart] > valueStart && Enumerable.Range(valueStart + 1, _match[valueStart] - valueStart - 1).Any(q => IsId(q, "index")))
                    {
                        _c.Hit(FK.KeyIndex, a.Line);
                    }
                }
                else if (an == "style" && a.ExprValue && At(valueStart + 1) is { Kind: TokenKind.Punct, Text: "{" })
                {
                    _c.Hit(FK.InlineStyles, a.Line);
                }
                else if (an.Length > 2 && an.StartsWith("on", StringComparison.Ordinal) && char.IsUpper(an[2]) && a.ExprValue)
                {
                    // onClick={handleClick} / onClick={this.handleClick}
                    int end = _match[valueStart];
                    if (end > valueStart && end - valueStart <= 4 && At(end - 1) is { Kind: TokenKind.Identifier } h)
                    {
                        _c.Count(FK.HandlerAttrs, a.Line);
                        if (h.Text.StartsWith("handle", StringComparison.Ordinal))
                        {
                            _c.Count(FK.HandlerAttrsHandle, a.Line);
                        }
                    }
                }
            }
        }
    }

    // ------------------------------------------------------------------ тесты

    private void Tests()
    {
        bool mocks = false;
        int caseCount = 0;
        bool onlyCreate = true;
        for (int i = 0; i < _t.Length; i++)
        {
            var t = _t[i];
            if (t.Kind == TokenKind.Identifier && t.Text is "fn" or "mock" or "spyOn" && IsP(i - 1, ".") && IsId(i - 2, "jest") || IsId(i - 2, "vi") && IsP(i - 1, "."))
            {
                mocks = true;
            }

            if (t.Kind == TokenKind.Identifier && t.Text is "it" or "test" && IsP(i + 1, "(") && At(i + 2) is { Kind: TokenKind.String or TokenKind.Template } title)
            {
                caseCount++;
                onlyCreate &= Regex.IsMatch(title.Text, "^should (create|be created)$");
            }

            if (!_c.In(t.Line))
            {
                continue;
            }

            if (t.Kind is TokenKind.String or TokenKind.Template && FrontendText.ExampleData().IsMatch(t.Text))
            {
                _c.Hit(FK.ExampleData, t.Line);
            }

            if (t.Kind != TokenKind.Identifier)
            {
                continue;
            }

            switch (t.Text)
            {
                case "it" or "test" when IsP(i + 1, "(") && At(i + 2) is { Kind: TokenKind.String or TokenKind.Template } s:
                    _c.Count(FK.TestCases, t.Line);
                    if (s.Text.StartsWith("should ", StringComparison.OrdinalIgnoreCase))
                    {
                        _c.Count(FK.TestShould, t.Line);
                    }

                    break;
                case "each" when IsP(i - 1, ".") && At(i - 2) is { } d && d.Text is "it" or "test" or "describe":
                    _c.Hit(FK.TestEach, t.Line);
                    break;
                case "clearAllMocks" or "resetAllMocks" or "restoreAllMocks":
                    _c.Hit(FK.TestClearMocks, t.Line);
                    break;
                case "getByRole" or "findByRole" or "queryByRole" or "getAllByRole" or "findAllByRole" or "getByLabelText" or "findByLabelText" or "queryByLabelText":
                    _c.Hit(FK.RtlRoleQueries, t.Line);
                    break;
                case "getByTestId" or "queryByTestId" or "findByTestId" or "getAllByTestId" or "getByText" or "queryByText" or "querySelector" or "querySelectorAll":
                    _c.Count(FK.RtlOtherQueries, t.Line);
                    break;
                case "find" when IsP(i - 1, ".") && IsId(i - 2, "wrapper"):
                    _c.Count(FK.RtlOtherQueries, t.Line);
                    break;
                case "userEvent" when IsP(i + 1, "."):
                    _c.Hit(FK.UserEvent, t.Line);
                    break;
                case "fireEvent" when IsP(i + 1, "."):
                    _c.Count(FK.FireEvent, t.Line);
                    break;
                case "toMatchSnapshot" or "toMatchInlineSnapshot":
                    _c.Hit(FK.Snapshots, t.Line);
                    break;
                case "toBe" or "toEqual" or "toStrictEqual" or "toHaveBeenCalledWith" or "toHaveBeenCalledTimes" or "toHaveTextContent" or "toHaveLength" or "toContain"
                    or "toHaveValue" or "toHaveAttribute" or "toMatchObject" or "toHaveBeenLastCalledWith" or "toHaveProperty":
                    _c.Count(FK.StrongAsserts, t.Line);
                    break;
                case "toThrow" when IsP(i + 1, "(") && !IsP(i + 2, ")"):
                    _c.Count(FK.StrongAsserts, t.Line);
                    break;
                case "toBeTruthy" or "toBeFalsy" or "toBeDefined" or "toBeUndefined" or "toHaveBeenCalled" or "toThrow":
                    _c.Count(FK.WeakAsserts, t.Line);
                    break;
            }
        }

        int scoped = _t.Select(x => x.Line).FirstOrDefault(_c.In);
        if (scoped > 0)
        {
            _c.Count(FK.TestFiles, scoped);
            if (mocks)
            {
                _c.Count(FK.TestFilesWithMocks, scoped);
            }

            if (caseCount > 0 && onlyCreate)
            {
                _c.Count(FK.CliSkeletonSpecs, scoped);
            }
        }
    }

    // ------------------------------------------------------------------ ручные привычки

    private void Habits()
    {
        for (int i = 0; i < _t.Length; i++)
        {
            var t = _t[i];
            if (!_c.In(t.Line))
            {
                continue;
            }

            if (IsId(i, "debugger"))
            {
                _c.Hit(FK.Debugger, t.Line);
            }
            else if (IsId(i, "alert") && IsP(i + 1, "(") && (!IsP(i - 1, ".") || IsId(i - 2, "window")))
            {
                _c.Hit(FK.Alerts, t.Line);
            }
            else if (IsId(i, "setTimeout") && IsP(i + 1, "(") && _match[i + 1] > 0 && IsP(_match[i + 1] - 2, ",") &&
                     At(_match[i + 1] - 1) is { Kind: TokenKind.Number, Text: "0" })
            {
                _c.Hit(FK.SetTimeoutZero, t.Line);
            }
            else if (IsId(i, "reload") && IsP(i - 1, ".") && IsId(i - 2, "location"))
            {
                _c.Hit(FK.Reloads, t.Line);
            }
            else if ((IsId(i, "$") || IsId(i, "jQuery")) && IsP(i + 1, "(") && !IsP(i - 1, "."))
            {
                _c.Hit(FK.JQuery, t.Line);
            }
            else if (t.Kind == TokenKind.Number && !_c.IsTest && t.Text is not ("0" or "1" or "2" or "100" or "10" or "1000" or "0.5") &&
                     !IsP(i - 1, "=") && !IsP(i - 1, ":") && !IsP(i - 1, "[") && !(IsP(i - 1, "-") && IsP(i - 2, "=")) && (IsP(i - 1, "*") || IsP(i - 1, "/") ||
                                                                                                                      IsP(i - 1, ">") || IsP(i - 1, "<") ||
                                                                                                                      IsP(i - 1, ">=") || IsP(i - 1, "<=") ||
                                                                                                                      IsP(i - 1, "===") || IsP(i - 1, "+") ||
                                                                                                                      IsP(i - 1, "-")))
            {
                _c.Hit(FK.MagicNumbers, t.Line);
            }
        }

        var seen = new HashSet<int>();
        foreach (var t in _all.Where(t => (t.Kind is TokenKind.Identifier || t.IsComment) && _c.In(t.Line)))
        {
            if (!seen.Contains(t.Line) && FrontendText.HasTypo(t.Text))
            {
                seen.Add(t.Line);
                _c.Hit(FK.Typos, t.Line);
            }
        }
    }

    // ------------------------------------------------------------------ форматирование

    private static readonly Regex KeywordNoSpaceRegex = new(@"\b(if|for|while|switch|catch)\(", RegexOptions.Compiled);
    private static readonly Regex BraceNoSpaceRegex = new(@"\)\{|\belse\{|\}else\b", RegexOptions.Compiled);
    private static readonly Regex OperatorNoSpaceRegex = new(@"[A-Za-z0-9_\)\]](===|!==|==|!=|<=|>=|&&|\|\||\+=|-=)[A-Za-z0-9_\(""'`]|[A-Za-z0-9_\)\]]=[A-Za-z0-9_\(""'`]", RegexOptions.Compiled);
    private static readonly Regex StringsRegex = new(@"""(?:\\.|[^""\\])*""|'(?:\\.|[^'\\])*'|`[^`]*`|//.*$|/\*.*?\*/|=>|<[^<>]*>", RegexOptions.Compiled);

    private void Formatting()
    {
        var lines = new SortedSet<int>(_t.Select(t => t.Line).Where(_c.In));
        int blank = 0;
        int firstLine = _t.Length > 0 ? _t[0].Line : 1;
        int lastLine = _t.Length > 0 ? _t[^1].EndLine : 0;
        bool tabs = false, spaces = false;
        for (int l = firstLine; l <= lastLine; l++)
        {
            var text = _c.LineText(l);
            if (text.Trim().Length == 0)
            {
                blank++;
                if (blank == 2 && _c.In(l))
                {
                    _c.Hit(FK.DoubleBlank, l);
                }

                continue;
            }

            blank = 0;
            if (!lines.Contains(l))
            {
                continue;
            }

            if (text.Length > 0 && (text[^1] == ' ' || text[^1] == '\t'))
            {
                _c.Hit(FK.TrailingWhitespace, l);
            }

            if (text.StartsWith('\t'))
            {
                tabs = true;
                _c.Count(FK.TabIndent, l);
            }
            else if (text.StartsWith("  ", StringComparison.Ordinal))
            {
                spaces = true;
            }

            if (text.Length > 120 && !text.TrimStart().StartsWith("//", StringComparison.Ordinal) && !text.Contains("http", StringComparison.Ordinal) &&
                !text.TrimStart().StartsWith("import", StringComparison.Ordinal) && !text.Contains("className=", StringComparison.Ordinal))
            {
                _c.Hit(FK.LongLines, l);
            }

            var code = StringsRegex.Replace(text, "\"\"");
            if (KeywordNoSpaceRegex.IsMatch(code))
            {
                _c.Hit(FK.KeywordNoSpace, l);
            }

            if (BraceNoSpaceRegex.IsMatch(code))
            {
                _c.Hit(FK.BraceNoSpace, l);
            }

            if (OperatorNoSpaceRegex.IsMatch(code) && !code.TrimStart().StartsWith('<'))
            {
                _c.Hit(FK.OperatorNoSpace, l);
            }
        }

        if (tabs && spaces && lines.Count > 0)
        {
            _c.Count(FK.MixedIndentFiles, lines.Min);
        }

        // Кавычки и точки с запятой (по добавленным строкам файла).
        int single = 0, dbl = 0, semis = 0, missing = 0;
        for (int i = 0; i < _t.Length; i++)
        {
            var t = _t[i];
            if (!_c.In(t.Line))
            {
                continue;
            }

            if (t.Kind == TokenKind.String && t.Value is { Length: > 1 } v && !IsId(i - 1, "from") && !IsId(i - 1, "import"))
            {
                if (v[0] == '\'')
                {
                    single++;
                }
                else
                {
                    dbl++;
                }
            }

            if (IsP(i, ";") && (i + 1 >= _t.Length || _t[i + 1].NewLineBefore))
            {
                semis++;
            }

            if (i > 0 && t.NewLineBefore && t.Kind == TokenKind.Keyword && t.Text is "const" or "let" or "var" or "return" or "throw" or "import" or "export" &&
                At(i - 1) is { } p && (p.Kind is TokenKind.Identifier or TokenKind.Number or TokenKind.String or TokenKind.Template or TokenKind.Regex ||
                                       p.Kind == TokenKind.Punct && p.Text is ")" or "]" || p.Kind == TokenKind.Keyword && p.Text is "true" or "false" or "null" or "this") &&
                !(p.Kind == TokenKind.Punct && p.Text == ")" && _match[i - 1] > 0 && At(_match[i - 1] - 1) is { } kw && kw.Text is "if" or "for" or "while"))
            {
                missing++;
            }
        }

        int first = lines.Count > 0 ? lines.Min : 0;
        if (first == 0)
        {
            return;
        }

        _c.Count(FK.SingleQuotes, first, single);
        _c.Count(FK.DoubleQuotes, first, dbl);
        if (Math.Min(single, dbl) >= 2 && Math.Min(single, dbl) >= 0.2 * (single + dbl))
        {
            _c.Hit(FK.MixedQuoteFiles, first);
        }

        _c.Count(FK.Semicolons, first, semis);
        _c.Count(FK.MissingSemicolons, first, missing);
        if (Math.Min(semis, missing) >= 2 && Math.Min(semis, missing) >= 0.2 * (semis + missing))
        {
            _c.Hit(FK.MixedSemicolonFiles, first);
        }
    }

    // ------------------------------------------------------------------ функции

    private void Functions()
    {
        foreach (var f in _funcs.Where(f => f.BlockBody && _c.In(f.StartLine)))
        {
            int length = f.EndLine - f.StartLine + 1;
            int complexity = 1;
            int maxNest = 0;
            var stack = new Stack<bool>();
            for (int j = f.BodyOpen + 1; j < f.BodyClose && j < _t.Length; j++)
            {
                var t = _t[j];
                if (t.Kind == TokenKind.Keyword && t.Text is "if" or "for" or "while" or "case" or "catch" or "do" ||
                    t.Kind == TokenKind.Punct && t.Text is "&&" or "||" or "??" or "?" && !IsP(j + 1, ":") && !IsP(j + 1, ".") && !IsP(j + 1, ")"))
                {
                    complexity++;
                }

                if (IsP(j, "{"))
                {
                    bool control = IsP(j - 1, ")") && _match[j - 1] > 0 && At(_match[j - 1] - 1) is { } kw && kw.Text is "if" or "for" or "while" or "switch" or "catch" ||
                                   IsId(j - 1, "else") || IsId(j - 1, "try") || IsId(j - 1, "finally") || IsId(j - 1, "do");
                    stack.Push(control);
                    maxNest = Math.Max(maxNest, stack.Count(x => x));
                }
                else if (IsP(j, "}") && stack.Count > 0)
                {
                    stack.Pop();
                }
            }

            _c.Count(FK.Functions, f.StartLine);
            _c.S.MethodLengths.Add(length);
            _c.S.MethodComplexities.Add(complexity);
            if (length > 60)
            {
                _c.Hit(FK.LongFunctions, f.StartLine);
            }

            if (maxNest > 3)
            {
                _c.Hit(FK.DeepNesting, f.StartLine);
            }

            if (complexity > 12)
            {
                _c.Hit(FK.ComplexFunctions, f.StartLine);
            }
        }
    }
}
