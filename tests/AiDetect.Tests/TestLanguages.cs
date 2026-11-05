using AiDetect.Core.Languages;
using AiDetect.Languages.CSharp;

namespace AiDetect.Tests;

/// <summary>Реестр языков для тестов ядра и детектора C#.</summary>
internal static class TestLanguages
{
    public static LanguageRegistry Registry { get; } = new([new CSharpLanguage()]);
}
