using SharpAudio.Api.IntegrationTests.Support;
using SharpAudio.Api.Services;
using Xunit;

namespace SharpAudio.Api.IntegrationTests;

/// <summary>
/// Exercises AudioFormatConverter directly (no HTTP, no downloaded ASR model weights) against
/// real ffmpeg-encoded FLAC/MP3 fixtures, proving non-WAV uploads normalize correctly. Unlike
/// AsrCircularTests, this needs no ASR_MODEL_TESTS opt-in - only ffmpeg's presence (see
/// FfmpegTestGate) - so it runs whenever the test image has ffmpeg installed.
/// </summary>
public sealed class AudioFormatConverterTests
{
    [SkippableTheory]
    [InlineData("flac")]
    [InlineData("mp3")]
    public async Task ToPcm16WavAsync_normalizes_non_wav_uploads(string format)
    {
        Skip.IfNot(FfmpegTestGate.IsAvailable, FfmpegTestGate.SkipReason);

        var wavBytes = GenerateSineWav(durationSeconds: 1.0, sampleRate: 16000);
        var encodedBytes = await FfmpegTestEncoder.EncodeAsync(wavBytes, format);

        var normalized = await AudioFormatConverter.ToPcm16WavAsync(encodedBytes, CancellationToken.None);

        var durationSeconds = WavAudioUtils.GetDurationSeconds(normalized);
        Assert.True(durationSeconds is > 0.8 and < 1.2,
            $"Normalized {format} duration {durationSeconds:F2}s should be close to the original 1.0s.");
    }

    private static byte[] GenerateSineWav(double durationSeconds, int sampleRate)
    {
        var sampleCount = (int)(durationSeconds * sampleRate);
        var pcm = new byte[sampleCount * 2];
        for (var i = 0; i < sampleCount; i++)
        {
            var value = (short)(short.MaxValue * 0.25 * Math.Sin(2 * Math.PI * 440.0 * i / sampleRate));
            var bytes = BitConverter.GetBytes(value);
            pcm[i * 2] = bytes[0];
            pcm[i * 2 + 1] = bytes[1];
        }

        using var stream = new MemoryStream(44 + pcm.Length);
        using var writer = new BinaryWriter(stream);
        var byteRate = sampleRate * 2;

        writer.Write("RIFF"u8.ToArray());
        writer.Write(36 + pcm.Length);
        writer.Write("WAVE"u8.ToArray());
        writer.Write("fmt "u8.ToArray());
        writer.Write(16);
        writer.Write((short)1);
        writer.Write((short)1);
        writer.Write(sampleRate);
        writer.Write(byteRate);
        writer.Write((short)2);
        writer.Write((short)16);
        writer.Write("data"u8.ToArray());
        writer.Write(pcm.Length);
        writer.Write(pcm);

        return stream.ToArray();
    }
}
