using AiDetect.Core.Languages;
using AiDetect.Languages.CSharp;

namespace AiDetect.Cli;

/// <summary>Языки, которые знает программа. Новый язык — новая строка здесь (порядок = порядок в списке на странице).</summary>
public static class AppLanguages
{
    public static LanguageRegistry Registry { get; } = new([
        new CSharpLanguage(),
    ]);
}
