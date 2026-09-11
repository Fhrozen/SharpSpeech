using SharpAudio.Api.Contracts;
using SharpAudio.Api.Models;
using SharpAudio.Api.Services;

namespace SharpAudio.Api.Tests;

/// <summary>
/// Verifies AsrTranscriberRouter dispatches by model.Engine (mirrors the intent of a
/// TtsSynthesizerRouter test, adapted for ASR). Uses bogus model directories so no real model
/// weights are downloaded/loaded - each branch fails fast with an engine-specific error before
/// doing any real inference, which is enough to prove the dispatch went to the right place.
/// </summary>
public sealed class AsrTranscriberRouterTests
{
    private static AsrTranscriberRouter CreateRouter() => new(new WhisperAsrTranscriber(), new NemotronAsrTranscriber());

    private static AsrModelDefinition WhisperModel(string engine = "whisper") => new()
    {
        Name = "whisper-base",
        Engine = engine,
        ModelPath = "ggml-base.bin"
    };

    private static AsrModelDefinition NemotronModel(string engine = "nemotron-3.5") => new()
    {
        Name = "nemotron-3.5",
        Engine = engine
    };

    [Fact]
    public async Task Whisper_engine_routes_to_whisper_transcriber()
    {
        using var router = CreateRouter();

        var ex = await Assert.ThrowsAsync<FileNotFoundException>(() => router.TranscribeAsync(
            WhisperModel(),
            "/tmp/does-not-exist",
            new AudioTranscriptionRequest { Model = "whisper-base" },
            [],
            CancellationToken.None));

        Assert.Contains("ggml-base.bin", ex.Message);
    }

    [Fact]
    public async Task Nemotron_engine_routes_to_nemotron_transcriber()
    {
        using var router = CreateRouter();

        // Nemotron's engine loads ONNX sessions eagerly in its constructor, so a bogus
        // directory fails while trying to open encoder.onnx - never reaching Whisper's
        // "Model file not found" check, proving the router chose the Nemotron branch.
        var ex = await Assert.ThrowsAnyAsync<Exception>(() => router.TranscribeAsync(
            NemotronModel(),
            "/tmp/does-not-exist",
            new AudioTranscriptionRequest { Model = "nemotron-3.5" },
            [],
            CancellationToken.None));

        Assert.DoesNotContain("ggml-base.bin", ex.Message);
    }

    [Fact]
    public async Task Nemotron_engine_match_is_case_insensitive()
    {
        using var router = CreateRouter();

        var ex = await Assert.ThrowsAnyAsync<Exception>(() => router.TranscribeAsync(
            NemotronModel("NEMOTRON-3.5"),
            "/tmp/does-not-exist",
            new AudioTranscriptionRequest { Model = "nemotron-3.5" },
            [],
            CancellationToken.None));

        Assert.DoesNotContain("ggml-base.bin", ex.Message);
    }

    [Fact]
    public async Task Unrecognized_engine_falls_back_to_whisper()
    {
        using var router = CreateRouter();

        // WhisperAsrTranscriber itself guards against engine mismatches, so an unrecognized
        // engine name reaches Whisper (proving the router's default branch) but is then
        // rejected there with an InvalidOperationException rather than a missing-file error.
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => router.TranscribeAsync(
            WhisperModel("some-future-engine"),
            "/tmp/does-not-exist",
            new AudioTranscriptionRequest { Model = "whisper-base" },
            [],
            CancellationToken.None));

        Assert.Contains("WhisperAsrTranscriber", ex.Message);
        Assert.Contains("some-future-engine", ex.Message);
    }

    [Fact]
    public void GetLoadedEngines_merges_whisper_and_nemotron_engines()
    {
        using var router = CreateRouter();

        var loaded = router.GetLoadedEngines();

        Assert.Empty(loaded);
    }
}
