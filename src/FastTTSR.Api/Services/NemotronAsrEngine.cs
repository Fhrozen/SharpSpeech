using System.Text.Json;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;

namespace FastTTSR.Api.Services;

/// <summary>
/// Cache-aware streaming FastConformer-RNNT ASR engine (encoder + LSTM predictor + joint, 3
/// chained ONNX sessions), mirrors SupertonicTtsEngine's multi-session-in-one-class pattern.
/// Tensor names/shapes below are as confirmed by the Phase 3 ONNX-graph inspection spike - see
/// docs/ASR_IMPLEMENTATION_PLAN.md.
/// </summary>
public sealed class NemotronAsrEngine : IDisposable
{
    // Fixed architecture constants confirmed via InferenceSession.InputMetadata/OutputMetadata.
    private const int EncoderLayers = 24;
    private const int EncoderHidden = 1024;
    private const int CacheChannelFrames = 56;
    private const int ConvContext = 8;
    private const int DecoderLayers = 2;
    private const int DecoderHidden = 640;
    private const int PreEncodeCacheSize = 9;
    private const int ChunkNewFrames = 56; // chunk_samples (8960) / hop_length (160)
    private const int ChunkTotalFrames = PreEncodeCacheSize + ChunkNewFrames; // 65
    private const int EncoderOutputFramesPerChunk = 7; // ChunkNewFrames / subsampling_factor (8)

    private readonly InferenceSession _encoder;
    private readonly InferenceSession _decoder;
    private readonly InferenceSession _joint;
    private readonly NemotronVocabulary _vocabulary;
    private readonly NemotronFeatureExtractor _featureExtractor;
    private readonly int _sampleRate;
    private readonly long _blankId;
    private readonly int _maxSymbolsPerStep;
    private readonly long _defaultLanguageId;
    private bool _disposed;

    public NemotronAsrEngine(string modelDirectory)
    {
        var options = new Microsoft.ML.OnnxRuntime.SessionOptions { LogSeverityLevel = OrtLoggingLevel.ORT_LOGGING_LEVEL_ERROR };
        _encoder = new InferenceSession(Path.Combine(modelDirectory, "encoder.onnx"), options);
        _decoder = new InferenceSession(Path.Combine(modelDirectory, "decoder.onnx"), options);
        _joint = new InferenceSession(Path.Combine(modelDirectory, "joint.onnx"), options);

        _vocabulary = new NemotronVocabulary(Path.Combine(modelDirectory, "vocab.txt"));

        var audioParams = JsonDocument.Parse(File.ReadAllText(Path.Combine(modelDirectory, "audio_processor_config.json")))
            .RootElement.GetProperty("audio_params");
        _sampleRate = audioParams.GetProperty("sample_rate").GetInt32();
        _featureExtractor = new NemotronFeatureExtractor(
            sampleRate: _sampleRate,
            nFft: audioParams.GetProperty("n_fft").GetInt32(),
            hopLength: audioParams.GetProperty("hop_length").GetInt32(),
            winLength: audioParams.GetProperty("window_length").GetInt32(),
            nMels: audioParams.GetProperty("n_mels").GetInt32(),
            fMin: (float)audioParams.GetProperty("fmin").GetDouble(),
            fMax: (float)audioParams.GetProperty("fmax").GetDouble(),
            preemphasis: (float)audioParams.GetProperty("preemphasis").GetDouble(),
            logZeroGuard: (float)audioParams.GetProperty("log_zero_guard_value").GetDouble());

        var genaiConfigPath = Path.Combine(modelDirectory, "genai_config.json");
        var model = JsonDocument.Parse(File.ReadAllText(genaiConfigPath)).RootElement.GetProperty("model");
        _blankId = model.TryGetProperty("blank_id", out var blankEl) ? blankEl.GetInt64() : _vocabulary.BlankId;
        _maxSymbolsPerStep = model.TryGetProperty("max_symbols_per_step", out var maxSymEl) ? maxSymEl.GetInt32() : 10;
        _defaultLanguageId = _vocabulary.ResolveLanguageId("en-US", _blankId);
    }

    public (string Text, string? DetectedLanguage) Transcribe(byte[] wavBytes, string? language)
    {
        var samples = WavAudioUtils.ReadMonoFloat(wavBytes, _sampleRate);
        var melFrames = _featureExtractor.ComputeLogMel(samples);
        var languageId = _vocabulary.ResolveLanguageId(language, _defaultLanguageId);

        var encoderOutputs = RunEncoderChunks(melFrames, languageId);
        var tokenIds = RunRnntGreedyDecode(encoderOutputs);

        return (_vocabulary.Decode(tokenIds), language);
    }

    private float[][] RunEncoderChunks(float[][] melFrames, long languageId)
    {
        // Prepend PreEncodeCacheSize zero frames so the first chunk has a (silent) lookback window.
        var nMels = melFrames.Length > 0 ? melFrames[0].Length : 128;
        var padded = new float[PreEncodeCacheSize + melFrames.Length][];
        for (var i = 0; i < PreEncodeCacheSize; i++)
        {
            padded[i] = new float[nMels];
        }

        Array.Copy(melFrames, 0, padded, PreEncodeCacheSize, melFrames.Length);

        var cacheLastChannel = new float[EncoderLayers * CacheChannelFrames * EncoderHidden];
        var cacheLastTime = new float[EncoderLayers * EncoderHidden * ConvContext];
        long cacheLastChannelLen = 0;

        var allOutputs = new List<float[]>();
        var chunkStart = 0;

        while (chunkStart < melFrames.Length)
        {
            var audioSignal = new float[ChunkTotalFrames * nMels];
            var validFrames = 0;

            for (var i = 0; i < ChunkTotalFrames; i++)
            {
                var srcIndex = chunkStart + i;
                if (srcIndex >= padded.Length)
                {
                    break;
                }

                Array.Copy(padded[srcIndex], 0, audioSignal, i * nMels, nMels);
                if (i >= PreEncodeCacheSize)
                {
                    validFrames++;
                }
            }

            var inputs = new List<NamedOnnxValue>
            {
                NamedOnnxValue.CreateFromTensor("audio_signal", new DenseTensor<float>(audioSignal, new[] { 1, ChunkTotalFrames, nMels })),
                NamedOnnxValue.CreateFromTensor("length", new DenseTensor<long>(new long[] { Math.Min(validFrames, ChunkNewFrames) }, new[] { 1 })),
                NamedOnnxValue.CreateFromTensor("cache_last_channel", new DenseTensor<float>(cacheLastChannel, new[] { 1, EncoderLayers, CacheChannelFrames, EncoderHidden })),
                NamedOnnxValue.CreateFromTensor("cache_last_time", new DenseTensor<float>(cacheLastTime, new[] { 1, EncoderLayers, EncoderHidden, ConvContext })),
                NamedOnnxValue.CreateFromTensor("cache_last_channel_len", new DenseTensor<long>(new long[] { cacheLastChannelLen }, new[] { 1 })),
                NamedOnnxValue.CreateFromTensor("lang_id", new DenseTensor<long>(new long[] { languageId }, new[] { 1 })),
            };

            using var results = _encoder.Run(inputs);
            var outputs = results.First(r => r.Name == "outputs").AsTensor<float>();
            var encodedLengths = results.First(r => r.Name == "encoded_lengths").AsTensor<long>()[0];
            cacheLastChannel = results.First(r => r.Name == "cache_last_channel_next").AsTensor<float>().ToArray();
            cacheLastTime = results.First(r => r.Name == "cache_last_time_next").AsTensor<float>().ToArray();
            cacheLastChannelLen = results.First(r => r.Name == "cache_last_channel_len_next").AsTensor<long>()[0];

            var validOutputFrames = (int)Math.Min(encodedLengths, EncoderOutputFramesPerChunk);
            for (var t = 0; t < validOutputFrames; t++)
            {
                var frame = new float[EncoderHidden];
                for (var d = 0; d < EncoderHidden; d++)
                {
                    frame[d] = outputs[0, t, d];
                }

                allOutputs.Add(frame);
            }

            chunkStart += ChunkNewFrames;
        }

        return allOutputs.ToArray();
    }

    private List<int> RunRnntGreedyDecode(float[][] encoderOutputs)
    {
        var tokenIds = new List<int>();
        long lastToken = _blankId;
        var hState = new float[DecoderLayers * DecoderHidden];
        var cState = new float[DecoderLayers * DecoderHidden];

        foreach (var encoderFrame in encoderOutputs)
        {
            var symbolsThisFrame = 0;

            while (symbolsThisFrame < _maxSymbolsPerStep)
            {
                var (decoderOutput, hOut, cOut) = RunDecoderStep(lastToken, hState, cState);
                var predicted = RunJoint(encoderFrame, decoderOutput);

                if (predicted == _blankId)
                {
                    break;
                }

                tokenIds.Add((int)predicted);
                lastToken = predicted;
                hState = hOut;
                cState = cOut;
                symbolsThisFrame++;
            }
        }

        return tokenIds;
    }

    private (float[] DecoderOutput, float[] HOut, float[] COut) RunDecoderStep(long lastToken, float[] hState, float[] cState)
    {
        var inputs = new List<NamedOnnxValue>
        {
            NamedOnnxValue.CreateFromTensor("targets", new DenseTensor<long>(new[] { lastToken }, new[] { 1, 1 })),
            NamedOnnxValue.CreateFromTensor("h_in", new DenseTensor<float>(hState, new[] { DecoderLayers, 1, DecoderHidden })),
            NamedOnnxValue.CreateFromTensor("c_in", new DenseTensor<float>(cState, new[] { DecoderLayers, 1, DecoderHidden })),
        };

        using var results = _decoder.Run(inputs);
        var rawOutput = results.First(r => r.Name == "decoder_output").AsTensor<float>(); // [1, 640, 1] (batch, hidden, seq)
        var hOut = results.First(r => r.Name == "h_out").AsTensor<float>().ToArray();
        var cOut = results.First(r => r.Name == "c_out").AsTensor<float>().ToArray();

        // Transpose (batch, hidden, seq=1) -> flat hidden vector for the single decode step.
        var decoderOutput = new float[DecoderHidden];
        for (var d = 0; d < DecoderHidden; d++)
        {
            decoderOutput[d] = rawOutput[0, d, 0];
        }

        return (decoderOutput, hOut, cOut);
    }

    private long RunJoint(float[] encoderFrame, float[] decoderOutput)
    {
        var inputs = new List<NamedOnnxValue>
        {
            NamedOnnxValue.CreateFromTensor("encoder_output", new DenseTensor<float>(encoderFrame, new[] { 1, 1, EncoderHidden })),
            NamedOnnxValue.CreateFromTensor("decoder_output", new DenseTensor<float>(decoderOutput, new[] { 1, 1, DecoderHidden })),
        };

        using var results = _joint.Run(inputs);
        var logits = results.First(r => r.Name == "joint_output").AsTensor<float>(); // [1,1,1,vocab]

        var vocabSize = logits.Dimensions[3];
        var bestId = 0L;
        var bestScore = float.NegativeInfinity;
        for (var v = 0; v < vocabSize; v++)
        {
            var score = logits[0, 0, 0, v];
            if (score > bestScore)
            {
                bestScore = score;
                bestId = v;
            }
        }

        return bestId;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _encoder.Dispose();
        _decoder.Dispose();
        _joint.Dispose();
        _disposed = true;
    }
}
