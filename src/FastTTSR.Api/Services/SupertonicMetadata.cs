using System.Collections.ObjectModel;

namespace FastTTSR.Api.Services;

public static class SupertonicMetadata
{
    private static readonly string[] CanonicalLanguagesInternal =
    [
        "en", "ko", "ja", "ar", "bg", "cs", "da", "de", "el", "es",
        "et", "fi", "fr", "hi", "hr", "hu", "id", "it", "lt", "lv",
        "nl", "pl", "pt", "ro", "ru", "sk", "sl", "sv", "tr", "uk", "vi"
    ];

    private static readonly string[] CanonicalSpeakersInternal =
    [
        "M1", "M2", "M3", "M4", "M5",
        "F1", "F2", "F3", "F4", "F5"
    ];

    private static readonly IReadOnlyDictionary<string, string> SpeakerNames =
        new ReadOnlyDictionary<string, string>(
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["M1"] = "Alex",
                ["M2"] = "James",
                ["M3"] = "Robert",
                ["M4"] = "Sam",
                ["M5"] = "Daniel",
                ["F1"] = "Sarah",
                ["F2"] = "Lily",
                ["F3"] = "Jessica",
                ["F4"] = "Olivia",
                ["F5"] = "Emily"
            });

    private static readonly IReadOnlyDictionary<string, string> SpeakerDescriptions =
        new ReadOnlyDictionary<string, string>(
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["M1"] = "A lively, upbeat male voice with confident energy and a standard, clear tone.",
                ["M2"] = "A deep, robust male voice; calm, composed, and serious with a grounded presence.",
                ["M3"] = "A polished, authoritative male voice; confident and trustworthy with strong presentation quality.",
                ["M4"] = "A soft, neutral-toned male voice; gentle and approachable with a youthful, friendly quality.",
                ["M5"] = "A warm, soft-spoken male voice; calm and soothing with a natural storytelling quality.",
                ["F1"] = "A calm female voice with a slightly low tone; steady and composed.",
                ["F2"] = "A bright, cheerful female voice; lively, playful, and youthful with spirited energy.",
                ["F3"] = "A clear, professional announcer-style female voice; articulate and broadcast-ready.",
                ["F4"] = "A crisp, confident female voice; distinct and expressive with strong delivery.",
                ["F5"] = "A kind, gentle female voice; soft-spoken, calm, and naturally soothing."
            });

    private static readonly IReadOnlyDictionary<string, string> LanguageAliases =
        new ReadOnlyDictionary<string, string>(
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["english"]    = "en",
                ["korean"]     = "ko",
                ["japanese"]   = "ja",
                ["arabic"]     = "ar",
                ["bulgarian"]  = "bg",
                ["czech"]      = "cs",
                ["danish"]     = "da",
                ["german"]     = "de",
                ["greek"]      = "el",
                ["spanish"]    = "es",
                ["estonian"]   = "et",
                ["finnish"]    = "fi",
                ["french"]     = "fr",
                ["hindi"]      = "hi",
                ["croatian"]   = "hr",
                ["hungarian"]  = "hu",
                ["indonesian"] = "id",
                ["italian"]    = "it",
                ["lithuanian"] = "lt",
                ["latvian"]    = "lv",
                ["dutch"]      = "nl",
                ["polish"]     = "pl",
                ["portuguese"] = "pt",
                ["romanian"]   = "ro",
                ["russian"]    = "ru",
                ["slovak"]     = "sk",
                ["slovenian"]  = "sl",
                ["swedish"]    = "sv",
                ["turkish"]    = "tr",
                ["ukrainian"]  = "uk",
                ["vietnamese"] = "vi"
            });

    public const string EngineName = "supertonic-3";
    public const string DefaultSpeaker = "M1";

    public static IReadOnlyList<string> SupportedLanguages => CanonicalLanguagesInternal;
    public static IReadOnlyList<string> SupportedSpeakers  => CanonicalSpeakersInternal;

    public static string GetSpeakerName(string? speakerId)
    {
        if (string.IsNullOrWhiteSpace(speakerId))
            return "Unknown";

        return SpeakerNames.TryGetValue(speakerId, out var name) ? name : speakerId;
    }

    public static string GetSpeakerDescription(string? speakerId)
    {
        if (string.IsNullOrWhiteSpace(speakerId))
            return string.Empty;

        return SpeakerDescriptions.TryGetValue(speakerId, out var description) ? description : string.Empty;
    }

    public static bool IsSupertonic3Engine(string? engine) =>
        string.Equals(engine, EngineName, StringComparison.OrdinalIgnoreCase);

    public static bool TryNormalizeLanguage(string? language, out string normalizedLanguage)
    {
        if (string.IsNullOrWhiteSpace(language))
        {
            normalizedLanguage = CanonicalLanguagesInternal[0]; // "en"
            return true;
        }

        var candidate = language.Trim().ToLowerInvariant();

        if (CanonicalLanguagesInternal.Contains(candidate, StringComparer.OrdinalIgnoreCase))
        {
            normalizedLanguage = candidate;
            return true;
        }

        if (LanguageAliases.TryGetValue(candidate, out var aliasValue))
        {
            normalizedLanguage = aliasValue;
            return true;
        }

        normalizedLanguage = string.Empty;
        return false;
    }

    public static bool TryNormalizeSpeaker(string? speaker, out string normalizedSpeaker)
    {
        normalizedSpeaker = string.Empty;

        if (string.IsNullOrWhiteSpace(speaker))
        {
            return false;
        }

        var candidate = CanonicalSpeakersInternal.FirstOrDefault(s =>
            string.Equals(s, speaker.Trim(), StringComparison.OrdinalIgnoreCase));

        if (candidate is null)
        {
            return false;
        }

        normalizedSpeaker = candidate;
        return true;
    }

    public static bool IsDefaultVoiceValue(string? value) =>
        string.IsNullOrWhiteSpace(value) ||
        string.Equals(value, "default", StringComparison.OrdinalIgnoreCase);
}
