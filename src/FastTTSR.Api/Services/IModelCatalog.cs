using FastTTSR.Api.Models;

namespace FastTTSR.Api.Services;

public interface IModelCatalog
{
    IReadOnlyCollection<TtsModelDefinition> GetSupportedModels();
    bool TryGetModel(string modelName, out TtsModelDefinition? model);
}
