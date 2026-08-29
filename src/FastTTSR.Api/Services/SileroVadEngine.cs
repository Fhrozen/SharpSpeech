using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;

namespace FastTTSR.Api.Services;

/// <summary>Raw Silero VAD ONNX session wrapper - ported from the validated Python reference's
/// SileroVadOrt (pyscripts/nemotron_speech_ort_only.py). Tensor names ("input"/"state"/"sr" in,
/// "output"/"stateN" out) and the (2,1,128) LSTM state shape are Silero VAD's own fixed ONNX
/// export contract, not something read from any config file.</summary>
public interface ISileroVadEngine
{
    /// <summary>Resets the LSTM state/context carried between calls - call once per utterance.</summary>
    void Reset();

    /// <summary>Runs Silero VAD over fixed-size windows of <paramref name="samples"/>, returning
    /// true if any window's speech probability meets <paramref name="threshold"/>.</summary>
    bool ContainsSpeech(float[] samples, float threshold);
}

public sealed class SileroVadEngine : ISileroVadEngine, IDisposable
{
    private readonly InferenceSession _session;
    private readonly int _sampleRate;
    private readonly int _windowSize;
    private readonly int _contextSize;
    private float[] _state = [];
    private float[] _context = [];
    private bool _disposed;

    public SileroVadEngine(string modelPath, int sampleRate)
    {
        if (sampleRate != 8000 && sampleRate != 16000)
        {
            throw new ArgumentException($"SileroVad only supports 8000/16000 Hz, got {sampleRate}.");
        }

        _session = new InferenceSession(modelPath);
        _sampleRate = sampleRate;

        var factor = sampleRate / 8000;
        _windowSize = 256 * factor;
        _contextSize = 32 * factor;

        Reset();
    }

    public void Reset()
    {
        _state = new float[2 * 1 * 128];
        _context = new float[_contextSize];
    }

    public bool ContainsSpeech(float[] samples, float threshold)
    {
        var offset = 0;
        while (offset + _windowSize <= samples.Length)
        {
            var window = new float[_windowSize];
            Array.Copy(samples, offset, window, 0, _windowSize);
            if (ProcessWindow(window) >= threshold)
            {
                return true;
            }

            offset += _windowSize;
        }

        if (offset < samples.Length)
        {
            var padded = new float[_windowSize];
            Array.Copy(samples, offset, padded, 0, samples.Length - offset);
            if (ProcessWindow(padded) >= threshold)
            {
                return true;
            }
        }

        return false;
    }

    private float ProcessWindow(float[] window)
    {
        var input = new float[_contextSize + window.Length];
        Array.Copy(_context, input, _contextSize);
        Array.Copy(window, 0, input, _contextSize, window.Length);

        var inputs = new List<NamedOnnxValue>
        {
            NamedOnnxValue.CreateFromTensor("input", new DenseTensor<float>(input, new[] { 1, input.Length })),
            NamedOnnxValue.CreateFromTensor("state", new DenseTensor<float>(_state, new[] { 2, 1, 128 })),
            NamedOnnxValue.CreateFromTensor("sr", new DenseTensor<long>(new long[] { _sampleRate }, Array.Empty<int>())),
        };

        using var results = _session.Run(inputs);
        var output = results.First(r => r.Name == "output").AsTensor<float>();
        var stateN = results.First(r => r.Name == "stateN").AsTensor<float>();

        _state = stateN.ToArray();
        Array.Copy(window, window.Length - _contextSize, _context, 0, _contextSize);

        return output[0, 0];
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _session.Dispose();
        _disposed = true;
    }
}
