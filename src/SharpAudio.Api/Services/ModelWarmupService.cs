using SharpAudio.Api.Models;

namespace SharpAudio.Api.Services;

public sealed class ModelWarmupService : BackgroundService
{
    private readonly IModelCatalog _catalog;
    private readonly IModelCache _cache;
    private readonly ILogger<ModelWarmupService> _logger;

    public ModelWarmupService(IModelCatalog catalog, IModelCache cache, ILogger<ModelWarmupService> logger)
    {
        _catalog = catalog;
        _cache = cache;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        foreach (TtsModelDefinition model in _catalog.GetSupportedModels())
        {
            try
            {
                await _cache.EnsureModelAsync(model, stoppingToken);
                _logger.LogInformation("Model {Model} is ready", model.Name);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to warm up model {Model}", model.Name);
            }
        }
    }
}
