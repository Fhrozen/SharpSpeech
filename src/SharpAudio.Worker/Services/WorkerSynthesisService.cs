using System.Text;
using SharpAudio.Api.Services;
using SharpAudio.Worker.Grpc;
using Grpc.Core;

namespace SharpAudio.Worker.Services;

/// <summary>
/// gRPC service that performs TTS synthesis in worker process
/// </summary>
public sealed class WorkerSynthesisService : WorkerSynthesis.WorkerSynthesisBase, IDisposable
{
    private readonly IdleMonitor _idleMonitor;
    private KokoroTtsEngine? _kokoroEngine;
    private SupertonicTtsEngine? _supertonicEngine;
    private string? _loadedModelKey;
    private readonly object _engineLock = new();
    private bool _disposed;

    public WorkerSynthesisService(IdleMonitor idleMonitor)
    {
        _idleMonitor = idleMonitor;
    }

    public override async Task<SynthesizeResponse> Synthesize(SynthesizeRequest request, ServerCallContext context)
    {
        _idleMonitor.RecordActivity();
        var startTime = DateTime.UtcNow;

        try
        {
            Console.WriteLine($"[Worker] Synthesize request: engine={request.Engine}, model={request.ModelName}");

            // Determine which engine to use and get correct sample rate
            byte[] audioBytes;
            int sampleRate;
            
            if (string.Equals(request.Engine, "kokoro", StringComparison.OrdinalIgnoreCase))
            {
                sampleRate = 24000; // Kokoro always uses 24000 Hz
                audioBytes = await SynthesizeWithKokoroAsync(request, context.CancellationToken);
            }
            else if (string.Equals(request.Engine, "supertonic", StringComparison.OrdinalIgnoreCase) ||
                     string.Equals(request.Engine, "supertonic-3", StringComparison.OrdinalIgnoreCase))
            {
                var engine = GetOrCreateSupertonicEngine(request);
                sampleRate = engine.SampleRate; // Get actual sample rate (22050 Hz for Supertonic-3)
                audioBytes = await SynthesizeWithSupertonicAsync(request, context.CancellationToken);
            }
            else
            {
                throw new RpcException(new Status(StatusCode.InvalidArgument, $"Unsupported engine: {request.Engine}"));
            }

            if (audioBytes.Length == 0)
            {
                sampleRate = 24000; // Fallback uses 24000 Hz
                audioBytes = GenerateFallbackSilence(request);
            }

            // Generate WAV file with header using correct sample rate
            var wavBytes = CreateWavFile(audioBytes, sampleRate: sampleRate, channels: 1, bitsPerSample: 16);

            // Calculate metrics
            var processingTime = (DateTime.UtcNow - startTime).TotalSeconds;
            var audioDuration = CalculateAudioDuration(audioBytes.Length, sampleRate: sampleRate, channels: 1, bitsPerSample: 16);
            var charCount = request.Input.Length;

            Console.WriteLine($"[Worker] Synthesis complete: {charCount} chars, {processingTime:F2}s processing, {audioDuration:F2}s audio, sampleRate={sampleRate} Hz");
            // Console.Error.WriteLine($"[Worker] Synthesis complete: {charCount} chars, {processingTime:F2}s processing, {audioDuration:F2}s audio, sampleRate={sampleRate} Hz");

            return new SynthesizeResponse
            {
                AudioBytes = Google.Protobuf.ByteString.CopyFrom(wavBytes),
                ContentType = "audio/wav",
                FileName = $"{request.ModelName}.wav",
                ProcessingTimeSeconds = processingTime,
                AudioDurationSeconds = audioDuration,
                CharacterCount = charCount
            };
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[Worker] Synthesis failed: {ex.GetType().Name}: {ex.Message}");
            Console.Error.WriteLine($"[Worker] Stack trace: {ex.StackTrace}");
            
            // Return fallback silence
            var fallbackBytes = GenerateFallbackSilence(request);
            var wavBytes = CreateWavFile(fallbackBytes, sampleRate: 24000, channels: 1, bitsPerSample: 16);
            var processingTime = (DateTime.UtcNow - startTime).TotalSeconds;
            var durationSeconds = Math.Clamp(request.Input.Length / 30.0, 0.5, 8.0);

            return new SynthesizeResponse
            {
                AudioBytes = Google.Protobuf.ByteString.CopyFrom(wavBytes),
                ContentType = "audio/wav",
                FileName = $"{request.ModelName}.wav",
                ProcessingTimeSeconds = processingTime,
                AudioDurationSeconds = durationSeconds,
                CharacterCount = request.Input.Length
            };
        }
    }

    private async Task<byte[]> SynthesizeWithKokoroAsync(SynthesizeRequest request, CancellationToken cancellationToken)
    {
        var engine = GetOrCreateKokoroEngine(request);
        var speed = Math.Clamp(request.Speed, 0.5f, 4.0f);
        var language = DetermineLanguage(request);

        // Load speaker voice if needed
        LoadKokoroSpeakerVoice(engine, request);

        cancellationToken.ThrowIfCancellationRequested();

        // Perform synthesis on thread pool to avoid blocking gRPC thread
        return await Task.Run(() => engine.SynthesizeToBytes(request.Input, language, speed), cancellationToken);
    }

    private async Task<byte[]> SynthesizeWithSupertonicAsync(SynthesizeRequest request, CancellationToken cancellationToken)
    {
        var engine = GetOrCreateSupertonicEngine(request);
        var speed = Math.Clamp(request.Speed, 0.5f, 4.0f);
        var language = string.IsNullOrWhiteSpace(request.Language) ? "en" : request.Language;
        var style = LoadSupertonicStyle(request);

        // Console.WriteLine($"[Worker] Supertonic synthesis: input_speed={request.Speed}, clamped_speed={speed}, language={language}, voice={request.Voice}, input_length={request.Input.Length}");
        // Console.Error.WriteLine($"[Worker] Supertonic synthesis: input_speed={request.Speed}, clamped_speed={speed}, language={language}, voice={request.Voice}, input_length={request.Input.Length}");

        cancellationToken.ThrowIfCancellationRequested();

        // Perform synthesis on thread pool - USE NAMED PARAMETERS
        var audioBytes = await Task.Run(() => 
            engine.SynthesizeToBytes(
                text: request.Input, 
                lang: language, 
                style: style, 
                totalStep: 8, 
                speed: speed
            ), 
            cancellationToken);
        
        // Console.WriteLine($"[Worker] Supertonic synthesis complete: audio_size={audioBytes.Length} bytes, used_speed={speed}");
        // Console.Error.WriteLine($"[Worker] Supertonic synthesis complete: audio_size={audioBytes.Length} bytes, used_speed={speed}");
        
        return audioBytes;
    }

    private KokoroTtsEngine GetOrCreateKokoroEngine(SynthesizeRequest request)
    {
        lock (_engineLock)
        {
            var modelKey = $"kokoro:{request.ModelName}";
            
            if (_kokoroEngine == null || _loadedModelKey != modelKey)
            {
                // Dispose old engine if exists
                _kokoroEngine?.Dispose();
                _supertonicEngine?.Dispose();
                _supertonicEngine = null;

                Console.WriteLine($"[Worker] Loading Kokoro model: {request.ModelPath}");
                
                if (!File.Exists(request.ModelPath))
                {
                    throw new FileNotFoundException($"Model file not found: {request.ModelPath}");
                }

                _kokoroEngine = new KokoroTtsEngine(request.ModelPath);
                _loadedModelKey = modelKey;
            }

            return _kokoroEngine;
        }
    }

    private SupertonicTtsEngine GetOrCreateSupertonicEngine(SynthesizeRequest request)
    {
        lock (_engineLock)
        {
            var modelKey = $"supertonic:{request.ModelName}";
            
            if (_supertonicEngine == null || _loadedModelKey != modelKey)
            {
                // Dispose old engine if exists
                _kokoroEngine?.Dispose();
                _kokoroEngine = null;
                _supertonicEngine?.Dispose();

                Console.WriteLine($"[Worker] Loading Supertonic model from directory: {request.ModelPath}");
                
                // Supertonic takes a directory path, not a file path
                if (!Directory.Exists(request.ModelPath))
                {
                    throw new DirectoryNotFoundException($"Model directory not found: {request.ModelPath}");
                }

                _supertonicEngine = new SupertonicTtsEngine(request.ModelPath);
                _loadedModelKey = modelKey;
            }

            return _supertonicEngine;
        }
    }

    private void LoadKokoroSpeakerVoice(KokoroTtsEngine engine, SynthesizeRequest request)
    {
        var speaker = request.Voice;
        if (KokoroMetadata.IsDefaultVoiceValue(speaker))
        {
            speaker = request.Speakers.FirstOrDefault() ?? "";
        }

        if (string.IsNullOrWhiteSpace(speaker) || string.IsNullOrWhiteSpace(request.VoicesPath))
        {
            return;
        }

        var voiceFile = Path.Combine(request.VoicesPath, $"{speaker}{request.VoiceFileExtension}");

        if (File.Exists(voiceFile))
        {
            engine.LoadSpeakers(voiceFile);
        }
        else
        {
            // Try to find any voice file as fallback
            var anyVoiceFile = request.Speakers
                .Select(s => Path.Combine(request.VoicesPath, $"{s}{request.VoiceFileExtension}"))
                .FirstOrDefault(File.Exists);

            if (anyVoiceFile != null)
            {
                Console.WriteLine($"[Worker] Using fallback voice: {Path.GetFileName(anyVoiceFile)}");
                engine.LoadSpeakers(anyVoiceFile);
            }
        }
    }

    private SupertonicStyle LoadSupertonicStyle(SynthesizeRequest request)
    {
        var speaker = request.Voice;
        if (string.IsNullOrWhiteSpace(speaker))
        {
            speaker = request.Speakers.FirstOrDefault() ?? "";
        }

        if (string.IsNullOrWhiteSpace(speaker) || string.IsNullOrWhiteSpace(request.VoicesPath))
        {
            // Return zero-filled style as fallback
            return new SupertonicStyle([], [1, 1, 1], [], [1, 1, 1]);
        }

        var voiceFile = Path.Combine(request.VoicesPath, $"{speaker}{request.VoiceFileExtension}");

        if (File.Exists(voiceFile))
        {
            return SupertonicTtsEngine.LoadVoiceStyle(voiceFile);
        }

        // Try to find any voice file as fallback
        var anyVoiceFile = request.Speakers
            .Select(s => Path.Combine(request.VoicesPath, $"{s}{request.VoiceFileExtension}"))
            .FirstOrDefault(File.Exists);

        if (anyVoiceFile != null)
        {
            Console.WriteLine($"[Worker] Using fallback Supertonic style: {Path.GetFileName(anyVoiceFile)}");
            return SupertonicTtsEngine.LoadVoiceStyle(anyVoiceFile);
        }

        // Last resort: zero-filled style
        Console.WriteLine($"[Worker] No Supertonic style files found, using zero style");
        return new SupertonicStyle([], [1, 1, 1], [], [1, 1, 1]);
    }

    private static string DetermineLanguage(SynthesizeRequest request)
    {
        if (KokoroMetadata.TryNormalizeLanguage(request.Language, out var normalized) &&
            KokoroMetadata.TryMapToEspeakVoice(normalized, out var espeakVoice))
        {
            return espeakVoice;
        }

        return "en-us";
    }

    private static byte[] CreateWavFile(byte[] pcmData, int sampleRate, short channels, short bitsPerSample)
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream, Encoding.ASCII, leaveOpen: true);

        var dataSize = pcmData.Length;
        var byteRate = sampleRate * channels * bitsPerSample / 8;
        var blockAlign = (short)(channels * bitsPerSample / 8);

        // RIFF header
        writer.Write(Encoding.ASCII.GetBytes("RIFF"));
        writer.Write(36 + dataSize); // File size - 8
        writer.Write(Encoding.ASCII.GetBytes("WAVE"));

        // fmt chunk
        writer.Write(Encoding.ASCII.GetBytes("fmt "));
        writer.Write(16); // fmt chunk size
        writer.Write((short)1); // Audio format (1 = PCM)
        writer.Write(channels);
        writer.Write(sampleRate);
        writer.Write(byteRate);
        writer.Write(blockAlign);
        writer.Write(bitsPerSample);

        // data chunk
        writer.Write(Encoding.ASCII.GetBytes("data"));
        writer.Write(dataSize);
        writer.Write(pcmData);

        writer.Flush();
        return stream.ToArray();
    }

    private static double CalculateAudioDuration(int pcmDataSize, int sampleRate, short channels, short bitsPerSample)
    {
        var bytesPerSample = bitsPerSample / 8;
        var sampleCount = pcmDataSize / (channels * bytesPerSample);
        return (double)sampleCount / sampleRate;
    }

    private static byte[] GenerateFallbackSilence(SynthesizeRequest request)
    {
        const int sampleRate = 24000;
        const short channels = 1;
        const short bitsPerSample = 16;

        var durationSeconds = Math.Clamp(request.Input.Length / 30.0, 0.5, 8.0);
        var sampleCount = (int)(sampleRate * durationSeconds);
        var dataSize = sampleCount * channels * bitsPerSample / 8;

        return new byte[dataSize];
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
            _kokoroEngine?.Dispose();
            _supertonicEngine?.Dispose();
            _kokoroEngine = null;
            _supertonicEngine = null;
        }

        _disposed = true;
    }
}
