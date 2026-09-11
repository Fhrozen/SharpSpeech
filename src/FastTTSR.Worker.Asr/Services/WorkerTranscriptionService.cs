using SharpAudio.Api.Services;
using SharpAudio.Worker.Asr.Grpc;
using Grpc.Core;

namespace SharpAudio.Worker.Asr.Services;

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
            var engine = GetOrCreateNemotronEngine(request.ModelName, request.ModelPath);
            (text, detectedLanguage, _) = await Task.Run(() => engine.Transcribe(audioBytes, language), context.CancellationToken);
        }
        else
        {
            var engine = GetOrCreateWhisperEngine(request.ModelName, request.ModelPath);
            (text, detectedLanguage, _) = await engine.TranscribeAsync(audioBytes, language, context.CancellationToken);
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

    public override async Task TranscribeStream(
        IAsyncStreamReader<TranscribeStreamChunk> requestStream,
        IServerStreamWriter<TranscribeStreamUpdate> responseStream,
        ServerCallContext context)
    {
        _idleMonitor.RecordActivity();

        if (!await requestStream.MoveNext(context.CancellationToken) || requestStream.Current.PayloadCase != TranscribeStreamChunk.PayloadOneofCase.Config)
        {
            throw new RpcException(new Status(StatusCode.InvalidArgument, "The first TranscribeStream message must set 'config'."));
        }

        var config = requestStream.Current.Config;
        Console.WriteLine($"[Worker.Asr] TranscribeStream started: engine={config.Engine}, model={config.ModelName}");
        var language = string.IsNullOrEmpty(config.Language) ? null : config.Language;
        var segmentSeconds = config.SegmentSeconds > 0 ? config.SegmentSeconds : new SharpAudio.Api.Options.AsrStreamingOptions().DefaultSegmentSeconds;

        using IStreamingTranscriptionSession session = string.Equals(config.Engine, "nemotron-3.5", StringComparison.OrdinalIgnoreCase)
            ? GetOrCreateNemotronEngine(config.ModelName, config.ModelPath).CreateStreamingSession(language, config.UseVad, segmentSeconds)
            : new WhisperStreamingSession(GetOrCreateWhisperEngine(config.ModelName, config.ModelPath), language, segmentSeconds);

        while (await requestStream.MoveNext(context.CancellationToken))
        {
            _idleMonitor.RecordActivity();
            var chunk = requestStream.Current;
            if (chunk.PayloadCase != TranscribeStreamChunk.PayloadOneofCase.AudioChunk)
            {
                continue;
            }

            var updates = await session.ProcessChunkAsync(chunk.AudioChunk.ToByteArray(), context.CancellationToken);
            foreach (var update in updates)
            {
                await responseStream.WriteAsync(new TranscribeStreamUpdate { Text = update.Text, IsFinal = false, IsSegmentFinal = update.IsSegmentFinal });
            }
        }

        var finalText = await session.FinishAsync(context.CancellationToken);
        await responseStream.WriteAsync(new TranscribeStreamUpdate { Text = finalText, IsFinal = true });
        Console.WriteLine("[Worker.Asr] TranscribeStream finished");
    }

    private WhisperAsrEngine GetOrCreateWhisperEngine(string modelName, string modelPath)
    {
        lock (_engineLock)
        {
            var modelKey = $"whisper:{modelName}";

            if (_whisperEngine == null || _loadedModelKey != modelKey)
            {
                _whisperEngine?.Dispose();
                _nemotronEngine?.Dispose();
                _nemotronEngine = null;

                Console.WriteLine($"[Worker.Asr] Loading Whisper model: {modelPath}");

                if (!File.Exists(modelPath))
                {
                    throw new FileNotFoundException($"Model file not found: {modelPath}");
                }

                _whisperEngine = new WhisperAsrEngine(modelPath);
                _loadedModelKey = modelKey;
            }

            return _whisperEngine;
        }
    }

    private NemotronAsrEngine GetOrCreateNemotronEngine(string modelName, string modelPath)
    {
        lock (_engineLock)
        {
            var modelKey = $"nemotron-3.5:{modelName}";

            if (_nemotronEngine == null || _loadedModelKey != modelKey)
            {
                _nemotronEngine?.Dispose();
                _whisperEngine?.Dispose();
                _whisperEngine = null;

                Console.WriteLine($"[Worker.Asr] Loading Nemotron model from directory: {modelPath}");

                if (!Directory.Exists(modelPath))
                {
                    throw new DirectoryNotFoundException($"Model directory not found: {modelPath}");
                }

                _nemotronEngine = new NemotronAsrEngine(modelPath);
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
