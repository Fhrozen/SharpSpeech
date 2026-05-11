namespace FastTTSR.Api.Contracts;

public sealed record ModelDefinitionResponse(
    string Name,
    string DisplayName,
    string Description,
    IReadOnlyList<string> SupportedLanguages,
    IReadOnlyList<string> Speakers);
