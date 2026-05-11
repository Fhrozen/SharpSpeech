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
        Assert.True(catalog.TryGetModel("supertonic-3", out _));
    }

    [Fact]
    public void Kokoro_models_use_voices_directory_configuration()
    {
        var catalog = new ModelCatalog();

        Assert.True(catalog.TryGetModel("kokoro-q4", out var q4));
        Assert.Equal("voices", q4!.VoicesPath);
        Assert.Equal(".bin", q4.VoiceFileExtension);
        Assert.False(string.IsNullOrWhiteSpace(q4.VoicesBaseUrl));
    }
}
