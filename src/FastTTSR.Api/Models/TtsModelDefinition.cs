namespace FastTTSR.Api.Models;

public sealed record TtsModelDefinition(
    string Name,
    string DisplayName,
    string Description,
    string HuggingFaceRepository,
    string[] Files,
    IReadOnlyList<string> SupportedLanguages,
    IReadOnlyList<string> Speakers);
