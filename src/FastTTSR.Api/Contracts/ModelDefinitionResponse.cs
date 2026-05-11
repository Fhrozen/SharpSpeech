namespace FastTTSR.Api.Contracts;

/// <summary>
/// Detailed information about a TTS model
/// </summary>
/// <param name="Name">Model identifier (e.g., kokoro-q4)</param>
/// <param name="DisplayName">Human-readable model name</param>
/// <param name="Description">Model description and capabilities</param>
/// <param name="SupportedLanguages">List of supported language codes</param>
/// <param name="Speakers">List of available speaker voices</param>
public sealed record ModelDefinitionResponse(
    string Name,
    string DisplayName,
    string Description,
    IReadOnlyList<string> SupportedLanguages,
    IReadOnlyList<string> Speakers);
