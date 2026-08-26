using FastTTSR.Api.Contracts;
using FastTTSR.Api.Models;

namespace FastTTSR.Api.Services;

public interface IAsrTranscriber
{
    Task<TranscriptionResult> TranscribeAsync(
        AsrModelDefinition model,
        string modelDirectory,
        AudioTranscriptionRequest request,
        byte[] audioBytes,
        CancellationToken cancellationToken);
}
