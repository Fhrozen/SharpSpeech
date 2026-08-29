using System.Text;

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
