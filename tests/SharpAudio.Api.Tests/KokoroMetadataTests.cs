using SharpAudio.Api.Services;

namespace SharpAudio.Api.Tests;

public sealed class KokoroMetadataTests
{
    [Theory]
    [InlineData("ja", "ja-jp")]
    [InlineData("j", "ja-jp")]
    [InlineData("en", "en-us")]
    [InlineData("b", "en-gb")]
    [InlineData("z", "zh-cn")]
    [InlineData("pt", "pt-br")]
    public void Normalizes_language_aliases(string input, string expected)
    {
        var ok = KokoroMetadata.TryNormalizeLanguage(input, out var actual);
        Assert.True(ok);
        Assert.Equal(expected, actual);
    }

    [Theory]
    [InlineData("ja-jp", "ja")]
    [InlineData("zh-cn", "zh")]
    [InlineData("en-gb", "en-gb")]
    public void Maps_normalized_languages_to_espeak_voices(string language, string expectedEspeak)
    {
        var ok = KokoroMetadata.TryMapToEspeakVoice(language, out var actual);
        Assert.True(ok);
        Assert.Equal(expectedEspeak, actual);
    }

    [Theory]
    [InlineData("alloy", "am_v0adam")]
    [InlineData("shimmer", "af_sky")]
    [InlineData("jf_alpha", "jf_alpha")]
    public void Normalizes_speaker_aliases_and_canonical_values(string input, string expected)
    {
        var ok = KokoroMetadata.TryNormalizeSpeaker(input, out var actual);
        Assert.True(ok);
        Assert.Equal(expected, actual);
    }
}
