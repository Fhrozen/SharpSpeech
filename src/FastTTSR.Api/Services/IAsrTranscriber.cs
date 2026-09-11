using SharpAudio.Api.Contracts;
using SharpAudio.Api.Models;

namespace SharpAudio.Api.Services;

public interface IAsrTranscriber
{
    Task<TranscriptionResult> TranscribeAsync(
        AsrModelDefinition model,
        string modelDirectory,
        AudioTranscriptionRequest request,
        byte[] audioBytes,
        CancellationToken cancellationToken);

    /// <summary>Creates a stateful streaming session for live transcription over WebSocket - works
    /// identically whether ASR runs in-process or in worker mode (the worker-mode implementation
    /// opens a duplex gRPC call under the hood). <paramref name="segmentSeconds"/> is the max
    /// segment duration before a forced commit (see <see cref="SegmentBoundaryPolicy"/>).</summary>
    Task<IStreamingTranscriptionSession> CreateStreamingSessionAsync(
        AsrModelDefinition model,
        string modelDirectory,
        string? language,
        bool enableVad,
        double segmentSeconds,
        CancellationToken cancellationToken);
}
