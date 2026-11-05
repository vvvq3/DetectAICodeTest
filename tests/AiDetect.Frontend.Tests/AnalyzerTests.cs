using AiDetect.Core.Analysis;
using AiDetect.Core.Languages;
using AiDetect.Core.Model;
using AiDetect.Core.Preprocessing;
using AiDetect.Languages.CSharp;
using AiDetect.Languages.Frontend;

namespace AiDetect.Frontend.Tests;

public class AnalyzerTests
{
    private static readonly LanguageRegistry Registry = new([new CSharpLanguage(), new FrontendLanguage()]);

    private static FileChange NewFile(string path, string content) => new()
    {
        OldPath = path, NewPath = path, IsNew = true, NewContent = content,
        Diff = "@@ -0,0 +1," + content.Split('\n').Length + " @@\n" + string.Join("\n", content.Split('\n').Select(l => "+" + l)),
    };

    private static MergeRequestData AiMr() => new()
    {
        Title = "Add UserList component with tests",
        Description = "## Summary\n- Add `UserList` component\n- Add tests\n\n## Test plan\n- [x] unit tests",
        SourceBranch = "feature/user-list",
        Commits =
        [
            new CommitInfo { Sha = "a1", Title = "Add UserList component", Message = "Add UserList component\n\n- fetch users with abort\n- loading and error states" },
            new CommitInfo { Sha = "a2", Title = "Add tests for UserList", Message = "Add tests for UserList" },
        ],
        Changes =
        [
            NewFile("src/components/UserList.tsx", ExtractorTests.AiReact),
            NewFile("src/components/UserList.test.tsx", """
                import { render, screen } from '@testing-library/react';
                import userEvent from '@testing-library/user-event';
                import { UserList } from './UserList';

                describe('UserList', () => {
                  beforeEach(() => {
                    jest.clearAllMocks();
                  });

                  it('should render users', async () => {
                    // Arrange
                    global.fetch = jest.fn().mockResolvedValue({ ok: true, json: async () => [{ id: '1', name: 'John Doe' }] });
                    // Act
                    render(<UserList endpoint="/api/users" />);
                    // Assert
                    expect(await screen.findByRole('button', { name: /john doe/i })).toBeInTheDocument();
                  });

                  it('should show error message when request fails', async () => {
                    global.fetch = jest.fn().mockResolvedValue({ ok: false, status: 500 });
                    render(<UserList endpoint="/api/users" />);
                    expect(await screen.findByRole('alert')).toHaveTextContent('HTTP error! status: 500');
                  });

                  it.each([['Alice'], ['Bob']])('should call onSelect with %s', async (name) => {
                    const onSelect = jest.fn();
                    global.fetch = jest.fn().mockResolvedValue({ ok: true, json: async () => [{ id: '1', name }] });
                    render(<UserList endpoint="/api/users" onSelect={onSelect} />);
                    await userEvent.click(await screen.findByRole('button', { name: new RegExp(name, 'i') }));
                    expect(onSelect).toHaveBeenCalledWith({ id: '1', name });
                  });
                });
                """),
        ],
        Repository = new RepositoryContext
        {
            ConfigFiles =
            [
                new SourceFile { Path = "package.json", Content = """{ "dependencies": { "react": "^17.0.1", "@reduxjs/toolkit": "^1.5.0" }, "devDependencies": { "typescript": "~4.1.3", "jest": "^26.6.0", "@testing-library/react": "^11.2.2" } }""" },
            ],
        },
    };

    private static MergeRequestData HumanMr() => new()
    {
        Title = "user list",
        Description = "",
        SourceBranch = "users",
        Commits =
        [
            new CommitInfo { Sha = "h1", Title = "user list", Message = "user list" },
            new CommitInfo { Sha = "h2", Title = "fix", Message = "fix" },
        ],
        Changes =
        [
            NewFile("src/components/UserList.js", ExtractorTests.HumanReact + "\n" + ExtractorTests.HumanReact.Replace("UserList", "UserList2")),
        ],
    };

    [Fact]
    public void AiStyleFrontendMrScoresHigh()
    {
        var report = new MergeRequestAnalyzer(Registry).Analyze(AiMr(), new AnalyzerOptions { Parameters = new ModelParameters(), Language = "frontend" });
        Assert.Equal("frontend", report.LanguageId);
        Assert.True(report.Probability > 0.85, $"p={report.Probability:0.00}");
        Assert.Contains("React 17", report.Stack);
        Assert.Contains("TypeScript 4", report.Stack);
        Assert.Contains("Redux Toolkit", report.Stack);
        Assert.Contains(report.Signals, s => s.Id == "async.effectCleanup" && s.Contribution > 0);
        Assert.Contains(report.Signals, s => s.Id == "err.instanceofMessage" && s.Contribution > 0);
    }

    [Fact]
    public void HumanStyleFrontendMrScoresLow()
    {
        var report = new MergeRequestAnalyzer(Registry).Analyze(HumanMr(), new AnalyzerOptions { Parameters = new ModelParameters(), Language = "js" });
        Assert.Equal("frontend", report.LanguageId);
        Assert.True(report.Probability < 0.25, $"p={report.Probability:0.00}");
        Assert.Contains(report.Signals, s => s.Id == "habits.markers" && s.Contribution < 0);
    }

    [Fact]
    public void AutoDetectsLanguageAndWarnsAboutOtherLanguage()
    {
        var mr = AiMr();
        mr.Changes.Add(NewFile("src/Api/UsersController.cs", "public class UsersController\n{\n    public int Get() => 1;\n}\n"));
        var report = new MergeRequestAnalyzer(Registry).Analyze(mr, new AnalyzerOptions { Parameters = new ModelParameters() });
        Assert.Equal("frontend", report.LanguageId);
        Assert.True(report.LanguageAuto);
        Assert.DoesNotContain(report.Warnings, w => w.Contains("C#-кода", StringComparison.Ordinal));

        var big = AiMr();
        big.Changes.Add(NewFile("src/Api/UsersController.cs", string.Join("\n", Enumerable.Range(0, 40).Select(i => $"// line {i}"))));
        var r2 = new MergeRequestAnalyzer(Registry).Analyze(big, new AnalyzerOptions { Parameters = new ModelParameters(), Language = "frontend" });
        Assert.Contains(r2.Warnings, w => w.Contains("C#-кода", StringComparison.Ordinal) && w.Contains("C# (.NET)", StringComparison.Ordinal));

        var cs = new MergeRequestAnalyzer(Registry).Analyze(big, new AnalyzerOptions { Parameters = new ModelParameters(), Language = "csharp" });
        Assert.Equal("csharp", cs.LanguageId);
        Assert.Contains(cs.Warnings, w => w.Contains("JS/TS-кода", StringComparison.Ordinal));
    }

    [Fact]
    public void RegistryResolvesAliases()
    {
        Assert.Equal("frontend", Registry.Get("ts").Id);
        Assert.Equal("frontend", Registry.Get("React").Id);
        Assert.Equal("csharp", Registry.Get("c#").Id);
        Assert.True(LanguageRegistry.IsAuto("авто"));
        Assert.Throws<ArgumentException>(() => Registry.Get("cobol"));
    }

    [Fact]
    public void EmptyAndTinyMrsDoNotCrash()
    {
        var analyzer = new MergeRequestAnalyzer(Registry);
        var empty = analyzer.Analyze(new MergeRequestData(), new AnalyzerOptions { Parameters = new ModelParameters(), Language = "frontend" });
        Assert.Contains("Недостаточно JS/TS-кода", empty.Verdict);
        var broken = new MergeRequestData { Changes = [NewFile("src/a.tsx", "export const A = () => <div><span>{x.map(i => <b>)}")] };
        var r = analyzer.Analyze(broken, new AnalyzerOptions { Parameters = new ModelParameters(), Language = "frontend" });
        Assert.InRange(r.Probability, 0, 1);
    }
}

public class ClassifierConventionsStackTests
{
    [Theory]
    [InlineData("dist/main.js", true)]
    [InlineData("src/app/app.component.ts", false)]
    [InlineData("src/vendor/jquery.js", true)]
    [InlineData("public/lib/chart.min.js", true)]
    [InlineData("src/types/global.d.ts", true)]
    [InlineData("src/__generated__/graphql.ts", true)]
    [InlineData("src/locales/ru.js", true)]
    [InlineData("package-lock.json", true)]
    [InlineData("src/components/Button.tsx", false)]
    public void GeneratedFiles(string path, bool expected) => Assert.Equal(expected, FrontendFileClassifier.IsGenerated(path, null));

    [Fact]
    public void AngularCliSkeletonSpecIsGenerated()
    {
        var spec = """
            describe('UsersComponent', () => {
              let component: UsersComponent;
              beforeEach(async () => { await TestBed.configureTestingModule({ declarations: [UsersComponent] }).compileComponents(); });
              it('should create', () => { expect(component).toBeTruthy(); });
            });
            """;
        Assert.True(FrontendFileClassifier.IsGenerated("src/app/users/users.component.spec.ts", spec));
        Assert.False(FrontendFileClassifier.IsGenerated("src/app/users/users.component.spec.ts", spec.Replace("should create", "should list users")));
    }

    [Theory]
    [InlineData("src/components/Button.test.tsx", Layer.Tests)]
    [InlineData("src/components/Button.stories.tsx", Layer.Stories)]
    [InlineData("src/hooks/useUsers.ts", Layer.Hook)]
    [InlineData("src/store/users.slice.ts", Layer.State)]
    [InlineData("src/app/users/users.service.ts", Layer.Service)]
    [InlineData("src/api/users.ts", Layer.ApiClient)]
    [InlineData("src/app/app-routing.module.ts", Layer.Routing)]
    [InlineData("src/app/users/users.component.html", Layer.Template)]
    [InlineData("src/styles/main.scss", Layer.Styles)]
    [InlineData("src/types/user.ts", Layer.Types)]
    [InlineData("src/utils/format.ts", Layer.Utils)]
    [InlineData("src/components/UserCard.vue", Layer.Component)]
    [InlineData("src/assets/i18n/en.json", Layer.I18n)]
    [InlineData("package.json", Layer.BuildProject)]
    public void Layers(string path, Layer expected) => Assert.Equal(expected, FrontendFileClassifier.ClassifyLayer(path, null));

    [Fact]
    public void ConventionsFromConfigs()
    {
        var repo = new RepositoryContext
        {
            ConfigFiles =
            [
                new SourceFile { Path = ".prettierrc", Content = "{ \"singleQuote\": true }" },
                new SourceFile { Path = ".eslintrc.json", Content = """{ "extends": ["airbnb", "plugin:@typescript-eslint/recommended"], "rules": { "@typescript-eslint/explicit-function-return-type": "error", "no-console": "off" } }""" },
                new SourceFile { Path = "tsconfig.json", Content = """{ "compilerOptions": { "strict": true } }""" },
                new SourceFile { Path = "package.json", Content = """{ "devDependencies": { "husky": "^4" }, "husky": { "hooks": {} } }""" },
            ],
        };
        var c = FrontendConventions.Detect(repo);
        Assert.True(c.Prettier);
        Assert.True(c.StrictTs);
        Assert.True(c.EnforcedOnCommit);
        Assert.Contains("eqeqeq", c.Rules);
        Assert.Contains("no-explicit-any", c.Rules);
        Assert.Contains("explicit-function-return-type", c.Rules);
        Assert.Equal(0, c.Factor("fmt.irregular"));
        Assert.Equal(0, c.Factor("syn.eqeq"));
        Assert.True(c.Factor("ts.returnTypes") < 0.3);
        Assert.Equal(1, c.Factor("async.effectCleanup"));
        Assert.True(c.Any);

        var tslint = FrontendConventions.Detect(new RepositoryContext { ConfigFiles = [new SourceFile { Path = "tslint.json", Content = """{ "rules": { "no-any": true, "triple-equals": true, "quotemark": [true, "single"] } }""" }] });
        Assert.Contains("no-explicit-any", tslint.Rules);
        Assert.Contains("eqeqeq", tslint.Rules);
        Assert.Contains("quotes", tslint.Rules);
        Assert.False(FrontendConventions.Detect(new RepositoryContext()).Any);
    }

    [Fact]
    public void StackFromNearestPackageJsonAndImports()
    {
        var mr = new MergeRequestData
        {
            Changes =
            [
                new FileChange { OldPath = "apps/web/src/app/x.component.ts", NewPath = "apps/web/src/app/x.component.ts", NewContent = "import { Component } from '@angular/core';\nimport { Store } from '@ngrx/store';" },
            ],
            Repository = new RepositoryContext
            {
                ConfigFiles =
                [
                    new SourceFile { Path = "package.json", Content = """{ "devDependencies": { "lerna": "^3" } }""" },
                    new SourceFile { Path = "apps/web/package.json", Content = """{ "dependencies": { "@angular/core": "~11.0.5", "rxjs": "~6.6.0", "@angular/material": "^11" }, "devDependencies": { "typescript": "~4.0.2", "karma": "~5.1.0", "jasmine-core": "~3.6.0" } }""" },
                ],
            },
        };
        var stack = FrontendStack.Detect(mr);
        Assert.Equal("TypeScript 4 · Angular 11 · NgRx · RxJS · Angular Material · Karma · Jasmine", stack.ToString());
        Assert.True(stack.Has("angular"));

        var csharp = new CSharpLanguage().DetectStack(new MergeRequestData
        {
            Repository = new RepositoryContext
            {
                ConfigFiles = [new SourceFile { Path = "src/Api/Api.csproj", Content = """<Project Sdk="Microsoft.NET.Sdk.Web"><PropertyGroup><TargetFramework>net8.0</TargetFramework></PropertyGroup><ItemGroup><PackageReference Include="Microsoft.EntityFrameworkCore.SqlServer" Version="8.0.0" /><PackageReference Include="MediatR" Version="12.0.0" /><PackageReference Include="xunit" Version="2.6.0" /></ItemGroup></Project>""" }],
            },
        });
        Assert.Equal("C# · .NET 8 · ASP.NET Core · EF Core · MediatR · SQL Server · xUnit", csharp.ToString());
    }
}
