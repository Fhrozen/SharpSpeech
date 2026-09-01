namespace FastTTSR.Api.IntegrationTests.Support;

/// <summary>Checks whether a real published FastTTSR.Worker.Asr executable exists at the path
/// AsrWorkerOptions would use, so worker-mode tests can skip gracefully in environments (e.g. the
/// plain SDK image used by ./dotnet.sh, which never publishes the worker projects) that don't have
/// it, instead of failing when WorkerProcessManager can't spawn a process.</summary>
public static class WorkerAsrExecutableGate
{
    public const string SkipReason = "Skipped: no published FastTTSR.Worker.Asr executable found next to the API output.";

    public static bool IsAvailable => ResolveExecutablePath() is { } path && File.Exists(path);

    private static string? ResolveExecutablePath()
    {
        var configured = Environment.GetEnvironmentVariable("AsrWorkerOptions__ExecutablePath") ?? "./worker-asr/FastTTSR.Worker.Asr";
        var candidate = Path.Combine(AppContext.BaseDirectory, configured);
        if (File.Exists(candidate))
        {
            return candidate;
        }

        // On Windows-published outputs (not expected in this repo's Docker images, but harmless to check).
        var withExe = candidate + ".exe";
        return File.Exists(withExe) ? withExe : candidate;
    }
}
