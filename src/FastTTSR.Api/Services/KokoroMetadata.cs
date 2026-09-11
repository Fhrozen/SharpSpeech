using System.Collections.ObjectModel;

namespace SharpAudio.Api.Services;

public static class KokoroMetadata
{
    private static readonly string[] CanonicalLanguagesInternal =
    [
        "en-us",
        "en-gb",
        "es",
        "fr-fr",
        "hi",
        "it",
        "ja-jp",
        "pt-br",
        "zh-cn"
    ];

    private static readonly string[] CanonicalSpeakersInternal =
    [
        "af_alloy", "af_aoede", "af_bella", "af_heart", "af_jessica", "af_kore", "af_nicole", "af_nova",
        "af_river", "af_sarah", "af_sky",
        "am_adam", "am_echo", "am_eric", "am_fenrir", "am_liam", "am_michael", "am_onyx", "am_puck", "am_santa",
        "bf_alice", "bf_emma", "bf_isabella", "bf_lily",
        "bm_daniel", "bm_fable", "bm_george", "bm_lewis",
        "ef_dora", "em_alex", "em_santa",
        "ff_siwis",
        "hf_alpha", "hf_beta", "hm_omega", "hm_psi",
        "if_sara", "im_nicola",
        "jf_alpha", "jf_gongitsune", "jf_nezumi", "jf_tebukuro", "jm_kumo",
        "pf_dora", "pm_alex", "pm_santa",
        "zf_xiaobei", "zf_xiaoni", "zf_xiaoxiao", "zf_xiaoyi",
        "zm_yunjian", "zm_yunxi", "zm_yunxia", "zm_yunyang"
    ];

    private static readonly IReadOnlyDictionary<string, string> LanguageAliases = new ReadOnlyDictionary<string, string>(
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["a"] = "en-us",
            ["en"] = "en-us",
            ["en-us"] = "en-us",
            ["english"] = "en-us",

            ["b"] = "en-gb",
            ["en-gb"] = "en-gb",
            ["british-english"] = "en-gb",

            ["e"] = "es",
            ["es"] = "es",
            ["es-es"] = "es",
            ["spanish"] = "es",

            ["f"] = "fr-fr",
            ["fr"] = "fr-fr",
            ["fr-fr"] = "fr-fr",
            ["french"] = "fr-fr",

            ["h"] = "hi",
            ["hi"] = "hi",
            ["hi-in"] = "hi",
            ["hindi"] = "hi",

            ["i"] = "it",
            ["it"] = "it",
            ["it-it"] = "it",
            ["italian"] = "it",

            ["j"] = "ja-jp",
            ["ja"] = "ja-jp",
            ["ja-jp"] = "ja-jp",
            ["japanese"] = "ja-jp",

            ["p"] = "pt-br",
            ["pt"] = "pt-br",
            ["pt-br"] = "pt-br",
            ["portuguese"] = "pt-br",
            ["brazilian-portuguese"] = "pt-br",

            ["z"] = "zh-cn",
            ["zh"] = "zh-cn",
            ["zh-cn"] = "zh-cn",
            ["chinese"] = "zh-cn",
            ["mandarin"] = "zh-cn"
        });

    private static readonly IReadOnlyDictionary<string, string> EspeakVoices = new ReadOnlyDictionary<string, string>(
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["en-us"] = "en-us",
            ["en-gb"] = "en-gb",
            ["es"] = "es",
            ["fr-fr"] = "fr",
            ["hi"] = "hi",
            ["it"] = "it",
            ["ja-jp"] = "ja",
            ["pt-br"] = "pt-br",
            ["zh-cn"] = "zh"
        });

    private static readonly IReadOnlyDictionary<string, string> SpeakerAliases = new ReadOnlyDictionary<string, string>(
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["alloy"] = "am_v0adam",
            ["ash"] = "af_v0nicole",
            ["coral"] = "bf_v0emma",
            ["echo"] = "af_v0bella",
            ["fable"] = "af_sarah",
            ["onyx"] = "bm_george",
            ["nova"] = "bf_v0isabella",
            ["sage"] = "am_michael",
            ["shimmer"] = "af_sky"
        });

    public static IReadOnlyList<string> SupportedLanguages => CanonicalLanguagesInternal;

    public static IReadOnlyList<string> SupportedSpeakers => CanonicalSpeakersInternal;

    public static bool IsKokoroEngine(string? engine) =>
        string.Equals(engine, "kokoro", StringComparison.OrdinalIgnoreCase);

    public static bool TryNormalizeLanguage(string? language, out string normalizedLanguage)
    {
        if (string.IsNullOrWhiteSpace(language))
        {
            normalizedLanguage = CanonicalLanguagesInternal[0];
            return true;
        }

        var candidate = language.Trim().ToLowerInvariant();
        if (LanguageAliases.TryGetValue(candidate, out var aliasValue))
        {
            normalizedLanguage = aliasValue;
            return true;
        }

        if (CanonicalLanguagesInternal.Contains(candidate, StringComparer.OrdinalIgnoreCase))
        {
            normalizedLanguage = candidate;
            return true;
        }

        normalizedLanguage = string.Empty;
        return false;
    }

    public static bool TryMapToEspeakVoice(string normalizedLanguage, out string espeakVoice) =>
        EspeakVoices.TryGetValue(normalizedLanguage, out espeakVoice!);

    public static bool TryNormalizeSpeaker(string? speaker, out string normalizedSpeaker)
    {
        normalizedSpeaker = string.Empty;

        if (string.IsNullOrWhiteSpace(speaker))
        {
            return false;
        }

        var candidate = speaker.Trim();
        if (SpeakerAliases.TryGetValue(candidate, out var mapped))
        {
            normalizedSpeaker = mapped;
            return true;
        }

        var canonical = CanonicalSpeakersInternal.FirstOrDefault(s =>
            string.Equals(s, candidate, StringComparison.OrdinalIgnoreCase));

        if (canonical is null)
        {
            return false;
        }

        normalizedSpeaker = canonical;
        return true;
    }

    public static bool IsDefaultVoiceValue(string? value) =>
        string.IsNullOrWhiteSpace(value) ||
        string.Equals(value, "default", StringComparison.OrdinalIgnoreCase);
}
