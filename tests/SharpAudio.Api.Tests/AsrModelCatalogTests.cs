using SharpAudio.Api.Services;

namespace SharpAudio.Api.Tests;

public sealed class AsrModelCatalogTests
{
    [Fact]
    public void Supports_expected_models()
    {
        var catalog = new AsrModelCatalog();

        Assert.True(catalog.TryGetModel("whisper-base", out _));
        Assert.True(catalog.TryGetModel("nemotron-3.5", out _));
    }

    [Fact]
    public void Whisper_model_uses_whisper_engine_and_supports_auto_detect()
    {
        var catalog = new AsrModelCatalog();

        Assert.True(catalog.TryGetModel("whisper-base", out var whisper));
        Assert.Equal("whisper", whisper!.Engine);
        Assert.True(whisper.SupportsLanguageAutoDetect);
        Assert.False(whisper.SupportsVad);
        Assert.True(whisper.SupportsStreaming);
        Assert.NotEmpty(whisper.Assets);
        Assert.Contains(whisper.Assets, a => a.RelativePath == whisper.ModelPath);
    }

    [Fact]
    public void Nemotron_model_uses_nemotron_engine_and_exposes_supported_languages()
    {
        var catalog = new AsrModelCatalog();

        Assert.True(catalog.TryGetModel("nemotron-3.5", out var nemotron));
        Assert.Equal("nemotron-3.5", nemotron!.Engine);
        Assert.True(nemotron.SupportsLanguageAutoDetect);
        Assert.True(nemotron.SupportsVad);
        Assert.True(nemotron.SupportsStreaming);
        Assert.Contains("en", nemotron.SupportedLanguages);
        Assert.Contains("ja", nemotron.SupportedLanguages);
        Assert.True(nemotron.SupportedLanguages.Count > 10);
    }

    [Fact]
    public void Nemotron_model_declares_all_required_onnx_and_config_assets()
    {
        var catalog = new AsrModelCatalog();

        Assert.True(catalog.TryGetModel("nemotron-3.5", out var nemotron));
        var relativePaths = nemotron!.Assets.Select(a => a.RelativePath).ToArray();

        Assert.Contains("encoder.onnx", relativePaths);
        Assert.Contains("encoder.onnx.data", relativePaths);
        Assert.Contains("decoder.onnx", relativePaths);
        Assert.Contains("decoder.onnx.data", relativePaths);
        Assert.Contains("joint.onnx", relativePaths);
        Assert.Contains("joint.onnx.data", relativePaths);
        Assert.Contains("audio_processor_config.json", relativePaths);
        Assert.Contains("vocab.txt", relativePaths);
    }

    [Fact]
    public void Unknown_model_name_is_not_found()
    {
        var catalog = new AsrModelCatalog();

        Assert.False(catalog.TryGetModel("does-not-exist", out var model));
        Assert.Null(model);
    }

    [Fact]
    public void GetSupportedModels_returns_all_catalog_entries()
    {
        var catalog = new AsrModelCatalog();

        var models = catalog.GetSupportedModels();

        Assert.Equal(2, models.Count);
        Assert.Contains(models, m => m.Name == "whisper-base");
        Assert.Contains(models, m => m.Name == "nemotron-3.5");
    }
}
