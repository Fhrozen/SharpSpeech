using FastTTSR.Api.Contracts;
using FastTTSR.Api.Models;
using FastTTSR.Api.Services;

namespace FastTTSR.Api.Tests;

public sealed class SherpaOnnxTtsSynthesizerTests
{
    private static readonly byte[] RiffHeader = [82, 73, 70, 70];

    [Fact]
    public async Task Returns_fallback_wav_when_in_process_sherpa_cannot_initialize()
    {
        var synthesizer = new SherpaOnnxTtsSynthesizer();
        var model = new TtsModelDefinition
        {
            Name = "kokoro-q4",
            Engine = "kokoro",
            ModelPath = "onnx/model_q4.onnx",
            VoicesPath = "voices",
            Speakers = ["af_bella"]
        };

        var modelDirectory = Path.Combine(Path.GetTempPath(), "non-existent-models");
        var result = await synthesizer.SynthesizeAsync(model, modelDirectory, new OpenAiSpeechRequest
        {
            Model = "kokoro-q4",
            Input = "hello world",
            Speaker = "af_bella"
        }, CancellationToken.None);

        Assert.Equal("audio/wav", result.ContentType);
        Assert.NotEmpty(result.AudioBytes);
        Assert.Equal(RiffHeader, result.AudioBytes.Take(4).ToArray());
    }
}
