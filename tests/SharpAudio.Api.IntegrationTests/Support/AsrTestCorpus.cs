using System.Text.RegularExpressions;

namespace FastTTSR.Api.IntegrationTests.Support;

public sealed record AsrTestSample(string Id, string Type, string? Speaker, string Text);

public sealed record ConversationTurn(string Speaker, string Text);

public sealed record AsrConversationSample(string Id, IReadOnlyList<ConversationTurn> Turns);

/// <summary>Parses TestData/test_text.md into short/long text samples and conversation scripts.</summary>
public static class AsrTestCorpus
{
    private static readonly Regex HeadingPattern = new(
        @"^##\s+(?<id>[\w-]+)\s+\(type:\s*(?<type>\w+)(?:,\s*speaker:\s*(?<speaker>\w+))?\)\s*$",
        RegexOptions.Compiled);

    private static readonly Regex TurnPattern = new(@"^\[(?<speaker>\w+)\]\s+(?<text>.+)$", RegexOptions.Compiled);

    public static (IReadOnlyList<AsrTestSample> Samples, IReadOnlyList<AsrConversationSample> Conversations) Load()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "TestData", "test_text.md");
        var lines = File.ReadAllLines(path);

        var samples = new List<AsrTestSample>();
        var conversations = new List<AsrConversationSample>();

        string? currentId = null;
        string? currentType = null;
        string? currentSpeaker = null;
        var bodyLines = new List<string>();

        void FlushCurrent()
        {
            if (currentId is null || currentType is null)
            {
                return;
            }

            if (currentType == "conversation")
            {
                var turns = bodyLines
                    .Select(l => TurnPattern.Match(l))
                    .Where(m => m.Success)
                    .Select(m => new ConversationTurn(m.Groups["speaker"].Value, m.Groups["text"].Value.Trim()))
                    .ToArray();

                conversations.Add(new AsrConversationSample(currentId, turns));
            }
            else
            {
                var text = string.Join(' ', bodyLines.Where(l => !string.IsNullOrWhiteSpace(l))).Trim();
                samples.Add(new AsrTestSample(currentId, currentType, currentSpeaker, text));
            }
        }

        foreach (var rawLine in lines)
        {
            var line = rawLine.TrimEnd();
            var headingMatch = HeadingPattern.Match(line);

            if (headingMatch.Success)
            {
                FlushCurrent();
                currentId = headingMatch.Groups["id"].Value;
                currentType = headingMatch.Groups["type"].Value;
                currentSpeaker = headingMatch.Groups["speaker"].Success ? headingMatch.Groups["speaker"].Value : null;
                bodyLines = [];
            }
            else if (currentId is not null && !line.StartsWith('#') && !line.StartsWith('>'))
            {
                bodyLines.Add(line);
            }
        }

        FlushCurrent();

        return (samples, conversations);
    }
}
