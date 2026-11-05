namespace AiDetect.Languages.CSharp;

/// <summary>Имена счётчиков признаков C#.</summary>
public static class K
{
    // Объём
    public const string Files = "files";
    public const string NewFiles = "files.new";
    public const string CodeLines = "lines.code";
    public const string CommentLines = "lines.comment";
    public const string TotalLines = "lines.total";

    // Документация и комментарии
    public const string DocTargets = "doc.targets";
    public const string DocTargetsDocumented = "doc.documented";
    public const string DocMethodsWithParams = "doc.methodsWithParams";
    public const string DocMethodsComplete = "doc.methodsComplete";
    public const string DocBlocks = "doc.blocks";
    public const string DocTemplatePhrase = "doc.templatePhrase";
    public const string InheritDoc = "doc.inheritdoc";
    public const string LineComments = "cmt.total";
    public const string NarrativeComments = "cmt.narrative";
    public const string RestatingComments = "cmt.restating";
    public const string StepComments = "cmt.step";
    public const string TodoComments = "cmt.todo";
    public const string TicketRefs = "cmt.ticket";
    public const string CommentedOutCode = "cmt.commentedCode";
    public const string CyrillicComments = "cmt.cyrillic";
    public const string CyrillicPolishedComments = "cmt.cyrillicPolished";
    public const string LatinComments = "cmt.latin";
    public const string SentenceComments = "cmt.sentence";
    public const string LowercaseComments = "cmt.lowercase";

    // Именование
    public const string CtParams = "ct.params";
    public const string CtFullName = "ct.fullName";
    public const string CtShortName = "ct.shortName";
    public const string CtDefault = "ct.default";

    // Синтаксис
    public const string FileScopedNs = "syn.fileScopedNs";
    public const string BlockNs = "syn.blockNs";
    public const string NullEqualityChecks = "syn.nullEq";
    public const string NullPatternChecks = "syn.nullPattern";
    public const string ImplicitNew = "syn.implicitNew";
    public const string ExplicitNew = "syn.explicitNew";
    public const string CollectionExpressions = "syn.collectionExpr";
    public const string NewEmptyCollection = "syn.newEmptyCollection";
    public const string SwitchExpressions = "syn.switchExpr";
    public const string SwitchStatements = "syn.switchStmt";
    public const string Records = "syn.records";
    public const string RequiredMembers = "syn.required";
    public const string InitAccessors = "syn.init";
    public const string PrimaryCtorClasses = "syn.primaryCtor";
    public const string RawStrings = "syn.rawString";
    public const string ExpressionBodied = "syn.exprBodied";
    public const string PropertyPatterns = "syn.propertyPattern";
    public const string IsNullOrWhiteSpace = "api.isNullOrWhiteSpace";
    public const string IsNullOrEmpty = "api.isNullOrEmpty";
    public const string StringEmpty = "api.stringEmpty";
    public const string EmptyStringLiteral = "api.emptyLiteral";
    public const string StringComparisonUse = "api.stringComparison";
    public const string ToLowerInvariant = "api.toLowerInvariant";
    public const string ToLowerPlain = "api.toLowerPlain";
    public const string UtcNow = "api.utcNow";
    public const string LocalNow = "api.localNow";
    public const string Nameof = "api.nameof";
    public const string StringAutoProps = "prop.stringAuto";
    public const string StringEmptyInitializers = "prop.stringEmptyInit";
    public const string NullForgivingInit = "prop.nullForgivingInit";
    public const string ReadOnlyCollections = "api.readOnlyCollections";
    public const string MutableCollectionsInSignatures = "api.mutableCollections";
    public const string VarDeclarations = "syn.var";
    public const string ExplicitDeclarations = "syn.explicitType";

    // Защитный код
    public const string GuardableMembers = "guard.members";
    public const string GuardThrowIfHelpers = "guard.throwIf";
    public const string GuardCoalesceThrow = "guard.coalesceThrow";
    public const string GuardIfThrow = "guard.ifThrow";
    public const string GuardedMembers = "guard.guardedMembers";

    // Async
    public const string AsyncMethods = "async.methods";
    public const string AsyncWithCt = "async.withCt";
    public const string AsyncSuffix = "async.suffix";
    public const string AwaitsInCtMethods = "async.awaitsInCt";
    public const string AwaitsPassingCt = "async.awaitsPassingCt";
    public const string ConfigureAwaitFalse = "async.configureAwait";
    public const string SyncOverAsync = "async.syncOverAsync";
    public const string AsyncVoid = "async.void";

    // Логирование и исключения
    public const string LogCalls = "log.calls";
    public const string LogTemplated = "log.templated";
    public const string LogInterpolated = "log.interpolated";
    public const string LogPlain = "log.plain";
    public const string ThrowMessages = "exc.messages";
    public const string ThrowMessageSentence = "exc.sentence";
    public const string ThrowMessageQuoted = "exc.quoted";
    public const string ThrowMessageCyrillic = "exc.cyrillic";
    public const string ThrowMessageLowercase = "exc.lowercase";
    public const string ThrowWithNameof = "exc.nameof";
    public const string ThrowEx = "exc.throwEx";
    public const string Rethrow = "exc.rethrow";
    public const string CatchClauses = "catch.total";
    public const string CatchVarE = "catch.varE";
    public const string CatchVarEx = "catch.varEx";
    public const string EmptyCatch = "catch.empty";

    // Структура
    public const string ConcreteClasses = "cls.concrete";
    public const string SealedClasses = "cls.sealed";
    public const string PrivateFields = "fld.private";
    public const string ReadonlyFields = "fld.readonly";
    public const string UnderscoreFields = "fld.underscore";
    public const string Methods = "mth.count";
    public const string TypesOrderChecked = "ord.checked";
    public const string TypesOrdered = "ord.ordered";
    public const string Regions = "misc.region";
    public const string ConsoleWrites = "misc.console";
    public const string StringFormat = "misc.stringFormat";
    public const string StringConcat = "misc.stringConcat";
    public const string LinqQuerySyntax = "misc.linqQuery";
    public const string ThreadSleep = "misc.threadSleep";
    public const string Typos = "misc.typos";
    public const string TranslitIdentifiers = "name.translit";
    public const string HungarianNames = "name.hungarian";
    public const string CyrillicIdentifiers = "name.cyrillic";
    public const string EmojiInStrings = "misc.emoji";
    public const string ProducesResponseType = "web.producesResponseType";
    public const string ControllerActions = "web.actions";
    public const string OptionsSectionName = "misc.sectionName";
    public const string AsNoTracking = "ef.asNoTracking";
    public const string MagicNumbers = "misc.magicNumbers";
    public const string PublicFields = "fld.public";
    public const string MembersChecked = "acc.members";
    public const string ExplicitAccess = "acc.explicit";
    public const string NamedArguments = "syn.namedArgs";
    public const string ConstDeclarations = "syn.const";
    public const string GenericExceptionThrows = "exc.generic";
    public const string SpecificExceptionThrows = "exc.specific";
    public const string DeepNestedMethods = "mth.deepNesting";
    public const string ReturnsChecked = "fmt.returns";
    public const string ReturnsWithBlankLine = "fmt.blankBeforeReturn";
    public const string DocExceptionTags = "doc.exceptionTags";

    // Форматирование
    public const string TrailingWhitespace = "fmt.trailingWs";
    public const string CommentNoSpace = "fmt.commentNoSpace";
    public const string KeywordNoSpace = "fmt.keywordNoSpace";
    public const string BraceSameLineNoSpace = "fmt.braceNoSpace";
    public const string CuddledElse = "fmt.cuddledElse";
    public const string DoubleBlankLines = "fmt.doubleBlank";
    public const string CommaNoSpace = "fmt.commaNoSpace";
    public const string LongLines = "fmt.longLines";
    public const string TabIndent = "fmt.tabs";
    public const string OperatorNoSpace = "fmt.operatorNoSpace";
    public const string KAndRBraces = "fmt.kAndR";

    // Тесты
    public const string TestMethods = "test.methods";
    public const string TestAaa = "test.aaa";
    public const string TestNamePattern = "test.namePattern";
    public const string TestNameShould = "test.nameShould";
    public const string TestNameLower = "test.nameLowercase";
    public const string TestTheory = "test.theory";
    public const string TestInlineData = "test.inlineData";
    public const string TestExampleData = "test.exampleData";
}
