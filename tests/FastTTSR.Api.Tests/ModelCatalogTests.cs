using FastTTSR.Api.Services;

namespace FastTTSR.Api.Tests;

public sealed class ModelCatalogTests
{
    [Fact]
    public void Supports_expected_models()
    {
        var catalog = new ModelCatalog();

        Assert.True(catalog.TryGetModel("kokoro-tts", out _));
        Assert.True(catalog.TryGetModel("supertonic-3", out _));
    }
}
