namespace FastTTSR.Api.Services;

/// <summary>
/// Naive streaming session for Whisper, which has no true incremental decode API: buffers all raw
/// PCM16 mono 16kHz audio received so far and periodically re-transcribes the whole buffer from
/// scratch via the existing batch <see cref="WhisperAsrEngine.TranscribeAsync"/>, replacing the
/// previous partial result rather than emitting a delta.
/// </summary>
public sealed class WhisperStreamingSession(WhisperAsrEngine engine, string? language) : IStreamingTranscriptionSession
{
    private const int SampleRateValue = 16000;
    private const double MinNewAudioSecondsBetweenPasses = 2.0;
    private const int MinNewSamplesBetweenPasses = (int)(MinNewAudioSecondsBetweenPasses * SampleRateValue);

    private readonly List<byte> _pcm = [];
    private int _samplesAtLastPass;
    private string _lastText = string.Empty;
    private bool _finished;

    public int SampleRate => SampleRateValue;

    public async Task<string?> ProcessChunkAsync(byte[] pcm16Chunk, CancellationToken cancellationToken)
    {
        if (_finished)
        {
            return null;
        }

        _pcm.AddRange(pcm16Chunk);
        var totalSamples = _pcm.Count / 2;

        if (totalSamples - _samplesAtLastPass < MinNewSamplesBetweenPasses)
        {
            return null;
        }

        _samplesAtLastPass = totalSamples;
        var text = await RunPassAsync(cancellationToken);

        if (text == _lastText)
        {
            return null;
        }

        _lastText = text;
        return text;
    }

    public async Task<string> FinishAsync(CancellationToken cancellationToken)
    {
        _finished = true;

        if (_pcm.Count > 0)
        {
            _lastText = await RunPassAsync(cancellationToken);
        }

        return _lastText;
    }

    private async Task<string> RunPassAsync(CancellationToken cancellationToken)
    {
        var wavBytes = WavAudioUtils.WrapPcm16MonoAsWav(_pcm.ToArray(), SampleRateValue);
        var (text, _, _) = await engine.TranscribeAsync(wavBytes, language, cancellationToken);
        return text;
    }

    public void Dispose()
    {
    }
}
