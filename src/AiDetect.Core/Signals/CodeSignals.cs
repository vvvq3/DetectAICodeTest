using AiDetect.Core.Analysis;
using AiDetect.Core.Features;

namespace AiDetect.Core.Signals;

/// <summary>
/// Сигналы по самому коду (стилометрия на Roslyn) с поправкой на норму проекта.
/// Каждый признак: доля/плотность → свидетельство e ∈ [−1, 1] между «типично для человека» и «типично для Claude».
/// </summary>
internal sealed class CodeSignals(AnalysisContext ctx, SignalBuilder b)
{
    private readonly CodeStats _a = ctx.Added;
    private readonly CodeStats? _base = ctx.Baseline;

    private static readonly HashSet<string> GenericNames = new(StringComparer.Ordinal)
    {
        "result", "results", "response", "data", "item", "items", "value", "values", "entity", "entities", "request", "model",
        "dto", "output", "input", "content", "payload", "options", "settings", "existing", "query", "command", "builder",
    };

    private static readonly string[] GenericPrefixes = ["existing", "normalized", "updated", "trimmed", "created", "validated", "sanitized", "filtered", "mapped"];

    private static readonly HashSet<string> Abbreviations = new(StringComparer.OrdinalIgnoreCase)
    {
        "cfg", "conf", "repo", "repos", "mgr", "svc", "srv", "req", "res", "resp", "msg", "tmp", "temp", "cnt", "idx", "str", "obj", "arr",
        "lst", "dt", "val", "num", "btn", "usr", "pwd", "conn", "cmd", "prm", "params", "prms", "ctx", "dict", "col", "cols", "elem", "el",
        "doc", "docs", "fn", "func", "cb", "evt", "ev", "addr", "pos", "len", "sz", "buf", "ret", "rslt", "lst", "qry", "db", "dbo", "ent",
        "sb", "ms", "ts", "tm", "ctl", "ctrl", "rq", "rs", "ds", "dr", "dto2", "mdl", "itm", "q", "r", "c", "d", "t", "u", "o", "s", "p", "m", "n",
    };

    private static readonly HashSet<string> ShortNameExceptions = new(StringComparer.Ordinal) { "id", "i", "j", "k", "ok", "_", "x", "y", "z", "e", "ex" };

    public void Evaluate()
    {
        Documentation();
        Comments();
        Naming();
        Syntax();
        Defensive();
        Async();
        Logging();
        Exceptions();
        Structure();
        AgentIdioms();
        HumanHabits();
        Formatting();
        Tests();
        ProjectShift();
    }

    // ------------------------------------------------------------------ документация

    private void Documentation()
    {
        b.Rate("doc.coverage", SignalGroup.Documentation,
            ai: "XML-doc почти на каждом публичном члене", human: "Публичные члены почти без XML-doc",
            hits: K.DocTargetsDocumented, total: K.DocTargets, humanAt: 0.05, aiAt: 0.85, k: 5, weight: 1.2, alpha: 0.7,
            detail: (h, t) => $"{h:0} из {t:0} публичных классов/методов/свойств с XML-doc", exAi: K.DocMethodsComplete);

        b.Rate("doc.complete", SignalGroup.Documentation,
            ai: "XML-doc заполнен полностью: <param> на каждый параметр и <returns>", human: "XML-doc неполный (нет <param>/<returns>)",
            hits: K.DocMethodsComplete, total: K.DocMethodsWithParams, humanAt: 0.3, aiAt: 0.95, k: 3, weight: 0.4, alpha: 0.5,
            detail: (h, t) => $"{h:0} из {t:0} документированных методов с параметрами", exAi: K.DocMethodsComplete);

        b.Rate("doc.template", SignalGroup.Documentation,
            ai: "Шаблонные формулировки XML-doc («Gets or sets…», «A token to cancel…»)", human: "XML-doc без шаблонных формулировок",
            hits: K.DocTemplatePhrase, total: K.DocBlocks, humanAt: 0.05, aiAt: 0.5, k: 4, weight: 0.5, alpha: 0.8,
            detail: (h, t) => $"{h:0} из {t:0} блоков документации", exAi: K.DocTemplatePhrase, minTotal: 2);

        if (_a[K.DocExceptionTags] >= 1)
        {
            b.Add("doc.exceptionTags", SignalGroup.Documentation, "XML-doc документирует исключения: <exception cref=…>",
                1, SignalBuilder.Rel(_a[K.DocExceptionTags], 1), 0.15, $"{_a[K.DocExceptionTags]:0} блоков", [], _a[K.DocExceptionTags]);
        }

        if (_a[K.InheritDoc] >= 2)
        {
            b.Add("doc.inheritdoc", SignalGroup.Documentation, "Реализации помечены /// <inheritdoc />",
                1, SignalBuilder.Rel(_a[K.InheritDoc], 2), 0.4, $"{_a[K.InheritDoc]:0} раз", [], _a[K.InheritDoc]);
        }
    }

    // ------------------------------------------------------------------ комментарии

    private void Comments()
    {
        double prose = _a[K.LineComments] - _a[K.CommentedOutCode];
        if (prose >= 3)
        {
            b.Add("cmt.narrative", SignalGroup.Comments,
                _a[K.NarrativeComments] / prose >= 0.275 ? "Пошаговые комментарии-«повествование» (// Validate…, // Map…, // Save…)" : "Мало пошаговых комментариев-повествований",
                Soften(SignalBuilder.Map(_a[K.NarrativeComments] / prose, 0.05, 0.5), 0.3), SignalBuilder.Rel(prose, 4), 0.7,
                $"{_a[K.NarrativeComments]:0} из {prose:0} комментариев начинаются с глагола-действия", _a.GetExamples(K.NarrativeComments),
                _a[K.NarrativeComments] / prose);

            b.Add("cmt.restating", SignalGroup.Comments,
                _a[K.RestatingComments] / prose >= 0.15 ? "Комментарии пересказывают следующую строку кода" : "Комментарии не пересказывают код",
                Soften(SignalBuilder.Map(_a[K.RestatingComments] / prose, 0.0, 0.3), 0.25), SignalBuilder.Rel(prose, 4), 0.5,
                $"{_a[K.RestatingComments]:0} из {prose:0} комментариев повторяют идентификаторы следующей строки", _a.GetExamples(K.RestatingComments),
                _a[K.RestatingComments] / prose);

            var lower = _a[K.LowercaseComments] / prose;
            b.Add("cmt.case", SignalGroup.Comments,
                lower >= 0.3 ? "Комментарии со строчной буквы, «разговорные»" : "Комментарии аккуратные: с заглавной буквы, грамотные",
                SignalBuilder.Map(lower, 0.5, 0.03), SignalBuilder.Rel(prose, 5), 0.3,
                $"{_a[K.LowercaseComments]:0} из {prose:0} комментариев начинаются со строчной", lower >= 0.3 ? _a.GetExamples(K.LowercaseComments) : [], lower);
        }

        if (_a[K.StepComments] >= 2)
        {
            b.Add("cmt.step", SignalGroup.Comments, "Нумерованные шаги в комментариях (// Step 1:, // 2. …)",
                1, SignalBuilder.Rel(_a[K.StepComments], 2), 0.6, $"{_a[K.StepComments]:0} комментариев-шагов", _a.GetExamples(K.StepComments), _a[K.StepComments]);
        }

        // Язык комментариев и сообщений: в проекте пишут по-русски, а новый код — на английском (или наоборот).
        double cyr = _a[K.CyrillicComments] + _a[K.ThrowMessageCyrillic];
        double lat = _a[K.LatinComments] + (_a[K.ThrowMessages] - _a[K.ThrowMessageCyrillic]);
        if (cyr + lat >= 3)
        {
            double share = cyr / (cyr + lat);
            double? baseShare = null;
            if (_base is not null)
            {
                double bc = _base[K.CyrillicComments] + _base[K.ThrowMessageCyrillic];
                double bl = _base[K.LatinComments] + (_base[K.ThrowMessages] - _base[K.ThrowMessageCyrillic]);
                if (bc + bl >= 5)
                {
                    baseShare = bc / (bc + bl);
                }
            }

            if (share >= 0.3)
            {
                // Грамотные русские комментарии с заглавной буквы Claude тоже пишет (по инструкции CLAUDE.md), небрежные — почти только человек.
                double polished = _a[K.CyrillicComments] > 0 ? _a[K.CyrillicPolishedComments] / _a[K.CyrillicComments] : 0;
                double strength = Math.Min(1, share * 1.3) * (1 - (0.7 * polished));
                b.Add("cmt.language", SignalGroup.Comments,
                    polished >= 0.7 ? "Комментарии на русском (аккуратные — так пишет и Claude по инструкции, слабый признак)" : "Небрежные комментарии на русском — так обычно пишет человек",
                    -strength, SignalBuilder.Rel(cyr, 3), 1.1,
                    $"{cyr:0} русскоязычных из {cyr + lat:0}" + (baseShare is { } bs ? $" (в проекте: {SignalBuilder.Pct(bs)})" : ""),
                    _a.GetExamples(K.CyrillicComments), share);
            }
            else if (baseShare is >= 0.4)
            {
                b.Add("cmt.language", SignalGroup.Comments, "В проекте комментарии на русском, а в МР — грамотный английский",
                    1, SignalBuilder.Rel(lat, 3), 1.2, $"в МР русских {SignalBuilder.Pct(share)}, в проекте {SignalBuilder.Pct(baseShare.Value)}", [], share);
            }
        }
    }

    // ------------------------------------------------------------------ именование

    private void Naming()
    {
        var names = _a.LocalNames.Concat(_a.ParameterNames).Where(n => n.Length > 0).ToList();
        if (names.Count >= 6)
        {
            double avg = names.Average(n => n.Length);
            double? baseAvg = _base is not null && _base.LocalNames.Count + _base.ParameterNames.Count >= 10
                ? _base.LocalNames.Concat(_base.ParameterNames).Average(n => n.Length)
                : null;
            double e = SignalBuilder.Map(avg, 5.0, 9.5);
            e = SignalBuilder.AdjustForBaseline(e, baseAvg is { } ba ? SignalBuilder.Map(ba, 5.0, 9.5) : null, 0.4);
            b.Add("name.length", SignalGroup.Naming,
                avg >= 7.5 ? "Длинные описательные имена переменных и параметров" : "Короткие имена переменных и параметров",
                e, SignalBuilder.Rel(names.Count, 15), 0.4,
                $"средняя длина {avg:0.0} символа по {names.Count} именам" + (baseAvg is { } ba2 ? $" (в проекте {ba2:0.0})" : ""), [], avg);
            b.TrackShift("name.length", "длина имён", avg, baseAvg, e, baseAvg is { } ba3 ? SignalBuilder.Map(ba3, 5.0, 9.5) : null, v => $"{v:0.0}");

            var shortNames = names.Where(n => n.Length <= 3 && !ShortNameExceptions.Contains(n)).ToList();
            double shortShare = (double)shortNames.Count / names.Count;
            b.Add("name.short", SignalGroup.Naming,
                shortShare >= 0.08 ? $"Короткие и сокращённые имена ({string.Join(", ", shortNames.Distinct().Take(6))})" : "Почти нет коротких имён (≤ 3 символов)",
                Math.Min(0.4, SignalBuilder.Map(shortShare, 0.15, 0.01)), SignalBuilder.Rel(names.Count, 15), 0.7,
                $"{shortNames.Count} из {names.Count} имён ≤ 3 символов", [], shortShare);

            var abbrev = names.Where(n => Abbreviations.Contains(n) && !ShortNameExceptions.Contains(n)).ToList();
            double abbrevShare = (double)abbrev.Count / names.Count;
            if (abbrev.Count > 0 || names.Count >= 12)
            {
                double ea = abbrev.Count == 0 ? 0.3 : -Math.Min(1, 0.2 + (abbrevShare * 8));
                b.Add("name.abbrev", SignalGroup.Naming,
                    abbrev.Count > 0 ? $"Сокращения в именах ({string.Join(", ", abbrev.Distinct().Take(6))})" : "Нет сокращений в именах",
                    ea, SignalBuilder.Rel(names.Count, 15), 0.7,
                    $"{abbrev.Count} из {names.Count} имён — сокращения", [], abbrevShare);
            }

            var locals = _a.LocalNames;
            if (locals.Count >= 5)
            {
                var generic = locals.Where(n => GenericNames.Contains(n) || GenericPrefixes.Any(p => n.StartsWith(p, StringComparison.Ordinal) && n.Length > p.Length)).ToList();
                double gShare = (double)generic.Count / locals.Count;
                b.Add("name.generic", SignalGroup.Naming,
                    gShare >= 0.12 ? $"Типовые «агентские» имена: {string.Join(", ", generic.Distinct().Take(6))}" : "Мало типовых имён (result, existing…, normalized…)",
                    SignalBuilder.Map(gShare, 0.03, 0.25), SignalBuilder.Rel(locals.Count, 10), 0.4,
                    $"{generic.Count} из {locals.Count} локальных переменных", [], gShare);
            }
        }

        double translit = _a[K.TranslitIdentifiers] + _a[K.CyrillicIdentifiers];
        if (translit >= 1)
        {
            b.Add("name.translit", SignalGroup.Naming, "Транслит или кириллица в идентификаторах (zakaz, polzovatel, kolvo…) — Claude так не пишет",
                -Math.Min(1, 0.5 + (0.25 * translit)), 1, 1.0, $"{translit:0} строк",
                _a.GetExamples(K.TranslitIdentifiers).Concat(_a.GetExamples(K.CyrillicIdentifiers)).Take(3).ToList(), translit);
        }

        double allNames = _a.LocalNames.Count + _a[K.PrivateFields];
        if (_a[K.HungarianNames] >= 2 && allNames >= 8)
        {
            double share = _a[K.HungarianNames] / allNames;
            b.Add("name.hungarian", SignalGroup.Naming, "Венгерская нотация в именах (strName, lstItems, m_field)",
                -Math.Min(1, share * 6), SignalBuilder.Rel(_a[K.HungarianNames], 2), 0.6, $"{_a[K.HungarianNames]:0} имён из {allNames:0}",
                _a.GetExamples(K.HungarianNames), share);
        }

        b.Rate("name.ct", SignalGroup.Naming,
            ai: "Токен отмены всегда назван полностью: cancellationToken", human: "Токен отмены назван коротко (ct, token)",
            hits: K.CtFullName, total: K.CtParams, humanAt: 0.3, aiAt: 1.0, k: 2, weight: 0.6, alpha: 0.4,
            detail: (h, t) => $"{h:0} из {t:0} параметров CancellationToken", exHuman: K.CtShortName);

        var lambdas = _a.LambdaParameterNames;
        if (lambdas.Count >= 5)
        {
            double xShare = lambdas.Count(n => n is "x" or "y" or "z" or "a" or "b" or "c" or "i" or "q" or "t" or "s") / (double)lambdas.Count;
            b.Add("name.lambda", SignalGroup.Naming,
                xShare >= 0.4 ? "Лямбды с безликими параметрами x => x…" : "Параметры лямбд осмысленные (order => order.Id / o => o.Id)",
                SignalBuilder.Map(xShare, 0.7, 0.1), SignalBuilder.Rel(lambdas.Count, 8), 0.45,
                $"{xShare * 100:0}% лямбд с параметрами x/y/a/b…", [], xShare);
        }
    }

    /// <summary>Ослабляет «человеческую» сторону признака: отсутствие ИИ-паттерна — слабое свидетельство.</summary>
    private static double Soften(double e, double humanFactor) => e < 0 ? e * humanFactor : e;

    private static bool IsWord(string n) => n is "name" or "user" or "item" or "data" or "code" or "type" or "list" or "path" or "file" or "line"
        or "text" or "date" or "time" or "key" or "map" or "set" or "sum" or "max" or "min" or "url" or "uri" or "row" or "tag" or "age" or "day"
        or "log" or "job" or "task" or "node" or "page" or "size" or "port" or "host" or "mode" or "kind" or "role" or "unit" or "rule" or "step"
        or "next" or "prev" or "last" or "head" or "tail" or "body" or "form" or "view" or "hash" or "salt" or "json" or "xml" or "html" or "sql"
        or "csv" or "bytes" or "token" or "count" or "index" or "order" or "price" or "total" or "query" or "items" or "field" or "parts" or "part"
        or "rate" or "flag" or "args" or "user" or "role" or "dto" or "id" or "ids" or "cost" or "city" or "zip" or "now" or "end" or "start" or "from"
        or "to" or "skip" or "take" or "seed" or "lock" or "sort" or "plan" or "slot" or "room" or "seat" or "team" or "game" or "card" or "deal";

    // ------------------------------------------------------------------ синтаксис и API

    private void Syntax()
    {
        b.Rate("syn.nullPattern", SignalGroup.ModernSyntax,
            ai: "Проверки на null через is null / is not null", human: "Проверки на null через == null / != null",
            hits: K.NullPatternChecks, total: null, totalKeys: [K.NullPatternChecks, K.NullEqualityChecks], humanAt: 0.15, aiAt: 0.95, k: 3, weight: 0.8, alpha: 0.6,
            detail: (h, t) => $"{h:0} из {t:0} проверок на null — через is", exAi: K.NullPatternChecks, exHuman: K.NullEqualityChecks);

        b.Rate("syn.fileScopedNs", SignalGroup.ModernSyntax,
            ai: "Новые файлы с file-scoped namespace", human: "Новые файлы с блочным namespace { }",
            hits: K.FileScopedNs, total: null, totalKeys: [K.FileScopedNs, K.BlockNs], humanAt: 0.2, aiAt: 1.0, k: 2, weight: 0.4, alpha: 0.7,
            detail: (h, t) => $"{h:0} из {t:0} новых файлов");

        double modern = _a[K.CollectionExpressions] + _a[K.ImplicitNew] + _a[K.SwitchExpressions] + _a[K.Records] + _a[K.RequiredMembers] +
                        _a[K.InitAccessors] + _a[K.PrimaryCtorClasses] + _a[K.RawStrings] + _a[K.PropertyPatterns];
        double lines = _a[K.CodeLines];
        if (lines >= 40)
        {
            double density = modern * 100 / lines;
            double? baseDensity = null;
            if (_base is not null && _base[K.CodeLines] >= 80)
            {
                double bm = _base[K.CollectionExpressions] + _base[K.ImplicitNew] + _base[K.SwitchExpressions] + _base[K.Records] + _base[K.RequiredMembers] +
                            _base[K.InitAccessors] + _base[K.PrimaryCtorClasses] + _base[K.RawStrings] + _base[K.PropertyPatterns];
                baseDensity = bm * 100 / _base[K.CodeLines];
            }

            double e = SignalBuilder.Map(density, 0.3, 3.5);
            double? eb = baseDensity is { } bd ? SignalBuilder.Map(bd, 0.3, 3.5) : null;
            var examples = _a.GetExamples(K.CollectionExpressions).Concat(_a.GetExamples(K.SwitchExpressions)).Concat(_a.GetExamples(K.RequiredMembers))
                .Concat(_a.GetExamples(K.PrimaryCtorClasses)).Take(3).ToList();
            b.Add("syn.modern", SignalGroup.ModernSyntax,
                density >= 1.9 ? "Плотно используются новейшие возможности C# ([], new(), switch-выражения, record, required, primary ctor)" : "Новые возможности C# почти не используются",
                SignalBuilder.AdjustForBaseline(e, eb, 0.6), SignalBuilder.Rel(lines, 60), 0.6,
                $"{modern:0} конструкций на {lines:0} строк ({density:0.0} на 100 строк)" + (baseDensity is { } bd2 ? $", в проекте {bd2:0.0}" : ""), examples, density);
            b.TrackShift("syn.modern", "новый синтаксис C#", density, baseDensity, e, eb, v => $"{v:0.0}/100 строк");
        }

        b.Rate("api.isNullOrWhiteSpace", SignalGroup.ModernSyntax,
            ai: "string.IsNullOrWhiteSpace вместо IsNullOrEmpty", human: "string.IsNullOrEmpty вместо IsNullOrWhiteSpace",
            hits: K.IsNullOrWhiteSpace, total: null, totalKeys: [K.IsNullOrWhiteSpace, K.IsNullOrEmpty], humanAt: 0.3, aiAt: 0.95, k: 2, weight: 0.35, alpha: 0.3,
            detail: (h, t) => $"{h:0} из {t:0} проверок строк", exHuman: K.IsNullOrEmpty);

        b.Rate("api.stringEmpty", SignalGroup.ModernSyntax,
            ai: "string.Empty вместо \"\"", human: "Пустые строки литералом \"\"",
            hits: K.StringEmpty, total: null, totalKeys: [K.StringEmpty, K.EmptyStringLiteral], humanAt: 0.2, aiAt: 0.9, k: 3, weight: 0.2, alpha: 0.5,
            detail: (h, t) => $"{h:0} из {t:0} пустых строк", exHuman: K.EmptyStringLiteral);

        b.Rate("prop.stringEmptyInit", SignalGroup.ModernSyntax,
            ai: "Строковые свойства инициализированы = string.Empty", human: "Строковые свойства без инициализации",
            hits: K.StringEmptyInitializers, total: K.StringAutoProps, humanAt: 0.05, aiAt: 0.7, k: 3, weight: 0.5, alpha: 0.5,
            detail: (h, t) => $"{h:0} из {t:0} строковых автосвойств", exAi: K.StringEmptyInitializers);

        b.Rate("api.readOnlyCollections", SignalGroup.ModernSyntax,
            ai: "IReadOnlyList/IReadOnlyCollection в сигнатурах", human: "List<>/массивы в сигнатурах",
            hits: K.ReadOnlyCollections, total: null, totalKeys: [K.ReadOnlyCollections, K.MutableCollectionsInSignatures], humanAt: 0.05, aiAt: 0.6, k: 3, weight: 0.55, alpha: 0.5,
            detail: (h, t) => $"{h:0} из {t:0} коллекций в сигнатурах — read-only");

        b.Rate("api.utcNow", SignalGroup.ModernSyntax,
            ai: "Время только в UTC (DateTime.UtcNow / DateTimeOffset)", human: "Используется DateTime.Now (локальное время)",
            hits: K.UtcNow, total: null, totalKeys: [K.UtcNow, K.LocalNow], humanAt: 0.3, aiAt: 1.0, k: 2, weight: 0.5, alpha: 0.3,
            detail: (h, t) => $"{h:0} из {t:0} обращений ко времени — UTC", exHuman: K.LocalNow);

        double cmpGood = _a[K.StringComparisonUse] + _a[K.ToLowerInvariant];
        double cmpBad = _a[K.ToLowerPlain];
        if (cmpGood + cmpBad >= 2)
        {
            double r = cmpGood / (cmpGood + cmpBad);
            b.Add("api.stringComparison", SignalGroup.ModernSyntax,
                r >= 0.6 ? "Явный StringComparison / ToLowerInvariant" : "ToLower()/ToUpper() без культуры",
                SignalBuilder.Map(r, 0.3, 0.95), SignalBuilder.Rel(cmpGood + cmpBad, 2), 0.4,
                $"{cmpGood:0} корректных сравнений строк из {cmpGood + cmpBad:0}", r < 0.6 ? _a.GetExamples(K.ToLowerPlain) : [], r);
        }

        if (lines >= 60)
        {
            double nameofDensity = _a[K.Nameof] * 100 / lines;
            if (_a[K.Nameof] > 0 || lines >= 150)
            {
                b.Add("api.nameof", SignalGroup.ModernSyntax,
                    nameofDensity >= 0.8 ? "Частое использование nameof(...)" : "nameof почти не используется",
                    SignalBuilder.Map(nameofDensity, 0.05, 1.5), SignalBuilder.Rel(lines, 100), 0.5,
                    $"{_a[K.Nameof]:0} раз на {lines:0} строк", [], nameofDensity);
            }
        }
    }

    // ------------------------------------------------------------------ защитный код

    private void Defensive()
    {
        b.Rate("guard.rate", SignalGroup.Defensive,
            ai: "Guard-проверки аргументов в начале публичных методов", human: "Публичные методы без проверок аргументов",
            hits: K.GuardedMembers, total: K.GuardableMembers, humanAt: 0.05, aiAt: 0.55, k: 4, weight: 0.8, alpha: 0.4,
            detail: (h, t) => $"{h:0} из {t:0} публичных методов/конструкторов с параметрами-ссылками", exAi: K.GuardThrowIfHelpers);

        if (_a[K.GuardThrowIfHelpers] >= 1)
        {
            b.Add("guard.throwIf", SignalGroup.Defensive, "Современные guard-хелперы: ArgumentNullException.ThrowIfNull / ThrowIfNullOrWhiteSpace",
                1, SignalBuilder.Rel(_a[K.GuardThrowIfHelpers], 2), 0.5, $"{_a[K.GuardThrowIfHelpers]:0} вызовов",
                _a.GetExamples(K.GuardThrowIfHelpers), _a[K.GuardThrowIfHelpers]);
        }
    }

    // ------------------------------------------------------------------ async

    private void Async()
    {
        b.Rate("async.ct", SignalGroup.Async,
            ai: "CancellationToken принимается почти всеми async-методами", human: "Async-методы без CancellationToken",
            hits: K.AsyncWithCt, total: K.AsyncMethods, humanAt: 0.1, aiAt: 0.85, k: 4, weight: 1.4, alpha: 0.25,
            detail: (h, t) => $"{h:0} из {t:0} async-методов принимают CancellationToken", exHuman: K.AsyncWithCt + ".missing");

        b.Rate("async.ctPropagation", SignalGroup.Async,
            ai: "Токен отмены передаётся дальше по всей цепочке вызовов", human: "Токен отмены принимается, но не передаётся дальше",
            hits: K.AwaitsPassingCt, total: K.AwaitsInCtMethods, humanAt: 0.35, aiAt: 0.9, k: 4, weight: 0.8, alpha: 0.2,
            detail: (h, t) => $"{h:0} из {t:0} await-вызовов получают токен");

        b.Rate("async.ctDefault", SignalGroup.Async,
            ai: "CancellationToken cancellationToken = default", human: "Токен отмены без значения по умолчанию",
            hits: K.CtDefault, total: K.CtParams, humanAt: 0.05, aiAt: 0.7, k: 3, weight: 0.4, alpha: 0.4,
            detail: (h, t) => $"{h:0} из {t:0} параметров-токенов с = default");

        b.Rate("async.suffix", SignalGroup.Async,
            ai: "Суффикс Async у всех асинхронных методов", human: "Асинхронные методы без суффикса Async",
            hits: K.AsyncSuffix, total: K.AsyncMethods, humanAt: 0.4, aiAt: 0.97, k: 4, weight: 0.6, alpha: 0.4,
            detail: (h, t) => $"{h:0} из {t:0} async-методов (кроме действий контроллеров)", exHuman: K.AsyncSuffix + ".missing");

        double bad = _a[K.SyncOverAsync] + _a[K.AsyncVoid];
        if (bad > 0)
        {
            b.Add("async.antipatterns", SignalGroup.Async, "Анти-паттерны async: .Result / .Wait() / async void",
                -Math.Min(1, 0.4 + 0.3 * bad), 1, 1.0, $"{bad:0} мест", _a.GetExamples(K.SyncOverAsync).Concat(_a.GetExamples(K.AsyncVoid)).ToList(), bad);
        }

        if (_a[K.ConfigureAwaitFalse] >= 2)
        {
            b.Add("async.configureAwait", SignalGroup.Async, "ConfigureAwait(false) на await-вызовах",
                0.7, SignalBuilder.Rel(_a[K.ConfigureAwaitFalse], 3), 0.3, $"{_a[K.ConfigureAwaitFalse]:0} раз", [], _a[K.ConfigureAwaitFalse]);
        }
    }

    // ------------------------------------------------------------------ логирование

    private void Logging()
    {
        b.Rate("log.templated", SignalGroup.Logging,
            ai: "Структурное логирование: шаблоны {OrderId} вместо интерполяции", human: "Логирование через интерполяцию/конкатенацию строк",
            hits: K.LogTemplated, total: null, totalKeys: [K.LogTemplated, K.LogInterpolated], humanAt: 0.3, aiAt: 1.0, k: 3, weight: 0.9, alpha: 0.3,
            detail: (h, t) => $"{h:0} из {t:0} вызовов логгера с параметрами — через шаблон", exAi: K.LogTemplated, exHuman: K.LogInterpolated);

        double lines = _a[K.CodeLines];
        if (lines >= 80 && _a[K.Methods] >= 4)
        {
            double perMethod = _a[K.LogCalls] / Math.Max(1, _a[K.Methods] - _a[K.TestMethods]);
            if (_a[K.LogCalls] >= 3)
            {
                b.Add("log.density", SignalGroup.Logging,
                    perMethod >= 0.5 ? "Подробное логирование почти в каждом методе" : "Логирование точечное",
                    SignalBuilder.Map(perMethod, 0.1, 0.9), SignalBuilder.Rel(_a[K.LogCalls], 5), 0.4,
                    $"{_a[K.LogCalls]:0} вызовов логгера на {_a[K.Methods] - _a[K.TestMethods]:0} методов", _a.GetExamples(K.LogTemplated), perMethod);
            }
        }
    }

    // ------------------------------------------------------------------ исключения

    private void Exceptions()
    {
        b.Rate("exc.sentence", SignalGroup.Exceptions,
            ai: "Сообщения исключений — полные предложения с точкой", human: "Сообщения исключений короткие, без точки",
            hits: K.ThrowMessageSentence, total: K.ThrowMessages, humanAt: 0.1, aiAt: 0.7, k: 3, weight: 0.6, alpha: 0.4,
            detail: (h, t) => $"{h:0} из {t:0} сообщений", exAi: K.ThrowMessageSentence);

        b.Rate("exc.lowercase", SignalGroup.Exceptions,
            ai: "Сообщения исключений с заглавной буквы", human: "Сообщения исключений со строчной буквы",
            hits: K.ThrowMessageLowercase, total: K.ThrowMessages, humanAt: 0.5, aiAt: 0.0, k: 3, weight: 0.4, alpha: 0.4,
            detail: (h, t) => $"{h:0} из {t:0} сообщений со строчной", exHuman: K.ThrowMessageLowercase);

        if (_a[K.ThrowMessageQuoted] >= 2)
        {
            b.Add("exc.quoted", SignalGroup.Exceptions, "Значения в сообщениях взяты в кавычки: '{orderId}'",
                1, SignalBuilder.Rel(_a[K.ThrowMessageQuoted], 2), 0.2, $"{_a[K.ThrowMessageQuoted]:0} сообщений",
                _a.GetExamples(K.ThrowMessageQuoted), _a[K.ThrowMessageQuoted]);
        }

        double catches = _a[K.CatchVarE] + _a[K.CatchVarEx];
        if (catches >= 2)
        {
            double eShare = _a[K.CatchVarE] / catches;
            b.Add("exc.catchName", SignalGroup.Exceptions,
                eShare >= 0.35 ? "catch (Exception e) — короткое имя исключения" : "catch (Exception ex) — стандартное имя",
                SignalBuilder.Map(eShare, 0.7, 0.0), SignalBuilder.Rel(catches, 3), 0.4, $"{_a[K.CatchVarE]:0} из {catches:0} catch с именем e",
                _a.GetExamples(K.CatchVarE), eShare);
        }

        double thrown = _a[K.GenericExceptionThrows] + _a[K.SpecificExceptionThrows];
        if (thrown >= 2)
        {
            double genericShare = _a[K.GenericExceptionThrows] / thrown;
            b.Add("exc.generic", SignalGroup.Exceptions,
                genericShare >= 0.2 ? "Бросается общий new Exception(...) вместо конкретного типа" : "Только конкретные типы исключений (InvalidOperationException, KeyNotFoundException…)",
                SignalBuilder.Map(genericShare, 0.4, 0.0), SignalBuilder.Rel(thrown, 3), 0.6,
                $"{_a[K.GenericExceptionThrows]:0} из {thrown:0} throw new — общий Exception", _a.GetExamples(K.GenericExceptionThrows), genericShare);
        }

        double bad = _a[K.ThrowEx] + _a[K.EmptyCatch];
        if (bad > 0)
        {
            b.Add("exc.antipatterns", SignalGroup.Exceptions, "Ручные анти-паттерны: throw ex; / пустой catch { }",
                -Math.Min(1, 0.5 + 0.25 * bad), 1, 0.8, $"{bad:0} мест", _a.GetExamples(K.ThrowEx).Concat(_a.GetExamples(K.EmptyCatch)).ToList(), bad);
        }
    }

    // ------------------------------------------------------------------ структура

    private void Structure()
    {
        b.Rate("cls.sealed", SignalGroup.Structure,
            ai: "Классы помечены sealed", human: "Классы не запечатаны (нет sealed)",
            hits: K.SealedClasses, total: K.ConcreteClasses, humanAt: 0.05, aiAt: 0.6, k: 3, weight: 0.7, alpha: 0.6,
            detail: (h, t) => $"{h:0} из {t:0} новых конкретных классов", exAi: K.SealedClasses);

        b.Rate("fld.readonly", SignalGroup.Structure,
            ai: "Все приватные поля readonly", human: "Изменяемые приватные поля (без readonly)",
            hits: K.ReadonlyFields, total: K.PrivateFields, humanAt: 0.5, aiAt: 1.0, k: 3, weight: 0.5, alpha: 0.5,
            detail: (h, t) => $"{h:0} из {t:0} приватных полей readonly");

        b.Rate("ord.members", SignalGroup.Structure,
            ai: "Строгий порядок членов: поля → конструктор → свойства → методы", human: "Члены классов перемешаны",
            hits: K.TypesOrdered, total: K.TypesOrderChecked, humanAt: 0.4, aiAt: 1.0, k: 3, weight: 0.4, alpha: 0.4,
            detail: (h, t) => $"{h:0} из {t:0} классов упорядочены");

        var lengths = _a.MethodLengths;
        if (lengths.Count >= 5)
        {
            double longShare = lengths.Count(l => l > 50) / (double)lengths.Count;
            int max = lengths.Max();
            b.Add("mth.long", SignalGroup.Structure,
                longShare >= 0.1 ? $"Есть длинные методы (до {max} строк)" : "Все методы компактные",
                SignalBuilder.Map(longShare, 0.15, 0.0), SignalBuilder.Rel(lengths.Count, 6), 0.3,
                $"{lengths.Count(l => l > 50)} из {lengths.Count} методов длиннее 50 строк, максимум {max}", [], longShare);

            double avgCx = _a.MethodComplexities.Count > 0 ? _a.MethodComplexities.Average() : 0;
            int maxCx = _a.MethodComplexities.Count > 0 ? _a.MethodComplexities.Max() : 0;
            if (maxCx >= 12)
            {
                b.Add("mth.complexity", SignalGroup.Structure, $"Очень запутанный метод (цикломатическая сложность {maxCx})",
                    -Math.Min(1, (maxCx - 10) / 10.0), 1, 0.4, $"средняя сложность {avgCx:0.0}, максимум {maxCx}", [], maxCx);
            }
        }

        b.Rate("acc.explicit", SignalGroup.Structure,
            ai: "Явный модификатор доступа у каждого члена класса (private/public везде)", human: "Члены классов без явного модификатора доступа",
            hits: K.ExplicitAccess, total: K.MembersChecked, humanAt: 0.75, aiAt: 1.0, k: 6, weight: 0.4, alpha: 0.6,
            detail: (h, t) => $"{h:0} из {t:0} членов с явным модификатором", exHuman: K.ExplicitAccess + ".missing");

        if (_a.MethodLengths.Count >= 5)
        {
            double deepShare = _a[K.DeepNestedMethods] / _a.MethodLengths.Count;
            b.Add("mth.nesting", SignalGroup.Structure,
                deepShare >= 0.05 ? "Глубокая вложенность if/for/try (4+ уровня)" : "Плоский код: ранние возвраты вместо вложенности",
                SignalBuilder.Map(deepShare, 0.15, 0.0), SignalBuilder.Rel(_a.MethodLengths.Count, 6), 0.5,
                $"{_a[K.DeepNestedMethods]:0} из {_a.MethodLengths.Count} методов с вложенностью ≥ 4", _a.GetExamples(K.DeepNestedMethods), deepShare);
        }

        if (_a[K.PublicFields] >= 1)
        {
            b.Add("fld.public", SignalGroup.Structure, "Публичные изменяемые поля вместо свойств",
                -Math.Min(1, 0.5 + 0.25 * _a[K.PublicFields]), 1, 0.4, $"{_a[K.PublicFields]:0} полей", _a.GetExamples(K.PublicFields), _a[K.PublicFields]);
        }
    }

    // ------------------------------------------------------------------ характерные для агента идиомы

    private void AgentIdioms()
    {
        double lines = _a[K.CodeLines];
        if (_a[K.EmojiInStrings] >= 1)
        {
            b.Add("idiom.emoji", SignalGroup.Structure, "Эмодзи в строках кода (✅, 🚀 в выводе/логах)",
                1, SignalBuilder.Rel(_a[K.EmojiInStrings], 1), 0.6, $"{_a[K.EmojiInStrings]:0} строк", _a.GetExamples(K.EmojiInStrings), _a[K.EmojiInStrings]);
        }

        if (_a[K.OptionsSectionName] >= 1 && (_base is null || _base[K.OptionsSectionName] == 0))
        {
            b.Add("idiom.sectionName", SignalGroup.Structure, "Options-класс с константой SectionName (типовой шаблон агента)",
                1, 1, 0.4, $"{_a[K.OptionsSectionName]:0} раз", _a.GetExamples(K.OptionsSectionName), _a[K.OptionsSectionName]);
        }

        if (_a[K.ControllerActions] >= 2)
        {
            double perAction = _a[K.ProducesResponseType] / _a[K.ControllerActions];
            double? basePer = _base is not null && _base[K.ControllerActions] >= 3 ? _base[K.ProducesResponseType] / _base[K.ControllerActions] : null;
            double e = SignalBuilder.Map(perAction, 0.2, 2.0);
            b.Add("idiom.producesResponseType", SignalGroup.Structure,
                e >= 0 ? "Каждое действие контроллера размечено [ProducesResponseType]" : "Действия контроллеров без [ProducesResponseType]",
                SignalBuilder.AdjustForBaseline(e, basePer is { } bp ? SignalBuilder.Map(bp, 0.2, 2.0) : null, 0.7), SignalBuilder.Rel(_a[K.ControllerActions], 3), 0.3,
                $"{_a[K.ProducesResponseType]:0} атрибутов на {_a[K.ControllerActions]:0} действий" + (basePer is { } bp2 ? $" (в проекте {bp2:0.0} на действие)" : ""),
                _a.GetExamples(K.ProducesResponseType), perAction);
        }

        if (_a[K.AsNoTracking] >= 1 && (_base is null || _base[K.AsNoTracking] == 0))
        {
            b.Add("idiom.asNoTracking", SignalGroup.Structure, "AsNoTracking() в запросах на чтение (в остальном коде проекта не используется)",
                0.8, SignalBuilder.Rel(_a[K.AsNoTracking], 1), 0.3, $"{_a[K.AsNoTracking]:0} раз", [], _a[K.AsNoTracking]);
        }

        if (lines >= 80)
        {
            double magic = _a[K.MagicNumbers] * 100 / lines;
            double consts = _a[K.ConstDeclarations] * 100 / lines;
            if (_a[K.MagicNumbers] + _a[K.ConstDeclarations] >= 3)
            {
                double e = SignalBuilder.Map(magic - consts, 2.0, -0.5);
                b.Add("idiom.constants", SignalGroup.Structure,
                    e >= 0 ? "Числа вынесены в именованные константы" : "«Магические числа» прямо в коде",
                    e, SignalBuilder.Rel(lines, 100), 0.1, $"{_a[K.MagicNumbers]:0} литералов в выражениях, {_a[K.ConstDeclarations]:0} констант", [], magic - consts);
            }
        }
    }

    // ------------------------------------------------------------------ ручные привычки

    private void HumanHabits()
    {
        var parts = new (string Key, string Title)[]
        {
            (K.TodoComments, "TODO/FIXME/HACK"),
            (K.TicketRefs, "ссылки на задачи"),
            (K.CommentedOutCode, "закомментированный код"),
            (K.Regions, "#region"),
            (K.ConsoleWrites, "Console.WriteLine"),
            (K.StringFormat, "string.Format"),
            (K.StringConcat, "склейка строк через +"),
            (K.LinqQuerySyntax, "LINQ query-синтаксис"),
            (K.ThreadSleep, "Thread.Sleep"),
            (K.Typos, "опечатки"),
        };
        double lines = _a[K.CodeLines];
        if (lines < 40)
        {
            return;
        }

        double total = parts.Sum(p => _a[p.Key]);
        double per100 = total * 100 / lines;
        var found = parts.Where(p => _a[p.Key] > 0).Select(p => $"{p.Title}: {_a[p.Key]:0}").ToList();
        double e = total == 0 ? 0.35 : SignalBuilder.Map(per100, 1.2, 0.0) * 1.0;
        if (total > 0)
        {
            e = Math.Min(e, -0.3);
        }

        var examples = parts.SelectMany(p => _a.GetExamples(p.Key).Take(2)).Take(4).ToList();
        b.Add("habits.markers", SignalGroup.HumanHabits,
            total == 0 ? "Нет «ручных» следов: TODO, закомментированного кода, #region, Console.WriteLine, string.Format" : "«Ручные» следы в коде",
            e, SignalBuilder.Rel(lines, 80), 1.4,
            total == 0 ? $"0 на {lines:0} строк кода" : string.Join("; ", found), examples, per100);
    }

    // ------------------------------------------------------------------ форматирование

    private void Formatting()
    {
        double lines = _a[K.CodeLines];
        if (lines < 40)
        {
            return;
        }

        string[] keys = [K.TrailingWhitespace, K.KeywordNoSpace, K.BraceSameLineNoSpace, K.DoubleBlankLines, K.CommaNoSpace, K.OperatorNoSpace, K.LongLines, K.CommentNoSpace];
        double total = keys.Sum(k => _a[k]);
        double per100 = total * 100 / lines;
        double? basePer100 = null;
        if (_base is not null && _base[K.CodeLines] >= 100)
        {
            basePer100 = keys.Sum(k => _base[k]) * 100 / _base[K.CodeLines];
        }

        static double Ev(double total, double per100) => total == 0 ? 0.4 : -Math.Min(1, 0.15 + (per100 / 2));
        double e = Ev(total, per100);
        double? eb = basePer100 is { } bp ? Ev(bp, bp) : null;
        var examples = keys.SelectMany(k => _a.GetExamples(k).Take(1)).Take(4).ToList();
        b.Add("fmt.irregular", SignalGroup.Formatting,
            total == 0 ? "Идеальное форматирование: ни одного пробела в конце строки, if(…) без пробела, двойной пустой строки" : "Небрежности форматирования (пробелы в конце строк, if(…), x=y, двойные пустые строки)",
            SignalBuilder.AdjustForBaseline(e, eb, 0.5), SignalBuilder.Rel(lines, 80), 1.0,
            $"{total:0} небрежностей на {lines:0} строк" + (basePer100 is { } b2 ? $" (в проекте {b2:0.0} на 100 строк)" : ""), examples, per100);
        b.TrackShift("fmt.irregular", "аккуратность форматирования", per100, basePer100, e, eb, v => $"{v:0.0}/100 строк");

        b.Rate("fmt.blankBeforeReturn", SignalGroup.Formatting,
            ai: "Пустая строка перед return почти везде (ровная «вёрстка» блоков)", human: "return без отбивки пустой строкой",
            hits: K.ReturnsWithBlankLine, total: K.ReturnsChecked, humanAt: 0.3, aiAt: 0.9, k: 5, weight: 0.15, alpha: 0.5,
            detail: (h, t) => $"{h:0} из {t:0} return после пустой строки");

        if (lines >= 80)
        {
            double named = _a[K.NamedArguments] * 100 / lines;
            double? baseNamed = _base is not null && _base[K.CodeLines] >= 100 ? _base[K.NamedArguments] * 100 / _base[K.CodeLines] : null;
            if (_a[K.NamedArguments] >= 2)
            {
                double en = SignalBuilder.Map(named, 0.1, 1.5);
                b.Add("syn.namedArgs", SignalGroup.ModernSyntax, "Именованные аргументы для «непонятных» литералов (pageSize: 10, leaveOpen: true)",
                    SignalBuilder.AdjustForBaseline(Math.Max(0, en), baseNamed is { } bn ? SignalBuilder.Map(bn, 0.1, 1.5) : null, 0.5),
                    SignalBuilder.Rel(_a[K.NamedArguments], 3), 0.1, $"{_a[K.NamedArguments]:0} на {lines:0} строк", [], named);
            }
        }

        double kr = _a[K.KAndRBraces] * 100 / lines;
        if (_a[K.KAndRBraces] >= 2)
        {
            double? baseKr = _base is not null && _base[K.CodeLines] >= 100 ? _base[K.KAndRBraces] * 100 / _base[K.CodeLines] : null;
            double ek = SignalBuilder.AdjustForBaseline(-Math.Min(1, kr / 2), baseKr is { } bk ? -Math.Min(1, bk / 2) : null, 0.8);
            b.Add("fmt.kAndR", SignalGroup.Formatting, "Открывающая скобка на той же строке (K&R/Java-стиль) — нетипично для Claude в C#",
                ek, SignalBuilder.Rel(_a[K.KAndRBraces], 2), 0.6, $"{_a[K.KAndRBraces]:0} раз", _a.GetExamples(K.KAndRBraces), kr);
        }

        if (_a[K.TabIndent] >= 5 && _a[K.TabIndent] / lines > 0.3)
        {
            double? baseTabs = _base is not null && _base[K.CodeLines] >= 100 ? _base[K.TabIndent] / _base[K.CodeLines] : null;
            if (baseTabs is null or < 0.3)
            {
                b.Add("fmt.tabs", SignalGroup.Formatting, "Отступы табуляцией (Claude пишет пробелами)",
                    -0.7, 1, 0.4, $"{_a[K.TabIndent]:0} строк с табами", [], _a[K.TabIndent] / lines);
            }
        }
    }

    // ------------------------------------------------------------------ тесты

    private void Tests()
    {
        b.Rate("test.aaa", SignalGroup.Tests,
            ai: "Тесты размечены // Arrange / // Act / // Assert", human: "Тесты без разметки Arrange/Act/Assert",
            hits: K.TestAaa, total: K.TestMethods, humanAt: 0.1, aiAt: 0.8, k: 3, weight: 1.0, alpha: 0.4,
            detail: (h, t) => $"{h:0} из {t:0} тестов", exAi: K.TestAaa);

        b.Rate("test.naming", SignalGroup.Tests,
            ai: "Имена тестов по шаблону Method_Scenario_ExpectedResult", human: "Имена тестов без единого шаблона",
            hits: K.TestNamePattern, total: K.TestMethods, humanAt: 0.15, aiAt: 0.9, k: 3, weight: 0.4, alpha: 0.5,
            detail: (h, t) => $"{h:0} из {t:0} тестов", exAi: K.TestNamePattern);

        b.Rate("test.theory", SignalGroup.Tests,
            ai: "Параметризованные тесты [Theory]/[InlineData] на граничные случаи", human: "Нет параметризованных тестов",
            hits: K.TestTheory, total: K.TestMethods, humanAt: 0.03, aiAt: 0.3, k: 4, weight: 0.35, alpha: 0.3,
            detail: (h, t) => $"{h:0} из {t:0} тестов, {_a[K.TestInlineData]:0} наборов данных");

        if (_a[K.TestExampleData] >= 1)
        {
            b.Add("test.exampleData", SignalGroup.Tests, "Типовые тестовые данные (test@example.com, John Doe)",
                1, SignalBuilder.Rel(_a[K.TestExampleData], 2), 0.4, $"{_a[K.TestExampleData]:0} раз", _a.GetExamples(K.TestExampleData), _a[K.TestExampleData]);
        }

        if (_a[K.TestNameLower] >= 1 && _a[K.TestMethods] >= 1)
        {
            b.Add("test.lowerNames", SignalGroup.Tests, "Тесты с именами со строчной буквы (test1, checkSomething)",
                -0.8, SignalBuilder.Rel(_a[K.TestNameLower], 1), 0.5, $"{_a[K.TestNameLower]:0} тестов", [], _a[K.TestNameLower]);
        }
    }

    // ------------------------------------------------------------------ сдвиг относительно проекта

    private void ProjectShift()
    {
        var items = b.ShiftItems;
        if (items.Count < 3)
        {
            return;
        }

        double mean = items.Average(i => i.Delta);
        var top = items.OrderByDescending(i => Math.Abs(i.Delta)).Take(4)
            .Select(i => $"{i.Title}: МР {i.Format(i.MrValue)} / проект {i.Format(i.BaseValue)}").ToList();
        double e = Math.Clamp(mean * 1.2, -1, 1);
        b.Add("shift.style", SignalGroup.ProjectShift,
            e >= 0 ? "Код МР заметно «аккуратнее» и современнее существующего кода проекта" : "Код МР похож на существующий код проекта или проще него",
            e, SignalBuilder.Rel(items.Count, 4) * SignalBuilder.Rel(ctx.BaselineFiles, 3), 1.2,
            string.Join("; ", top), [], mean);
    }
}
