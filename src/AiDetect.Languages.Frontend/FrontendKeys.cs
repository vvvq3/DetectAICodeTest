using AiDetect.Core.Features;

namespace AiDetect.Languages.Frontend;

/// <summary>Имена счётчиков признаков фронтенда (JS/TS, React, Angular, Vue, шаблоны, CSS).</summary>
public static class FK
{
    // Объём
    public const string Files = StatKeys.Files;
    public const string NewFiles = StatKeys.NewFiles;
    public const string CodeLines = StatKeys.CodeLines;
    public const string CommentLines = StatKeys.CommentLines;
    public const string TotalLines = StatKeys.TotalLines;
    public const string ScriptLines = "lines.script";
    public const string MarkupLines = "lines.markup";
    public const string StyleLines = "lines.style";
    public const string TsLines = "lines.ts";
    public const string ScriptFiles = "files.script";

    // Документация
    public const string DocTargets = "doc.targets";
    public const string DocDocumented = "doc.documented";
    public const string JsDocBlocks = "doc.blocks";
    public const string JsDocTemplate = "doc.template";
    public const string JsDocTypedParams = "doc.typedParams";
    public const string JsDocParams = "doc.params";
    public const string JsDocExtraTags = "doc.extraTags";
    public const string PropsMembers = "doc.propsMembers";
    public const string PropsMembersDocumented = "doc.propsMembersDocumented";
    public const string FileHeader = "doc.fileHeader";

    // Комментарии
    public const string LineComments = "cmt.total";
    public const string Narrative = "cmt.narrative";
    public const string Restating = "cmt.restating";
    public const string JsxSection = "cmt.jsxSection";
    public const string MarkupSection = "cmt.markupSection";
    public const string CssSection = "cmt.cssSection";
    public const string Casual = "cmt.casual";
    public const string CyrillicComments = "cmt.cyrillic";
    public const string CyrillicPolished = "cmt.cyrillicPolished";
    public const string LatinComments = "cmt.latin";
    public const string EmojiInCode = "cmt.emoji";
    public const string Todo = "cmt.todo";
    public const string TicketRefs = "cmt.ticket";
    public const string CommentedOutCode = "cmt.commentedCode";
    public const string EslintDisable = "cmt.eslintDisable";
    public const string TsIgnore = "cmt.tsIgnore";
    public const string TsExpectError = "cmt.tsExpectError";
    public const string ChangeNote = "cmt.changeNote";
    public const string AaaComments = "cmt.aaa";

    // Именование
    public const string CatchParams = "name.catch";
    public const string CatchParamError = "name.catchError";
    public const string CatchParamShort = "name.catchShort";
    public const string ArrayCallbacks = "name.arrayCb";
    public const string ArrayCallbackSingleLetter = "name.arrayCbLetter";
    public const string ArrayCallbackDescriptive = "name.arrayCbWord";
    public const string Abbreviations = "name.abbrev";
    public const string GenericNames = "name.generic";
    public const string LocalNames = "name.locals";
    public const string HandlerAttrs = "name.handlerAttrs";
    public const string HandlerAttrsHandle = "name.handlerHandle";
    public const string BoolStates = "name.boolStates";
    public const string BoolStatesPrefixed = "name.boolPrefixed";
    public const string Interfaces = "name.interfaces";
    public const string InterfacesIPrefix = "name.iPrefix";
    public const string UnderscorePrivate = "name.underscore";
    public const string ScreamingConsts = "name.screaming";
    public const string Translit = "name.translit";
    public const string CyrillicIdentifiers = "name.cyrillic";

    // Типизация TS
    public const string TypeAnnotations = "ts.annotations";
    public const string AnyTypes = "ts.any";
    public const string UnknownTypes = "ts.unknown";
    public const string DoubleCasts = "ts.doubleCast";
    public const string NonNull = "ts.nonNull";
    public const string AsCasts = "ts.asCast";
    public const string AsConst = "ts.asConst";
    public const string UtilityTypes = "ts.utility";
    public const string TypeGuards = "ts.typeGuard";
    public const string LiteralUnions = "ts.literalUnion";
    public const string Enums = "ts.enum";
    public const string ExportedFunctions = "ts.exportedFns";
    public const string ExportedFunctionsTyped = "ts.exportedFnsTyped";
    public const string StateHooks = "ts.stateHooks";
    public const string StateHooksGeneric = "ts.stateHooksGeneric";
    public const string ReadonlyMods = "ts.readonly";
    public const string ImportTypes = "ts.importType";
    public const string Imports = "ts.imports";

    // Синтаксис
    public const string Nullish = "syn.nullish";
    public const string OrDefault = "syn.orDefault";
    public const string OptionalChain = "syn.optChain";
    public const string AndChain = "syn.andChain";
    public const string StrictEq = "syn.strictEq";
    public const string LooseEq = "syn.looseEq";
    public const string ConstDecl = "syn.const";
    public const string LetDecl = "syn.let";
    public const string VarDecl = "syn.var";
    public const string Concat = "syn.concat";
    public const string TemplateSubst = "syn.template";
    public const string ThenCalls = "syn.then";
    public const string AwaitExpr = "syn.await";
    public const string ParseIntNoRadix = "syn.parseIntNoRadix";
    public const string IndexLoops = "syn.indexLoop";
    public const string ForOfLoops = "syn.forOf";
    public const string FunctionalUpdates = "syn.functionalUpdate";
    public const string SetterCalls = "syn.setterCalls";
    public const string OptionalCall = "syn.optionalCall";
    public const string ArrowFunctions = "syn.arrows";
    public const string FunctionExpressions = "syn.functionExpr";
    public const string NewApis = "syn.newApis";

    // Ошибки
    public const string CatchBlocks = "err.catch";
    public const string InstanceofMessage = "err.instanceofMessage";
    public const string ErrorMessageCast = "err.messageCast";
    public const string ResponseOk = "err.responseOk";
    public const string FetchCalls = "err.fetch";
    public const string HttpErrorPhrase = "err.httpPhrase";
    public const string FailedMessages = "err.failedMsg";
    public const string ErrorStrings = "err.strings";
    public const string ThrowNewError = "err.throwNew";
    public const string ThrowLiteral = "err.throwLiteral";
    public const string EmptyCatch = "err.emptyCatch";
    public const string CatchLogOnly = "err.catchLogOnly";
    public const string ContextGuard = "err.contextGuard";
    public const string LoadingTriad = "err.loadingTriad";
    public const string StatefulComponents = "err.statefulComponents";

    // Async, отмена, отписка
    public const string Effects = "async.effects";
    public const string EffectsWithResource = "async.effectsResource";
    public const string EffectsWithCleanup = "async.effectsCleanup";
    public const string EffectsResourceCleaned = "async.effectsResourceCleaned";
    public const string AbortControllers = "async.abort";
    public const string CancelFlags = "async.cancelFlag";
    public const string IsMountedFlags = "async.isMounted";
    public const string AsyncEffect = "async.effectAsync";
    public const string DepsDisable = "async.depsDisable";
    public const string AbortErrorCheck = "async.abortError";
    public const string Subscribes = "async.subscribe";
    public const string SubscribeCalls = "async.subscribeAll";
    public const string SubscribesTornDown = "async.subscribeTornDown";
    public const string DestroySubjects = "async.destroySubject";
    public const string AsyncPipes = "async.asyncPipe";
    public const string VueUnmountHooks = "async.vueUnmount";
    public const string PromiseAll = "async.promiseAll";
    public const string AsyncFunctions = "async.functions";

    // Логирование
    public const string ConsoleErrors = "log.consoleError";
    public const string ConsoleErrorTemplated = "log.errorTemplate";
    public const string ConsoleLogs = "log.consoleLog";

    // React
    public const string Components = "react.components";
    public const string MemoHooks = "react.memo";
    public const string CustomHooks = "react.customHooks";
    public const string KeyAttrs = "react.keys";
    public const string KeyIndex = "react.keyIndex";
    public const string FcType = "react.fc";
    public const string PropsTypeNamed = "react.propsNamed";
    public const string PropsTyped = "react.propsTyped";
    public const string ClassComponents = "react.classComponents";
    public const string PropTypes = "react.propTypes";
    public const string DisplayName = "react.displayName";
    public const string InlineStyles = "react.inlineStyle";
    public const string DerivedStateEffect = "react.derivedState";

    // Angular
    public const string NgComponents = "ng.components";
    public const string NgCtorParams = "ng.ctorParams";
    public const string NgCtorReadonly = "ng.ctorReadonly";
    public const string NgInject = "ng.inject";
    public const string NgObserverSubscribe = "ng.subscribeObserver";
    public const string NgNestedSubscribe = "ng.nestedSubscribe";
    public const string NgHttpCalls = "ng.httpCalls";
    public const string NgHttpTyped = "ng.httpTyped";
    public const string NgOnPush = "ng.onPush";
    public const string NgFor = "ng.for";
    public const string NgForTrackBy = "ng.trackBy";
    public const string NgSignals = "ng.signals";
    public const string NgRxOperators = "ng.rxOperators";
    public const string NgManualDom = "ng.manualDom";

    // Vue
    public const string VueComponents = "vue.components";
    public const string VueScriptSetup = "vue.scriptSetup";
    public const string VueLangTs = "vue.langTs";
    public const string VueProps = "vue.props";
    public const string VuePropsTyped = "vue.propsTyped";
    public const string VueWithDefaults = "vue.withDefaults";
    public const string VueEmits = "vue.emits";
    public const string VueEmitsTyped = "vue.emitsTyped";
    public const string VueOptionsApi = "vue.optionsApi";
    public const string VueFor = "vue.for";
    public const string VueForKeyed = "vue.forKeyed";
    public const string VueForKeyIndex = "vue.forKeyIndex";
    public const string VueIfWithFor = "vue.ifWithFor";
    public const string VueScopedStyles = "vue.scoped";

    // Разметка и доступность (JSX, Angular, Vue)
    public const string Elements = "a11y.elements";
    public const string Buttons = "a11y.buttons";
    public const string ButtonsTyped = "a11y.buttonsTyped";
    public const string AriaAttrs = "a11y.aria";
    public const string RoleAttrs = "a11y.role";
    public const string TestIds = "a11y.testId";
    public const string Images = "a11y.images";
    public const string ImagesAlt = "a11y.imagesAlt";
    public const string Inputs = "a11y.inputs";
    public const string LabelsFor = "a11y.labelFor";
    public const string ClickOnDiv = "a11y.divClick";
    public const string HardcodedTexts = "a11y.texts";

    // CSS
    public const string CssDecls = "css.decls";
    public const string CssVars = "css.vars";
    public const string CssCustomProps = "css.customProps";
    public const string CssImportant = "css.important";
    public const string CssHugeZ = "css.zHuge";
    public const string CssVendor = "css.vendor";
    public const string CssFocusVisible = "css.focusVisible";
    public const string CssOutlineNone = "css.outlineNone";
    public const string CssReducedMotion = "css.reducedMotion";
    public const string CssTransitions = "css.transition";
    public const string CssLlmValues = "css.llmValues";
    public const string CssRem = "css.rem";
    public const string CssPx = "css.px";
    public const string CssDeepNesting = "css.deepNesting";
    public const string CssRules = "css.rules";

    // Ручные привычки
    public const string Debugger = "habits.debugger";
    public const string Alerts = "habits.alert";
    public const string SetTimeoutZero = "habits.timeout0";
    public const string Reloads = "habits.reload";
    public const string JQuery = "habits.jquery";
    public const string Typos = "habits.typos";
    public const string MagicNumbers = "habits.magic";

    // Форматирование
    public const string TrailingWhitespace = "fmt.trailing";
    public const string KeywordNoSpace = "fmt.kwNoSpace";
    public const string BraceNoSpace = "fmt.braceNoSpace";
    public const string OperatorNoSpace = "fmt.opNoSpace";
    public const string DoubleBlank = "fmt.doubleBlank";
    public const string LongLines = "fmt.longLines";
    public const string SingleQuotes = "fmt.singleQuotes";
    public const string DoubleQuotes = "fmt.doubleQuotes";
    public const string MixedQuoteFiles = "fmt.mixedQuotes";
    public const string Semicolons = "fmt.semis";
    public const string MissingSemicolons = "fmt.noSemis";
    public const string MixedSemicolonFiles = "fmt.mixedSemis";
    public const string TabIndent = "fmt.tabs";
    public const string MixedIndentFiles = "fmt.mixedIndent";

    // Тесты
    public const string TestCases = "test.cases";
    public const string TestShould = "test.should";
    public const string TestEach = "test.each";
    public const string TestClearMocks = "test.clearMocks";
    public const string TestFilesWithMocks = "test.filesWithMocks";
    public const string RtlRoleQueries = "test.roleQueries";
    public const string RtlOtherQueries = "test.otherQueries";
    public const string UserEvent = "test.userEvent";
    public const string FireEvent = "test.fireEvent";
    public const string Snapshots = "test.snapshot";
    public const string StrongAsserts = "test.strongAsserts";
    public const string WeakAsserts = "test.weakAsserts";
    public const string ExampleData = "test.exampleData";
    public const string TestFiles = "test.files";
    public const string CliSkeletonSpecs = "test.cliSkeleton";

    // Структура
    public const string Functions = "fn.count";
    public const string LongFunctions = "fn.long";
    public const string DeepNesting = "fn.deep";
    public const string ComplexFunctions = "fn.complex";
}
