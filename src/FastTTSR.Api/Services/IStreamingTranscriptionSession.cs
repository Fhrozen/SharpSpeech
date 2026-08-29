namespace FastTTSR.Api.Services;

/// <summary>
/// A stateful, per-connection incremental transcription session for streaming ASR over
/// WebSocket - accumulates raw PCM16 mono audio chunks (at <see cref="SampleRate"/>) and returns
/// the full transcript-so-far as it becomes available, rather than a delta, so the client can
/// simply replace the displayed text on every message.
/// </summary>
public interface IStreamingTranscriptionSession : IDisposable
{
    /// <summary>Sample rate the session expects incoming raw PCM16 audio chunks to already be at.</summary>
    int SampleRate { get; }

    /// <summary>Feeds a chunk of raw 16-bit PCM mono audio, returning the updated full
    /// transcript-so-far, or null if nothing changed since the last call.</summary>
    Task<string?> ProcessChunkAsync(byte[] pcm16Chunk, CancellationToken cancellationToken);

    /// <summary>Flushes any buffered audio and returns the final complete transcript.</summary>
    Task<string> FinishAsync(CancellationToken cancellationToken);
}
