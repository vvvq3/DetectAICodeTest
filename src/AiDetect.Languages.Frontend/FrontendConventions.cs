using System.Text.RegularExpressions;
using AiDetect.Core.Analysis;
using AiDetect.Core.Model;

namespace AiDetect.Languages.Frontend;

/// <summary>
/// Конвенции фронтенд-проекта, найденные лексически в конфигах (без запуска Node.js): Prettier/Biome, правила ESLint и TSLint
/// (включая популярные пресеты), строгий tsconfig, stylelint, eslint-plugin-jsdoc, husky/lint-staged. Каждое правило, делающее
/// «ИИ-подобную» черту обязательной для всех, ослабляет соответствующий признак — как StyleCop для C#.
/// </summary>
public sealed partial class FrontendConventions : ProjectConventions
{
    public bool Prettier { get; private init; }
    public bool StrictTs { get; private init; }
    public bool Stylelint { get; private init; }
    public bool JsDocRequired { get; private init; }
    public bool EnforcedOnCommit { get; private init; }
    public bool EditorConfigTrim { get; private init; }
    public HashSet<string> Rules { get; private init; } = new(StringComparer.Ordinal);

    public override bool Any => base.Any || Prettier || StrictTs || Rules.Count > 0 || Stylelint || JsDocRequired;

    /// <summary>Пресет ESLint → правила, которые он включает (только относящиеся к нашим признакам).</summary>
    private static readonly (string Preset, string[] Rules)[] Presets =
    [
        ("airbnb", ["eqeqeq", "no-var", "prefer-const", "prefer-template", "radix", "no-console", "button-has-type", "no-array-index-key", "function-component-definition", "jsx-a11y", "no-param-reassign", "no-nested-ternary"]),
        ("standard", ["eqeqeq", "no-var", "prefer-const"]),
        ("@typescript-eslint/recommended", ["no-explicit-any", "ban-ts-comment", "no-var", "prefer-const"]),
        ("@typescript-eslint/strict", ["no-explicit-any", "no-non-null-assertion", "ban-ts-comment", "prefer-optional-chain", "prefer-nullish-coalescing"]),
        ("@typescript-eslint/stylistic", ["consistent-type-definitions", "array-type", "prefer-optional-chain"]),
        ("jsx-a11y/recommended", ["jsx-a11y"]),
        ("jsx-a11y/strict", ["jsx-a11y"]),
        ("@angular-eslint/recommended", ["use-lifecycle-interface", "no-empty-lifecycle-method"]),
        ("@angular-eslint/template/accessibility", ["jsx-a11y"]),
        ("vue/vue3-recommended", ["require-v-for-key", "no-use-v-if-with-v-for"]),
        ("vue/recommended", ["require-v-for-key", "no-use-v-if-with-v-for"]),
        ("vue/vue3-essential", ["require-v-for-key", "no-use-v-if-with-v-for"]),
        ("vue/essential", ["require-v-for-key", "no-use-v-if-with-v-for"]),
        ("testing-library/react", ["prefer-screen-queries", "prefer-user-event"]),
        ("jest/recommended", ["valid-title"]),
        ("react-app", ["eqeqeq", "no-var"]),
        ("next/core-web-vitals", ["eqeqeq"]),
        ("prettier", ["prettier"]),
    ];

    /// <summary>Имена правил, которые ищем в конфигах (без префикса плагина).</summary>
    private static readonly string[] KnownRules =
    [
        "no-explicit-any", "explicit-function-return-type", "explicit-module-boundary-types", "no-non-null-assertion", "ban-ts-comment",
        "prefer-nullish-coalescing", "prefer-optional-chain", "consistent-type-imports", "naming-convention", "eqeqeq", "no-var", "prefer-const",
        "prefer-template", "radix", "no-console", "no-debugger", "no-alert", "button-has-type", "no-array-index-key", "function-component-definition",
        "prefer-on-push-component-change-detection", "use-track-by-function", "prefer-standalone", "prefer-inject", "define-props-declaration",
        "define-emits-declaration", "require-v-for-key", "no-use-v-if-with-v-for", "valid-title", "prefer-each", "prefer-screen-queries", "prefer-user-event",
        "no-unused-vars", "no-unused-imports", "require-jsdoc", "curly", "brace-style", "quotes", "semi", "indent", "no-trailing-spaces", "no-multiple-empty-lines",
        "max-len", "exhaustive-deps", "no-empty", "prefer-arrow-callback", "comma-dangle",
        // TSLint
        "no-any", "triple-equals", "no-var-keyword", "typedef", "no-non-null-assertion", "quotemark", "semicolon", "no-trailing-whitespace", "whitespace",
        "no-consecutive-blank-lines", "max-line-length", "no-console", "member-access", "use-life-cycle-interface", "prefer-on-push",
        "template-use-track-by-function", "variable-name", "no-unused-variable", "no-empty", "only-arrow-functions",
    ];

    [GeneratedRegex(@"[""']?(?:[@\w/-]+/)?([a-z][\w-]+)[""']?\s*:\s*\[?\s*(?:[""']?(error|warn|warning)[""']?|[12]|true)\b")]
    private static partial Regex RuleRegex();

    public static new FrontendConventions Detect(RepositoryContext repo)
    {
        bool prettier = false, strict = false, stylelint = false, jsdoc = false, enforced = false, trim = false;
        var rules = new HashSet<string>(StringComparer.Ordinal);
        foreach (var f in repo.ConfigFiles)
        {
            var name = Path.GetFileName(f.Path).ToLowerInvariant();
            var c = f.Content;
            if (name.StartsWith(".prettierrc", StringComparison.Ordinal) || name.StartsWith("prettier.config", StringComparison.Ordinal) || name is "biome.json" or "biome.jsonc" or "dprint.json" or ".dprint.json")
            {
                prettier = true;
            }
            else if (name.StartsWith(".stylelintrc", StringComparison.Ordinal) || name.StartsWith("stylelint.config", StringComparison.Ordinal))
            {
                stylelint = true;
            }
            else if (name.StartsWith("tsconfig", StringComparison.Ordinal) && name.EndsWith(".json", StringComparison.Ordinal))
            {
                if (Regex.IsMatch(c, @"""strict""\s*:\s*true") || Regex.IsMatch(c, @"""noImplicitAny""\s*:\s*true"))
                {
                    strict = true;
                }

                if (Regex.IsMatch(c, @"""noUnusedLocals""\s*:\s*true"))
                {
                    rules.Add("no-unused-vars");
                }
            }
            else if (name.StartsWith(".eslintrc", StringComparison.Ordinal) || name.StartsWith("eslint.config", StringComparison.Ordinal) || name is "tslint.json" or ".tslintrc")
            {
                AddRules(c, rules);
            }
            else if (name == "package.json")
            {
                if (Regex.IsMatch(c, @"""prettier""\s*:"))
                {
                    prettier |= Regex.IsMatch(c, @"""prettier""\s*:\s*(""[^""]+""|\{)");
                }

                if (Regex.IsMatch(c, @"""eslintConfig""\s*:"))
                {
                    var start = c.IndexOf("\"eslintConfig\"", StringComparison.Ordinal);
                    AddRules(c[start..], rules);
                }

                if (Regex.IsMatch(c, @"""(husky|lint-staged|simple-git-hooks|lefthook)""\s*:"))
                {
                    enforced = true;
                }

                if (Regex.IsMatch(c, @"""(eslint-plugin-jsdoc|typedoc|@compodoc/compodoc)""\s*:"))
                {
                    jsdoc = true;
                }

                if (Regex.IsMatch(c, @"""stylelint""\s*:"))
                {
                    stylelint = true;
                }
            }
            else if (name is ".lintstagedrc" or ".lintstagedrc.json" or "lint-staged.config.js" or ".huskyrc" or ".huskyrc.json" || f.Path.Replace('\\', '/').Contains(".husky/", StringComparison.Ordinal))
            {
                enforced = true;
            }
            else if (name == ".editorconfig" && Regex.IsMatch(c, @"trim_trailing_whitespace\s*=\s*true", RegexOptions.IgnoreCase))
            {
                trim = true;
            }
        }

        if (rules.Contains("prettier"))
        {
            prettier = true;
        }

        if (rules.Contains("require-jsdoc"))
        {
            jsdoc = true;
        }

        var (conventional, headings) = DetectCommon(repo);
        foreach (var f in repo.ConfigFiles.Where(f => Path.GetFileName(f.Path).Equals("package.json", StringComparison.OrdinalIgnoreCase)))
        {
            if (Regex.IsMatch(f.Content, @"""(@commitlint/[a-z-]+|commitizen|cz-conventional-changelog|semantic-release|standard-version)""\s*:"))
            {
                conventional = true;
            }
        }

        var notes = new List<string>();
        if (prettier)
        {
            notes.Add("Prettier");
        }

        if (rules.Count > 0)
        {
            notes.Add($"ESLint/TSLint ({rules.Count(r => r != "prettier")} правил, влияющих на признаки)");
        }

        if (strict)
        {
            notes.Add("строгий TypeScript");
        }

        if (stylelint)
        {
            notes.Add("stylelint");
        }

        if (jsdoc)
        {
            notes.Add("обязательный JSDoc");
        }

        if (enforced)
        {
            notes.Add("проверки при коммите (husky/lint-staged)");
        }

        AddCommonNotes(notes, conventional, headings);
        return new FrontendConventions
        {
            Prettier = prettier,
            StrictTs = strict,
            Stylelint = stylelint,
            JsDocRequired = jsdoc,
            EnforcedOnCommit = enforced,
            EditorConfigTrim = trim,
            Rules = rules,
            ConventionalCommits = conventional,
            MrTemplateHeadings = headings,
            Notes = notes,
        };
    }

    private static void AddRules(string config, HashSet<string> rules)
    {
        foreach (Match m in RuleRegex().Matches(config))
        {
            var rule = m.Groups[1].Value;
            if (KnownRules.Contains(rule))
            {
                rules.Add(Canonical(rule));
            }
        }

        foreach (var (preset, presetRules) in Presets)
        {
            if (Regex.IsMatch(config, @"[""'](plugin:)?" + Regex.Escape(preset) + @"[""'/]") || preset == "airbnb" && Regex.IsMatch(config, @"[""']airbnb(-base|-typescript)?[""']"))
            {
                foreach (var r in presetRules)
                {
                    rules.Add(r);
                }
            }
        }

        if (Regex.IsMatch(config, @"jsx-a11y"))
        {
            rules.Add("jsx-a11y");
        }
    }

    /// <summary>Правила TSLint → имена ESLint.</summary>
    private static string Canonical(string rule) => rule switch
    {
        "no-any" => "no-explicit-any",
        "triple-equals" => "eqeqeq",
        "no-var-keyword" => "no-var",
        "typedef" => "explicit-function-return-type",
        "quotemark" => "quotes",
        "semicolon" => "semi",
        "no-trailing-whitespace" => "no-trailing-spaces",
        "no-consecutive-blank-lines" => "no-multiple-empty-lines",
        "max-line-length" => "max-len",
        "use-life-cycle-interface" => "use-lifecycle-interface",
        "prefer-on-push" => "prefer-on-push-component-change-detection",
        "template-use-track-by-function" => "use-track-by-function",
        "variable-name" => "naming-convention",
        "no-unused-variable" or "no-unused-imports" => "no-unused-vars",
        _ => rule,
    };

    private bool R(params string[] names) => names.Any(Rules.Contains);

    public override double Factor(string id)
    {
        double f = base.Factor(id);
        double strictness = EnforcedOnCommit ? 1.0 : 0.85;
        double Off(double factor) => factor * strictness + (1 - strictness) * Math.Max(factor, 0.5);

        if (Prettier && id is "fmt.irregular" or "fmt.mixedStyle")
        {
            f *= 0;
        }

        if (!Prettier && R("quotes", "semi", "indent", "no-trailing-spaces", "no-multiple-empty-lines", "curly", "brace-style", "max-len") && id is "fmt.irregular" or "fmt.mixedStyle")
        {
            f *= Off(0.4);
        }

        if (EditorConfigTrim && id == "fmt.irregular")
        {
            f *= 0.7;
        }

        f *= id switch
        {
            "ts.returnTypes" when R("explicit-function-return-type", "explicit-module-boundary-types") => Off(0.2),
            "ts.any" when R("no-explicit-any") => Off(0.3),
            "ts.nonNull" when R("no-non-null-assertion") => Off(0.3),
            "ts.tsIgnore" when R("ban-ts-comment") => Off(0.3),
            "syn.nullish" or "syn.optChain" when R("prefer-nullish-coalescing", "prefer-optional-chain") => Off(0.3),
            "ts.importType" when R("consistent-type-imports") => 0,
            "name.iPrefix" or "name.underscore" or "name.screaming" when R("naming-convention") => Off(0.3),
            "syn.eqeq" when R("eqeqeq") => 0,
            "syn.var" when R("no-var", "prefer-const") => Off(0.2),
            "syn.concat" when R("prefer-template") => Off(0.2),
            "syn.radix" when R("radix") => 0,
            "log.consoleLog" when R("no-console") => Off(0.5),
            "a11y.buttonType" when R("button-has-type") => Off(0.2),
            "react.keyIndex" when R("no-array-index-key") => Off(0.3),
            "react.fcType" when R("function-component-definition") => Off(0.3),
            "a11y.aria" or "a11y.labelFor" or "a11y.altText" or "a11y.divClick" when R("jsx-a11y") => Off(0.3),
            "ng.onPush" when R("prefer-on-push-component-change-detection") => Off(0.2),
            "ng.trackBy" when R("use-track-by-function") => Off(0.2),
            "ng.modernApis" when R("prefer-standalone", "prefer-inject") => Off(0.2),
            "vue.propsTyped" when R("define-props-declaration") => Off(0.2),
            "vue.emitsTyped" when R("define-emits-declaration") => Off(0.2),
            "vue.vFor" when R("require-v-for-key", "no-use-v-if-with-v-for") => Off(0.4),
            "test.should" or "test.each" when R("valid-title", "prefer-each") => Off(0.3),
            "test.roleQueries" or "test.userEvent" when R("prefer-screen-queries", "prefer-user-event") => Off(0.3),
            "async.effectCleanup" or "async.depsDisable" when R("exhaustive-deps") => 1.0,
            "doc.coverage" or "doc.template" or "doc.propsMembers" when JsDocRequired => 0.2,
            "css.important" or "css.nesting" or "css.vars" or "css.llmValues" when Stylelint => 0.5,
            "ts.genericState" when StrictTs => 0.6,
            "err.instanceofMessage" when StrictTs => 0.7,
            "habits.markers" when R("no-debugger", "no-alert", "no-console") => 0.85,
            _ => 1.0,
        };
        return f;
    }

    /// <summary>Конфиги фронтенда, которые стоит загрузить из корня репозитория или пакета.</summary>
    public static bool IsConfigFile(string fileName)
    {
        var n = fileName.ToLowerInvariant();
        return n is "package.json" or "angular.json" or "tslint.json" or "biome.json" or "biome.jsonc" or ".browserslistrc" or "nx.json" or "components.json" or
                   ".lintstagedrc" or ".lintstagedrc.json" or ".huskyrc" or ".huskyrc.json" or "lint-staged.config.js" or "vue.config.js" or "nuxt.config.ts" or
                   "nuxt.config.js" or "next.config.js" or "next.config.mjs" or "next.config.ts" or "vite.config.ts" or "vite.config.js" or "jest.config.js" or
                   "jest.config.ts" or "vitest.config.ts" or "karma.conf.js" or "tailwind.config.js" or "tailwind.config.ts" ||
               n.StartsWith("tsconfig", StringComparison.Ordinal) && n.EndsWith(".json", StringComparison.Ordinal) ||
               n.StartsWith(".eslintrc", StringComparison.Ordinal) || n.StartsWith("eslint.config", StringComparison.Ordinal) ||
               n.StartsWith(".prettierrc", StringComparison.Ordinal) || n.StartsWith("prettier.config", StringComparison.Ordinal) ||
               n.StartsWith(".stylelintrc", StringComparison.Ordinal) || n.StartsWith("stylelint.config", StringComparison.Ordinal);
    }
}
