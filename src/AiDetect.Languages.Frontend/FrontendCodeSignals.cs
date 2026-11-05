using System.Globalization;
using AiDetect.Core.Analysis;
using AiDetect.Core.Features;
using AiDetect.Core.Signals;

namespace AiDetect.Languages.Frontend;

/// <summary>
/// Сигналы по фронтенд-коду: доли и плотности признаков → свидетельство e ∈ [−1, 1] между «типично для человека» и «типично для Claude»,
/// с поправкой на код проекта (базовая линия) и его конвенции. Экспертные веса — по исследованию docs/frontend/RESEARCH.md; калибровка уточняет.
/// </summary>
internal sealed class FrontendCodeSignals(AnalysisContext ctx, SignalBuilder b)
{
    private readonly CodeStats _a = ctx.Added;
    private readonly CodeStats? _base = ctx.Baseline;

    public void Evaluate()
    {
        Documentation();
        Comments();
        Naming();
        Typing();
        Syntax();
        Errors();
        Async();
        Logging();
        Framework();
        Accessibility();
        Styles();
        HumanHabits();
        Formatting();
        Tests();
        Structure();
        ProjectShift();
    }

    // ------------------------------------------------------------------ утилиты

    private static Func<CodeStats, double> S(params string[] keys) => s => keys.Sum(k => s[k]);

    private static string Per100(double h, double t) => $"{h:0} на {t:0} строк ({(t > 0 ? h * 100 / t : 0).ToString("0.0", CultureInfo.InvariantCulture)} на 100)";

    private static Func<double, double, string> OfTotal(string what) => (h, t) => $"{h:0} из {t:0} {what}";

    private IReadOnlyList<CodeExample> Ex(params string[] keys) => keys.SelectMany(k => _a.GetExamples(k)).Take(4).ToList();

    private void R(string id, SignalGroup g, string ai, string human, Func<CodeStats, double> hits, Func<CodeStats, double> total, double humanAt, double aiAt,
        double k, double w, double alpha, Func<double, double, string> detail, string[]? exAi = null, string[]? exHuman = null, double minTotal = 1) =>
        b.RateBy(id, g, ai, human, hits, total, humanAt, aiAt, k, w, alpha, detail, exAi is null ? null : Ex(exAi), exHuman is null ? null : Ex(exHuman), minTotal);

    private void Flag(string id, SignalGroup g, string title, string key, double e, double w, string what)
    {
        double n = _a[key];
        if (n > 0)
        {
            b.Add(id, g, title, e, SignalBuilder.Rel(n, 0.5), w, $"{what}: {n:0}", _a.GetExamples(key), n);
        }
    }

    // ------------------------------------------------------------------ документация

    private void Documentation()
    {
        R("doc.coverage", SignalGroup.Documentation, "JSDoc почти на каждом экспортируемом объявлении и публичном методе",
            "Экспортируемые функции и методы почти без JSDoc", S(FK.DocDocumented), S(FK.DocTargets), 0.05, 0.7, 5, 0.8, 0.7,
            OfTotal("экспортируемых объявлений/публичных методов с JSDoc"), [FK.DocDocumented]);

        R("doc.template", SignalGroup.Documentation, "JSDoc по шаблону: «Returns…», «Fetches…», «Custom hook that…»",
            "JSDoc написан своими словами, не по шаблону", S(FK.JsDocTemplate), S(FK.JsDocBlocks), 0.1, 0.7, 3, 0.6, 0.5,
            OfTotal("JSDoc-блоков начинаются с шаблонной фразы"), [FK.JsDocTemplate]);

        R("doc.propsMembers", SignalGroup.Documentation, "Каждое поле интерфейса пропсов/опций документировано /** … */",
            "Поля интерфейсов пропсов без документации", S(FK.PropsMembersDocumented), S(FK.PropsMembers), 0.0, 0.6, 5, 0.6, 0.6,
            OfTotal("полей интерфейсов …Props/…Options с JSDoc"), [FK.PropsMembersDocumented]);

        R("doc.typedParams", SignalGroup.Documentation, "JSDoc без типов в фигурных скобках (типы — в TypeScript)",
            "В TS-коде JSDoc с типами {string} — привычка из JavaScript", S(FK.JsDocTypedParams), S(FK.JsDocBlocks), 0.5, 0.0, 3, 0.3, 0.6,
            OfTotal("JSDoc-блоков с @param {Тип}"), null, [FK.JsDocTypedParams], minTotal: 2);

        Flag("doc.fileHeader", SignalGroup.Documentation, "Файл начинается с комментария-пути или описания файла («// src/hooks/useAuth.ts»)",
            FK.FileHeader, 1, 0.6, "файлов с такой шапкой");
    }

    // ------------------------------------------------------------------ комментарии

    private void Comments()
    {
        R("cmt.narrative", SignalGroup.Comments, "Комментарии-повествования: «// Fetch users», «// Handle form submission», «// Получаем…»",
            "Комментарии не пересказывают шаги кода", S(FK.Narrative), S(FK.LineComments), 0.05, 0.45, 4, 1.0, 0.5,
            OfTotal("комментариев — «глагол + что делаем»"), [FK.Narrative]);

        R("cmt.restating", SignalGroup.Comments, "Комментарии пересказывают следующую строку кода",
            "Комментарии добавляют информацию, а не пересказывают код", S(FK.Restating), S(FK.LineComments), 0.02, 0.25, 4, 0.6, 0.4,
            OfTotal("комментариев повторяют имена из следующей строки"), [FK.Restating]);

        R("cmt.jsxSection", SignalGroup.Comments, "Комментарии-заголовки секций разметки: {/* Header */}, <!-- Loading state -->",
            "Разметка без комментариев-заголовков", S(FK.JsxSection, FK.MarkupSection), S(FK.Elements), 0.0, 0.08, 20, 0.9, 0.5,
            OfTotal("элементов разметки с комментарием-заголовком секции"), [FK.JsxSection, FK.MarkupSection], minTotal: 8);

        R("cmt.cssSection", SignalGroup.Comments, "Комментарии-заголовки в стилях: /* Header styles */, /* Responsive */",
            "Стили без комментариев-заголовков", S(FK.CssSection), S(FK.CssRules), 0.0, 0.15, 10, 0.5, 0.5,
            OfTotal("CSS-правил с комментарием-заголовком"), [FK.CssSection], minTotal: 5);

        R("cmt.casual", SignalGroup.Comments, "Комментарии оформлены аккуратно (с заглавной, с пробелом после //)",
            "Небрежные комментарии: со строчной, «//без пробела», «???», сленг", S(FK.Casual), S(FK.LineComments), 0.4, 0.0, 4, 0.9, 0.3,
            OfTotal("комментариев написаны небрежно"), null, [FK.Casual], minTotal: 2);

        Flag("cmt.changeNote", SignalGroup.Comments, "Комментарии-отчёты о правке: «// Updated to use…», «// Changed from X to Y»",
            FK.ChangeNote, 0.8, 0.5, "комментариев-отчётов");

        Flag("cmt.emoji", SignalGroup.Comments, "Эмодзи в комментариях кода (✅ ❌ 🚀)", FK.EmojiInCode, 1, 0.6, "комментариев с эмодзи");

        // Язык комментариев: небрежный русский — человек; английские комментарии в русскоязычном проекте — ИИ.
        double cyr = _a[FK.CyrillicComments];
        double total = _a[FK.LineComments];
        if (cyr >= 2)
        {
            double polished = _a[FK.CyrillicPolished] / cyr;
            double e = polished >= 0.9 ? 0.25 : polished < 0.5 ? -0.6 : -0.1;
            b.Add("cmt.language", SignalGroup.Comments,
                e > 0 ? "Русские комментарии безупречно оформлены" : "Русские комментарии в «живом» стиле",
                e, SignalBuilder.Rel(cyr, 3), 0.6, $"{cyr:0} русских комментариев, аккуратных {polished * 100:0}%", _a.GetExamples(FK.CyrillicComments), polished);
        }
        else if (_base is not null && _base[FK.LineComments] >= 5 && _base[FK.CyrillicComments] >= 0.5 * _base[FK.LineComments] && _a[FK.LatinComments] >= 3)
        {
            b.Add("cmt.language", SignalGroup.Comments, "Английские комментарии в проекте, где комментируют по-русски",
                0.8, SignalBuilder.Rel(_a[FK.LatinComments], 3), 0.6,
                $"в MR {_a[FK.LatinComments]:0} английских комментариев, в коде проекта {(_base[FK.CyrillicComments] * 100 / _base[FK.LineComments]):0}% русских", [], 1);
        }
        else if (total == 0 && _a[FK.ScriptLines] >= 150)
        {
            // Claude Code по умолчанию почти не пишет комментариев — это слабый нейтральный факт, не сигнал.
        }
    }

    // ------------------------------------------------------------------ именование

    private void Naming()
    {
        R("name.catchParam", SignalGroup.Naming, "Ошибки называются полностью: catch (error), .catch((error) =>",
            "Короткие имена ошибок: catch (e), catch (err)", S(FK.CatchParamError), S(FK.CatchParams), 0.1, 0.9, 3, 0.7, 0.6,
            OfTotal("перехватов с именем error"), null, [FK.CatchParamShort]);

        R("name.lambdaParam", SignalGroup.Naming, "Параметры колбэков .map/.filter — полные слова: users.map((user) => …)",
            "Однобуквенные параметры колбэков: .map(x => …), .filter(i => …)", S(FK.ArrayCallbackSingleLetter), S(FK.ArrayCallbacks), 0.6, 0.05, 4, 0.6, 0.6,
            OfTotal("колбэков массивов с однобуквенным параметром"), null, [FK.ArrayCallbackSingleLetter]);

        R("name.abbrev", SignalGroup.Naming, "Нет сокращённых имён переменных",
            "Сокращённые имена: res, btn, tmp, arr, cb, el…", S(FK.Abbreviations), S(FK.LocalNames), 0.08, 0.0, 15, 0.6, 0.6,
            OfTotal("локальных имён и параметров — сокращения"), null, [FK.Abbreviations], minTotal: 5);

        R("name.generic", SignalGroup.Naming, "Шаблонные имена: result, data, filteredItems, updatedState…",
            "Имена переменных предметные, не шаблонные", S(FK.GenericNames), S(FK.LocalNames), 0.02, 0.12, 15, 0.3, 0.5,
            OfTotal("локальных имён шаблонные"), minTotal: 5);

        R("name.handler", SignalGroup.Naming, "Обработчики событий единообразно названы handleX (onClick={handleClick})",
            "Обработчики названы по-разному", S(FK.HandlerAttrsHandle), S(FK.HandlerAttrs), 0.2, 0.9, 4, 0.4, 0.7,
            OfTotal("обработчиков в разметке названы handle…"), minTotal: 2);

        R("name.iPrefix", SignalGroup.Naming, "Интерфейсы без префикса I",
            "Интерфейсы с префиксом I (IUser, IProps) — привычка C#/старого TS", S(FK.InterfacesIPrefix), S(FK.Interfaces), 0.6, 0.0, 3, 0.3, 0.8,
            OfTotal("интерфейсов с префиксом I"), null, [FK.InterfacesIPrefix], minTotal: 2);

        R("name.underscore", SignalGroup.Naming, "Приватные поля без подчёркивания",
            "Приватные поля с подчёркиванием: private _foo", S(FK.UnderscorePrivate), S(FK.ScriptLines), 0.006, 0.0, 150, 0.3, 0.8,
            Per100, null, [FK.UnderscorePrivate], minTotal: 30);

        R("name.screaming", SignalGroup.Naming, "Вынесенные константы SCREAMING_CASE вместо «магических» значений",
            "Константы почти не выносятся", S(FK.ScreamingConsts), S(FK.ScriptLines), 0.0, 0.015, 150, 0.3, 0.6, Per100, [FK.ScreamingConsts], minTotal: 30);

        double translit = _a[FK.Translit] + _a[FK.CyrillicIdentifiers];
        if (translit > 0)
        {
            b.Add("name.translit", SignalGroup.Naming, "Транслит или кириллица в именах (zakaz, polzovatel, кириллические идентификаторы)",
                -1, SignalBuilder.Rel(translit, 0.7), 1.0, $"{translit:0} имён", Ex(FK.Translit, FK.CyrillicIdentifiers), translit);
        }

        var names = _a.LocalNames.Where(n => n.Length > 1).ToList();
        if (names.Count >= 10)
        {
            double avg = names.Average(n => n.Length);
            double e = SignalBuilder.Map(avg, 4.5, 8.0);
            double? eb = null;
            if (_base is not null && _base.LocalNames.Count(n => n.Length > 1) >= 10)
            {
                double bavg = _base.LocalNames.Where(n => n.Length > 1).Average(n => n.Length);
                eb = SignalBuilder.Map(bavg, 4.5, 8.0);
                b.TrackShift("name.length", "Длина имён", avg, bavg, e, eb, v => v.ToString("0.0", CultureInfo.InvariantCulture));
            }

            b.Add("name.length", SignalGroup.Naming, e >= 0 ? "Длинные описательные имена локальных переменных" : "Короткие имена локальных переменных",
                SignalBuilder.AdjustForBaseline(e, eb, 0.6), SignalBuilder.Rel(names.Count, 20), 0.3, $"средняя длина {avg:0.0} символа ({names.Count} имён)", [], avg);
        }
    }

    // ------------------------------------------------------------------ типизация

    private void Typing()
    {
        if (_a[FK.TsLines] < 20)
        {
            return;
        }

        R("ts.any", SignalGroup.Typing, "any почти не используется", "Типы any в коде (: any, as any)", S(FK.AnyTypes), S(FK.TsLines), 0.02, 0.002, 150, 0.5, 0.6,
            Per100, null, [FK.AnyTypes], minTotal: 20);

        R("ts.unknown", SignalGroup.Typing, "unknown вместо any: catch (error: unknown), Record<string, unknown>", "unknown не используется",
            S(FK.UnknownTypes), S(FK.TsLines), 0.0, 0.008, 150, 0.4, 0.6, Per100, [FK.UnknownTypes], minTotal: 20);

        R("ts.nonNull", SignalGroup.Typing, "Нет утверждений non-null (!)", "Утверждения non-null: foo!.bar, value!",
            S(FK.NonNull), S(FK.TsLines), 0.01, 0.0, 150, 0.5, 0.6, Per100, null, [FK.NonNull], minTotal: 20);

        Flag("ts.tsIgnore", SignalGroup.Typing, "Подавление проверки типов: @ts-ignore / @ts-nocheck", FK.TsIgnore, -1, 0.6, "подавлений");

        R("ts.asCast", SignalGroup.Typing, "Мало приведений типов через as", "Много приведений типов: x as Foo",
            S(FK.AsCasts), S(FK.TsLines), 0.012, 0.002, 150, 0.3, 0.6, Per100, null, [FK.AsCasts], minTotal: 20);

        R("ts.asConst", SignalGroup.Typing, "as const для неизменяемых литералов", "as const не используется",
            S(FK.AsConst), S(FK.TsLines), 0.0, 0.005, 150, 0.3, 0.6, Per100, [FK.AsConst], minTotal: 20);

        R("ts.utility", SignalGroup.Typing, "Служебные типы: Record, Partial, Pick, Omit, ReturnType…", "Служебные типы почти не используются",
            S(FK.UtilityTypes), S(FK.TsLines), 0.0, 0.01, 150, 0.4, 0.6, Per100, [FK.UtilityTypes], minTotal: 20);

        Flag("ts.typeGuard", SignalGroup.Typing, "Предикаты типов: (x): x is Foo", FK.TypeGuards, 0.6, 0.3, "предикатов");

        R("ts.literalUnion", SignalGroup.Typing, "Объединения строковых литералов вместо enum: type Status = 'idle' | 'loading'",
            "enum вместо объединений литералов", S(FK.LiteralUnions), S(FK.LiteralUnions, FK.Enums), 0.2, 0.9, 2, 0.4, 0.6,
            OfTotal("перечислений сделаны объединением литералов"), [FK.LiteralUnions], [FK.Enums]);

        R("ts.returnTypes", SignalGroup.Typing, "Явные возвращаемые типы у экспортируемых функций и публичных методов",
            "Возвращаемые типы не указываются", S(FK.ExportedFunctionsTyped), S(FK.ExportedFunctions), 0.2, 0.8, 4, 0.4, 0.7,
            OfTotal("экспортируемых функций/публичных методов с типом результата"), minTotal: 2);

        R("ts.genericState", SignalGroup.Typing, "Типизированное состояние: useState<User | null>(null), ref<T>()",
            "Состояние без типа при пустом начальном значении: useState(null)", S(FK.StateHooksGeneric), S(FK.StateHooks), 0.2, 0.9, 3, 0.5, 0.6,
            OfTotal("useState/useRef/ref с пустым начальным значением типизированы"), [FK.StateHooksGeneric]);

        R("ts.importType", SignalGroup.Typing, "import type для типов", "Типы импортируются обычным import",
            S(FK.ImportTypes), S(FK.Imports), 0.0, 0.15, 10, 0.2, 0.7, OfTotal("импортов — import type"), [FK.ImportTypes], minTotal: 5);
    }

    // ------------------------------------------------------------------ синтаксис

    private void Syntax()
    {
        R("syn.nullish", SignalGroup.ModernSyntax, "?? вместо || для значений по умолчанию", "Значения по умолчанию через ||",
            S(FK.Nullish), S(FK.Nullish, FK.OrDefault), 0.1, 0.8, 3, 0.6, 0.6, OfTotal("значений по умолчанию через ??"), [FK.Nullish], [FK.OrDefault]);

        R("syn.optChain", SignalGroup.ModernSyntax, "Опциональные цепочки ?. вместо a && a.b", "Проверки цепочкой a && a.b",
            S(FK.OptionalChain), S(FK.OptionalChain, FK.AndChain), 0.3, 0.95, 3, 0.4, 0.6, OfTotal("безопасных обращений через ?."), null, [FK.AndChain]);

        R("syn.eqeq", SignalGroup.ModernSyntax, "Только строгое сравнение ===", "Нестрогое сравнение == / !=",
            S(FK.LooseEq), S(FK.LooseEq, FK.StrictEq), 0.3, 0.0, 5, 0.5, 0.6, OfTotal("сравнений нестрогие"), null, [FK.LooseEq]);

        R("syn.var", SignalGroup.ModernSyntax, "Нет var", "Объявления через var",
            S(FK.VarDecl), S(FK.VarDecl, FK.LetDecl, FK.ConstDecl), 0.2, 0.0, 8, 0.6, 0.6, OfTotal("объявлений через var"), null, [FK.VarDecl]);

        R("syn.let", SignalGroup.ModernSyntax, "Почти всё объявлено через const", "Много let",
            S(FK.LetDecl), S(FK.LetDecl, FK.ConstDecl), 0.4, 0.05, 8, 0.4, 0.6, OfTotal("объявлений через let"), minTotal: 4);

        R("syn.concat", SignalGroup.ModernSyntax, "Шаблонные строки вместо склейки через +", "Склейка строк через +",
            S(FK.Concat), S(FK.Concat, FK.TemplateSubst), 0.6, 0.05, 4, 0.5, 0.6, OfTotal("сборок строк через +"), null, [FK.Concat]);

        R("syn.then", SignalGroup.ModernSyntax, "async/await вместо цепочек .then()", "Цепочки .then() вместо async/await",
            S(FK.ThenCalls), S(FK.ThenCalls, FK.AwaitExpr), 0.6, 0.05, 4, 0.6, 0.6, OfTotal("асинхронных вызовов через .then()"), null, [FK.ThenCalls]);

        Flag("syn.radix", SignalGroup.ModernSyntax, "parseInt без основания системы счисления", FK.ParseIntNoRadix, -0.6, 0.3, "вызовов");

        R("syn.indexLoop", SignalGroup.ModernSyntax, "Циклы for…of и методы массивов вместо for (let i = 0…)", "Индексные циклы for (let i = 0; …)",
            S(FK.IndexLoops), S(FK.IndexLoops, FK.ForOfLoops, FK.ArrayCallbacks), 0.3, 0.0, 4, 0.3, 0.6, OfTotal("переборов — индексные циклы"), null, [FK.IndexLoops]);

        R("syn.functionalUpdate", SignalGroup.ModernSyntax, "Функциональные обновления состояния: setCount((prev) => prev + 1)",
            "Состояние обновляется без функциональной формы", S(FK.FunctionalUpdates), S(FK.SetterCalls), 0.0, 0.25, 8, 0.4, 0.5,
            OfTotal("вызовов setX(…) в функциональной форме"), [FK.FunctionalUpdates], minTotal: 3);

        R("syn.optionalCall", SignalGroup.ModernSyntax, "Необязательные колбэки вызываются через onChange?.(value)", "Опциональные вызовы ?.() не используются",
            S(FK.OptionalCall), S(FK.ScriptLines), 0.0, 0.004, 200, 0.3, 0.6, Per100, [FK.OptionalCall], minTotal: 30);

        R("syn.functionExpr", SignalGroup.ModernSyntax, "Стрелочные функции вместо function-выражений", "function-выражения вместо стрелочных функций",
            S(FK.FunctionExpressions), S(FK.FunctionExpressions, FK.ArrowFunctions), 0.4, 0.02, 5, 0.4, 0.6, OfTotal("анонимных функций — function-выражения"),
            null, [FK.FunctionExpressions]);

        R("syn.newApis", SignalGroup.ModernSyntax, "Новейшие API: structuredClone, crypto.randomUUID, .at(-1), ??=, replaceAll",
            "Новейшие API не используются", S(FK.NewApis), S(FK.ScriptLines), 0.0, 0.005, 200, 0.2, 1.0, Per100, [FK.NewApis], minTotal: 30);
    }

    // ------------------------------------------------------------------ ошибки

    private void Errors()
    {
        R("err.instanceofMessage", SignalGroup.Defensive, "Сужение ошибки: error instanceof Error ? error.message : 'Unknown error'",
            "Ошибка используется без сужения типа", S(FK.InstanceofMessage), S(FK.CatchBlocks), 0.0, 0.4, 3, 1.0, 0.5,
            OfTotal("catch-блоков с проверкой instanceof Error"), [FK.InstanceofMessage]);

        R("err.messageCast", SignalGroup.Defensive, "Сообщение ошибки берётся без приведений", "Сообщение ошибки через e.message / (e as Error).message",
            S(FK.ErrorMessageCast), S(FK.CatchBlocks), 0.4, 0.0, 3, 0.5, 0.6, OfTotal("обращений к e.message без проверки типа"), null, [FK.ErrorMessageCast]);

        R("err.responseOk", SignalGroup.Defensive, "Проверка ответа fetch: if (!response.ok) throw new Error(…)", "Ответ fetch не проверяется",
            S(FK.ResponseOk), S(FK.FetchCalls), 0.0, 0.8, 2, 0.8, 0.5, OfTotal("вызовов fetch с проверкой response.ok"), [FK.ResponseOk, FK.HttpErrorPhrase]);

        R("err.failedMsg", SignalGroup.Defensive, "Типовые сообщения об ошибках: «Failed to load…», «Something went wrong», «Unknown error»",
            "Нет шаблонных сообщений об ошибках", S(FK.FailedMessages, FK.HttpErrorPhrase), S(FK.ScriptLines), 0.0, 0.006, 150, 0.8, 0.5,
            Per100, [FK.FailedMessages, FK.HttpErrorPhrase], minTotal: 30);

        R("err.loadingTriad", SignalGroup.Defensive, "Тройка состояний загрузка/ошибка/данные со сбросом в finally",
            "Состояния загрузки и ошибки не оформлены", S(FK.LoadingTriad), S(FK.StatefulComponents), 0.1, 0.7, 2, 0.5, 0.6,
            OfTotal("файлов с запросами держат loading + error + finally"), [FK.LoadingTriad]);

        Flag("err.contextGuard", SignalGroup.Defensive, "Защита контекста: «useX must be used within XProvider»", FK.ContextGuard, 0.7, 0.4, "проверок");

        R("err.antipatterns", SignalGroup.Defensive, "Нет пустых catch и throw строк", "Анти-паттерны ошибок: пустой catch, throw 'строка', catch только с console.log",
            S(FK.EmptyCatch, FK.ThrowLiteral, FK.CatchLogOnly), S(FK.CatchBlocks, FK.ThrowNewError, FK.ThrowLiteral), 0.3, 0.0, 3, 0.6, 0.5,
            OfTotal("обработок ошибок с анти-паттерном"), null, [FK.EmptyCatch, FK.ThrowLiteral, FK.CatchLogOnly]);
    }

    // ------------------------------------------------------------------ async, отмена, отписка

    private void Async()
    {
        R("async.effectCleanup", SignalGroup.Async, "Эффекты с ресурсами (подписки, таймеры, слушатели, запросы) всегда возвращают очистку",
            "Эффекты с ресурсами без очистки", S(FK.EffectsResourceCleaned), S(FK.EffectsWithResource), 0.2, 0.9, 2, 1.0, 0.5,
            OfTotal("useEffect с ресурсом возвращают функцию очистки"), [FK.EffectsResourceCleaned]);

        R("async.abort", SignalGroup.Async, "Отмена запросов: AbortController / флаг cancelled в эффектах",
            "Запросы в эффектах не отменяются", S(FK.AbortControllers, FK.CancelFlags), S(FK.EffectsWithResource), 0.0, 0.5, 2, 0.8, 0.5,
            OfTotal("эффектов с ресурсом используют AbortController или флаг отмены"), [FK.AbortControllers, FK.CancelFlags]);

        Flag("async.isMounted", SignalGroup.Async, "Старый ручной паттерн isMounted", FK.IsMountedFlags, -0.6, 0.4, "использований");
        Flag("async.effectAsync", SignalGroup.Async, "useEffect(async () => …) — асинхронная функция эффекта (анти-паттерн)", FK.AsyncEffect, -0.8, 0.5, "эффектов");
        Flag("async.depsDisable", SignalGroup.Async, "Отключено правило зависимостей хуков (eslint-disable … exhaustive-deps)", FK.DepsDisable, -0.7, 0.5, "отключений");

        R("async.ngTeardown", SignalGroup.Async, "Подписки в компонентах Angular отписываются: takeUntil(destroy$), takeUntilDestroyed, unsubscribe",
            "Подписки в компонентах без отписки", S(FK.SubscribesTornDown), S(FK.Subscribes), 0.2, 0.95, 2, 1.0, 0.5,
            OfTotal("подписок в компонентах/директивах с отпиской"), null, [FK.Subscribes]);

        R("async.asyncPipe", SignalGroup.Async, "Потоки выводятся в шаблоне через | async", "Ручные subscribe с записью в поля вместо | async",
            S(FK.AsyncPipes), S(FK.AsyncPipes, FK.Subscribes), 0.0, 0.5, 3, 0.4, 0.6, OfTotal("потоков в шаблоне через async-pipe"), [FK.AsyncPipes], minTotal: 2);

        R("async.vueCleanup", SignalGroup.Async, "Очистка в onUnmounted/onBeforeUnmount", "Компоненты Vue без хуков очистки",
            S(FK.VueUnmountHooks), S(FK.VueComponents), 0.0, 0.3, 3, 0.3, 0.6, OfTotal("компонентов Vue с хуком очистки"), [FK.VueUnmountHooks], minTotal: 2);

        R("async.promiseAll", SignalGroup.Async, "Независимые запросы параллельно: Promise.all / allSettled", "Независимые await идут последовательно",
            S(FK.PromiseAll), S(FK.AwaitExpr), 0.0, 0.1, 8, 0.3, 0.6, OfTotal("await-ов в Promise.all"), [FK.PromiseAll], minTotal: 3);
    }

    // ------------------------------------------------------------------ логирование

    private void Logging()
    {
        R("log.errorTemplate", SignalGroup.Logging, "console.error с шаблоном «Предложение:» и объектом ошибки: console.error('Failed to fetch users:', error)",
            "console.error в свободной форме", S(FK.ConsoleErrorTemplated), S(FK.ConsoleErrors), 0.1, 0.8, 2, 1.0, 0.5,
            OfTotal("console.error/warn по шаблону «Сообщение:», ошибка"), [FK.ConsoleErrorTemplated]);

        R("log.consoleLog", SignalGroup.Logging, "Нет отладочных console.log в коде", "Отладочные console.log в прикладном коде",
            S(FK.ConsoleLogs), S(FK.ScriptLines), 0.01, 0.0, 100, 0.8, 0.4, Per100, null, [FK.ConsoleLogs], minTotal: 30);
    }

    // ------------------------------------------------------------------ идиомы фреймворков

    private void Framework()
    {
        var g = SignalGroup.FrameworkIdioms;

        // React
        R("react.memoDensity", g, "Много useCallback/useMemo/memo на компонент", "Мемоизация почти не используется",
            S(FK.MemoHooks), S(FK.Components), 0.2, 1.5, 3, 0.4, 0.7, OfTotal("useCallback/useMemo/memo на компоненты"), minTotal: 2);
        R("react.customHooks", g, "Логика вынесена в собственные хуки useX", "Собственных хуков нет",
            S(FK.CustomHooks), S(FK.Components), 0.0, 0.4, 3, 0.4, 0.6, OfTotal("новых хуков на компоненты"), [FK.CustomHooks], minTotal: 2);
        R("react.keyIndex", g, "key — устойчивые идентификаторы", "key={index} в списках",
            S(FK.KeyIndex), S(FK.KeyAttrs), 0.4, 0.0, 3, 0.6, 0.6, OfTotal("атрибутов key — индекс массива"), null, [FK.KeyIndex]);
        R("react.fcType", g, "Компоненты без React.FC", "Компоненты через React.FC / FC<Props>",
            S(FK.FcType), S(FK.Components), 0.6, 0.0, 3, 0.3, 0.8, OfTotal("компонентов через FC"), null, [FK.FcType], minTotal: 2);
        R("react.legacy", g, "Функциональные компоненты без PropTypes", "Классовые компоненты и PropTypes",
            S(FK.ClassComponents, FK.PropTypes), S(FK.Components), 0.5, 0.0, 3, 0.3, 0.9, OfTotal("компонентов в старом стиле"), null, [FK.ClassComponents, FK.PropTypes]);
        R("react.propsNamed", g, "Тип пропсов назван по компоненту: function UserCard({ … }: UserCardProps)", "Пропсы не типизированы или названы иначе",
            S(FK.PropsTypeNamed), S(FK.Components), 0.1, 0.8, 3, 0.4, 0.7, OfTotal("компонентов с типом пропсов ИмяProps"), minTotal: 2);
        Flag("react.displayName", g, "displayName задан вручную", FK.DisplayName, 0.4, 0.2, "присваиваний");
        Flag("react.derivedState", g, "Производное состояние через useEffect + setState («You might not need an effect»)", FK.DerivedStateEffect, -0.6, 0.4, "эффектов");
        R("react.inlineStyle", g, "Стили без style={{…}}", "Инлайн-стили style={{…}} в разметке",
            S(FK.InlineStyles), S(FK.Elements), 0.08, 0.0, 20, 0.3, 0.6, OfTotal("элементов с инлайн-стилем"), null, [FK.InlineStyles], minTotal: 8);

        // Angular
        R("ng.ctorReadonly", g, "Зависимости в конструкторе private readonly", "Зависимости в конструкторе без readonly",
            S(FK.NgCtorReadonly), S(FK.NgCtorParams), 0.05, 0.8, 3, 0.6, 0.7, OfTotal("параметров конструктора с readonly"));
        R("ng.subscribeObserver", g, "subscribe с объектом-наблюдателем: subscribe({ next, error })", "Позиционные колбэки subscribe(d => …, e => …)",
            S(FK.NgObserverSubscribe), S(FK.SubscribeCalls), 0.0, 0.6, 3, 0.5, 0.6, OfTotal("subscribe с объектом { next, error }"), [FK.NgObserverSubscribe]);
        Flag("ng.nestedSubscribe", g, "subscribe внутри subscribe", FK.NgNestedSubscribe, -0.6, 0.5, "вложенных подписок");
        R("ng.typedHttp", g, "Типизированные запросы HttpClient: http.get<User[]>(…)", "Запросы HttpClient без типа результата",
            S(FK.NgHttpTyped), S(FK.NgHttpCalls), 0.2, 0.9, 3, 0.5, 0.6, OfTotal("вызовов HttpClient с типом"));
        R("ng.trackBy", g, "*ngFor с trackBy", "*ngFor без trackBy", S(FK.NgForTrackBy), S(FK.NgFor), 0.0, 0.6, 3, 0.4, 0.6, OfTotal("*ngFor с trackBy"), [FK.NgForTrackBy]);
        R("ng.onPush", g, "Компоненты с ChangeDetectionStrategy.OnPush", "Компоненты без OnPush",
            S(FK.NgOnPush), S(FK.NgComponents), 0.0, 0.8, 2, 0.4, 0.7, OfTotal("компонентов с OnPush"), [FK.NgOnPush]);
        R("ng.modernApis", g, "Новейшие API Angular: inject(), signal()/computed(), @if/@for", "Классический Angular без новых API",
            S(FK.NgSignals, FK.NgInject), S(FK.ScriptLines, FK.MarkupLines), 0.0, 0.02, 150, 0.2, 1.0, Per100, minTotal: 30);
        Flag("ng.manualDom", g, "Ручная работа с DOM: document.querySelector, nativeElement.style", FK.NgManualDom, -0.5, 0.4, "обращений");

        // Vue
        R("vue.propsTyped", g, "Пропсы объявлены типом: defineProps<Props>()", "Пропсы объявлены массивом или объектом без типа",
            S(FK.VuePropsTyped), S(FK.VueProps), 0.0, 0.9, 2, 0.5, 0.7, OfTotal("объявлений пропсов типизированы"), [FK.VuePropsTyped]);
        R("vue.emitsTyped", g, "События объявлены типом: defineEmits<{…}>()", "События без типов",
            S(FK.VueEmitsTyped), S(FK.VueEmits), 0.0, 0.9, 2, 0.4, 0.7, OfTotal("объявлений событий типизированы"), [FK.VueEmitsTyped]);
        Flag("vue.withDefaults", g, "withDefaults(defineProps<Props>(), {…})", FK.VueWithDefaults, 0.5, 0.3, "использований");
        R("vue.optionsApi", g, "Composition API без this.$refs / methods / data()", "Options API: data(), methods, this.$refs",
            S(FK.VueOptionsApi), S(FK.ScriptLines), 0.02, 0.0, 100, 0.3, 0.9, Per100, null, [FK.VueOptionsApi], minTotal: 30);
        R("vue.vFor", g, "v-for всегда с устойчивым :key", "v-for без :key, :key=\"index\" или v-if вместе с v-for",
            s => s[FK.VueForKeyIndex] + s[FK.VueIfWithFor] + Math.Max(0, s[FK.VueFor] - s[FK.VueForKeyed]), S(FK.VueFor), 0.4, 0.0, 3, 0.5, 0.6,
            OfTotal("v-for с проблемой ключа"), null, [FK.VueForKeyIndex, FK.VueIfWithFor]);
        R("vue.scriptSetup", g, "<script setup lang=\"ts\">", "Компоненты без <script setup>",
            S(FK.VueScriptSetup), S(FK.VueComponents), 0.0, 0.9, 2, 0.2, 0.9, OfTotal("компонентов с <script setup>"), minTotal: 2);
    }

    // ------------------------------------------------------------------ разметка и доступность

    private void Accessibility()
    {
        var g = SignalGroup.Accessibility;
        R("a11y.buttonType", g, "У каждой кнопки явный type=\"button\"/\"submit\"", "Кнопки без атрибута type",
            S(FK.ButtonsTyped), S(FK.Buttons), 0.1, 0.9, 2, 0.8, 0.6, OfTotal("кнопок с явным type"), [FK.ButtonsTyped]);
        R("a11y.aria", g, "Атрибуты доступности aria-* в разметке", "Почти нет aria-атрибутов",
            S(FK.AriaAttrs), S(FK.Elements), 0.0, 0.08, 20, 0.5, 0.6, OfTotal("элементов с aria-атрибутами"), [FK.AriaAttrs], minTotal: 8);
        R("a11y.testId", g, "data-testid в разметке", "Нет data-testid", S(FK.TestIds), S(FK.Elements), 0.0, 0.1, 20, 0.3, 0.7,
            OfTotal("элементов с data-testid"), [FK.TestIds], minTotal: 8);
        R("a11y.labelFor", g, "Поля ввода связаны с подписями (label for/htmlFor)", "Подписи полей не связаны с полями",
            S(FK.LabelsFor), S(FK.Inputs), 0.0, 0.7, 2, 0.3, 0.6, OfTotal("полей ввода с связанной подписью"), [FK.LabelsFor]);
        R("a11y.altText", g, "У изображений есть alt", "Изображения без alt", S(FK.ImagesAlt), S(FK.Images), 0.3, 1.0, 2, 0.3, 0.6,
            OfTotal("изображений с alt"));
        R("a11y.divClick", g, "Клики только на интерактивных элементах", "Клик на div/span без role и клавиатуры",
            S(FK.ClickOnDiv), S(FK.Elements), 0.04, 0.0, 20, 0.4, 0.6, OfTotal("элементов с кликом на div/span"), null, [FK.ClickOnDiv], minTotal: 8);
    }

    // ------------------------------------------------------------------ стили

    private void Styles()
    {
        var g = SignalGroup.Styles;
        R("css.vars", g, "Значения через CSS-переменные var(--…)", "Значения стилей захардкожены", S(FK.CssVars), S(FK.CssDecls), 0.0, 0.25, 10, 0.5, 0.7,
            OfTotal("деклараций через var(--…)"), [FK.CssVars], minTotal: 5);
        R("css.llmValues", g, "Типовые значения ИИ: box-shadow rgba(0,0,0,.1), transition 0.2s ease, палитра Tailwind",
            "Нет «типовых» значений стилей", S(FK.CssLlmValues), S(FK.CssDecls), 0.0, 0.06, 15, 0.6, 0.5, OfTotal("деклараций с типовыми значениями"),
            [FK.CssLlmValues], minTotal: 5);
        Flag("css.focusVisible", g, "Стили :focus-visible для клавиатурного фокуса", FK.CssFocusVisible, 0.6, 0.3, "правил");
        if (_a[FK.CssOutlineNone] > 0 && _a[FK.CssFocusVisible] == 0)
        {
            b.Add("css.outlineNone", g, "outline: none без замены фокуса", -0.4, SignalBuilder.Rel(_a[FK.CssOutlineNone], 0.5), 0.3,
                $"{_a[FK.CssOutlineNone]:0} деклараций", _a.GetExamples(FK.CssOutlineNone), _a[FK.CssOutlineNone]);
        }

        Flag("css.reducedMotion", g, "@media (prefers-reduced-motion)", FK.CssReducedMotion, 0.8, 0.3, "блоков");
        R("css.important", g, "Нет !important", "!important в стилях", S(FK.CssImportant), S(FK.CssDecls), 0.05, 0.0, 15, 0.5, 0.5,
            OfTotal("деклараций с !important"), null, [FK.CssImportant], minTotal: 5);
        Flag("css.zHuge", g, "«Магические» z-index ≥ 999", FK.CssHugeZ, -0.5, 0.3, "деклараций");
        R("css.vendor", g, "Нет ручных вендорных префиксов", "Ручные вендорные префиксы -webkit-/-moz-", S(FK.CssVendor), S(FK.CssDecls), 0.04, 0.0, 20, 0.3, 0.7,
            OfTotal("деклараций с префиксом"), null, [FK.CssVendor], minTotal: 5);
        R("css.nesting", g, "Неглубокая вложенность стилей", "Глубокая вложенность SCSS (≥ 4 уровней)", S(FK.CssDeepNesting), S(FK.CssRules), 0.15, 0.0, 10, 0.2, 0.6,
            OfTotal("правил с глубокой вложенностью"), null, [FK.CssDeepNesting], minTotal: 5);
        R("css.rem", g, "Размеры в rem", "Размеры в px", S(FK.CssRem), S(FK.CssRem, FK.CssPx), 0.05, 0.6, 10, 0.3, 0.7, OfTotal("размеров в rem"), minTotal: 5);
    }

    // ------------------------------------------------------------------ ручные привычки

    private void HumanHabits()
    {
        var parts = new (string Key, string Title)[]
        {
            (FK.Todo, "TODO/FIXME"),
            (FK.TicketRefs, "ссылки на задачи"),
            (FK.CommentedOutCode, "закомментированный код"),
            (FK.Debugger, "debugger"),
            (FK.EslintDisable, "eslint-disable"),
            (FK.Alerts, "alert()"),
            (FK.SetTimeoutZero, "setTimeout(…, 0)"),
            (FK.Reloads, "location.reload()"),
            (FK.JQuery, "jQuery $(…)"),
            (FK.Typos, "опечатки"),
        };
        double lines = _a[FK.CodeLines];
        if (lines < 40)
        {
            return;
        }

        double total = parts.Sum(p => _a[p.Key]);
        double per100 = total * 100 / lines;
        var found = parts.Where(p => _a[p.Key] > 0).Select(p => $"{p.Title}: {_a[p.Key]:0}").ToList();
        double e = total == 0 ? 0.35 : SignalBuilder.Map(per100, 1.2, 0.0);
        if (total > 0)
        {
            e = Math.Min(e, -0.3);
        }

        var examples = parts.SelectMany(p => _a.GetExamples(p.Key).Take(2)).Take(4).ToList();
        b.Add("habits.markers", SignalGroup.HumanHabits,
            total == 0 ? "Нет «ручных» следов: TODO, закомментированного кода, debugger, eslint-disable" : "«Ручные» следы в коде",
            e, SignalBuilder.Rel(lines, 80), 1.2, total == 0 ? $"0 на {lines:0} строк кода" : string.Join("; ", found), examples, per100);

        R("habits.magic", SignalGroup.HumanHabits, "Нет «магических» чисел в выражениях", "«Магические» числа в логике",
            S(FK.MagicNumbers), S(FK.ScriptLines), 0.02, 0.0, 100, 0.2, 0.6, Per100, null, [FK.MagicNumbers], minTotal: 30);
    }

    // ------------------------------------------------------------------ форматирование

    private void Formatting()
    {
        double lines = _a[FK.CodeLines];
        if (lines < 40)
        {
            return;
        }

        R("fmt.irregular", SignalGroup.Formatting, "Форматирование безупречно ровное", "Неровное форматирование: пробелы в конце строк, if(, ){, a=b, двойные пустые строки",
            S(FK.TrailingWhitespace, FK.KeywordNoSpace, FK.BraceNoSpace, FK.OperatorNoSpace, FK.DoubleBlank, FK.LongLines), S(FK.CodeLines), 0.03, 0.0, 60, 0.8, 0.5,
            Per100, null, [FK.TrailingWhitespace, FK.KeywordNoSpace, FK.BraceNoSpace, FK.OperatorNoSpace]);

        R("fmt.mixedStyle", SignalGroup.Formatting, "Единый стиль кавычек, точек с запятой и отступов", "Смешение кавычек, точек с запятой или отступов в файле",
            S(FK.MixedQuoteFiles, FK.MixedSemicolonFiles, FK.MixedIndentFiles), S(FK.ScriptFiles), 0.3, 0.0, 3, 0.5, 0.5,
            OfTotal("файлов со смешанным стилем"), null, [FK.MixedQuoteFiles, FK.MixedSemicolonFiles], minTotal: 2);
    }

    // ------------------------------------------------------------------ тесты

    private void Tests()
    {
        if (_a[FK.TestFiles] < 1)
        {
            return;
        }

        var g = SignalGroup.Tests;
        R("test.should", g, "Названия тестов единообразно «should …»", "Названия тестов в свободной форме", S(FK.TestShould), S(FK.TestCases), 0.2, 0.9, 5, 0.3, 0.7,
            OfTotal("тестов называются «should …»"), minTotal: 3);
        R("test.each", g, "Параметризованные тесты it.each / test.each", "Нет параметризованных тестов", S(FK.TestEach), S(FK.TestFiles), 0.0, 0.5, 2, 0.4, 0.6,
            OfTotal("тест-файлов с .each"), [FK.TestEach]);
        R("test.clearMocks", g, "Сброс моков в beforeEach: jest.clearAllMocks() / vi.clearAllMocks()", "Моки не сбрасываются явно",
            S(FK.TestClearMocks), S(FK.TestFilesWithMocks), 0.0, 0.7, 2, 0.5, 0.6, OfTotal("тест-файлов с моками сбрасывают их"), [FK.TestClearMocks]);
        R("test.roleQueries", g, "Запросы Testing Library по роли и подписи: getByRole, getByLabelText", "Поиск элементов по testid, тексту или селектору",
            S(FK.RtlRoleQueries), S(FK.RtlRoleQueries, FK.RtlOtherQueries), 0.05, 0.6, 4, 0.6, 0.5, OfTotal("запросов элементов — по роли/подписи"), [FK.RtlRoleQueries]);
        R("test.userEvent", g, "userEvent вместо fireEvent", "fireEvent вместо userEvent", S(FK.UserEvent), S(FK.UserEvent, FK.FireEvent), 0.1, 0.8, 3, 0.4, 0.6,
            OfTotal("событий через userEvent"), [FK.UserEvent]);
        R("test.snapshot", g, "Без snapshot-тестов", "Snapshot-тесты (toMatchSnapshot)", S(FK.Snapshots), S(FK.TestCases), 0.2, 0.0, 3, 0.4, 0.6,
            OfTotal("тестов со снапшотом"), null, [FK.Snapshots]);
        R("test.strongAsserts", g, "Точные проверки: toEqual, toHaveBeenCalledWith, toHaveTextContent", "Слабые проверки: toBeTruthy, toBeDefined",
            S(FK.StrongAsserts), S(FK.StrongAsserts, FK.WeakAsserts), 0.4, 0.9, 5, 0.4, 0.5, OfTotal("проверок — точные"), minTotal: 3);
        R("test.exampleData", g, "Шаблонные тестовые данные: test@example.com, John Doe", "Тестовые данные предметные",
            S(FK.ExampleData), S(FK.TestCases), 0.0, 0.3, 4, 0.4, 0.6, OfTotal("тестов с шаблонными данными"), [FK.ExampleData]);
        R("test.aaa", g, "Разметка тестов // Arrange / Act / Assert", "Тесты без разметки AAA", S(FK.AaaComments), S(FK.TestCases), 0.0, 0.5, 3, 0.6, 0.6,
            OfTotal("тестов с комментариями AAA"), [FK.AaaComments]);
    }

    // ------------------------------------------------------------------ структура

    private void Structure()
    {
        R("fn.long", SignalGroup.Structure, "Короткие функции и компоненты", "Длинные функции и компоненты (> 60 строк)",
            S(FK.LongFunctions), S(FK.Functions), 0.15, 0.02, 5, 0.3, 0.6, OfTotal("функций длиннее 60 строк"), null, [FK.LongFunctions], minTotal: 3);
        R("fn.nesting", SignalGroup.Structure, "Неглубокая вложенность условий", "Глубокая вложенность (> 3 уровней)",
            S(FK.DeepNesting), S(FK.Functions), 0.1, 0.0, 5, 0.3, 0.6, OfTotal("функций с глубокой вложенностью"), null, [FK.DeepNesting], minTotal: 3);
        R("fn.complexity", SignalGroup.Structure, "Простые функции", "Сложные функции (цикломатическая сложность > 12)",
            S(FK.ComplexFunctions), S(FK.Functions), 0.1, 0.0, 5, 0.2, 0.6, OfTotal("сложных функций"), null, [FK.ComplexFunctions], minTotal: 3);
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
            e, SignalBuilder.Rel(items.Count, 4) * SignalBuilder.Rel(ctx.BaselineFiles, 3), 1.2, string.Join("; ", top), [], mean);
    }
}
