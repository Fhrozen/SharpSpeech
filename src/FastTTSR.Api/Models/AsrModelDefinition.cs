namespace FastTTSR.Api.Models;

public sealed class AsrModelDefinition
{
    public string Name { get; init; } = string.Empty;
    public string DisplayName { get; init; } = string.Empty;
    public string Description { get; init; } = string.Empty;
    public string Engine { get; init; } = string.Empty;
    public string ModelPath { get; init; } = string.Empty;
    public IReadOnlyList<ModelAsset> Assets { get; init; } = [];
    public IReadOnlyList<string> SupportedLanguages { get; init; } = [];
    public bool SupportsLanguageAutoDetect { get; init; }
}
