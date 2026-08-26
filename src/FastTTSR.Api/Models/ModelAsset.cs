namespace FastTTSR.Api.Models;

/// <summary>Downloadable file shared by both TTS and ASR model definitions.</summary>
public sealed class ModelAsset
{
    public string RelativePath { get; init; } = string.Empty;
    public string Url { get; init; } = string.Empty;
}
