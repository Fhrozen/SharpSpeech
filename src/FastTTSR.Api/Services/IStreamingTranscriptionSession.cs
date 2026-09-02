namespace FastTTSR.Api.Services;

/// <summary>One incremental update from a streaming session: <paramref name="Text"/> is always
/// scoped to the CURRENT segment only (never the whole conversation, to keep every message
/// bounded in size regardless of utterance length). <paramref name="IsSegmentFinal"/> is true
/// exactly when this segment has just been committed (time limit or sentence-ending punctuation
/// reached) - the caller should append it to permanent history and start a fresh segment.</summary>
public readonly record struct StreamingUpdate(string Text, bool IsSegmentFinal);

/// <summary>
/// A stateful, per-connection incremental transcription session for streaming ASR over
/// WebSocket - accumulates raw PCM16 mono audio chunks (at <see cref="SampleRate"/>) and returns
/// updates scoped to the current segment (see <see cref="StreamingUpdate"/>) as they become
/// available, periodically committing a segment so no single message ever carries the whole
/// growing conversation.
/// </summary>
public interface IStreamingTranscriptionSession : IDisposable
{
    /// <summary>Sample rate the session expects incoming raw PCM16 audio chunks to already be at.</summary>
    int SampleRate { get; }

    /// <summary>Feeds a chunk of raw 16-bit PCM mono audio, returning zero or more updates
    /// produced by this chunk (usually 0 or 1, but a segment-final commit can be followed by the
    /// start of a new partial in the same call).</summary>
    Task<IReadOnlyList<StreamingUpdate>> ProcessChunkAsync(byte[] pcm16Chunk, CancellationToken cancellationToken);

    /// <summary>Flushes any buffered audio and returns the trailing, not-yet-committed segment's
    /// text only (previously-committed segments were already delivered via <see cref="ProcessChunkAsync"/>).</summary>
    Task<string> FinishAsync(CancellationToken cancellationToken);
}
