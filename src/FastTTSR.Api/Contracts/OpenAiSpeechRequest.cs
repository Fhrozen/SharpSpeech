namespace FastTTSR.Api.Contracts;

public sealed class OpenAiSpeechRequest
{
    public string Model { get; init; } = string.Empty;
    public string Input { get; init; } = string.Empty;
    public string Voice { get; init; } = "default";
    public string ResponseFormat { get; init; } = "wav";
    public float Speed { get; init; } = 1.0f;
    public string? Language { get; init; }
    public string? Speaker { get; init; }
}
