using System.Text.RegularExpressions;

namespace FastTTSR.Api.IntegrationTests.Support;

/// <summary>Word Error Rate: word-level edit distance normalized by reference word count.</summary>
public static class WordErrorRate
{
    private static readonly Regex NonWord = new(@"[^\w\s]", RegexOptions.Compiled);

    public static double Compute(string reference, string hypothesis)
    {
        var refWords = Normalize(reference);
        var hypWords = Normalize(hypothesis);

        if (refWords.Length == 0)
        {
            return hypWords.Length == 0 ? 0 : 1;
        }

        var dp = new int[refWords.Length + 1, hypWords.Length + 1];
        for (var i = 0; i <= refWords.Length; i++) dp[i, 0] = i;
        for (var j = 0; j <= hypWords.Length; j++) dp[0, j] = j;

        for (var i = 1; i <= refWords.Length; i++)
        {
            for (var j = 1; j <= hypWords.Length; j++)
            {
                dp[i, j] = refWords[i - 1] == hypWords[j - 1]
                    ? dp[i - 1, j - 1]
                    : 1 + Math.Min(dp[i - 1, j - 1], Math.Min(dp[i - 1, j], dp[i, j - 1]));
            }
        }

        return (double)dp[refWords.Length, hypWords.Length] / refWords.Length;
    }

    private static string[] Normalize(string text) =>
        NonWord.Replace(text.ToLowerInvariant(), "")
            .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
}
