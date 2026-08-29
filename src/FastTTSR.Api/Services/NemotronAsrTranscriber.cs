using System.Collections.Concurrent;
using FastTTSR.Api.Contracts;
using FastTTSR.Api.Models;

namespace FastTTSR.Api.Services;

public sealed class NemotronAsrTranscriber : IAsrTranscriber, IIdleTrackingTranscriber, IDisposable
{
    private const string NemotronEngine = "nemotron-3.5";
    private readonly ConcurrentDictionary<string, NemotronAsrEngine> _engines = new();
    private readonly ConcurrentDictionary<string, DateTime> _lastAccessTimes = new();
    private bool _disposed;

    public Task<TranscriptionResult> TranscribeAsync(
        AsrModelDefinition model,
        string modelDirectory,
        AudioTranscriptionRequest request,
        byte[] audioBytes,
        CancellationToken cancellationToken)
    {
        if (!string.Equals(model.Engine, NemotronEngine, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException($"NemotronAsrTranscriber cannot handle engine '{model.Engine}'.");
        }

        var startTime = DateTime.UtcNow;
        var engine = GetOrCreateEngine(model, modelDirectory);

        var (text, detectedLanguage) = engine.Transcribe(audioBytes, request.Language, request.EnableVad);

        var processingTime = (DateTime.UtcNow - startTime).TotalSeconds;
        var audioDuration = WavAudioUtils.GetDurationSeconds(audioBytes);

        return Task.FromResult(new TranscriptionResult(text, detectedLanguage, processingTime, audioDuration, text.Length));
    }

    public IStreamingTranscriptionSession CreateStreamingSession(AsrModelDefinition model, string modelDirectory, string? language, bool enableVad)
    {
        if (!string.Equals(model.Engine, NemotronEngine, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException($"NemotronAsrTranscriber cannot handle engine '{model.Engine}'.");
        }

        return GetOrCreateEngine(model, modelDirectory).CreateStreamingSession(language, enableVad);
    }

    private NemotronAsrEngine GetOrCreateEngine(AsrModelDefinition model, string modelDirectory)
    {
        var cacheKey = $"{model.Name}:{modelDirectory}";

        var engine = _engines.GetOrAdd(cacheKey, _ =>
        {
            Console.WriteLine($"Creating Nemotron engine for {model.Name} in: {modelDirectory}");
            return new NemotronAsrEngine(modelDirectory);
        });

        _lastAccessTimes[cacheKey] = DateTime.UtcNow;
        return engine;
    }

    public IReadOnlyDictionary<string, DateTime> GetLoadedEngines() => _lastAccessTimes;

    public bool TryReleaseEngine(string engineKey)
    {
        if (_engines.TryRemove(engineKey, out var engine))
        {
            engine.Dispose();
            _lastAccessTimes.TryRemove(engineKey, out _);
            return true;
        }

        return false;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        foreach (var engine in _engines.Values)
        {
            engine.Dispose();
        }

        _disposed = true;
    }
}
