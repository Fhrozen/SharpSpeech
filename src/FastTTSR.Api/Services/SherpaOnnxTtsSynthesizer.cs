using System.Text;
using FastTTSR.Api.Contracts;
using FastTTSR.Api.Models;

namespace FastTTSR.Api.Services;

public sealed class SherpaOnnxTtsSynthesizer : ITtsSynthesizer
{
    public Task<SynthesisResult> SynthesizeAsync(
        TtsModelDefinition model,
        string modelDirectory,
        OpenAiSpeechRequest request,
        CancellationToken cancellationToken)
    {
        // This placeholder keeps API compatibility while allowing the backend/frontend flow
        // to be tested end-to-end in environments without native sherpa-onnx binaries.
        // Replace with direct sherpa-onnx bindings or process invocation if required.
        var wav = GenerateSilentWave(durationSeconds: Math.Clamp(request.Input.Length / 30.0, 1, 8));
        return Task.FromResult(new SynthesisResult(wav, "audio/wav", $"{model.Name}.wav"));
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
