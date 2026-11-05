using System.Text.Json;
using System.Text.RegularExpressions;
using AiDetect.Core.Languages;
using AiDetect.Core.Model;

namespace AiDetect.Languages.Frontend;

/// <summary>Стек фронтенд-проекта по package.json (ближайший к изменённым файлам и корневой), angular.json и импортам в коде.</summary>
public static partial class FrontendStack
{
    private static readonly (string Package, string Title, string Tag)[] Frameworks =
    [
        ("next", "Next.js", "nextjs"),
        ("@remix-run/react", "Remix", "remix"),
        ("gatsby", "Gatsby", "gatsby"),
        ("react", "React", "react"),
        ("@angular/core", "Angular", "angular"),
        ("angularjs", "AngularJS", "angularjs"),
        ("angular", "AngularJS", "angularjs"),
        ("nuxt", "Nuxt", "nuxt"),
        ("nuxt3", "Nuxt", "nuxt"),
        ("vue", "Vue", "vue"),
        ("svelte", "Svelte", "svelte"),
        ("solid-js", "Solid", "solid"),
        ("preact", "Preact", "preact"),
        ("@ionic/angular", "Ionic", "ionic"),
        ("react-native", "React Native", "reactnative"),
        ("electron", "Electron", "electron"),
        ("@nestjs/core", "NestJS", "nestjs"),
        ("express", "Express", "express"),
    ];

    private static readonly (string Package, string Title, string Tag)[] Libraries =
    [
        ("@reduxjs/toolkit", "Redux Toolkit", "redux"),
        ("redux", "Redux", "redux"),
        ("mobx", "MobX", "mobx"),
        ("zustand", "Zustand", "zustand"),
        ("recoil", "Recoil", "recoil"),
        ("@tanstack/react-query", "TanStack Query", "reactquery"),
        ("react-query", "React Query", "reactquery"),
        ("swr", "SWR", "swr"),
        ("@ngrx/store", "NgRx", "ngrx"),
        ("@ngxs/store", "NGXS", "ngxs"),
        ("rxjs", "RxJS", "rxjs"),
        ("pinia", "Pinia", "pinia"),
        ("vuex", "Vuex", "vuex"),
        ("react-router-dom", "React Router", "reactrouter"),
        ("react-router", "React Router", "reactrouter"),
        ("vue-router", "Vue Router", "vuerouter"),
        ("@apollo/client", "Apollo", "apollo"),
        ("apollo-client", "Apollo", "apollo"),
        ("graphql", "GraphQL", "graphql"),
        ("axios", "axios", "axios"),
        ("react-hook-form", "React Hook Form", "reacthookform"),
        ("formik", "Formik", "formik"),
        ("zod", "Zod", "zod"),
        ("yup", "Yup", "yup"),
        ("tailwindcss", "Tailwind CSS", "tailwind"),
        ("styled-components", "styled-components", "styledcomponents"),
        ("@emotion/react", "Emotion", "emotion"),
        ("@mui/material", "MUI", "mui"),
        ("@material-ui/core", "Material-UI", "mui"),
        ("@angular/material", "Angular Material", "angularmaterial"),
        ("antd", "Ant Design", "antd"),
        ("ng-zorro-antd", "NG-ZORRO", "antd"),
        ("primeng", "PrimeNG", "primeng"),
        ("primevue", "PrimeVue", "primevue"),
        ("vuetify", "Vuetify", "vuetify"),
        ("element-ui", "Element UI", "element"),
        ("element-plus", "Element Plus", "element"),
        ("bootstrap", "Bootstrap", "bootstrap"),
        ("@chakra-ui/react", "Chakra UI", "chakra"),
        ("sass", "SCSS", "scss"),
        ("node-sass", "SCSS", "scss"),
        ("less", "Less", "less"),
        ("@storybook/react", "Storybook", "storybook"),
        ("@storybook/angular", "Storybook", "storybook"),
        ("@storybook/vue3", "Storybook", "storybook"),
        ("@storybook/vue", "Storybook", "storybook"),
        ("storybook", "Storybook", "storybook"),
        ("i18next", "i18next", "i18n"),
        ("react-intl", "react-intl", "i18n"),
        ("vue-i18n", "vue-i18n", "i18n"),
        ("@ngx-translate/core", "ngx-translate", "i18n"),
    ];

    private static readonly (string Package, string Title, string Tag)[] Testing =
    [
        ("vitest", "Vitest", "vitest"),
        ("jest", "Jest", "jest"),
        ("karma", "Karma", "karma"),
        ("jasmine-core", "Jasmine", "jasmine"),
        ("mocha", "Mocha", "mocha"),
        ("@testing-library/react", "Testing Library", "testinglibrary"),
        ("@testing-library/vue", "Testing Library", "testinglibrary"),
        ("@testing-library/angular", "Testing Library", "testinglibrary"),
        ("enzyme", "Enzyme", "enzyme"),
        ("@vue/test-utils", "Vue Test Utils", "vuetestutils"),
        ("cypress", "Cypress", "cypress"),
        ("@playwright/test", "Playwright", "playwright"),
        ("protractor", "Protractor", "protractor"),
    ];

    [GeneratedRegex(@"(?:import\s+(?:[\w*{}\s,]+\s+from\s+)?|require\()\s*['""]([@\w./-]+)['""]")]
    private static partial Regex ImportRegex();

    public static StackInfo Detect(MergeRequestData mr)
    {
        var stack = new StackInfo();
        var deps = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var changedDirs = mr.Changes.Where(c => FrontendFileClassifier.IsSource(c.Path)).Select(c => (Path.GetDirectoryName(c.Path) ?? "").Replace('\\', '/')).ToList();

        // Ближайшие к изменённым файлам package.json — первыми, корневой — последним.
        var packages = mr.Repository.ConfigFiles
            .Where(f => Path.GetFileName(f.Path).Equals("package.json", StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(f => Relevance(f.Path, changedDirs))
            .ToList();
        foreach (var pkg in packages)
        {
            ReadDependencies(pkg.Content, deps);
        }

        // Импорты в изменённых файлах (если package.json нет или в нём не всё).
        var imports = new HashSet<string>(StringComparer.Ordinal);
        foreach (var c in mr.Changes.Where(c => FrontendFileClassifier.IsScript(c.Path) && c.NewContent is not null))
        {
            foreach (Match m in ImportRegex().Matches(c.NewContent!))
            {
                var spec = m.Groups[1].Value;
                if (spec.StartsWith('.') || spec.StartsWith('/'))
                {
                    continue;
                }

                var parts = spec.Split('/');
                imports.Add(spec.StartsWith('@') && parts.Length > 1 ? parts[0] + "/" + parts[1] : parts[0]);
            }
        }

        bool Has(string package) => deps.ContainsKey(package) || imports.Contains(package);
        string Ver(string package) => deps.TryGetValue(package, out var v) ? Major(v) : "";

        bool ts = Has("typescript") || mr.Changes.Any(c => c.Path.EndsWith(".ts", StringComparison.OrdinalIgnoreCase) || c.Path.EndsWith(".tsx", StringComparison.OrdinalIgnoreCase)) ||
                  mr.Changes.Any(c => c.Path.EndsWith(".vue", StringComparison.OrdinalIgnoreCase) && c.NewContent?.Contains("lang=\"ts\"", StringComparison.Ordinal) == true);
        stack.Add(ts ? Join("TypeScript", Ver("typescript")) : "JavaScript", ts ? "typescript" : "javascript");

        var seenTags = new HashSet<string>(StringComparer.Ordinal);
        foreach (var (package, title, tag) in Frameworks)
        {
            if (Has(package) && seenTags.Add(tag))
            {
                if (tag == "angularjs" && Has("@angular/core"))
                {
                    continue;
                }

                if (tag == "react" && Has("react-native"))
                {
                    continue;
                }

                stack.Add(Join(title, Ver(package)), tag);
            }
        }

        if (!stack.Has("vue") && mr.Changes.Any(c => c.Path.EndsWith(".vue", StringComparison.OrdinalIgnoreCase)))
        {
            stack.Add("Vue", "vue");
        }

        if (!stack.Has("angular") && mr.Repository.ConfigFiles.Any(f => Path.GetFileName(f.Path).Equals("angular.json", StringComparison.OrdinalIgnoreCase)))
        {
            stack.Add("Angular", "angular");
        }

        if (!stack.Has("react") && !stack.Has("vue") && !stack.Has("angular") &&
            mr.Changes.Any(c => c.Path.EndsWith(".tsx", StringComparison.OrdinalIgnoreCase) || c.Path.EndsWith(".jsx", StringComparison.OrdinalIgnoreCase)))
        {
            stack.Add("React", "react");
        }

        foreach (var (package, title, tag) in Libraries)
        {
            if (Has(package) && seenTags.Add(tag))
            {
                stack.Add(title, tag);
            }
        }

        if (!stack.Has("scss") && mr.Changes.Any(c => c.Path.EndsWith(".scss", StringComparison.OrdinalIgnoreCase)))
        {
            stack.Add("SCSS", "scss");
        }

        foreach (var (package, title, tag) in Testing)
        {
            if (Has(package) && seenTags.Add(tag))
            {
                stack.Add(title, tag);
            }
        }

        stack.Source = packages.Count > 0 ? "package.json" : imports.Count > 0 ? "импорты в коде" : "";
        return stack;
    }

    private static int Relevance(string packagePath, List<string> changedDirs)
    {
        var dir = (Path.GetDirectoryName(packagePath) ?? "").Replace('\\', '/');
        if (dir.Length == 0)
        {
            return 0;
        }

        return changedDirs.Count(d => d == dir || d.StartsWith(dir + "/", StringComparison.Ordinal)) > 0 ? dir.Length + 1 : -1;
    }

    private static void ReadDependencies(string json, Dictionary<string, string> deps)
    {
        try
        {
            using var doc = JsonDocument.Parse(json, new JsonDocumentOptions { AllowTrailingCommas = true, CommentHandling = JsonCommentHandling.Skip });
            foreach (var section in new[] { "dependencies", "devDependencies", "peerDependencies" })
            {
                if (doc.RootElement.ValueKind == JsonValueKind.Object && doc.RootElement.TryGetProperty(section, out var obj) && obj.ValueKind == JsonValueKind.Object)
                {
                    foreach (var p in obj.EnumerateObject())
                    {
                        deps.TryAdd(p.Name, p.Value.ValueKind == JsonValueKind.String ? p.Value.GetString() ?? "" : "");
                    }
                }
            }
        }
        catch (JsonException)
        {
            // Повреждённый package.json — стек по импортам.
        }
    }

    /// <summary>«^17.0.2» → «17», «~4.1.3» → «4.1» для TypeScript, «workspace:*» → «».</summary>
    private static string Major(string version)
    {
        var m = Regex.Match(version, @"(\d+)(?:\.(\d+))?");
        return m.Success ? m.Groups[1].Value : "";
    }

    private static string Join(string title, string version) => version.Length > 0 ? title + " " + version : title;
}
