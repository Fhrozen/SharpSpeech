namespace FastTTSR.Api.Contracts;

/// <summary>
/// Parsed form fields from a POST /v1/audio/transcriptions request (the audio file itself is
/// handled separately as a byte array).
/// </summary>
public sealed class AudioTranscriptionRequest
{
    public string Model { get; init; } = string.Empty;
    public string? Language { get; init; }
    public string ResponseFormat { get; init; } = "json";
    public bool EnableVad { get; init; }
}
