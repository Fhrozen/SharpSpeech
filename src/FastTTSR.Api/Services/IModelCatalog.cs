using SharpAudio.Api.Models;

namespace SharpAudio.Api.Services;

public interface IModelCatalog
{
    IReadOnlyCollection<TtsModelDefinition> GetSupportedModels();
    bool TryGetModel(string modelName, out TtsModelDefinition? model);
}
