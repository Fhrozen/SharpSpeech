namespace FastTTSR.Api.Contracts;

/// <summary>
/// Detailed information about an ASR model
/// </summary>
/// <param name="Name">Model identifier (e.g., whisper-base)</param>
/// <param name="DisplayName">Human-readable model name</param>
/// <param name="Description">Model description and capabilities</param>
/// <param name="SupportedLanguages">List of supported language codes</param>
/// <param name="SupportsLanguageAutoDetect">Whether the model can auto-detect the spoken language</param>
public sealed record AsrModelDefinitionResponse(
    string Name,
    string DisplayName,
    string Description,
    IReadOnlyList<string> SupportedLanguages,
    bool SupportsLanguageAutoDetect);
