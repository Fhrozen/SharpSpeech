using System.Collections.Concurrent;
using System.Text;
using FastTTSR.Api.Contracts;
using FastTTSR.Api.Models;

namespace FastTTSR.Api.Services;

public sealed class KokoroTtsSynthesizer : ITtsSynthesizer, IDisposable
{
    private const string KokoroEngine = "kokoro";
    private readonly ConcurrentDictionary<string, KokoroTtsEngine> _engines = new();
    private bool _disposed;

    public Task<SynthesisResult> SynthesizeAsync(
        TtsModelDefinition model,
        string modelDirectory,
        OpenAiSpeechRequest request,
        CancellationToken cancellationToken)
    {
        var startTime = DateTime.UtcNow;
        
        // Only support Kokoro engine
        if (!string.Equals(model.Engine, KokoroEngine, StringComparison.OrdinalIgnoreCase))
        {
            return Task.FromResult(GenerateFallbackSilence(model, request, startTime));
        }

        try
        {
            var engine = GetOrCreateEngine(model, modelDirectory);
            var speed = Math.Clamp(request.Speed, 0.5f, 2.0f);
            var language = DetermineLanguage(request);
            
            // Load speaker voice if needed
            LoadSpeakerVoice(engine, model, modelDirectory, request);

            cancellationToken.ThrowIfCancellationRequested();

            // Synthesize audio
            var audioBytes = engine.SynthesizeToBytes(request.Input, language, speed);
            
            if (audioBytes.Length == 0)
            {
                return Task.FromResult(GenerateFallbackSilence(model, request, startTime));
            }

            // Generate WAV file with header
            var wavBytes = CreateWavFile(audioBytes, sampleRate: 24000, channels: 1, bitsPerSample: 16);
            
            // Calculate metrics
            var processingTime = (DateTime.UtcNow - startTime).TotalSeconds;
            var audioDuration = CalculateAudioDuration(audioBytes.Length, sampleRate: 24000, channels: 1, bitsPerSample: 16);
            var charCount = request.Input.Length;
            
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
            Console.Error.WriteLine($"Kokoro synthesis failed: {ex.Message}");
            return Task.FromResult(GenerateFallbackSilence(model, request, startTime));
        }
    }

    private KokoroTtsEngine GetOrCreateEngine(TtsModelDefinition model, string modelDirectory)
    {
        var cacheKey = $"{model.Name}:{modelDirectory}";
        
        return _engines.GetOrAdd(cacheKey, _ =>
        {
            var modelPath = Path.Combine(modelDirectory, model.ModelPath);
            
            if (!File.Exists(modelPath))
            {
                throw new FileNotFoundException($"Model file not found: {modelPath}");
            }

            Console.WriteLine($"Creating Kokoro engine for {model.Name} with model: {modelPath}");
            return new KokoroTtsEngine(modelPath);
        });
    }

    private void LoadSpeakerVoice(KokoroTtsEngine engine, TtsModelDefinition model, string modelDirectory, OpenAiSpeechRequest request)
    {
        var speaker = request.Speaker;
        if (KokoroMetadata.IsDefaultVoiceValue(speaker))
        {
            speaker = request.Voice;
        }
        if (KokoroMetadata.IsDefaultVoiceValue(speaker))
        {
            speaker = model.Speakers.FirstOrDefault();
        }

        if (string.IsNullOrWhiteSpace(speaker))
        {
            return;
        }

        var voicesPath = model.VoicesPath ?? "voices";
        var voicesDirectory = Path.Combine(modelDirectory, voicesPath);
        
        if (!Directory.Exists(voicesDirectory))
        {
            Console.WriteLine($"Voices directory not found: {voicesDirectory}");
            return;
        }

        var extension = string.IsNullOrWhiteSpace(model.VoiceFileExtension) ? ".bin" : model.VoiceFileExtension;
        var voiceFile = Path.Combine(voicesDirectory, $"{speaker}{extension}");

        if (File.Exists(voiceFile))
        {
            engine.LoadSpeakers(voiceFile);
        }
        else
        {
            // Try to find any voice file as fallback
            var anyVoiceFile = model.Speakers
                .Select(s => Path.Combine(voicesDirectory, $"{s}{extension}"))
                .FirstOrDefault(File.Exists);

            if (anyVoiceFile != null)
            {
                Console.WriteLine($"Using fallback voice: {Path.GetFileName(anyVoiceFile)}");
                engine.LoadSpeakers(anyVoiceFile);
            }
        }
    }

    private static string DetermineLanguage(OpenAiSpeechRequest request)
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

    private static SynthesisResult GenerateFallbackSilence(TtsModelDefinition model, OpenAiSpeechRequest request, DateTime startTime)
    {
        const int sampleRate = 24000;
        const short channels = 1;
        const short bitsPerSample = 16;

        var durationSeconds = Math.Clamp(request.Input.Length / 30.0, 0.5, 8.0);
        var sampleCount = (int)(sampleRate * durationSeconds);
        var dataSize = sampleCount * channels * bitsPerSample / 8;

        using var stream = new MemoryStream(44 + dataSize);
        using var writer = new BinaryWriter(stream, Encoding.ASCII, leaveOpen: true);

        var byteRate = sampleRate * channels * bitsPerSample / 8;
        var blockAlign = (short)(channels * bitsPerSample / 8);

        // WAV header
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
        var charCount = request.Input.Length;

        return new SynthesisResult(
            stream.ToArray(), 
            "audio/wav", 
            $"{model.Name}.wav",
            processingTime,
            durationSeconds,
            charCount);
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

        _engines.Clear();
        _disposed = true;
    }
}
