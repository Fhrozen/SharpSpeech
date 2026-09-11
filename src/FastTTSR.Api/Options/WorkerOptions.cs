namespace SharpAudio.Api.Options;

/// <summary>
/// Configuration options for worker process management
/// </summary>
public sealed class WorkerOptions
{
    public const string SectionName = "WorkerOptions";

    /// <summary>
    /// Path to the worker executable
    /// </summary>
    public string ExecutablePath { get; init; } = "./SharpAudio.Worker";

    /// <summary>
    /// Idle timeout in seconds before worker self-terminates
    /// </summary>
    public int IdleTimeoutSeconds { get; init; } = 60;

    /// <summary>
    /// Starting port for worker gRPC servers
    /// </summary>
    public int PortRangeStart { get; init; } = 50051;

    /// <summary>
    /// Maximum number of ports to try before giving up
    /// </summary>
    public int MaxPortAttempts { get; init; } = 100;

    /// <summary>
    /// Timeout in seconds for worker startup
    /// </summary>
    public int StartupTimeoutSeconds { get; init; } = 30;

    /// <summary>
    /// Enable worker process mode (if false, uses in-process synthesizers)
    /// </summary>
    public bool Enabled { get; init; } = true;
}
