using System.Collections.Concurrent;
using System.Text;
using SharpAudio.Api.Contracts;
using SharpAudio.Api.Models;

namespace SharpAudio.Api.Services;

/// <summary>
/// ITtsSynthesizer implementation for the Supertonic-3 engine.
/// Manages a pool of <see cref="SupertonicTtsEngine"/> instances (one per model+directory
/// combination) and delegates synthesis to the appropriate engine.
/// </summary>
public sealed class SupertonicTtsSynthesizer : ITtsSynthesizer, IIdleTrackingSynthesizer, IDisposable
{
    private readonly ConcurrentDictionary<string, SupertonicTtsEngine> _engines = new();
    private readonly ConcurrentDictionary<string, DateTime> _lastAccessTimes = new();
    private readonly object _engineLock = new();
    private bool _disposed;

    public Task<SynthesisResult> SynthesizeAsync(
        TtsModelDefinition model,
        string modelDirectory,
        OpenAiSpeechRequest request,
        CancellationToken cancellationToken)
    {
        var startTime = DateTime.UtcNow;

        if (!SupertonicMetadata.IsSupertonic3Engine(model.Engine))
        {
            return Task.FromResult(GenerateFallbackSilence(model, request, startTime));
        }

        try
        {
            var engine = GetOrCreateEngine(model, modelDirectory);

            // Resolve language
            var lang = "en";
            if (SupertonicMetadata.TryNormalizeLanguage(request.Language, out var normalizedLang))
            {
                lang = normalizedLang;
            }

            // Resolve speaker
            var speaker = ResolveSpeaker(model, request);

            // Load voice style
            var style = LoadVoiceStyle(model, modelDirectory, speaker);

            var speed = Math.Clamp(request.Speed, 0.5f, 2.0f);

            cancellationToken.ThrowIfCancellationRequested();

            var audioBytes = engine.SynthesizeToBytes(request.Input, lang, style, speed: speed);

            if (audioBytes.Length == 0)
            {
                return Task.FromResult(GenerateFallbackSilence(model, request, startTime));
            }

            var sampleRate = engine.SampleRate;
            var wavBytes   = CreateWavFile(audioBytes, sampleRate, channels: 1, bitsPerSample: 16);

            var processingTime = (DateTime.UtcNow - startTime).TotalSeconds;
            var audioDuration  = CalculateAudioDuration(audioBytes.Length, sampleRate, channels: 1, bitsPerSample: 16);
            var charCount      = request.Input.Length;

            return Task.FromResult(new SynthesisResult(
                wavBytes,
                "audio/wav",
                $"{model.Name}.wav",
                processingTime,
                audioDuration,
                charCount));
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Supertonic-3 synthesis failed: {ex.Message}");
            return Task.FromResult(GenerateFallbackSilence(model, request, startTime));
        }
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Private helpers
    // ──────────────────────────────────────────────────────────────────────────

    private SupertonicTtsEngine GetOrCreateEngine(TtsModelDefinition model, string modelDirectory)
    {
        var cacheKey = $"{model.Name}:{modelDirectory}";
        var engine = _engines.GetOrAdd(cacheKey, _ =>
        {
            // modelPath for supertonic-3 is the relative path of the onnx sub-directory
            var onnxDir = Path.Combine(modelDirectory, model.ModelPath);
            if (!Directory.Exists(onnxDir))
            {
                throw new DirectoryNotFoundException($"ONNX directory not found: {onnxDir}");
            }

            Console.WriteLine($"[Supertonic-3] Creating engine for {model.Name} from: {onnxDir}");
            return new SupertonicTtsEngine(onnxDir);
        });
        
        // Update last access time
        _lastAccessTimes[cacheKey] = DateTime.UtcNow;
        
        return engine;
    }

    private static string ResolveSpeaker(TtsModelDefinition model, OpenAiSpeechRequest request)
    {
        var speaker = request.Speaker;
        if (SupertonicMetadata.IsDefaultVoiceValue(speaker))
        {
            speaker = request.Voice;
        }

        if (SupertonicMetadata.IsDefaultVoiceValue(speaker))
        {
            speaker = model.Speakers.FirstOrDefault();
        }

        if (string.IsNullOrWhiteSpace(speaker))
        {
            speaker = SupertonicMetadata.DefaultSpeaker;
        }

        return speaker;
    }

    private static SupertonicStyle LoadVoiceStyle(TtsModelDefinition model,
        string modelDirectory, string speaker)
    {
        var voicesPath    = model.VoicesPath ?? "voice_styles";
        var voicesDir     = Path.Combine(modelDirectory, voicesPath);
        var extension     = string.IsNullOrWhiteSpace(model.VoiceFileExtension) ? ".json" : model.VoiceFileExtension;
        var voiceFile     = Path.Combine(voicesDir, $"{speaker}{extension}");

        if (File.Exists(voiceFile))
        {
            return SupertonicTtsEngine.LoadVoiceStyle(voiceFile);
        }

        // Fallback: try any available speaker file
        var fallback = model.Speakers
            .Select(s => Path.Combine(voicesDir, $"{s}{extension}"))
            .FirstOrDefault(File.Exists);

        if (fallback != null)
        {
            Console.WriteLine($"[Supertonic-3] Voice '{speaker}' not found, using fallback: {Path.GetFileName(fallback)}");
            return SupertonicTtsEngine.LoadVoiceStyle(fallback);
        }

        // Last resort: return a zero-filled style so the engine still runs
        // (shape [1,1,1] is a placeholder — the engine will produce silence or noise)
        Console.WriteLine($"[Supertonic-3] No voice style files found in {voicesDir}; using zero style");
        return new SupertonicStyle([], [1, 1, 1], [], [1, 1, 1]);
    }

    private static byte[] CreateWavFile(byte[] pcmData, int sampleRate, short channels, short bitsPerSample)
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream, Encoding.ASCII, leaveOpen: true);

        var dataSize   = pcmData.Length;
        var byteRate   = sampleRate * channels * bitsPerSample / 8;
        var blockAlign = (short)(channels * bitsPerSample / 8);

        writer.Write(Encoding.ASCII.GetBytes("RIFF"));
        writer.Write(36 + dataSize);
        writer.Write(Encoding.ASCII.GetBytes("WAVE"));
        writer.Write(Encoding.ASCII.GetBytes("fmt "));
        writer.Write(16);
        writer.Write((short)1);   // PCM
        writer.Write(channels);
        writer.Write(sampleRate);
        writer.Write(byteRate);
        writer.Write(blockAlign);
        writer.Write(bitsPerSample);
        writer.Write(Encoding.ASCII.GetBytes("data"));
        writer.Write(dataSize);
        writer.Write(pcmData);

        writer.Flush();
        return stream.ToArray();
    }

    private static double CalculateAudioDuration(int pcmDataSize, int sampleRate,
        short channels, short bitsPerSample)
    {
        var bytesPerSample = bitsPerSample / 8;
        var sampleCount    = pcmDataSize / (channels * bytesPerSample);
        return (double)sampleCount / sampleRate;
    }

    private static SynthesisResult GenerateFallbackSilence(TtsModelDefinition model,
        OpenAiSpeechRequest request, DateTime startTime)
    {
        const int sampleRate = 22050;
        const short channels = 1;
        const short bitsPerSample = 16;
        const double charsPerSecond = 30.0;
        const double minDuration = 0.5;
        const double maxDuration = 8.0;

        var durationSeconds = Math.Clamp(request.Input.Length / charsPerSecond, minDuration, maxDuration);
        var sampleCount     = (int)(sampleRate * durationSeconds);
        var dataSize        = sampleCount * channels * bitsPerSample / 8;

        using var stream = new MemoryStream(44 + dataSize);
        using var writer = new BinaryWriter(stream, Encoding.ASCII, leaveOpen: true);

        var byteRate   = sampleRate * channels * bitsPerSample / 8;
        var blockAlign = (short)(channels * bitsPerSample / 8);

        writer.Write(Encoding.ASCII.GetBytes("RIFF"));
        writer.Write(36 + dataSize);
        writer.Write(Encoding.ASCII.GetBytes("WAVE"));
        writer.Write(Encoding.ASCII.GetBytes("fmt "));
        writer.Write(16);
        writer.Write((short)1);
        writer.Write(channels);
        writer.Write(sampleRate);
        writer.Write(byteRate);
        writer.Write(blockAlign);
        writer.Write(bitsPerSample);
        writer.Write(Encoding.ASCII.GetBytes("data"));
        writer.Write(dataSize);
        writer.Write(new byte[dataSize]);
        writer.Flush();

        var processingTime = (DateTime.UtcNow - startTime).TotalSeconds;

        return new SynthesisResult(
            stream.ToArray(),
            "audio/wav",
            $"{model.Name}.wav",
            processingTime,
            durationSeconds,
            request.Input.Length);
    }

    public IReadOnlyDictionary<string, DateTime> GetLoadedEngines()
    {
        return _lastAccessTimes.ToDictionary(kvp => kvp.Key, kvp => kvp.Value);
    }

    public bool TryReleaseEngine(string engineKey)
    {
        lock (_engineLock)
        {
            if (_engines.TryRemove(engineKey, out var engine))
            {
                try
                {
                    engine.Dispose();
                    _lastAccessTimes.TryRemove(engineKey, out _);
                    return true;
                }
                catch (Exception ex)
                {
                    Console.Error.WriteLine($"Error disposing Supertonic-3 engine '{engineKey}': {ex.Message}");
                    return false;
                }
            }
            return false;
        }
    }

    public void Dispose()
    {
        if (_disposed) return;

        foreach (var engine in _engines.Values)
        {
            engine.Dispose();
        }

        _engines.Clear();
        _lastAccessTimes.Clear();
        _disposed = true;
    }
}
