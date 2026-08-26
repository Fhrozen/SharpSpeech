namespace FastTTSR.Api.Models;

public sealed record TranscriptionResult(
    string Text,
    string? DetectedLanguage,
    double ProcessingTimeSeconds,
    double AudioDurationSeconds,
    int CharacterCount)
{
    public double Rtf => ProcessingTimeSeconds / AudioDurationSeconds;
}
