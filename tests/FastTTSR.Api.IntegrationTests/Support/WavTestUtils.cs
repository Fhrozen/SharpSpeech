namespace FastTTSR.Api.IntegrationTests.Support;

/// <summary>Concatenates several PCM16 WAV clips (same sample rate/channels) into one long WAV,
/// inserting a short silence gap between clips - used to build the long conversation test audio.</summary>
public static class WavTestUtils
{
    public static byte[] Concatenate(IReadOnlyList<byte[]> clips, double silenceGapSeconds = 0.3)
    {
        var (sampleRate, channels, bitsPerSample) = ReadFormat(clips[0]);
        var bytesPerFrame = channels * (bitsPerSample / 8);
        var silenceBytes = new byte[(int)(silenceGapSeconds * sampleRate) * bytesPerFrame];

        using var pcm = new MemoryStream();
        for (var i = 0; i < clips.Count; i++)
        {
            var data = ReadData(clips[i]);
            pcm.Write(data, 0, data.Length);

            if (i < clips.Count - 1)
            {
                pcm.Write(silenceBytes, 0, silenceBytes.Length);
            }
        }

        return BuildWav(pcm.ToArray(), sampleRate, channels, bitsPerSample);
    }

    private static (int SampleRate, short Channels, short BitsPerSample) ReadFormat(byte[] wav)
    {
        var pos = 12;
        while (pos + 8 <= wav.Length)
        {
            var chunkId = System.Text.Encoding.ASCII.GetString(wav, pos, 4);
            var chunkSize = BitConverter.ToInt32(wav, pos + 4);
            var dataStart = pos + 8;

            if (chunkId == "fmt ")
            {
                var channels = BitConverter.ToInt16(wav, dataStart + 2);
                var sampleRate = BitConverter.ToInt32(wav, dataStart + 4);
                var bitsPerSample = BitConverter.ToInt16(wav, dataStart + 14);
                return (sampleRate, channels, bitsPerSample);
            }

            pos = dataStart + chunkSize + (chunkSize % 2);
        }

        throw new InvalidDataException("WAV fmt chunk not found.");
    }

    private static byte[] ReadData(byte[] wav)
    {
        var pos = 12;
        while (pos + 8 <= wav.Length)
        {
            var chunkId = System.Text.Encoding.ASCII.GetString(wav, pos, 4);
            var chunkSize = BitConverter.ToInt32(wav, pos + 4);
            var dataStart = pos + 8;

            if (chunkId == "data")
            {
                var data = new byte[chunkSize];
                Array.Copy(wav, dataStart, data, 0, chunkSize);
                return data;
            }

            pos = dataStart + chunkSize + (chunkSize % 2);
        }

        throw new InvalidDataException("WAV data chunk not found.");
    }

    private static byte[] BuildWav(byte[] pcmData, int sampleRate, short channels, short bitsPerSample)
    {
        using var stream = new MemoryStream(44 + pcmData.Length);
        using var writer = new BinaryWriter(stream);

        var byteRate = sampleRate * channels * bitsPerSample / 8;
        var blockAlign = (short)(channels * bitsPerSample / 8);

        writer.Write("RIFF"u8.ToArray());
        writer.Write(36 + pcmData.Length);
        writer.Write("WAVE"u8.ToArray());
        writer.Write("fmt "u8.ToArray());
        writer.Write(16);
        writer.Write((short)1);
        writer.Write(channels);
        writer.Write(sampleRate);
        writer.Write(byteRate);
        writer.Write(blockAlign);
        writer.Write(bitsPerSample);
        writer.Write("data"u8.ToArray());
        writer.Write(pcmData.Length);
        writer.Write(pcmData);

        return stream.ToArray();
    }
}
