using FastTTSR.Api.Contracts;
using FastTTSR.Api.Models;

namespace FastTTSR.Api.Services;

/// <summary>
/// Routes synthesis requests to the correct <see cref="ITtsSynthesizer"/> implementation
/// based on the model's engine name. This preserves the single-interface DI contract while
/// supporting multiple TTS backends (Kokoro, Supertonic-3, …).
/// </summary>
public sealed class TtsSynthesizerRouter : ITtsSynthesizer, IDisposable
{
    private readonly KokoroTtsSynthesizer _kokoro;
    private readonly SupertonicTtsSynthesizer _supertonic;
    private bool _disposed;

    public TtsSynthesizerRouter(
        KokoroTtsSynthesizer kokoro,
        SupertonicTtsSynthesizer supertonic)
    {
        _kokoro     = kokoro;
        _supertonic = supertonic;
    }

    public Task<SynthesisResult> SynthesizeAsync(
        TtsModelDefinition model,
        string modelDirectory,
        OpenAiSpeechRequest request,
        CancellationToken cancellationToken)
    {
        if (SupertonicMetadata.IsSupertonic3Engine(model.Engine))
        {
            return _supertonic.SynthesizeAsync(model, modelDirectory, request, cancellationToken);
        }

        // Default: Kokoro (and any future unrecognised engine falls back to Kokoro's
        // built-in graceful silence generation)
        return _kokoro.SynthesizeAsync(model, modelDirectory, request, cancellationToken);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _kokoro.Dispose();
        _supertonic.Dispose();
        _disposed = true;
    }
}
