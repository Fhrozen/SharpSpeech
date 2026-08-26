namespace FastTTSR.Api.Services;

/// <summary>Minimal WAV (RIFF/PCM) header reader, just enough to compute audio duration for metrics.</summary>
public static class WavAudioUtils
{
    public static double GetDurationSeconds(byte[] wavBytes)
    {
        if (!TryReadHeader(wavBytes, out var sampleRate, out var channels, out var bitsPerSample, out var dataSize) ||
            sampleRate <= 0 || channels <= 0 || bitsPerSample <= 0)
        {
            return 0;
        }

        var bytesPerSample = bitsPerSample / 8;
        var sampleCount = dataSize / (channels * bytesPerSample);
        return (double)sampleCount / sampleRate;
    }

    private static bool TryReadHeader(byte[] wav, out int sampleRate, out short channels, out short bitsPerSample, out int dataSize)
    {
        sampleRate = 0;
        channels = 0;
        bitsPerSample = 0;
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
                dataSize = chunkSize;
            }

            pos = chunkDataStart + chunkSize + (chunkSize % 2);
        }

        return sampleRate > 0 && channels > 0 && bitsPerSample > 0 && dataSize > 0;
    }
}
