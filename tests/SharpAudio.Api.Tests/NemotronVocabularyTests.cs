using FastTTSR.Api.Services;

namespace FastTTSR.Api.Tests;

public sealed class NemotronVocabularyTests
{
    private static NemotronVocabulary CreateVocabulary(out int helloId, out int worldId, out int languageTagId, out int blankId)
    {
        var path = Path.Combine(Path.GetTempPath(), $"nemotron-vocab-{Guid.NewGuid()}.txt");
        // "▁hello" / "▁world" as SentencePiece pieces, one language tag, blank last.
        File.WriteAllLines(path, ["\u2581hello", "\u2581world", "<en-US>", "<blank>"]);

        helloId = 0;
        worldId = 1;
        languageTagId = 2;
        blankId = 3;

        return new NemotronVocabulary(path);
    }

    [Fact]
    public void Decode_joins_pieces_and_strips_blank()
    {
        var vocabulary = CreateVocabulary(out var helloId, out var worldId, out _, out var blankId);

        var text = vocabulary.Decode([helloId, worldId, blankId]);

        Assert.Equal("hello world", text);
    }

    [Fact]
    public void Decode_extracts_leading_language_tag_without_including_it_in_text()
    {
        var vocabulary = CreateVocabulary(out var helloId, out _, out var languageTagId, out _);

        var text = vocabulary.Decode([languageTagId, helloId], out var detectedTag);

        Assert.Equal("hello", text);
        Assert.Equal("<en-US>", detectedTag);
    }

    [Fact]
    public void Decode_without_out_param_still_ignores_language_tag()
    {
        var vocabulary = CreateVocabulary(out var helloId, out _, out var languageTagId, out _);

        var text = vocabulary.Decode([languageTagId, helloId]);

        Assert.Equal("hello", text);
    }

    [Fact]
    public void IsWordStart_true_for_piece_with_sentencepiece_marker_false_for_continuation()
    {
        var path = Path.Combine(Path.GetTempPath(), $"nemotron-vocab-{Guid.NewGuid()}.txt");
        // "▁believe" split into a word-start piece ("▁belie") and a continuation piece ("ve").
        File.WriteAllLines(path, ["\u2581belie", "ve", "<blank>"]);
        var vocabulary = new NemotronVocabulary(path);

        Assert.True(vocabulary.IsWordStart(0));  // "▁belie"
        Assert.False(vocabulary.IsWordStart(1)); // "ve" - continues the previous word
    }
}
