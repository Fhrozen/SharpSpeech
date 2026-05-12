namespace FastTTSR.Api.Contracts;

/// <summary>
/// Speaker metadata with display name and description
/// </summary>
/// <param name="Id">Speaker identifier (e.g., M1, F1)</param>
/// <param name="Name">Human-readable speaker name (e.g., Alex, Sarah)</param>
/// <param name="Description">Speaker voice characteristics description</param>
public sealed record SpeakerMetadata(
    string Id,
    string Name,
    string Description);

/// <summary>
/// Detailed information about a TTS model
/// </summary>
/// <param name="Name">Model identifier (e.g., kokoro-q4)</param>
/// <param name="DisplayName">Human-readable model name</param>
/// <param name="Description">Model description and capabilities</param>
/// <param name="SupportedLanguages">List of supported language codes</param>
/// <param name="Speakers">List of available speaker voices</param>
/// <param name="SpeakerMetadata">Optional metadata about speakers (names and descriptions)</param>
public sealed record ModelDefinitionResponse(
    string Name,
    string DisplayName,
    string Description,
    IReadOnlyList<string> SupportedLanguages,
    IReadOnlyList<string> Speakers,
    IReadOnlyList<SpeakerMetadata>? SpeakerMetadata = null);
