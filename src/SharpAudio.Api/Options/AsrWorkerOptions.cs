namespace SharpAudio.Api.Options;

/// <summary>
/// Configuration options for the ASR worker process (mirrors <see cref="WorkerOptions"/>, kept as
/// a separate section/class so TTS and ASR workers can be configured independently).
/// </summary>
public sealed class AsrWorkerOptions
{
    public const string SectionName = "AsrWorkerOptions";

    /// <summary>Path to the ASR worker executable.</summary>
    public string ExecutablePath { get; init; } = "./worker-asr/SharpAudio.Worker.Asr";

    /// <summary>Idle timeout in seconds before the worker self-terminates.</summary>
    public int IdleTimeoutSeconds { get; init; } = 60;

    /// <summary>Starting port for the ASR worker's gRPC server (distinct range from TTS's 50051+).</summary>
    public int PortRangeStart { get; init; } = 50151;

    /// <summary>Maximum number of ports to try before giving up.</summary>
    public int MaxPortAttempts { get; init; } = 100;

    /// <summary>Timeout in seconds for worker startup.</summary>
    public int StartupTimeoutSeconds { get; init; } = 45;

    /// <summary>Enable ASR worker process mode (if false, uses in-process transcribers).</summary>
    public bool Enabled { get; init; } = true;
}
