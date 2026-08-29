namespace FastTTSR.Api.Contracts;

/// <summary>
/// Detailed information about an ASR model
/// </summary>
/// <param name="Name">Model identifier (e.g., whisper-base)</param>
/// <param name="DisplayName">Human-readable model name</param>
/// <param name="Description">Model description and capabilities</param>
/// <param name="SupportedLanguages">List of supported language codes</param>
/// <param name="SupportsLanguageAutoDetect">Whether the model can auto-detect the spoken language</param>
/// <param name="SupportsVad">Whether the model supports voice-activity-detection gating (skips inference on silent audio chunks)</param>
/// <param name="SupportsStreaming">Whether the model supports live streaming transcription over WebSocket</param>
public sealed record AsrModelDefinitionResponse(
    string Name,
    string DisplayName,
    string Description,
    IReadOnlyList<string> SupportedLanguages,
    bool SupportsLanguageAutoDetect,
    bool SupportsVad,
    bool SupportsStreaming);
