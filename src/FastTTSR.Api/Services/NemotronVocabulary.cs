using System.Text;
using System.Text.RegularExpressions;

namespace FastTTSR.Api.Services;

/// <summary>
/// Reads Nemotron's vocab.txt (plain id-per-line piece list) and decodes RNNT token id sequences
/// to text. Also resolves a language code to its vocab-embedded language-tag id (e.g. "en-US" -&gt;
/// the id of the "&lt;en-US&gt;" line), which doubles as the encoder's lang_id conditioning input -
/// no separate tokenizer.json parsing is needed.
/// </summary>
public sealed class NemotronVocabulary
{
    private static readonly Regex LanguageTagPattern = new("^<[a-zA-Z]{2}(-[A-Z]{2})?>$", RegexOptions.Compiled);

    private readonly IReadOnlyList<string> _pieces;
    private readonly IReadOnlyDictionary<string, long> _languageTagIds;

    public long BlankId { get; }

    public NemotronVocabulary(string vocabPath)
    {
        _pieces = File.ReadAllLines(vocabPath);
        BlankId = _pieces.Count - 1; // last line is "<blank>"

        var languageTags = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < _pieces.Count; i++)
        {
            if (LanguageTagPattern.IsMatch(_pieces[i]))
            {
                var code = _pieces[i].Trim('<', '>');
                languageTags[code] = i;
            }
        }

        _languageTagIds = languageTags;
    }

    public long ResolveLanguageId(string? language, long defaultLanguageId)
    {
        if (string.IsNullOrWhiteSpace(language))
        {
            return defaultLanguageId;
        }

        if (_languageTagIds.TryGetValue(language, out var exact))
        {
            return exact;
        }

        // Fall back to a prefix match (e.g. "en" -> "<en-US>") if no exact tag exists.
        foreach (var (code, id) in _languageTagIds)
        {
            if (code.StartsWith(language, StringComparison.OrdinalIgnoreCase))
            {
                return id;
            }
        }

        return defaultLanguageId;
    }

    public string Decode(IReadOnlyList<int> tokenIds)
    {
        var builder = new StringBuilder();

        foreach (var id in tokenIds)
        {
            if (id < 0 || id >= _pieces.Count || id == BlankId)
            {
                continue;
            }

            var piece = _pieces[id];
            if (piece.StartsWith('<') && piece.EndsWith('>'))
            {
                continue; // control/language tag, never part of the transcript
            }

            builder.Append(piece);
        }

        return builder.ToString().Replace('\u2581', ' ').Trim();
    }
}
