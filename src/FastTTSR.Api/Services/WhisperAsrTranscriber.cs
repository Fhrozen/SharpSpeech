using System.Collections.Concurrent;
using SharpAudio.Api.Contracts;
using SharpAudio.Api.Models;

namespace SharpAudio.Api.Services;

public sealed class WhisperAsrTranscriber : IAsrTranscriber, IIdleTrackingTranscriber, IDisposable
{
    private const string WhisperEngine = "whisper";
    private readonly ConcurrentDictionary<string, WhisperAsrEngine> _engines = new();
    private readonly ConcurrentDictionary<string, DateTime> _lastAccessTimes = new();
    private bool _disposed;

    public async Task<TranscriptionResult> TranscribeAsync(
        AsrModelDefinition model,
        string modelDirectory,
        AudioTranscriptionRequest request,
        byte[] audioBytes,
        CancellationToken cancellationToken)
    {
        if (!string.Equals(model.Engine, WhisperEngine, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException($"WhisperAsrTranscriber cannot handle engine '{model.Engine}'.");
        }

        var startTime = DateTime.UtcNow;
        var engine = GetOrCreateEngine(model, modelDirectory);

        var (text, detectedLanguage, rawSegments) = await engine.TranscribeAsync(audioBytes, request.Language, cancellationToken);

        var processingTime = (DateTime.UtcNow - startTime).TotalSeconds;
        var audioDuration = WavAudioUtils.GetDurationSeconds(audioBytes);
        var segments = request.IncludeSegments
            ? SegmentMerger.MergeShortSegments(rawSegments, request.MinSegmentDurationSeconds)
            : null;

        return new TranscriptionResult(text, detectedLanguage, processingTime, audioDuration, text.Length, segments);
    }

    public IStreamingTranscriptionSession CreateStreamingSession(AsrModelDefinition model, string modelDirectory, string? language, double segmentSeconds)
    {
        if (!string.Equals(model.Engine, WhisperEngine, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException($"WhisperAsrTranscriber cannot handle engine '{model.Engine}'.");
        }

        return new WhisperStreamingSession(GetOrCreateEngine(model, modelDirectory), language, segmentSeconds);
    }

    // Whisper has no VAD support - enableVad is accepted only to satisfy IAsrTranscriber's uniform signature.
    public Task<IStreamingTranscriptionSession> CreateStreamingSessionAsync(
        AsrModelDefinition model, string modelDirectory, string? language, bool enableVad, double segmentSeconds, CancellationToken cancellationToken) =>
        Task.FromResult(CreateStreamingSession(model, modelDirectory, language, segmentSeconds));

    private WhisperAsrEngine GetOrCreateEngine(AsrModelDefinition model, string modelDirectory)
    {
        var cacheKey = $"{model.Name}:{modelDirectory}";

        var engine = _engines.GetOrAdd(cacheKey, _ =>
        {
            var modelPath = Path.Combine(modelDirectory, model.ModelPath);

            if (!File.Exists(modelPath))
            {
                throw new FileNotFoundException($"Model file not found: {modelPath}");
            }

            Console.WriteLine($"Creating Whisper engine for {model.Name} with model: {modelPath}");
            return new WhisperAsrEngine(modelPath);
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
