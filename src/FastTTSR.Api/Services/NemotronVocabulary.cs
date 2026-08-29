using System.Text;
using System.Text.RegularExpressions;

namespace FastTTSR.Api.Services;

/// <summary>
/// Reads Nemotron's vocab.txt (plain id-per-line piece list, one entry per RNNT vocabulary id -
/// identical ordering/content to tokenizer.json's `model.vocab`) and decodes RNNT token id
/// sequences to text. Language conditioning is NOT derived from this file - see
/// <see cref="NemotronLanguages"/> for the encoder's actual lang_id mapping.
/// </summary>
public sealed class NemotronVocabulary
{
    private readonly IReadOnlyList<string> _pieces;

    public long BlankId { get; }

    public NemotronVocabulary(string vocabPath)
    {
        _pieces = File.ReadAllLines(vocabPath);
        BlankId = _pieces.Count - 1; // last line is "<blank>"
    }

    private static readonly Regex LanguageTagPattern = new("^<[A-Za-z]{2}-[A-Za-z]{2}>$", RegexOptions.Compiled);

    public string Decode(IReadOnlyList<int> tokenIds) => Decode(tokenIds, out _);

    /// <summary>Decodes token ids to text, also surfacing the leading `&lt;xx-XX&gt;` language tag
    /// the model emits when run in auto-detect mode (stripped from the returned text either way).</summary>
    public string Decode(IReadOnlyList<int> tokenIds, out string? detectedLanguageTag)
    {
        detectedLanguageTag = null;
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
                if (detectedLanguageTag is null && LanguageTagPattern.IsMatch(piece))
                {
                    detectedLanguageTag = piece;
                }

                continue; // control/language tag, never part of the transcript
            }

            builder.Append(piece);
        }

        return builder.ToString().Replace('\u2581', ' ').Trim();
    }
}
