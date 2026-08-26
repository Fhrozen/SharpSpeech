using FastTTSR.Api.Models;

namespace FastTTSR.Api.Services;

public interface IAsrModelCatalog
{
    IReadOnlyCollection<AsrModelDefinition> GetSupportedModels();
    bool TryGetModel(string modelName, out AsrModelDefinition? model);
}
