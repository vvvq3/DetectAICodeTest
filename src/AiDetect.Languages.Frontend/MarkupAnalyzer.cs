using System.Text.RegularExpressions;
using AiDetect.Languages.Frontend.Parsing;

namespace AiDetect.Languages.Frontend;

/// <summary>Признаки HTML-шаблонов (Angular, Vue) и стилей (CSS/SCSS/LESS).</summary>
internal sealed partial class MarkupAnalyzer(Ctx c)
{
    [GeneratedRegex(@"^[A-ZА-ЯЁ][\p{L}\s/&-]{1,40}$")]
    private static partial Regex SectionTitle();

    [GeneratedRegex(@"rgba\(\s*0\s*,\s*0\s*,\s*0\s*,\s*0?\.(05|08|1|12|15|2)\s*\)|\b0\.2s ease|\b0\.15s ease|\b150ms\b|\b200ms\b|cubic-bezier\(0\.4,\s*0,\s*0\.2,\s*1\)|#(3b82f6|ef4444|10b981|6b7280|e5e7eb|6366f1|f3f4f6|111827|374151|9ca3af|d1d5db|1f2937|f9fafb|2563eb|dc2626|059669|4f46e5|f59e0b|8b5cf6)\b|-apple-system,\s*BlinkMacSystemFont", RegexOptions.IgnoreCase)]
    private static partial Regex LlmValues();

    [GeneratedRegex(@"-?\d*\.?\d+rem\b")]
    private static partial Regex Rem();

    [GeneratedRegex(@"-?\d*\.?\d+px\b")]
    private static partial Regex Px();

    public void Template(HtmlScan scan, bool isAngular)
    {
        foreach (var l in scan.Elements.Select(e => e.Line).Concat(scan.Texts.Select(t => t.Line)).Concat(scan.Interpolations.Select(t => t.Line)).Distinct())
        {
            if (c.In(l))
            {
                c.S.Add(FK.CodeLines);
                c.S.Add(FK.MarkupLines);
            }
        }

        foreach (var cm in scan.Comments.Where(x => c.In(x.Line)))
        {
            var body = cm.Text.Trim();
            if (FrontendText.IsCommentedCode(body) || body.StartsWith('<'))
            {
                c.Hit(FK.CommentedOutCode, cm.Line);
            }
            else if (SectionTitle().IsMatch(body) && body.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length <= 4)
            {
                c.Hit(FK.MarkupSection, cm.Line);
            }

            if (FrontendText.Todo().IsMatch(body))
            {
                c.Hit(FK.Todo, cm.Line);
            }
        }

        foreach (var t in scan.Texts.Where(x => c.In(x.Line) && x.Text.Count(char.IsLetter) >= 3))
        {
            c.Count(FK.HardcodedTexts, t.Line);
        }

        foreach (var cf in scan.ControlFlow.Where(x => c.In(x.Line)))
        {
            c.Count(FK.NgSignals, cf.Line);
        }

        foreach (var i in scan.Interpolations.Where(x => c.In(x.Line)))
        {
            if (Regex.IsMatch(i.Text, @"\|\s*async\b"))
            {
                c.Hit(FK.AsyncPipes, i.Line);
            }
        }

        foreach (var e in scan.Elements.Where(x => c.In(x.Line)))
        {
            var name = e.Name.ToLowerInvariant();
            c.Count(FK.Elements, e.Line);
            if (name == "button")
            {
                c.Count(FK.Buttons, e.Line);
                if (e.HasAttr("type") || e.HasAttr(":type") || e.HasAttr("[type]"))
                {
                    c.Hit(FK.ButtonsTyped, e.Line);
                }
            }

            if (name == "img")
            {
                c.Count(FK.Images, e.Line);
                if (e.Attrs.Any(a => a.Name is "alt" or ":alt" or "[alt]" or "[attr.alt]" && !string.IsNullOrEmpty(a.Value)))
                {
                    c.Count(FK.ImagesAlt, e.Line);
                }
            }

            if (name is "input" or "select" or "textarea")
            {
                c.Count(FK.Inputs, e.Line);
            }

            if (name == "label" && (e.HasAttr("for") || e.HasAttr("[for]") || e.HasAttr(":for") || e.HasAttr("[attr.for]")))
            {
                c.Hit(FK.LabelsFor, e.Line);
            }

            bool click = e.Attrs.Any(a => a.Name is "(click)" or "@click" or "v-on:click" or "@click.prevent" or "@click.stop");
            if (name is "div" or "span" or "li" or "td" && click && !e.HasAttr("role") && !e.Attrs.Any(a => a.Name.Contains("keydown", StringComparison.Ordinal) ||
                                                                                                        a.Name.Contains("keyup", StringComparison.Ordinal)))
            {
                c.Hit(FK.ClickOnDiv, e.Line);
            }

            bool vFor = false, vIf = false;
            foreach (var a in e.Attrs)
            {
                var an = a.Name;
                var bare = an.Trim('[', ']', '(', ')').Replace("attr.", "", StringComparison.Ordinal).TrimStart(':');
                if (bare.StartsWith("aria-", StringComparison.Ordinal))
                {
                    c.Hit(FK.AriaAttrs, a.Line);
                }
                else if (bare == "role")
                {
                    c.Count(FK.RoleAttrs, a.Line);
                }
                else if (bare is "data-testid" or "data-test" or "data-cy" or "data-test-id" or "data-qa")
                {
                    c.Hit(FK.TestIds, a.Line);
                }
                if (a.Value is { } av && Regex.IsMatch(av, @"\|\s*async\b"))
                {
                    c.Hit(FK.AsyncPipes, a.Line);
                }

                if (an == "*ngFor")
                {
                    c.Count(FK.NgFor, a.Line);
                    if (a.Value?.Contains("trackBy", StringComparison.Ordinal) == true)
                    {
                        c.Hit(FK.NgForTrackBy, a.Line);
                    }
                }
                else if (an == "v-for")
                {
                    vFor = true;
                    c.Count(FK.VueFor, a.Line);
                    if (e.HasAttr(":key") || e.HasAttr("v-bind:key") || e.HasAttr("key"))
                    {
                        c.Count(FK.VueForKeyed, a.Line);
                    }

                    var key = e.Attr(":key")?.Value ?? e.Attr("v-bind:key")?.Value;
                    if (key is not null && Regex.IsMatch(key.Trim(), @"^(index|i|idx|ind|j)$"))
                    {
                        c.Hit(FK.VueForKeyIndex, a.Line);
                    }
                }
                else if (an is "v-if")
                {
                    vIf = true;
                }
                else if (an is "style" && !string.IsNullOrEmpty(a.Value) || an is "[style]" or ":style" or "[ngStyle]")
                {
                    c.Hit(FK.InlineStyles, a.Line);
                }
                else if (an is "(click)" or "@click" or "(change)" or "@change" or "(submit)" or "@submit" or "(ngSubmit)" or "@input" or "(input)")
                {
                    var handler = a.Value?.Trim() ?? "";
                    var m = Regex.Match(handler, @"^([A-Za-z_$][\w$]*)");
                    if (m.Success)
                    {
                        c.Count(FK.HandlerAttrs, a.Line);
                        if (m.Value.StartsWith("handle", StringComparison.Ordinal))
                        {
                            c.Count(FK.HandlerAttrsHandle, a.Line);
                        }
                    }
                }
            }

            if (vFor && vIf)
            {
                c.Hit(FK.VueIfWithFor, e.Line);
            }
        }
    }

    public void Style(CssScan scan, string content, int lineOffset)
    {
        var lines = new HashSet<int>(scan.Declarations.Select(d => d.Line).Concat(scan.Rules.Select(r => r.Line)).Concat(scan.AtRules.Select(a => a.Line)));
        foreach (var l in lines.Where(c.In))
        {
            c.S.Add(FK.CodeLines);
            c.S.Add(FK.StyleLines);
        }

        foreach (var r in scan.Rules.Where(r => c.In(r.Line)))
        {
            c.Count(FK.CssRules, r.Line);
            if (r.Depth >= 4)
            {
                c.Hit(FK.CssDeepNesting, r.Line);
            }

            if (r.Selector.Contains(":focus-visible", StringComparison.Ordinal))
            {
                c.Hit(FK.CssFocusVisible, r.Line);
            }
        }

        foreach (var a in scan.AtRules.Where(a => c.In(a.Line)))
        {
            if (a.Text.Contains("prefers-reduced-motion", StringComparison.Ordinal))
            {
                c.Hit(FK.CssReducedMotion, a.Line);
            }
        }

        foreach (var cm in scan.Comments.Where(x => c.In(x.Line)))
        {
            var body = cm.Text.Trim().Trim('=', '-', '*', ' ');
            if (FrontendText.IsCommentedCode(body) || Regex.IsMatch(body, @"^[\w-]+\s*:\s*[^;]+;$"))
            {
                c.Hit(FK.CommentedOutCode, cm.Line);
            }
            else if (SectionTitle().IsMatch(body) && body.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length <= 5)
            {
                c.Hit(FK.CssSection, cm.Line);
            }

            if (FrontendText.Todo().IsMatch(body))
            {
                c.Hit(FK.Todo, cm.Line);
            }
        }

        foreach (var d in scan.Declarations.Where(d => c.In(d.Line)))
        {
            c.Count(FK.CssDecls, d.Line);
            var p = d.Property.ToLowerInvariant();
            var v = d.Value;
            if (p.StartsWith("--", StringComparison.Ordinal))
            {
                c.Count(FK.CssCustomProps, d.Line);
            }

            if (v.Contains("var(--", StringComparison.Ordinal))
            {
                c.Count(FK.CssVars, d.Line);
            }

            if (v.Contains("!important", StringComparison.OrdinalIgnoreCase))
            {
                c.Hit(FK.CssImportant, d.Line);
            }

            if (p == "z-index" && int.TryParse(v.Replace("!important", "").Trim(), out var z) && z >= 999)
            {
                c.Hit(FK.CssHugeZ, d.Line);
            }

            if (p.StartsWith("-webkit-", StringComparison.Ordinal) || p.StartsWith("-moz-", StringComparison.Ordinal) || p.StartsWith("-ms-", StringComparison.Ordinal))
            {
                c.Hit(FK.CssVendor, d.Line);
            }

            if (p is "outline" && v.Trim() is "none" or "0")
            {
                c.Hit(FK.CssOutlineNone, d.Line);
            }

            if (p.StartsWith("transition", StringComparison.Ordinal))
            {
                c.Count(FK.CssTransitions, d.Line);
            }

            if (LlmValues().IsMatch(v) || p == "font-family" && LlmValues().IsMatch(v))
            {
                c.Hit(FK.CssLlmValues, d.Line);
            }

            c.Count(FK.CssRem, d.Line, Rem().Matches(v).Count);
            c.Count(FK.CssPx, d.Line, Px().Matches(v).Count);
        }

        // Пробелы в конце строк и прочее форматирование — для стилей только хвостовые пробелы.
        var all = content.Split('\n');
        for (int i = 0; i < all.Length; i++)
        {
            int line = i + 1 + lineOffset;
            var text = all[i].TrimEnd('\r');
            if (text.Length > 0 && text[^1] is ' ' or '\t' && text.Trim().Length > 0 && c.In(line))
            {
                c.Hit(FK.TrailingWhitespace, line);
            }
        }
    }
}
