using FastTTSR.Api.Options;
using Microsoft.Extensions.Options;

namespace FastTTSR.Api.Services;

/// <summary>
/// Background service that monitors model usage and automatically releases
/// idle models from memory after a configurable timeout period.
/// </summary>
public sealed class ModelIdleMonitorService : BackgroundService
{
    private readonly ILogger<ModelIdleMonitorService> _logger;
    private readonly ModelIdleMonitorOptions _options;
    private readonly KokoroTtsSynthesizer _kokoroSynthesizer;
    private readonly SupertonicTtsSynthesizer _supertonicSynthesizer;

    public ModelIdleMonitorService(
        ILogger<ModelIdleMonitorService> logger,
        IOptions<ModelIdleMonitorOptions> options,
        KokoroTtsSynthesizer kokoroSynthesizer,
        SupertonicTtsSynthesizer supertonicSynthesizer)
    {
        _logger = logger;
        _options = options.Value;
        _kokoroSynthesizer = kokoroSynthesizer;
        _supertonicSynthesizer = supertonicSynthesizer;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // If timeout is 0, monitoring is disabled
        if (_options.IdleTimeoutSeconds <= 0)
        {
            _logger.LogInformation("Model idle monitoring is disabled (IdleTimeoutSeconds = 0)");
            return;
        }

        _logger.LogInformation(
            "Model idle monitoring started. Timeout: {TimeoutSeconds}s, Check interval: {IntervalSeconds}s",
            _options.IdleTimeoutSeconds,
            _options.CheckIntervalSeconds);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(TimeSpan.FromSeconds(_options.CheckIntervalSeconds), stoppingToken);
                CheckAndReleaseIdleModels();
            }
            catch (OperationCanceledException)
            {
                // Expected when stopping
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in model idle monitoring");
            }
        }

        _logger.LogInformation("Model idle monitoring stopped");
    }

    private void CheckAndReleaseIdleModels()
    {
        var now = DateTime.UtcNow;
        var timeout = TimeSpan.FromSeconds(_options.IdleTimeoutSeconds);

        // Check Kokoro engines
        CheckEngines(_kokoroSynthesizer, "Kokoro", now, timeout);

        // Check Supertonic engines
        CheckEngines(_supertonicSynthesizer, "Supertonic", now, timeout);
    }

    private void CheckEngines(IIdleTrackingSynthesizer synthesizer, string engineType, DateTime now, TimeSpan timeout)
    {
        var loadedEngines = synthesizer.GetLoadedEngines();
        
        foreach (var (engineKey, lastAccessTime) in loadedEngines)
        {
            var idleTime = now - lastAccessTime;
            
            if (idleTime > timeout)
            {
                _logger.LogInformation(
                    "Releasing idle {EngineType} model '{EngineKey}' (idle for {IdleSeconds:F1}s)",
                    engineType,
                    engineKey,
                    idleTime.TotalSeconds);

                if (synthesizer.TryReleaseEngine(engineKey))
                {
                    _logger.LogInformation("Successfully released {EngineType} model '{EngineKey}'", engineType, engineKey);
                }
                else
                {
                    _logger.LogWarning("Failed to release {EngineType} model '{EngineKey}'", engineType, engineKey);
                }
            }
        }
    }
}
