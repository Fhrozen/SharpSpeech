using FastTTSR.Api.Models;

namespace FastTTSR.Api.Services;

/// <summary>Merges segments shorter than a minimum duration into a neighbor so no transcribed
/// text is ever silently dropped.</summary>
public static class SegmentMerger
{
    public static IReadOnlyList<TranscriptionSegment> MergeShortSegments(
        IReadOnlyList<TranscriptionSegment> segments, double minDurationSeconds)
    {
        if (segments.Count <= 1)
        {
            return segments;
        }

        var merged = new List<TranscriptionSegment>();
        foreach (var segment in segments)
        {
            if (merged.Count > 0 && segment.End - segment.Start < minDurationSeconds)
            {
                var previous = merged[^1];
                merged[^1] = previous with { End = segment.End, Text = $"{previous.Text} {segment.Text}".Trim() };
            }
            else
            {
                merged.Add(segment);
            }
        }

        // The very first segment had no predecessor to merge into above - merge it forward.
        if (merged.Count > 1 && merged[0].End - merged[0].Start < minDurationSeconds)
        {
            var first = merged[0];
            var second = merged[1];
            merged[1] = second with { Start = first.Start, Text = $"{first.Text} {second.Text}".Trim() };
            merged.RemoveAt(0);
        }

        for (var i = 0; i < merged.Count; i++)
        {
            merged[i] = merged[i] with { Id = i + 1 };
        }

        return merged;
    }
}
