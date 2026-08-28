using FastTTSR.Api.Models;

namespace FastTTSR.Api.Services;

/// <summary>Pre-downloads all ASR models on startup (mirrors ModelWarmupService).</summary>
public sealed class AsrModelWarmupService : BackgroundService
{
    private readonly IAsrModelCatalog _catalog;
    private readonly IModelCache _cache;
    private readonly ILogger<AsrModelWarmupService> _logger;

    public AsrModelWarmupService(IAsrModelCatalog catalog, IModelCache cache, ILogger<AsrModelWarmupService> logger)
    {
        _catalog = catalog;
        _cache = cache;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        foreach (AsrModelDefinition model in _catalog.GetSupportedModels())
        {
            try
            {
                await _cache.EnsureModelAsync(model, stoppingToken);
                _logger.LogInformation("ASR model {Model} is ready", model.Name);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to warm up ASR model {Model}", model.Name);
            }
        }
    }
}
