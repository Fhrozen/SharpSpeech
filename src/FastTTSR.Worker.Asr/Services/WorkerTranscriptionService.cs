using FastTTSR.Api.Services;
using FastTTSR.Worker.Asr.Grpc;
using Grpc.Core;

namespace FastTTSR.Worker.Asr.Services;

/// <summary>
/// gRPC service that performs ASR transcription in the worker process (mirrors
/// WorkerSynthesisService). Routes by request.Engine to Whisper or Nemotron, keeping only the
/// most-recently-used engine loaded at a time.
/// </summary>
public sealed class WorkerTranscriptionService : WorkerTranscription.WorkerTranscriptionBase, IDisposable
{
    private readonly IdleMonitor _idleMonitor;
    private WhisperAsrEngine? _whisperEngine;
    private NemotronAsrEngine? _nemotronEngine;
    private string? _loadedModelKey;
    private readonly object _engineLock = new();
    private bool _disposed;

    public WorkerTranscriptionService(IdleMonitor idleMonitor)
    {
        _idleMonitor = idleMonitor;
    }

    public override async Task<TranscribeResponse> Transcribe(TranscribeRequest request, ServerCallContext context)
    {
        _idleMonitor.RecordActivity();
        var startTime = DateTime.UtcNow;

        Console.WriteLine($"[Worker.Asr] Transcribe request: engine={request.Engine}, model={request.ModelName}, audio_bytes={request.AudioBytes.Length}");

        var audioBytes = request.AudioBytes.ToByteArray();
        var language = string.IsNullOrEmpty(request.Language) ? null : request.Language;

        string text;
        string? detectedLanguage;

        if (string.Equals(request.Engine, "nemotron-3.5", StringComparison.OrdinalIgnoreCase))
        {
            var engine = GetOrCreateNemotronEngine(request);
            (text, detectedLanguage) = await Task.Run(() => engine.Transcribe(audioBytes, language), context.CancellationToken);
        }
        else
        {
            var engine = GetOrCreateWhisperEngine(request);
            (text, detectedLanguage) = await engine.TranscribeAsync(audioBytes, language, context.CancellationToken);
        }

        var processingTime = (DateTime.UtcNow - startTime).TotalSeconds;
        var audioDuration = WavAudioUtils.GetDurationSeconds(audioBytes);

        Console.WriteLine($"[Worker.Asr] Transcription complete: {processingTime:F2}s processing, {audioDuration:F2}s audio");

        return new TranscribeResponse
        {
            Text = text,
            LanguageDetected = detectedLanguage ?? string.Empty,
            ProcessingTimeSeconds = processingTime,
            AudioDurationSeconds = audioDuration
        };
    }

    private WhisperAsrEngine GetOrCreateWhisperEngine(TranscribeRequest request)
    {
        lock (_engineLock)
        {
            var modelKey = $"whisper:{request.ModelName}";

            if (_whisperEngine == null || _loadedModelKey != modelKey)
            {
                _whisperEngine?.Dispose();
                _nemotronEngine?.Dispose();
                _nemotronEngine = null;

                Console.WriteLine($"[Worker.Asr] Loading Whisper model: {request.ModelPath}");

                if (!File.Exists(request.ModelPath))
                {
                    throw new FileNotFoundException($"Model file not found: {request.ModelPath}");
                }

                _whisperEngine = new WhisperAsrEngine(request.ModelPath);
                _loadedModelKey = modelKey;
            }

            return _whisperEngine;
        }
    }

    private NemotronAsrEngine GetOrCreateNemotronEngine(TranscribeRequest request)
    {
        lock (_engineLock)
        {
            var modelKey = $"nemotron-3.5:{request.ModelName}";

            if (_nemotronEngine == null || _loadedModelKey != modelKey)
            {
                _nemotronEngine?.Dispose();
                _whisperEngine?.Dispose();
                _whisperEngine = null;

                Console.WriteLine($"[Worker.Asr] Loading Nemotron model from directory: {request.ModelPath}");

                if (!Directory.Exists(request.ModelPath))
                {
                    throw new DirectoryNotFoundException($"Model directory not found: {request.ModelPath}");
                }

                _nemotronEngine = new NemotronAsrEngine(request.ModelPath);
                _loadedModelKey = modelKey;
            }

            return _nemotronEngine;
        }
    }

    public override Task<HealthCheckResponse> HealthCheck(HealthCheckRequest request, ServerCallContext context)
    {
        var idleDuration = _idleMonitor.GetIdleDuration();

        return Task.FromResult(new HealthCheckResponse
        {
            IsReady = true,
            ModelLoaded = _loadedModelKey ?? "none",
            IdleSeconds = idleDuration.TotalSeconds
        });
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        lock (_engineLock)
        {
            _whisperEngine?.Dispose();
            _nemotronEngine?.Dispose();
        }

        _disposed = true;
    }
}
