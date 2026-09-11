namespace SharpAudio.Api.Services;

/// <summary>
/// Decides when a streaming ASR session should commit (flush/bound) its current segment - shared
/// by <see cref="NemotronAsrEngine.StreamingSession"/> and <see cref="WhisperStreamingSession"/>
/// so both engines apply the exact same time/punctuation policy.
/// </summary>
public static class SegmentBoundaryPolicy
{
    // Below this, a trailing "." doesn't force a commit - avoids fragmenting on short interjections.
    private const double MinSegmentSecondsForPunctuation = 2.0;

    private static readonly char[] SentenceEndings = ['.', '!', '?'];

    /// <summary>True if the current segment should be committed now.</summary>
    public static bool ShouldCommit(double segmentElapsedSeconds, double maxSegmentSeconds, string currentSegmentText)
    {
        if (segmentElapsedSeconds >= maxSegmentSeconds)
        {
            return true;
        }

        return segmentElapsedSeconds >= MinSegmentSecondsForPunctuation
            && currentSegmentText.Length > 0
            && SentenceEndings.Contains(currentSegmentText[^1]);
    }
}
