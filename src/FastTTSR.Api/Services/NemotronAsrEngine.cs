using System.Text.Json;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;

namespace FastTTSR.Api.Services;

/// <summary>
/// Cache-aware streaming FastConformer-RNNT ASR engine (encoder + LSTM predictor + joint, 3
/// chained ONNX sessions), mirrors SupertonicTtsEngine's multi-session-in-one-class pattern.
/// All hyperparameters/tensor shapes are read from genai_config.json - NOT
/// audio_processor_config.json, which is missing several required details (lang_id mapping,
/// pre_encode_cache_size, chunk_samples, the correct log_eps value) and was only usable to get
/// the pipeline running without crashing, not to produce accurate transcriptions. This
/// implementation is ported from a validated Python/onnxruntime reference (see
/// pyscripts/nemotron_speech_ort_only.py) after the original config.json-only implementation was
/// found to produce garbled output - the encoder's output was nearly invariant to actual audio
/// content because it was fed lang_id values in the thousands (vocab.txt tag line indices)
/// instead of the small fixed integers (0-104) the model's language embedding actually expects.
/// </summary>
public sealed class NemotronAsrEngine : IDisposable
{
    private readonly InferenceSession _encoder;
    private readonly InferenceSession _decoder;
    private readonly InferenceSession _joint;
    private readonly NemotronVocabulary _vocabulary;
    private readonly NemotronFeatureExtractor _featureExtractor;

    private readonly int _sampleRate;
    private readonly int _chunkSamples;
    private readonly int _encoderLayers;
    private readonly int _encoderHidden;
    private readonly int _cacheChannelFrames; // genai_config.json's "left_context"
    private readonly int _convContext;
    private readonly int _decoderLayers;
    private readonly int _decoderHidden;
    private readonly long _blankId;
    private readonly int _maxSymbolsPerStep;
    private readonly float _blankPenalty;
    private bool _disposed;

    public NemotronAsrEngine(string modelDirectory)
    {
        var options = new Microsoft.ML.OnnxRuntime.SessionOptions { LogSeverityLevel = OrtLoggingLevel.ORT_LOGGING_LEVEL_ERROR };
        _encoder = new InferenceSession(Path.Combine(modelDirectory, "encoder.onnx"), options);
        _decoder = new InferenceSession(Path.Combine(modelDirectory, "decoder.onnx"), options);
        _joint = new InferenceSession(Path.Combine(modelDirectory, "joint.onnx"), options);

        _vocabulary = new NemotronVocabulary(Path.Combine(modelDirectory, "vocab.txt"));

        var model = JsonDocument.Parse(File.ReadAllText(Path.Combine(modelDirectory, "genai_config.json")))
            .RootElement.GetProperty("model");

        _sampleRate = model.GetProperty("sample_rate").GetInt32();
        _chunkSamples = model.GetProperty("chunk_samples").GetInt32();
        _blankId = model.GetProperty("blank_id").GetInt64();
        _maxSymbolsPerStep = model.GetProperty("max_symbols_per_step").GetInt32();
        _blankPenalty = model.TryGetProperty("search", out var search) && search.TryGetProperty("blank_penalty", out var penaltyEl)
            ? penaltyEl.GetSingle()
            : 0f;

        var encoderCfg = model.GetProperty("encoder");
        _encoderHidden = encoderCfg.GetProperty("hidden_size").GetInt32();
        _encoderLayers = encoderCfg.GetProperty("num_hidden_layers").GetInt32();
        _cacheChannelFrames = model.GetProperty("left_context").GetInt32();
        _convContext = model.GetProperty("conv_context").GetInt32();

        var decoderCfg = model.GetProperty("decoder");
        _decoderHidden = decoderCfg.GetProperty("hidden_size").GetInt32();
        _decoderLayers = decoderCfg.GetProperty("num_hidden_layers").GetInt32();

        _featureExtractor = new NemotronFeatureExtractor(
            sampleRate: _sampleRate,
            nFft: model.GetProperty("fft_size").GetInt32(),
            hopLength: model.GetProperty("hop_length").GetInt32(),
            winLength: model.GetProperty("win_length").GetInt32(),
            nMels: model.GetProperty("num_mels").GetInt32(),
            preemphasis: model.GetProperty("preemph").GetSingle(),
            logEps: model.GetProperty("log_eps").GetSingle(),
            preEncodeCacheSize: model.GetProperty("pre_encode_cache_size").GetInt32());
    }

    public (string Text, string? DetectedLanguage) Transcribe(byte[] wavBytes, string? language)
    {
        var samples = WavAudioUtils.ReadMonoFloat(wavBytes, _sampleRate);
        var languageId = NemotronLanguages.Resolve(language);

        var encoderOutputs = RunEncoderChunks(samples, languageId);
        var tokenIds = RunRnntGreedyDecode(encoderOutputs);

        var text = _vocabulary.Decode(tokenIds, out var languageTag);
        // In auto-detect mode the model itself emits the detected language as a leading tag;
        // fall back to it only when the caller didn't already specify a language explicitly.
        var detectedLanguage = language ?? languageTag?.Trim('<', '>');

        return (text, detectedLanguage);
    }

    private float[][] RunEncoderChunks(float[] samples, long languageId)
    {
        _featureExtractor.Reset();

        var cacheLastChannel = new float[_encoderLayers * _cacheChannelFrames * _encoderHidden];
        var cacheLastTime = new float[_encoderLayers * _encoderHidden * _convContext];
        long cacheLastChannelLen = 0;

        var allOutputs = new List<float[]>();

        for (var offset = 0; offset < samples.Length; offset += _chunkSamples)
        {
            var chunk = new float[_chunkSamples];
            var available = Math.Min(_chunkSamples, samples.Length - offset);
            Array.Copy(samples, offset, chunk, 0, available);
            // Remaining entries stay zero (right-padding the final, possibly-short chunk).

            var melChunk = _featureExtractor.ProcessChunk(chunk);
            var totalFrames = melChunk.Length;
            var nMels = totalFrames > 0 ? melChunk[0].Length : 0;

            var audioSignal = new float[totalFrames * nMels];
            for (var i = 0; i < totalFrames; i++)
            {
                Array.Copy(melChunk[i], 0, audioSignal, i * nMels, nMels);
            }

            var inputs = new List<NamedOnnxValue>
            {
                NamedOnnxValue.CreateFromTensor("audio_signal", new DenseTensor<float>(audioSignal, new[] { 1, totalFrames, nMels })),
                NamedOnnxValue.CreateFromTensor("length", new DenseTensor<long>(new long[] { totalFrames }, new[] { 1 })),
                NamedOnnxValue.CreateFromTensor("cache_last_channel", new DenseTensor<float>(cacheLastChannel, new[] { 1, _encoderLayers, _cacheChannelFrames, _encoderHidden })),
                NamedOnnxValue.CreateFromTensor("cache_last_time", new DenseTensor<float>(cacheLastTime, new[] { 1, _encoderLayers, _encoderHidden, _convContext })),
                NamedOnnxValue.CreateFromTensor("cache_last_channel_len", new DenseTensor<long>(new long[] { cacheLastChannelLen }, new[] { 1 })),
                NamedOnnxValue.CreateFromTensor("lang_id", new DenseTensor<long>(new long[] { languageId }, new[] { 1 })),
            };

            using var results = _encoder.Run(inputs);
            var outputs = results.First(r => r.Name == "outputs").AsTensor<float>();
            var encodedLengths = results.First(r => r.Name == "encoded_lengths").AsTensor<long>()[0];
            cacheLastChannel = results.First(r => r.Name == "cache_last_channel_next").AsTensor<float>().ToArray();
            cacheLastTime = results.First(r => r.Name == "cache_last_time_next").AsTensor<float>().ToArray();
            cacheLastChannelLen = results.First(r => r.Name == "cache_last_channel_len_next").AsTensor<long>()[0];

            var validOutputFrames = (int)Math.Min(encodedLengths, outputs.Dimensions[1]);
            for (var t = 0; t < validOutputFrames; t++)
            {
                var frame = new float[_encoderHidden];
                for (var d = 0; d < _encoderHidden; d++)
                {
                    frame[d] = outputs[0, t, d];
                }

                allOutputs.Add(frame);
            }
        }

        return allOutputs.ToArray();
    }

    private List<int> RunRnntGreedyDecode(float[][] encoderOutputs)
    {
        var tokenIds = new List<int>();
        long lastToken = _blankId;
        var hState = new float[_decoderLayers * _decoderHidden];
        var cState = new float[_decoderLayers * _decoderHidden];

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
            NamedOnnxValue.CreateFromTensor("h_in", new DenseTensor<float>(hState, new[] { _decoderLayers, 1, _decoderHidden })),
            NamedOnnxValue.CreateFromTensor("c_in", new DenseTensor<float>(cState, new[] { _decoderLayers, 1, _decoderHidden })),
        };

        using var results = _decoder.Run(inputs);
        var rawOutput = results.First(r => r.Name == "decoder_output").AsTensor<float>(); // [1, 640, 1] (batch, hidden, seq)
        var hOut = results.First(r => r.Name == "h_out").AsTensor<float>().ToArray();
        var cOut = results.First(r => r.Name == "c_out").AsTensor<float>().ToArray();

        // Transpose (batch, hidden, seq=1) -> flat hidden vector for the single decode step.
        var decoderOutput = new float[_decoderHidden];
        for (var d = 0; d < _decoderHidden; d++)
        {
            decoderOutput[d] = rawOutput[0, d, 0];
        }

        return (decoderOutput, hOut, cOut);
    }

    private long RunJoint(float[] encoderFrame, float[] decoderOutput)
    {
        var inputs = new List<NamedOnnxValue>
        {
            NamedOnnxValue.CreateFromTensor("encoder_output", new DenseTensor<float>(encoderFrame, new[] { 1, 1, _encoderHidden })),
            NamedOnnxValue.CreateFromTensor("decoder_output", new DenseTensor<float>(decoderOutput, new[] { 1, 1, _decoderHidden })),
        };

        using var results = _joint.Run(inputs);
        var logits = results.First(r => r.Name == "joint_output").AsTensor<float>(); // [1,1,1,vocab]

        var vocabSize = logits.Dimensions[3];
        var bestId = 0L;
        var bestScore = float.NegativeInfinity;
        for (var v = 0; v < vocabSize; v++)
        {
            var score = logits[0, 0, 0, v];
            if (v == _blankId)
            {
                score -= _blankPenalty;
            }

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
