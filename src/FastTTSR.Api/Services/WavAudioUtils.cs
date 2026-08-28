namespace FastTTSR.Api.Services;

/// <summary>Minimal WAV (RIFF/PCM) reader/resampler - just enough for ASR input, no external audio library.</summary>
public static class WavAudioUtils
{
    public static double GetDurationSeconds(byte[] wavBytes)
    {
        if (!TryReadHeader(wavBytes, out var sampleRate, out var channels, out var bitsPerSample, out _, out var dataSize) ||
            sampleRate <= 0 || channels <= 0 || bitsPerSample <= 0)
        {
            return 0;
        }

        var bytesPerSample = bitsPerSample / 8;
        var sampleCount = dataSize / (channels * bytesPerSample);
        return (double)sampleCount / sampleRate;
    }

    /// <summary>Decodes 16-bit PCM WAV bytes to mono float samples resampled to <paramref name="targetSampleRate"/>.</summary>
    public static float[] ReadMonoFloat(byte[] wavBytes, int targetSampleRate)
    {
        if (!TryReadHeader(wavBytes, out var sampleRate, out var channels, out var bitsPerSample, out var dataOffset, out var dataSize) ||
            bitsPerSample != 16)
        {
            throw new NotSupportedException("Only 16-bit PCM WAV audio is supported.");
        }

        var bytesPerSample = bitsPerSample / 8;
        var frameCount = dataSize / (channels * bytesPerSample);
        var mono = new float[frameCount];

        for (var i = 0; i < frameCount; i++)
        {
            var sum = 0;
            for (var c = 0; c < channels; c++)
            {
                var offset = dataOffset + (i * channels + c) * bytesPerSample;
                sum += BitConverter.ToInt16(wavBytes, offset);
            }

            mono[i] = (sum / (float)channels) / 32768f;
        }

        return sampleRate == targetSampleRate ? mono : Resample(mono, sampleRate, targetSampleRate);
    }

    /// <summary>Re-encodes arbitrary PCM16 WAV bytes as mono 16kHz PCM16 WAV - Whisper.net requires
    /// exactly 16kHz input and does not resample internally.</summary>
    public static byte[] ResampleToMono16kWav(byte[] wavBytes)
    {
        var samples = ReadMonoFloat(wavBytes, targetSampleRate: 16000);
        var pcm = new byte[samples.Length * 2];

        for (var i = 0; i < samples.Length; i++)
        {
            var value = (short)(Math.Clamp(samples[i], -1f, 1f) * short.MaxValue);
            var bytes = BitConverter.GetBytes(value);
            pcm[i * 2] = bytes[0];
            pcm[i * 2 + 1] = bytes[1];
        }

        using var stream = new MemoryStream(44 + pcm.Length);
        using var writer = new BinaryWriter(stream);

        const int sampleRate = 16000;
        const short channels = 1;
        const short bitsPerSample = 16;
        var byteRate = sampleRate * channels * bitsPerSample / 8;
        var blockAlign = (short)(channels * bitsPerSample / 8);

        writer.Write("RIFF"u8.ToArray());
        writer.Write(36 + pcm.Length);
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
        writer.Write(pcm.Length);
        writer.Write(pcm);

        return stream.ToArray();
    }

    private static float[] Resample(float[] samples, int fromRate, int toRate)
    {
        if (samples.Length == 0 || fromRate == toRate)
        {
            return samples;
        }

        var outLength = (int)((long)samples.Length * toRate / fromRate);
        var result = new float[outLength];

        for (var i = 0; i < outLength; i++)
        {
            var srcPos = (double)i * fromRate / toRate;
            var srcIndex = (int)srcPos;
            var frac = (float)(srcPos - srcIndex);

            var a = samples[Math.Min(srcIndex, samples.Length - 1)];
            var b = samples[Math.Min(srcIndex + 1, samples.Length - 1)];
            result[i] = a + (b - a) * frac;
        }

        return result;
    }

    private static bool TryReadHeader(byte[] wav, out int sampleRate, out short channels, out short bitsPerSample, out int dataOffset, out int dataSize)
    {
        sampleRate = 0;
        channels = 0;
        bitsPerSample = 0;
        dataOffset = 0;
        dataSize = 0;

        if (wav.Length < 44 || wav[0] != 'R' || wav[1] != 'I' || wav[2] != 'F' || wav[3] != 'F')
        {
            return false;
        }

        var pos = 12; // skip RIFF header + "WAVE"
        while (pos + 8 <= wav.Length)
        {
            var chunkId = System.Text.Encoding.ASCII.GetString(wav, pos, 4);
            var chunkSize = BitConverter.ToInt32(wav, pos + 4);
            var chunkDataStart = pos + 8;

            if (chunkId == "fmt " && chunkDataStart + 16 <= wav.Length)
            {
                channels = BitConverter.ToInt16(wav, chunkDataStart + 2);
                sampleRate = BitConverter.ToInt32(wav, chunkDataStart + 4);
                bitsPerSample = BitConverter.ToInt16(wav, chunkDataStart + 14);
            }
            else if (chunkId == "data")
            {
                dataOffset = chunkDataStart;
                dataSize = chunkSize;
            }

            pos = chunkDataStart + chunkSize + (chunkSize % 2);
        }

        return sampleRate > 0 && channels > 0 && bitsPerSample > 0 && dataSize > 0;
    }
}
