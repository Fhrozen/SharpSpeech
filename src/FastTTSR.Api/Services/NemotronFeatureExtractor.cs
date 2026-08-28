using System.Numerics;

namespace FastTTSR.Api.Services;

/// <summary>
/// Log-mel spectrogram feature extractor matching audio_processor_config.json's parameters
/// (n_fft=512, hop=160, win=400, n_mels=128, hann window, HTK mel scale). Self-contained (no
/// external DSP library) - implements its own radix-2 FFT and triangular mel filterbank.
/// </summary>
public sealed class NemotronFeatureExtractor
{
    private readonly int _nFft;
    private readonly int _hopLength;
    private readonly int _winLength;
    private readonly int _nMels;
    private readonly float _preemphasis;
    private readonly float _logZeroGuard;
    private readonly float[] _window;
    private readonly float[][] _melFilterbank; // [nMels][nFft/2+1]

    public NemotronFeatureExtractor(
        int sampleRate, int nFft, int hopLength, int winLength, int nMels,
        float fMin, float fMax, float preemphasis, float logZeroGuard)
    {
        _nFft = nFft;
        _hopLength = hopLength;
        _winLength = winLength;
        _nMels = nMels;
        _preemphasis = preemphasis;
        _logZeroGuard = logZeroGuard;
        _window = HannWindow(winLength);
        _melFilterbank = BuildMelFilterbank(nMels, nFft, sampleRate, fMin, fMax);
    }

    /// <summary>Computes log-mel features for the given mono PCM samples. Returns [numFrames][nMels].</summary>
    public float[][] ComputeLogMel(float[] samples)
    {
        // Pre-emphasis
        var emphasized = new float[samples.Length];
        emphasized[0] = samples[0];
        for (var i = 1; i < samples.Length; i++)
        {
            emphasized[i] = samples[i] - _preemphasis * samples[i - 1];
        }

        // Center-pad by nFft/2 on both sides so the first frame is centered at sample 0.
        var pad = _nFft / 2;
        var padded = new float[emphasized.Length + 2 * pad];
        Array.Copy(emphasized, 0, padded, pad, emphasized.Length);

        var numFrames = 1 + Math.Max(0, (padded.Length - _winLength) / _hopLength);
        var frames = new float[numFrames][];
        var fftBuffer = new Complex[_nFft];

        for (var f = 0; f < numFrames; f++)
        {
            var start = f * _hopLength;

            Array.Clear(fftBuffer, 0, _nFft);
            for (var i = 0; i < _winLength; i++)
            {
                var sampleIndex = start + i;
                var sample = sampleIndex < padded.Length ? padded[sampleIndex] : 0f;
                fftBuffer[i] = new Complex(sample * _window[i], 0);
            }

            Fft(fftBuffer);

            var powerBins = _nFft / 2 + 1;
            var power = new float[powerBins];
            for (var k = 0; k < powerBins; k++)
            {
                power[k] = (float)fftBuffer[k].Magnitude;
                power[k] *= power[k]; // mag_power = 2.0
            }

            var mel = new float[_nMels];
            for (var m = 0; m < _nMels; m++)
            {
                double sum = 0;
                var filter = _melFilterbank[m];
                for (var k = 0; k < powerBins; k++)
                {
                    sum += filter[k] * power[k];
                }

                mel[m] = (float)Math.Log(sum + _logZeroGuard);
            }

            frames[f] = mel;
        }

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

    private static float[][] BuildMelFilterbank(int nMels, int nFft, int sampleRate, float fMin, float fMax)
    {
        static double HzToMel(double hz) => 2595.0 * Math.Log10(1.0 + hz / 700.0);
        static double MelToHz(double mel) => 700.0 * (Math.Pow(10.0, mel / 2595.0) - 1.0);

        var powerBins = nFft / 2 + 1;
        var melMin = HzToMel(fMin);
        var melMax = HzToMel(fMax);

        var melPoints = new double[nMels + 2];
        for (var i = 0; i < melPoints.Length; i++)
        {
            melPoints[i] = melMin + (melMax - melMin) * i / (nMels + 1);
        }

        var binPoints = new int[nMels + 2];
        for (var i = 0; i < melPoints.Length; i++)
        {
            var hz = MelToHz(melPoints[i]);
            binPoints[i] = (int)Math.Floor((nFft + 1) * hz / sampleRate);
        }

        var filterbank = new float[nMels][];
        for (var m = 0; m < nMels; m++)
        {
            var filter = new float[powerBins];
            var left = binPoints[m];
            var center = binPoints[m + 1];
            var right = binPoints[m + 2];

            for (var k = left; k < center && k < powerBins; k++)
            {
                if (center > left)
                {
                    filter[k] = (float)(k - left) / (center - left);
                }
            }

            for (var k = center; k < right && k < powerBins; k++)
            {
                if (right > center)
                {
                    filter[k] = (float)(right - k) / (right - center);
                }
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
