using FastTTSR.Api.Options;
using Microsoft.Extensions.Options;

namespace FastTTSR.Api.Services;

/// <summary>
/// Background service that releases idle in-process ASR engines (mirrors ModelIdleMonitorService).
/// Shares the same ModelIdleMonitorOptions/MODEL_IDLE_TIMEOUT_SECONDS config as the TTS monitor.
/// </summary>
public sealed class AsrModelIdleMonitorService : BackgroundService
{
    private readonly ILogger<AsrModelIdleMonitorService> _logger;
    private readonly ModelIdleMonitorOptions _options;
    private readonly WhisperAsrTranscriber _whisper;
    private readonly NemotronAsrTranscriber _nemotron;

    public AsrModelIdleMonitorService(
        ILogger<AsrModelIdleMonitorService> logger,
        IOptions<ModelIdleMonitorOptions> options,
        WhisperAsrTranscriber whisper,
        NemotronAsrTranscriber nemotron)
    {
        _logger = logger;
        _options = options.Value;
        _whisper = whisper;
        _nemotron = nemotron;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (_options.IdleTimeoutSeconds <= 0)
        {
            _logger.LogInformation("ASR model idle monitoring is disabled (IdleTimeoutSeconds = 0)");
            return;
        }

        _logger.LogInformation(
            "ASR model idle monitoring started. Timeout: {TimeoutSeconds}s, Check interval: {IntervalSeconds}s",
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
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in ASR model idle monitoring");
            }
        }

        _logger.LogInformation("ASR model idle monitoring stopped");
    }

    private void CheckAndReleaseIdleModels()
    {
        var now = DateTime.UtcNow;
        var timeout = TimeSpan.FromSeconds(_options.IdleTimeoutSeconds);

        CheckEngines(_whisper, "Whisper", now, timeout);
        CheckEngines(_nemotron, "Nemotron", now, timeout);
    }

    private void CheckEngines(IIdleTrackingTranscriber transcriber, string engineType, DateTime now, TimeSpan timeout)
    {
        var loadedEngines = transcriber.GetLoadedEngines();

        foreach (var (engineKey, lastAccessTime) in loadedEngines)
        {
            var idleTime = now - lastAccessTime;

            if (idleTime > timeout)
            {
                _logger.LogInformation(
                    "Releasing idle {EngineType} model '{EngineKey}' (idle for {IdleSeconds:F1}s)",
                    engineType, engineKey, idleTime.TotalSeconds);

                if (transcriber.TryReleaseEngine(engineKey))
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
