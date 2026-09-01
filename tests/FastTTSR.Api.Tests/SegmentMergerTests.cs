using FastTTSR.Api.Models;
using FastTTSR.Api.Services;

namespace FastTTSR.Api.Tests;

public sealed class SegmentMergerTests
{
    [Fact]
    public void MergeShortSegments_leaves_segments_unchanged_when_all_meet_minimum_duration()
    {
        var segments = new[]
        {
            new TranscriptionSegment(1, 0.0, 1.0, "hello"),
            new TranscriptionSegment(2, 1.0, 2.5, "world")
        };

        var result = SegmentMerger.MergeShortSegments(segments, minDurationSeconds: 0.5);

        Assert.Equal(2, result.Count);
        Assert.Equal(segments[0], result[0]);
        Assert.Equal(segments[1], result[1]);
    }

    [Fact]
    public void MergeShortSegments_merges_a_short_middle_segment_into_the_previous_one()
    {
        var segments = new[]
        {
            new TranscriptionSegment(1, 0.0, 2.0, "hello"),
            new TranscriptionSegment(2, 2.0, 2.2, "uh"), // 0.2s - below the 0.5s threshold
            new TranscriptionSegment(3, 2.2, 4.0, "world")
        };

        var result = SegmentMerger.MergeShortSegments(segments, minDurationSeconds: 0.5);

        Assert.Equal(2, result.Count);
        Assert.Equal(1, result[0].Id);
        Assert.Equal(0.0, result[0].Start);
        Assert.Equal(2.2, result[0].End);
        Assert.Equal("hello uh", result[0].Text);
        Assert.Equal(2, result[1].Id);
        Assert.Equal("world", result[1].Text);
    }

    [Fact]
    public void MergeShortSegments_merges_a_short_first_segment_forward_into_the_next_one()
    {
        var segments = new[]
        {
            new TranscriptionSegment(1, 0.0, 0.2, "uh"), // 0.2s - below the 0.5s threshold, no predecessor
            new TranscriptionSegment(2, 0.2, 2.0, "hello world")
        };

        var result = SegmentMerger.MergeShortSegments(segments, minDurationSeconds: 0.5);

        Assert.Single(result);
        Assert.Equal(1, result[0].Id);
        Assert.Equal(0.0, result[0].Start);
        Assert.Equal(2.0, result[0].End);
        Assert.Equal("uh hello world", result[0].Text);
    }

    [Fact]
    public void MergeShortSegments_never_drops_text()
    {
        var segments = new[]
        {
            new TranscriptionSegment(1, 0.0, 0.1, "a"),
            new TranscriptionSegment(2, 0.1, 0.2, "b"),
            new TranscriptionSegment(3, 0.2, 0.3, "c"),
            new TranscriptionSegment(4, 0.3, 2.0, "d")
        };

        var result = SegmentMerger.MergeShortSegments(segments, minDurationSeconds: 0.5);

        var combinedText = string.Join(' ', result.Select(s => s.Text));
        Assert.Contains("a", combinedText);
        Assert.Contains("b", combinedText);
        Assert.Contains("c", combinedText);
        Assert.Contains("d", combinedText);
    }

    [Fact]
    public void MergeShortSegments_returns_input_unchanged_for_zero_or_one_segments()
    {
        Assert.Empty(SegmentMerger.MergeShortSegments([], 0.5));

        var single = new[] { new TranscriptionSegment(1, 0.0, 0.1, "hi") };
        var result = SegmentMerger.MergeShortSegments(single, 0.5);
        Assert.Same(single, result);
    }
}
