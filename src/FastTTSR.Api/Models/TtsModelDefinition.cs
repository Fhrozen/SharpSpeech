namespace FastTTSR.Api.Models;

public sealed class TtsModelDefinition
{
    public string Name { get; init; } = string.Empty;
    public string DisplayName { get; init; } = string.Empty;
    public string Description { get; init; } = string.Empty;
    public string Engine { get; init; } = string.Empty;
    public string ModelPath { get; init; } = string.Empty;
    public string? VoicesPath { get; init; }
    public string? TokensPath { get; init; }
    public IReadOnlyList<TtsModelAsset> Assets { get; init; } = [];
    public IReadOnlyList<string> SupportedLanguages { get; init; } = [];
    public IReadOnlyList<string> Speakers { get; init; } = [];
}
