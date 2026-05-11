namespace FastTTSR.Api.Models;

public sealed record SynthesisResult(
    byte[] AudioBytes, 
    string ContentType, 
    string FileName,
    double ProcessingTimeSeconds,
    double AudioDurationSeconds,
    int CharacterCount)
{
    public double CharsPerSecond => CharacterCount / ProcessingTimeSeconds;
    public double RTF => ProcessingTimeSeconds / AudioDurationSeconds;
}
