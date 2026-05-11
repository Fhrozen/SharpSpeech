using FastTTSR.Api.Models;

namespace FastTTSR.Api.Services;

public interface IModelCache
{
    Task<string> EnsureModelAsync(TtsModelDefinition model, CancellationToken cancellationToken);
}
