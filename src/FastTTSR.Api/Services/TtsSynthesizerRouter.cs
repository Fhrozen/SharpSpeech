using SharpAudio.Api.Contracts;
using SharpAudio.Api.Models;

namespace SharpAudio.Api.Services;

/// <summary>
/// Routes synthesis requests to the correct <see cref="ITtsSynthesizer"/> implementation
/// based on the model's engine name. This preserves the single-interface DI contract while
/// supporting multiple TTS backends (Kokoro, Supertonic-3, …).
/// </summary>
public sealed class TtsSynthesizerRouter : ITtsSynthesizer, IIdleTrackingSynthesizer, IDisposable
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

    public IReadOnlyDictionary<string, DateTime> GetLoadedEngines()
    {
        // Combine engines from both synthesizers
        var allEngines = new Dictionary<string, DateTime>();
        
        foreach (var kvp in _kokoro.GetLoadedEngines())
        {
            allEngines[kvp.Key] = kvp.Value;
        }
        
        foreach (var kvp in _supertonic.GetLoadedEngines())
        {
            allEngines[kvp.Key] = kvp.Value;
        }
        
        return allEngines;
    }

    public bool TryReleaseEngine(string engineKey)
    {
        // Try both synthesizers (only one will have it)
        return _kokoro.TryReleaseEngine(engineKey) || _supertonic.TryReleaseEngine(engineKey);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _kokoro.Dispose();
        _supertonic.Dispose();
        _disposed = true;
    }
}
