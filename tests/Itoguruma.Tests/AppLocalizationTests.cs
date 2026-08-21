using Itoguruma.Core;
using Xunit;

namespace Itoguruma.Tests;

[Collection("Localization")]
public sealed class AppLocalizationTests
{
    [Theory]
    [InlineData("en", "English")]
    [InlineData("ja", "日本語")]
    [InlineData("unsupported", "English")]
    public void TextUsesConfiguredLanguage(string language, string expected)
    {
        try
        {
            AppLocalization.Configure(language);
            Assert.Equal(expected, AppLocalization.Text("English", "日本語"));
        }
        finally
        {
            AppLocalization.Configure("en");
        }
    }
}

[CollectionDefinition("Localization", DisableParallelization = true)]
public sealed class LocalizationCollection;
