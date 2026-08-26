using FastTTSR.Api.Models;

namespace FastTTSR.Api.Services;

public interface IModelCache
{
    Task<string> EnsureModelAsync(TtsModelDefinition model, CancellationToken cancellationToken);

    Task<string> EnsureModelAsync(AsrModelDefinition model, CancellationToken cancellationToken);
}
