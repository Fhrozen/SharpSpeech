using SharpAudio.Api.Services;

namespace SharpAudio.Api.Tests;

public sealed class NemotronLanguagesTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("auto")]
    [InlineData("not-a-real-language")]
    public void Resolve_defaults_to_auto_detect(string? language)
    {
        Assert.Equal(101, NemotronLanguages.Resolve(language));
    }

    [Theory]
    [InlineData("en", 0)]
    [InlineData("en-US", 0)]
    [InlineData("ja", 10)]
    [InlineData("EN", 0)]
    public void Resolve_returns_known_language_ids(string language, long expectedId)
    {
        Assert.Equal(expectedId, NemotronLanguages.Resolve(language));
    }
}
