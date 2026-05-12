namespace FastTTSR.Api.Options;

public sealed class ModelIdleMonitorOptions
{
    public const string SectionName = "ModelIdleMonitor";

    /// <summary>
    /// Number of seconds of inactivity before a model is released from memory.
    /// Set to 0 to disable automatic model release.
    /// Can be overridden by MODEL_IDLE_TIMEOUT_SECONDS environment variable.
    /// </summary>
    public int IdleTimeoutSeconds { get; set; } = GetDefaultIdleTimeout();

    /// <summary>
    /// How often the monitor service checks for idle models (in seconds).
    /// </summary>
    public int CheckIntervalSeconds { get; set; } = 10;

    private static int GetDefaultIdleTimeout()
    {
        var envValue = Environment.GetEnvironmentVariable("MODEL_IDLE_TIMEOUT_SECONDS");
        if (int.TryParse(envValue, out var timeout) && timeout >= 0)
        {
            return timeout;
        }
        return 60; // Default to 60 seconds
    }
}
