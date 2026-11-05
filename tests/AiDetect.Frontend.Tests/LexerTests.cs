using AiDetect.Languages.Frontend.Parsing;

namespace AiDetect.Frontend.Tests;

public class LexerTests
{
    private static List<Token> Lex(string code, bool jsx = true, bool ts = true) => JsLexer.Tokenize(code, jsx, ts);

    [Fact]
    public void RegexVersusDivision()
    {
        var t = Lex("const a = b / c / d;\nconst r = /ab+c/gi.test(s);\nif (x) return /x/.exec(y);", jsx: false);
        Assert.Equal(2, t.Count(x => x.Kind == TokenKind.Regex));
        Assert.Equal(2, t.Count(x => x.Kind == TokenKind.Punct && x.Text == "/"));
        Assert.Contains(t, x => x.Kind == TokenKind.Regex && x.Text == "/ab+c/gi");
    }

    [Fact]
    public void TemplateLiteralsWithNestedExpressions()
    {
        var t = Lex("const s = `a ${b ? `x${c}` : '}'} d`; const n = 1;");
        Assert.Equal(2, t.Count(x => x.Kind == TokenKind.Template));
        Assert.True(t.First(x => x.Kind == TokenKind.Template).HasSubstitutions);
        Assert.Contains(t, x => x.Kind == TokenKind.Identifier && x.Text == "c");
        Assert.Contains(t, x => x.Kind == TokenKind.Identifier && x.Text == "n");
    }

    [Fact]
    public void JsxElementsAttributesAndComments()
    {
        var code = """
            export function Card({ title }: CardProps) {
              return (
                <div className="card" data-testid="card">
                  {/* Header */}
                  <h2 aria-label={title}>{title}</h2>
                  <button type="button" onClick={handleClick}>Save</button>
                  <>{items.map((item) => <Item key={item.id} {...item} />)}</>
                </div>
              );
            }
            """;
        var t = Lex(code);
        var opens = t.Where(x => x.Kind == TokenKind.JsxOpen).Select(x => x.Text).ToList();
        Assert.Equal(["div", "h2", "button", "", "Item"], opens);
        Assert.Contains(t, x => x.Kind == TokenKind.JsxAttr && x.Text == "data-testid" && x.Value == "card");
        Assert.Contains(t, x => x.Kind == TokenKind.JsxAttr && x.Text == "type" && x.Value == "button");
        Assert.Contains(t, x => x.Kind == TokenKind.JsxAttr && x.Text == "onClick" && x.ExprValue);
        Assert.Contains(t, x => x.IsComment && x.InJsx && x.Text.Contains("Header"));
        Assert.Contains(t, x => x.Kind == TokenKind.JsxText && x.Text == "Save");
        Assert.Contains(t, x => x.Kind == TokenKind.Identifier && x.Text == "handleClick");
    }

    [Fact]
    public void GenericsInTsxAreNotJsx()
    {
        var t = Lex("const id = <T,>(x: T) => x;\nconst f = <T extends object>(x: T): T => x;\nconst m = new Map<string, number>();");
        Assert.DoesNotContain(t, x => x.Kind == TokenKind.JsxOpen);
        Assert.Equal(2, t.Count(x => x.Kind == TokenKind.Punct && x.Text == "=>"));
    }

    [Fact]
    public void TypeAssertionsInTsFilesAreNotJsx()
    {
        var t = JsLexer.TokenizeFile("a.ts", "const x = <Foo>bar;\nconst y = a < b && c > d;");
        Assert.DoesNotContain(t, x => x.Kind == TokenKind.JsxOpen);
    }

    [Fact]
    public void ComparisonIsNotJsx()
    {
        var t = Lex("if (a < b) { c = d > e; }\nfor (let i = 0; i < n; i++) {}", ts: false);
        Assert.DoesNotContain(t, x => x.Kind == TokenKind.JsxOpen);
    }

    [Fact]
    public void UnterminatedConstructsDoNotThrow()
    {
        foreach (var code in new[] { "const s = 'abc", "const t = `abc ${x", "/* comment", "return <div><span>", "const r = /abc", "{{{{", "<", "a = <A b={" })
        {
            var t = Lex(code);
            Assert.NotNull(t);
        }
    }

    [Fact]
    public void LineNumbersAndOffset()
    {
        var t = JsLexer.Tokenize("const a = 1;\n\n// note\nlet b = 2;", false, false, lineOffset: 10);
        Assert.Equal(11, t[0].Line);
        Assert.Equal(13, t.First(x => x.IsComment).Line);
        Assert.Equal(14, t.First(x => x.Text == "let").Line);
    }

    [Fact]
    public void VueSfcBlocksWithNestedTemplates()
    {
        var sfc = """
            <template>
              <ul>
                <template v-for="item in items" :key="item.id">
                  <li>{{ item.name }}</li>
                </template>
              </ul>
            </template>

            <script setup lang="ts">
            const props = defineProps<{ items: Item[] }>();
            </script>

            <style scoped lang="scss">
            .list { color: red; }
            </style>
            """;
        var blocks = VueSfc.Parse(sfc);
        Assert.Equal(["template", "script", "style"], blocks.Select(b => b.Kind));
        Assert.Contains("v-for", blocks[0].Content);
        Assert.Equal("ts", blocks[1].Lang);
        Assert.True(blocks[1].Has("setup"));
        Assert.Equal(8, blocks[1].LineOffset);
        Assert.True(blocks[2].Has("scoped"));
    }

    [Fact]
    public void HtmlScannerReadsAngularAttributes()
    {
        var html = """
            <!-- Header -->
            <div class="list" *ngFor="let user of users; trackBy: trackById">
              <button type="button" (click)="onSelect(user)" [attr.aria-label]="user.name">{{ user.name | uppercase }}</button>
            </div>
            @if (loading) { <span>Loading</span> }
            """;
        var scan = HtmlScanner.Scan(html);
        Assert.Contains(scan.Comments, c => c.Text == "Header");
        var div = scan.Elements.First(e => e.Name == "div");
        Assert.Contains("trackBy", div.Attr("*ngFor")!.Value);
        var button = scan.Elements.First(e => e.Name == "button");
        Assert.Equal(3, button.Line);
        Assert.True(button.HasAttr("(click)"));
        Assert.Contains(scan.Interpolations, i => i.Text.Contains("uppercase"));
        Assert.Contains(scan.ControlFlow, c => c.Text == "@if");
    }

    [Fact]
    public void CssScannerFindsDeclarationsAndNesting()
    {
        var scss = """
            /* Card styles */
            .card {
              color: var(--text, #111);
              &__title { font-size: 1.25rem !important; }
              @media (prefers-reduced-motion: reduce) { transition: none; }
            }
            """;
        var scan = CssScanner.Scan(scss);
        Assert.Contains(scan.Comments, c => c.Text == "Card styles");
        Assert.Contains(scan.Declarations, d => d.Property == "color" && d.Value.StartsWith("var(--", StringComparison.Ordinal));
        Assert.Contains(scan.Declarations, d => d.Property == "font-size" && d.Depth == 2);
        Assert.Contains(scan.AtRules, a => a.Text.Contains("prefers-reduced-motion"));
        Assert.Equal(2, scan.MaxDepth);
    }
}
