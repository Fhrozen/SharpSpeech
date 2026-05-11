using System.Text;
using FastTTSR.Api.Contracts;
using FastTTSR.Api.Models;
using SherpaOnnx;

namespace FastTTSR.Api.Services;

public sealed class SherpaOnnxTtsSynthesizer : ITtsSynthesizer
{
    private const string KokoroEngine = "kokoro";
    private const string VitsEngine = "vits";

    public Task<SynthesisResult> SynthesizeAsync(
        TtsModelDefinition model,
        string modelDirectory,
        OpenAiSpeechRequest request,
        CancellationToken cancellationToken)
    {
        var bytes = TrySynthesizeWithSherpa(model, modelDirectory, request, cancellationToken);
        if (bytes is not null && bytes.Length > 0)
        {
            return Task.FromResult(new SynthesisResult(bytes, "audio/wav", $"{model.Name}.wav"));
        }

        var wav = GenerateSilentWave(durationSeconds: Math.Clamp(request.Input.Length / 30.0, 1, 8));
        return Task.FromResult(new SynthesisResult(wav, "audio/wav", $"{model.Name}.wav"));
    }

    private static byte[]? TrySynthesizeWithSherpa(
        TtsModelDefinition model,
        string modelDirectory,
        OpenAiSpeechRequest request,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        try
        {
            var speed = Math.Clamp(request.Speed, 0.5f, 2.0f);
            var sid = ResolveSpeakerId(model, request);
            using var tts = CreateOfflineTts(model, modelDirectory, request);
            if (tts is null)
            {
                return null;
            }

            var generated = tts.Generate(request.Input, speed, sid);
            cancellationToken.ThrowIfCancellationRequested();

            if (generated is null || generated.NumSamples <= 0)
            {
                return null;
            }

            var outputPath = Path.Combine(Path.GetTempPath(), $"fastttsr-{Guid.NewGuid():N}.wav");
            try
            {
                if (!generated.SaveToWaveFile(outputPath))
                {
                    return null;
                }

                return File.ReadAllBytes(outputPath);
            }
            finally
            {
                generated.Dispose();
                if (File.Exists(outputPath))
                {
                    File.Delete(outputPath);
                }
            }
        }
        catch
        {
            return null;
        }
    }

    private static OfflineTts? CreateOfflineTts(TtsModelDefinition model, string modelDirectory, OpenAiSpeechRequest request)
    {
        var config = new OfflineTtsConfig
        {
            Model = new OfflineTtsModelConfig
            {
                NumThreads = Math.Max(Environment.ProcessorCount, 1),
                Provider = "cpu"
            },
            MaxNumSentences = 2
        };

        if (string.Equals(model.Engine, KokoroEngine, StringComparison.OrdinalIgnoreCase))
        {
            config.Model.Kokoro = new OfflineTtsKokoroModelConfig
            {
                Model = Path.Combine(modelDirectory, model.ModelPath),
                Voices = ResolveKokoroVoicePath(model, modelDirectory, request),
                Tokens = string.IsNullOrWhiteSpace(model.TokensPath) ? string.Empty : Path.Combine(modelDirectory, model.TokensPath)
            };

            return new OfflineTts(config);
        }

        if (string.Equals(model.Engine, VitsEngine, StringComparison.OrdinalIgnoreCase))
        {
            config.Model.Vits = new OfflineTtsVitsModelConfig
            {
                Model = Path.Combine(modelDirectory, model.ModelPath),
                Tokens = Path.Combine(modelDirectory, model.TokensPath ?? "tokens.txt")
            };

            return new OfflineTts(config);
        }

        return null;
    }

    private static int ResolveSpeakerId(TtsModelDefinition model, OpenAiSpeechRequest request)
    {
        var speaker = request.Speaker ?? request.Voice;
        if (string.IsNullOrWhiteSpace(speaker))
        {
            return 0;
        }

        var sid = model.Speakers
            .Select((s, i) => new { Speaker = s, Index = i })
            .FirstOrDefault(x => string.Equals(x.Speaker, speaker, StringComparison.OrdinalIgnoreCase))
            ?.Index ?? 0;

        return Math.Max(sid, 0);
    }

    private static string ResolveKokoroVoicePath(TtsModelDefinition model, string modelDirectory, OpenAiSpeechRequest request)
    {
        var configuredPath = model.VoicesPath ?? "voices.bin";
        var voicePath = Path.Combine(modelDirectory, configuredPath);
        var extension = string.IsNullOrWhiteSpace(model.VoiceFileExtension) ? ".bin" : model.VoiceFileExtension;
        if (!Directory.Exists(voicePath))
        {
            return voicePath;
        }

        var speaker = request.Speaker ?? request.Voice;
        var selectedVoiceFile = Path.Combine(voicePath, $"{speaker}{extension}");
        if (File.Exists(selectedVoiceFile))
        {
            return selectedVoiceFile;
        }

        var defaultVoiceFile = model.Speakers.Select(s => Path.Combine(voicePath, $"{s}{extension}")).FirstOrDefault(File.Exists);
        return defaultVoiceFile ?? voicePath;
    }

    private static byte[] GenerateSilentWave(double durationSeconds)
    {
        const short channels = 1;
        const short bitsPerSample = 16;
        const int sampleRate = 24000;

        var sampleCount = (int)(sampleRate * durationSeconds);
        var dataSize = sampleCount * channels * bitsPerSample / 8;

        using var stream = new MemoryStream(44 + dataSize);
        using var writer = new BinaryWriter(stream, Encoding.ASCII, leaveOpen: true);

        writer.Write(Encoding.ASCII.GetBytes("RIFF"));
        writer.Write(36 + dataSize);
        writer.Write(Encoding.ASCII.GetBytes("WAVE"));
        writer.Write(Encoding.ASCII.GetBytes("fmt "));
        writer.Write(16);
        writer.Write((short)1);
        writer.Write(channels);
        writer.Write(sampleRate);
        writer.Write(sampleRate * channels * bitsPerSample / 8);
        writer.Write((short)(channels * bitsPerSample / 8));
        writer.Write(bitsPerSample);
        writer.Write(Encoding.ASCII.GetBytes("data"));
        writer.Write(dataSize);

        writer.Write(new byte[dataSize]);
        writer.Flush();

        return stream.ToArray();
    }
}
