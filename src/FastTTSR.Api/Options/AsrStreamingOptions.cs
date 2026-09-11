namespace SharpAudio.Api.Options;

/// <summary>
/// Configuration for streaming ASR segment commits (mirrors <see cref="AsrWorkerOptions"/>'s
/// standalone-section pattern). A "segment" is committed - flushed to the client and its state
/// bounded/reset - after <see cref="DefaultSegmentSeconds"/> of audio or sooner on
/// sentence-ending punctuation; see <see cref="Services.SegmentBoundaryPolicy"/>.
/// </summary>
public sealed class AsrStreamingOptions
{
    public const string SectionName = "AsrStreamingOptions";

    /// <summary>Default max segment duration (seconds) when the client doesn't request one.</summary>
    public double DefaultSegmentSeconds { get; init; } = 10;

    /// <summary>Smallest max segment duration (seconds) a client may request.</summary>
    public double MinSegmentSeconds { get; init; } = 5;

    /// <summary>Largest max segment duration (seconds) a client may request.</summary>
    public double MaxSegmentSeconds { get; init; } = 30;
}
