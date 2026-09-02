using FastTTSR.Api.Contracts;
using FastTTSR.Api.Models;

namespace FastTTSR.Api.Services;

/// <summary>
/// Routes transcription requests to the correct <see cref="IAsrTranscriber"/> implementation
/// based on the model's engine name (mirrors <see cref="TtsSynthesizerRouter"/>).
/// </summary>
public sealed class AsrTranscriberRouter : IAsrTranscriber, IIdleTrackingTranscriber, IDisposable
{
    private const string NemotronEngine = "nemotron-3.5";

    private readonly WhisperAsrTranscriber _whisper;
    private readonly NemotronAsrTranscriber _nemotron;
    private bool _disposed;

    public AsrTranscriberRouter(WhisperAsrTranscriber whisper, NemotronAsrTranscriber nemotron)
    {
        _whisper = whisper;
        _nemotron = nemotron;
    }

    public Task<TranscriptionResult> TranscribeAsync(
        AsrModelDefinition model,
        string modelDirectory,
        AudioTranscriptionRequest request,
        byte[] audioBytes,
        CancellationToken cancellationToken)
    {
        if (string.Equals(model.Engine, NemotronEngine, StringComparison.OrdinalIgnoreCase))
        {
            return _nemotron.TranscribeAsync(model, modelDirectory, request, audioBytes, cancellationToken);
        }

        // Default: Whisper
        return _whisper.TranscribeAsync(model, modelDirectory, request, audioBytes, cancellationToken);
    }

    public Task<IStreamingTranscriptionSession> CreateStreamingSessionAsync(
        AsrModelDefinition model,
        string modelDirectory,
        string? language,
        bool enableVad,
        double segmentSeconds,
        CancellationToken cancellationToken)
    {
        if (string.Equals(model.Engine, NemotronEngine, StringComparison.OrdinalIgnoreCase))
        {
            return _nemotron.CreateStreamingSessionAsync(model, modelDirectory, language, enableVad, segmentSeconds, cancellationToken);
        }

        return _whisper.CreateStreamingSessionAsync(model, modelDirectory, language, enableVad, segmentSeconds, cancellationToken);
    }

    public IReadOnlyDictionary<string, DateTime> GetLoadedEngines()
    {
        var allEngines = new Dictionary<string, DateTime>();

        foreach (var kvp in _whisper.GetLoadedEngines())
        {
            allEngines[kvp.Key] = kvp.Value;
        }

        foreach (var kvp in _nemotron.GetLoadedEngines())
        {
            allEngines[kvp.Key] = kvp.Value;
        }

        return allEngines;
    }

    public bool TryReleaseEngine(string engineKey) =>
        _whisper.TryReleaseEngine(engineKey) || _nemotron.TryReleaseEngine(engineKey);

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _whisper.Dispose();
        _nemotron.Dispose();
        _disposed = true;
    }
}
