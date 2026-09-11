using System.Diagnostics;

namespace FastTTSR.Api.IntegrationTests.Support;

/// <summary>Test-only helper that encodes WAV bytes into another container/codec via ffmpeg -
/// the reverse direction of production's AudioFormatConverter - used to build real FLAC/MP3
/// fixtures for tests instead of shipping binary test assets.</summary>
public static class FfmpegTestEncoder
{
    public static async Task<byte[]> EncodeAsync(byte[] wavBytes, string format)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = "ffmpeg",
            ArgumentList = { "-hide_banner", "-loglevel", "error", "-i", "pipe:0", "-f", format, "pipe:1" },
            UseShellExecute = false,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };

        using var process = Process.Start(startInfo) ?? throw new InvalidOperationException("Failed to start ffmpeg.");

        var stdoutTask = CopyToArrayAsync(process.StandardOutput.BaseStream);
        var stderrTask = process.StandardError.ReadToEndAsync();

        try
        {
            await process.StandardInput.BaseStream.WriteAsync(wavBytes);
            await process.StandardInput.BaseStream.FlushAsync();
        }
        finally
        {
            process.StandardInput.Close();
        }

        var output = await stdoutTask;
        var stderr = await stderrTask;
        await process.WaitForExitAsync();

        if (process.ExitCode != 0 || output.Length == 0)
        {
            throw new InvalidOperationException($"Test fixture encode to '{format}' failed: {stderr}");
        }

        return output;
    }

    private static async Task<byte[]> CopyToArrayAsync(Stream stream)
    {
        using var buffer = new MemoryStream();
        await stream.CopyToAsync(buffer);
        return buffer.ToArray();
    }
}
