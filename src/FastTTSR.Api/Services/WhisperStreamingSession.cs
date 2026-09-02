namespace FastTTSR.Api.Services;

/// <summary>
/// Naive streaming session for Whisper, which has no true incremental decode API: buffers raw
/// PCM16 mono 16kHz audio and periodically re-transcribes the whole buffer from scratch via the
/// existing batch <see cref="WhisperAsrEngine.TranscribeAsync"/>. To keep each pass's cost (and
/// each outbound message) bounded regardless of total utterance length, the buffer is trimmed
/// down to a small trailing overlap every time a segment is committed (time limit or
/// sentence-ending punctuation - see <see cref="SegmentBoundaryPolicy"/>).
/// </summary>
public sealed class WhisperStreamingSession(WhisperAsrEngine engine, string? language, double segmentSeconds) : IStreamingTranscriptionSession
{
    private const int SampleRateValue = 16000;
    private const double MinNewAudioSecondsBetweenPasses = 2.0;
    private const int MinNewSamplesBetweenPasses = (int)(MinNewAudioSecondsBetweenPasses * SampleRateValue);
    private const double OverlapSeconds = 0.5;
    private const int OverlapSamples = (int)(OverlapSeconds * SampleRateValue);

    private readonly List<byte> _pcm = [];
    private int _samplesAtLastPass;
    private string _lastText = string.Empty;
    private bool _finished;

    public int SampleRate => SampleRateValue;

    public async Task<IReadOnlyList<StreamingUpdate>> ProcessChunkAsync(byte[] pcm16Chunk, CancellationToken cancellationToken)
    {
        if (_finished)
        {
            return [];
        }

        _pcm.AddRange(pcm16Chunk);
        var totalSamples = _pcm.Count / 2;

        if (totalSamples - _samplesAtLastPass < MinNewSamplesBetweenPasses)
        {
            return [];
        }

        _samplesAtLastPass = totalSamples;
        var text = await RunPassAsync(cancellationToken);

        if (text == _lastText)
        {
            return [];
        }

        _lastText = text;

        if (!SegmentBoundaryPolicy.ShouldCommit(totalSamples / (double)SampleRateValue, segmentSeconds, text))
        {
            return [new StreamingUpdate(text, IsSegmentFinal: false)];
        }

        // Commit: trim the buffer down to a small trailing overlap so the NEXT pass re-transcribes
        // only the new segment, not the whole growing conversation.
        var overlapStart = Math.Max(0, _pcm.Count - OverlapSamples * 2);
        var overlap = _pcm.GetRange(overlapStart, _pcm.Count - overlapStart);
        _pcm.Clear();
        _pcm.AddRange(overlap);
        _samplesAtLastPass = 0;
        _lastText = string.Empty;

        return [new StreamingUpdate(text, IsSegmentFinal: true)];
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

