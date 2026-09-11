using FastTTSR.Api.Services;

namespace FastTTSR.Api.Tests;

public sealed class ModelCatalogTests
{
    [Fact]
    public void Supports_expected_models()
    {
        var catalog = new ModelCatalog();

        Assert.True(catalog.TryGetModel("kokoro-q4", out _));
        Assert.True(catalog.TryGetModel("kokoro-full", out _));
    }

    [Fact]
    public void Kokoro_models_expose_canonical_languages_and_speakers()
    {
        var catalog = new ModelCatalog();

        Assert.True(catalog.TryGetModel("kokoro-q4", out var q4));
        Assert.Equal("voices", q4!.VoicesPath);
        Assert.Equal(".bin", q4.VoiceFileExtension);
        Assert.False(string.IsNullOrWhiteSpace(q4.VoicesBaseUrl));
        Assert.Equal(KokoroMetadata.SupportedLanguages, q4.SupportedLanguages);
        Assert.Equal(KokoroMetadata.SupportedSpeakers, q4.Speakers);
        Assert.Contains("ja-jp", q4.SupportedLanguages);
        Assert.Contains("jf_alpha", q4.Speakers);
        Assert.True(q4.Speakers.Count > 50, $"Expected more than 50 speakers, but found {q4.Speakers.Count}");
    }
}
