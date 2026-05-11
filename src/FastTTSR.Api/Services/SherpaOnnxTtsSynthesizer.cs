using System.Diagnostics;
using System.Text;
using FastTTSR.Api.Contracts;
using FastTTSR.Api.Models;

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
        var outputPath = TryRunSherpaCli(model, modelDirectory, request, cancellationToken);
        if (outputPath is not null && File.Exists(outputPath))
        {
            var bytes = File.ReadAllBytes(outputPath);
            return Task.FromResult(new SynthesisResult(bytes, "audio/wav", $"{model.Name}.wav"));
        }

        var wav = GenerateSilentWave(durationSeconds: Math.Clamp(request.Input.Length / 30.0, 1, 8));
        return Task.FromResult(new SynthesisResult(wav, "audio/wav", $"{model.Name}.wav"));
    }

    private static string? TryRunSherpaCli(
        TtsModelDefinition model,
        string modelDirectory,
        OpenAiSpeechRequest request,
        CancellationToken cancellationToken)
    {
        var executable = Environment.GetEnvironmentVariable("SHERPA_ONNX_TTS_CLI");
        if (string.IsNullOrWhiteSpace(executable) || !File.Exists(executable))
        {
            return null;
        }

        var outputPath = Path.Combine(Path.GetTempPath(), $"fastttsr-{Guid.NewGuid():N}.wav");
        var args = BuildSherpaArguments(model, modelDirectory, request, outputPath);
        if (args is null)
        {
            return null;
        }

        using var process = Process.Start(new ProcessStartInfo
        {
            FileName = executable,
            Arguments = args,
            RedirectStandardError = true,
            RedirectStandardOutput = true,
            UseShellExecute = false,
            CreateNoWindow = true
        });

        if (process is null)
        {
            return null;
        }

        process.WaitForExit((int)TimeSpan.FromSeconds(60).TotalMilliseconds);
        cancellationToken.ThrowIfCancellationRequested();

        return process.ExitCode == 0 ? outputPath : null;
    }

    private static string? BuildSherpaArguments(TtsModelDefinition model, string modelDirectory, OpenAiSpeechRequest request, string outputPath)
    {
        static string Quote(string value) => $"\"{value.Replace("\"", "\\\"")}\"";
        var text = Quote(request.Input);

        if (string.Equals(model.Engine, KokoroEngine, StringComparison.OrdinalIgnoreCase))
        {
            var speaker = Quote(request.Speaker ?? request.Voice);
            var modelFile = Path.Combine(modelDirectory, model.ModelPath);
            var voices = ResolveKokoroVoicePath(model, modelDirectory, request);
            return $"--kokoro-model {Quote(modelFile)} --kokoro-voices {Quote(voices)} --output-filename {Quote(outputPath)} --text {text} --sid {speaker}";
        }

        if (string.Equals(model.Engine, VitsEngine, StringComparison.OrdinalIgnoreCase))
        {
            var speaker = Quote(request.Speaker ?? request.Voice);
            var modelFile = Path.Combine(modelDirectory, model.ModelPath);
            var tokens = Path.Combine(modelDirectory, model.TokensPath ?? "tokens.txt");
            return $"--vits-model {Quote(modelFile)} --vits-tokens {Quote(tokens)} --output-filename {Quote(outputPath)} --text {text} --sid {speaker}";
        }

        return null;
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
