using System.Numerics;

namespace SharpAudio.Api.Services;

/// <summary>
/// Streaming log-mel spectrogram feature extractor matching genai_config.json's parameters
/// (n_fft=512, hop=160, win=400, n_mels=128, Slaney mel scale + area normalization, natural log).
/// Self-contained (no external DSP library) - implements its own radix-2 FFT and mel filterbank.
///
/// Ported from a validated Python/onnxruntime reference implementation (see
/// pyscripts/nemotron_speech_ort_only.py): processes fixed-size raw-audio chunks statefully,
/// carrying `nFft/2` samples of real left-context audio (not zero padding) and the previous
/// chunk's last `pre_encode_cache_size` log-mel frames across calls, exactly mirroring how the
/// model was streamed/tested. This differs from a naive single-shot whole-utterance STFT in two
/// ways that materially affect model accuracy: the Hann window is zero-padded to sit centered
/// within the FFT frame (not left-aligned), and the mel scale/filterbank is Slaney (librosa
/// default), not the classic HTK formula.
/// </summary>
public sealed class NemotronFeatureExtractor
{
    private readonly int _nFft;
    private readonly int _hopLength;
    private readonly int _preEncodeCacheSize;
    private readonly float _preemphasis;
    private readonly float _logEps;
    private readonly int _pad; // nFft / 2
    private readonly float[] _window; // length nFft, Hann centered with zero padding on both sides
    private readonly float[][] _melFilterbank; // [nMels][nFft/2+1]

    private float[] _leftContext = [];
    private float[][] _melCache = [];

    public NemotronFeatureExtractor(
        int sampleRate, int nFft, int hopLength, int winLength, int nMels,
        float preemphasis, float logEps, int preEncodeCacheSize)
    {
        _nFft = nFft;
        _hopLength = hopLength;
        _preEncodeCacheSize = preEncodeCacheSize;
        _preemphasis = preemphasis;
        _logEps = logEps;
        _pad = nFft / 2;

        _window = new float[nFft];
        var hann = HannWindow(winLength);
        var offset = (nFft - winLength) / 2;
        Array.Copy(hann, 0, _window, offset, winLength);

        _melFilterbank = BuildMelFilterbank(nMels, nFft, sampleRate);

        Reset();
    }

    /// <summary>Clears streaming state (left-context audio + mel cache) for a new utterance.</summary>
    public void Reset()
    {
        _leftContext = new float[_pad];
        _melCache = new float[_preEncodeCacheSize][];
        for (var i = 0; i < _preEncodeCacheSize; i++)
        {
            _melCache[i] = new float[_melFilterbank.Length];
        }
    }

    /// <summary>
    /// Computes log-mel features for one fixed-size raw-audio chunk, prefixed with the previous
    /// chunk's trailing <c>pre_encode_cache_size</c> log-mel frames. Returns
    /// [pre_encode_cache_size + numNewFrames][nMels].
    /// </summary>
    public float[][] ProcessChunk(float[] chunk)
    {
        var newFrames = ComputeLogMelChunk(chunk);

        var full = new float[_preEncodeCacheSize + newFrames.Length][];
        Array.Copy(_melCache, 0, full, 0, _preEncodeCacheSize);
        Array.Copy(newFrames, 0, full, _preEncodeCacheSize, newFrames.Length);

        _melCache = newFrames.Length >= _preEncodeCacheSize
            ? newFrames[^_preEncodeCacheSize..]
            : full[^_preEncodeCacheSize..];

        return full;
    }

    private float[][] ComputeLogMelChunk(float[] chunk)
    {
        var nMels = _melFilterbank.Length;

        // Pre-emphasis across the left-context carried from the previous chunk + this chunk, so
        // the very first real sample of this chunk is emphasized against real prior audio instead
        // of a hard zero edge.
        var x = new double[_leftContext.Length + chunk.Length];
        Array.Copy(_leftContext, x, _leftContext.Length);
        for (var i = 0; i < chunk.Length; i++)
        {
            x[_leftContext.Length + i] = chunk[i];
        }

        var emphasized = new double[x.Length];
        emphasized[0] = x[0];
        for (var i = 1; i < x.Length; i++)
        {
            emphasized[i] = x[i] - _preemphasis * x[i - 1];
        }

        var xp = new double[emphasized.Length + _pad];
        Array.Copy(emphasized, xp, emphasized.Length);

        var numFrames = chunk.Length / _hopLength;
        var frames = new float[numFrames][];
        var fftBuffer = new Complex[_nFft];

        for (var f = 0; f < numFrames; f++)
        {
            var start = f * _hopLength;

            for (var i = 0; i < _nFft; i++)
            {
                var sampleIndex = start + i;
                var sample = sampleIndex < xp.Length ? xp[sampleIndex] : 0.0;
                fftBuffer[i] = new Complex(sample * _window[i], 0);
            }

            Fft(fftBuffer);

            var powerBins = _nFft / 2 + 1;
            var power = new double[powerBins];
            for (var k = 0; k < powerBins; k++)
            {
                power[k] = fftBuffer[k].Magnitude * fftBuffer[k].Magnitude; // mag_power = 2.0
            }

            var mel = new float[nMels];
            for (var m = 0; m < nMels; m++)
            {
                double sum = 0;
                var filter = _melFilterbank[m];
                for (var k = 0; k < powerBins; k++)
                {
                    sum += filter[k] * power[k];
                }

                mel[m] = (float)Math.Log(sum + _logEps);
            }

            frames[f] = mel;
        }

        // Carry the tail of the RAW (non-emphasized) chunk forward as next call's left-context.
        _leftContext = chunk.Length >= _pad ? chunk[^_pad..] : chunk;

        return frames;
    }

    private static float[] HannWindow(int length)
    {
        var window = new float[length];
        for (var i = 0; i < length; i++)
        {
            window[i] = (float)(0.5 - 0.5 * Math.Cos(2 * Math.PI * i / (length - 1)));
        }

        return window;
    }

    /// <summary>Slaney mel scale (librosa default), NOT the classic HTK formula.</summary>
    private static double HzToMelSlaney(double hz)
    {
        const double fSp = 200.0 / 3.0;
        const double minLogHz = 1000.0;
        var minLogMel = minLogHz / fSp;
        var logstep = Math.Log(6.4) / 27.0;
        return hz >= minLogHz ? minLogMel + Math.Log(Math.Max(hz, 1e-10) / minLogHz) / logstep : hz / fSp;
    }

    private static double MelToHzSlaney(double mel)
    {
        const double fSp = 200.0 / 3.0;
        const double minLogHz = 1000.0;
        var minLogMel = minLogHz / fSp;
        var logstep = Math.Log(6.4) / 27.0;
        return mel >= minLogMel ? minLogHz * Math.Exp(logstep * (mel - minLogMel)) : mel * fSp;
    }

    /// <summary>Slaney-style triangular mel filterbank with area normalization (librosa defaults).</summary>
    private static float[][] BuildMelFilterbank(int nMels, int nFft, int sampleRate)
    {
        var nFreqs = nFft / 2 + 1;
        var fftFreqs = new double[nFreqs];
        for (var k = 0; k < nFreqs; k++)
        {
            fftFreqs[k] = k * (sampleRate / 2.0) / (nFreqs - 1);
        }

        var melPoints = new double[nMels + 2];
        var melMin = HzToMelSlaney(0);
        var melMax = HzToMelSlaney(sampleRate / 2.0);
        for (var i = 0; i < melPoints.Length; i++)
        {
            melPoints[i] = melMin + (melMax - melMin) * i / (nMels + 1);
        }

        var melHz = new double[nMels + 2];
        for (var i = 0; i < melHz.Length; i++)
        {
            melHz[i] = MelToHzSlaney(melPoints[i]);
        }

        var fdiff = new double[nMels + 1];
        for (var i = 0; i < fdiff.Length; i++)
        {
            fdiff[i] = melHz[i + 1] - melHz[i];
        }

        var filterbank = new float[nMels][];
        for (var m = 0; m < nMels; m++)
        {
            var filter = new float[nFreqs];
            for (var k = 0; k < nFreqs; k++)
            {
                var lower = (melHz[m] - fftFreqs[k]) * -1 / fdiff[m];
                var upper = (melHz[m + 2] - fftFreqs[k]) / fdiff[m + 1];
                filter[k] = (float)Math.Max(0, Math.Min(lower, upper));
            }

            var enorm = 2.0 / (melHz[m + 2] - melHz[m]);
            for (var k = 0; k < nFreqs; k++)
            {
                filter[k] *= (float)enorm;
            }

            filterbank[m] = filter;
        }

        return filterbank;
    }

    private static void Fft(Complex[] buffer)
    {
        var n = buffer.Length;
        for (int i = 1, j = 0; i < n; i++)
        {
            var bit = n >> 1;
            for (; (j & bit) != 0; bit >>= 1)
            {
                j ^= bit;
            }

            j ^= bit;
            if (i < j)
            {
                (buffer[i], buffer[j]) = (buffer[j], buffer[i]);
            }
        }

        for (var len = 2; len <= n; len <<= 1)
        {
            var angle = -2 * Math.PI / len;
            var wLen = new Complex(Math.Cos(angle), Math.Sin(angle));

            for (var start = 0; start < n; start += len)
            {
                var w = Complex.One;
                for (var i = 0; i < len / 2; i++)
                {
                    var u = buffer[start + i];
                    var v = buffer[start + i + len / 2] * w;
                    buffer[start + i] = u + v;
                    buffer[start + i + len / 2] = u - v;
                    w *= wLen;
                }
            }
        }
    }
}

