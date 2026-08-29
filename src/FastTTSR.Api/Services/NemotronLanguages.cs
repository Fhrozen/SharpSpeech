namespace FastTTSR.Api.Services;

/// <summary>
/// Nemotron's supported language/locale codes and their encoder-conditioning lang_id. This
/// mapping is NOT derivable from any file shipped with the model (vocab.txt's `&lt;xx-YY&gt;`
/// tag lines are a red herring - those are tokenizer language markers, not the encoder's lang_id
/// input). It is the model card's own fixed prompt dictionary. Feeding a vocab-tag line index
/// here instead (an earlier, incorrect implementation did this) desensitizes the encoder to the
/// actual audio content, since it injects a huge out-of-range conditioning id into every chunk.
/// </summary>
public static class NemotronLanguages
{
    private static readonly IReadOnlyDictionary<string, long> CodeToId = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase)
    {
        ["en"] = 0, ["en-US"] = 0,
        ["en-GB"] = 1,
        ["es-ES"] = 2,
        ["es"] = 3, ["es-US"] = 3,
        ["zh-CN"] = 4, ["zh"] = 4,
        ["hi"] = 6, ["hi-IN"] = 6,
        ["ar"] = 7, ["ar-AR"] = 7,
        ["fr"] = 8, ["fr-FR"] = 8,
        ["de"] = 9, ["de-DE"] = 9,
        ["ja"] = 10, ["ja-JP"] = 10,
        ["ru"] = 11, ["ru-RU"] = 11,
        ["pt-BR"] = 12,
        ["pt"] = 13, ["pt-PT"] = 13,
        ["ko"] = 14, ["ko-KR"] = 14,
        ["it"] = 15, ["it-IT"] = 15,
        ["nl"] = 16, ["nl-NL"] = 16,
        ["pl"] = 17, ["pl-PL"] = 17,
        ["tr"] = 18, ["tr-TR"] = 18,
        ["uk"] = 19, ["uk-UA"] = 19,
        ["ro"] = 20, ["ro-RO"] = 20,
        ["el"] = 21, ["el-GR"] = 21,
        ["cs"] = 22, ["cs-CZ"] = 22,
        ["hu"] = 23, ["hu-HU"] = 23,
        ["sv"] = 24, ["sv-SE"] = 24,
        ["da"] = 25, ["da-DK"] = 25,
        ["fi"] = 26, ["fi-FI"] = 26,
        ["sk"] = 28, ["sk-SK"] = 28,
        ["hr"] = 29, ["hr-HR"] = 29,
        ["bg"] = 30, ["bg-BG"] = 30,
        ["lt"] = 31, ["lt-LT"] = 31,
        ["th"] = 32, ["th-TH"] = 32,
        ["vi"] = 33, ["vi-VN"] = 33,
        ["et"] = 60, ["et-EE"] = 60,
        ["lv"] = 61, ["lv-LV"] = 61,
        ["sl"] = 62, ["sl-SI"] = 62,
        ["he"] = 64, ["he-IL"] = 64,
        ["fr-CA"] = 100,
        ["auto"] = 101,
        ["mt"] = 102, ["mt-MT"] = 102,
        ["nb"] = 103, ["nb-NO"] = 103,
        ["nn"] = 104, ["nn-NO"] = 104,
    };

    /// <summary>Resolves a language/locale code to Nemotron's lang_id, defaulting to English (0).</summary>
    public static long Resolve(string? language) =>
        !string.IsNullOrWhiteSpace(language) && CodeToId.TryGetValue(language, out var id) ? id : 0;
}
