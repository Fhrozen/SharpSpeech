using System.Collections.Concurrent;
using SharpAudio.Api.Contracts;
using SharpAudio.Api.Models;

namespace SharpAudio.Api.Services;

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

        var (text, detectedLanguage, rawSegments) = engine.Transcribe(audioBytes, request.Language, request.EnableVad);

        var processingTime = (DateTime.UtcNow - startTime).TotalSeconds;
        var audioDuration = WavAudioUtils.GetDurationSeconds(audioBytes);
        var segments = request.IncludeSegments
            ? SegmentMerger.MergeShortSegments(rawSegments, request.MinSegmentDurationSeconds)
            : null;

        return Task.FromResult(new TranscriptionResult(text, detectedLanguage, processingTime, audioDuration, text.Length, segments));
    }

    public IStreamingTranscriptionSession CreateStreamingSession(AsrModelDefinition model, string modelDirectory, string? language, bool enableVad, double segmentSeconds)
    {
        if (!string.Equals(model.Engine, NemotronEngine, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException($"NemotronAsrTranscriber cannot handle engine '{model.Engine}'.");
        }

        return GetOrCreateEngine(model, modelDirectory).CreateStreamingSession(language, enableVad, segmentSeconds);
    }

    public Task<IStreamingTranscriptionSession> CreateStreamingSessionAsync(
        AsrModelDefinition model, string modelDirectory, string? language, bool enableVad, double segmentSeconds, CancellationToken cancellationToken) =>
        Task.FromResult(CreateStreamingSession(model, modelDirectory, language, enableVad, segmentSeconds));

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
