using System.Diagnostics;

namespace FastTTSR.Api.IntegrationTests.Support;

/// <summary>Checks whether the `ffmpeg` binary is available on PATH, so ffmpeg-dependent tests can
/// skip gracefully in environments (e.g. the plain SDK image used by ./dotnet.sh) that don't have
/// it installed, instead of failing.</summary>
public static class FfmpegTestGate
{
    private static readonly Lazy<bool> Available = new(CheckAvailable);

    public static bool IsAvailable => Available.Value;

    public const string SkipReason = "Skipped: ffmpeg binary not found on PATH.";

    private static bool CheckAvailable()
    {
        try
        {
            using var process = Process.Start(new ProcessStartInfo
            {
                FileName = "ffmpeg",
                Arguments = "-version",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false
            });

            process?.WaitForExit(5000);
            return process is { ExitCode: 0 };
        }
        catch
        {
            return false;
        }
    }
}
