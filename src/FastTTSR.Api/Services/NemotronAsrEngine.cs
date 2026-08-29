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
    private readonly string _modelDirectory;
    private readonly string _vadFilename;
    private readonly float _vadThreshold;
    private readonly double _vadSilenceDurationMs;
    private readonly double _vadPrefixPaddingMs;
    private readonly int _nFft;
    private readonly int _hopLength;
    private readonly int _winLength;
    private readonly int _nMels;
    private readonly float _preemphasis;
    private readonly float _logEps;
    private readonly int _preEncodeCacheSize;
    private SileroVadEngine? _vadEngine;
    private bool _disposed;

    /// <summary>Sample rate raw PCM audio must already be at before <see cref="Transcribe"/> or a
    /// streaming session sees it.</summary>
    public int SampleRate => _sampleRate;

    public NemotronAsrEngine(string modelDirectory)
    {
        _modelDirectory = modelDirectory;
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

        // Defaults mirror the Python reference's NemotronConfig.vad_* fallbacks for models/configs
        // that predate the "vad" section.
        var vadCfg = model.TryGetProperty("vad", out var vadEl) ? vadEl : default;
        _vadFilename = vadCfg.ValueKind == JsonValueKind.Object && vadCfg.TryGetProperty("filename", out var vf)
            ? vf.GetString() ?? "silero_vad.onnx"
            : "silero_vad.onnx";
        _vadThreshold = vadCfg.ValueKind == JsonValueKind.Object && vadCfg.TryGetProperty("threshold", out var vt) ? vt.GetSingle() : 0.5f;
        _vadSilenceDurationMs = vadCfg.ValueKind == JsonValueKind.Object && vadCfg.TryGetProperty("silence_duration_ms", out var vsd) ? vsd.GetDouble() : 3360;
        _vadPrefixPaddingMs = vadCfg.ValueKind == JsonValueKind.Object && vadCfg.TryGetProperty("prefix_padding_ms", out var vpp) ? vpp.GetDouble() : 560;

        _nFft = model.GetProperty("fft_size").GetInt32();
        _hopLength = model.GetProperty("hop_length").GetInt32();
        _winLength = model.GetProperty("win_length").GetInt32();
        _nMels = model.GetProperty("num_mels").GetInt32();
        _preemphasis = model.GetProperty("preemph").GetSingle();
        _logEps = model.GetProperty("log_eps").GetSingle();
        _preEncodeCacheSize = model.GetProperty("pre_encode_cache_size").GetInt32();

        _featureExtractor = CreateFeatureExtractor();
    }

    private NemotronFeatureExtractor CreateFeatureExtractor() => new(
        sampleRate: _sampleRate,
        nFft: _nFft,
        hopLength: _hopLength,
        winLength: _winLength,
        nMels: _nMels,
        preemphasis: _preemphasis,
        logEps: _logEps,
        preEncodeCacheSize: _preEncodeCacheSize);

    public (string Text, string? DetectedLanguage) Transcribe(byte[] wavBytes, string? language, bool enableVad = false)
    {
        var samples = WavAudioUtils.ReadMonoFloat(wavBytes, _sampleRate);
        var languageId = NemotronLanguages.Resolve(language);

        var encoderOutputs = RunEncoderChunks(samples, languageId, enableVad);
        var tokenIds = RunRnntGreedyDecode(encoderOutputs);

        var text = _vocabulary.Decode(tokenIds, out var languageTag);
        // In auto-detect mode the model itself emits the detected language as a leading tag;
        // fall back to it only when the caller didn't already specify a language explicitly.
        var detectedLanguage = language ?? languageTag?.Trim('<', '>');

        return (text, detectedLanguage);
    }

    private float[][] RunEncoderChunks(float[] samples, long languageId, bool enableVad)
    {
        _featureExtractor.Reset();

        SileroVadGate? vadGate = null;
        if (enableVad)
        {
            var vadEngine = GetOrCreateVadEngine();
            vadEngine.Reset();
            vadGate = new SileroVadGate(vadEngine, _vadThreshold, _chunkSamples, _sampleRate, _vadSilenceDurationMs, _vadPrefixPaddingMs);
        }

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

            if (vadGate?.ShouldDropChunk(chunk) == true)
            {
                continue; // skip encoder/decoder inference for a chunk classified as silence
            }

            var melChunk = _featureExtractor.ProcessChunk(chunk);
            allOutputs.AddRange(RunEncoderChunk(melChunk, languageId, ref cacheLastChannel, ref cacheLastTime, ref cacheLastChannelLen));
        }

        return allOutputs.ToArray();
    }

    /// <summary>Runs the encoder on one already-extracted mel chunk, threading the cache tensors
    /// (by ref) from one call to the next - the reusable primitive shared by both batch
    /// (<see cref="RunEncoderChunks"/>) and <see cref="StreamingSession"/> decoding.</summary>
    private float[][] RunEncoderChunk(float[][] melChunk, long languageId, ref float[] cacheLastChannel, ref float[] cacheLastTime, ref long cacheLastChannelLen)
    {
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
        var frames = new float[validOutputFrames][];
        for (var t = 0; t < validOutputFrames; t++)
        {
            var frame = new float[_encoderHidden];
            for (var d = 0; d < _encoderHidden; d++)
            {
                frame[d] = outputs[0, t, d];
            }

            frames[t] = frame;
        }

        return frames;
    }

    private SileroVadEngine GetOrCreateVadEngine() =>
        _vadEngine ??= new SileroVadEngine(Path.Combine(_modelDirectory, _vadFilename), _sampleRate);

    private List<int> RunRnntGreedyDecode(float[][] encoderOutputs)
    {
        var tokenIds = new List<int>();
        long lastToken = _blankId;
        var hState = new float[_decoderLayers * _decoderHidden];
        var cState = new float[_decoderLayers * _decoderHidden];

        foreach (var frame in encoderOutputs)
        {
            tokenIds.AddRange(DecodeFrame(frame, ref lastToken, ref hState, ref cState));
        }

        return tokenIds;
    }

    /// <summary>Greedily decodes one encoder frame, threading the RNNT predictor's LSTM state (by
    /// ref) from one call to the next - the reusable primitive shared by both batch
    /// (<see cref="RunRnntGreedyDecode"/>) and <see cref="StreamingSession"/> decoding.</summary>
    private List<int> DecodeFrame(float[] encoderFrame, ref long lastToken, ref float[] hState, ref float[] cState)
    {
        var tokenIds = new List<int>();
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
        _vadEngine?.Dispose();
        _disposed = true;
    }

    /// <summary>Creates a stateful streaming session - a fresh feature extractor and cache/decoder
    /// state, plus a dedicated VAD engine if requested (never the shared <see cref="_vadEngine"/>,
    /// since its internal LSTM state can't safely be shared across concurrent sessions).</summary>
    public StreamingSession CreateStreamingSession(string? language, bool enableVad) =>
        new(this, NemotronLanguages.Resolve(language), enableVad);

    /// <summary>
    /// Incremental cache-aware streaming session - mirrors the Python reference's
    /// NemotronOrtPipeline.process_chunk (encoder + greedy RNNT decode per chunk, threading cache/
    /// decoder state across calls) rather than <see cref="Transcribe"/>'s whole-utterance-at-once
    /// batch call. As a nested class it can reach the outer engine's private ONNX sessions and
    /// per-chunk primitives directly.
    /// </summary>
    public sealed class StreamingSession : IStreamingTranscriptionSession
    {
        private readonly NemotronAsrEngine _engine;
        private readonly long _languageId;
        private readonly NemotronFeatureExtractor _featureExtractor;
        private readonly SileroVadEngine? _vadEngine;
        private readonly SileroVadGate? _vadGate;
        private readonly List<byte> _pending = [];
        private readonly List<int> _tokenIds = [];

        private float[] _cacheLastChannel;
        private float[] _cacheLastTime;
        private long _cacheLastChannelLen;
        private long _lastToken;
        private float[] _hState;
        private float[] _cState;
        private bool _finished;

        internal StreamingSession(NemotronAsrEngine engine, long languageId, bool enableVad)
        {
            _engine = engine;
            _languageId = languageId;
            _featureExtractor = engine.CreateFeatureExtractor();

            _cacheLastChannel = new float[engine._encoderLayers * engine._cacheChannelFrames * engine._encoderHidden];
            _cacheLastTime = new float[engine._encoderLayers * engine._encoderHidden * engine._convContext];
            _cacheLastChannelLen = 0;

            _lastToken = engine._blankId;
            _hState = new float[engine._decoderLayers * engine._decoderHidden];
            _cState = new float[engine._decoderLayers * engine._decoderHidden];

            if (enableVad)
            {
                _vadEngine = new SileroVadEngine(Path.Combine(engine._modelDirectory, engine._vadFilename), engine._sampleRate);
                _vadGate = new SileroVadGate(_vadEngine, engine._vadThreshold, engine._chunkSamples, engine._sampleRate, engine._vadSilenceDurationMs, engine._vadPrefixPaddingMs);
            }
        }

        public int SampleRate => _engine._sampleRate;

        public Task<string?> ProcessChunkAsync(byte[] pcm16Chunk, CancellationToken cancellationToken)
        {
            if (_finished)
            {
                return Task.FromResult<string?>(null);
            }

            _pending.AddRange(pcm16Chunk);
            var chunkByteSize = _engine._chunkSamples * 2;
            var producedNewTokens = false;

            while (_pending.Count >= chunkByteSize)
            {
                var chunkBytes = _pending.GetRange(0, chunkByteSize).ToArray();
                _pending.RemoveRange(0, chunkByteSize);

                if (DecodeOneChunk(BytesToFloatSamples(chunkBytes, _engine._chunkSamples)))
                {
                    producedNewTokens = true;
                }
            }

            return Task.FromResult(producedNewTokens ? _engine._vocabulary.Decode(_tokenIds) : null);
        }

        public Task<string> FinishAsync(CancellationToken cancellationToken)
        {
            if (!_finished && _pending.Count > 0)
            {
                // Right-pad the trailing partial chunk with zeros, same as the batch path's last chunk.
                var chunk = new float[_engine._chunkSamples];
                var availableSamples = _pending.Count / 2;
                var pendingBytes = _pending.ToArray();
                for (var i = 0; i < availableSamples; i++)
                {
                    chunk[i] = BitConverter.ToInt16(pendingBytes, i * 2) / 32768f;
                }

                DecodeOneChunk(chunk);
                _pending.Clear();
            }

            _finished = true;
            return Task.FromResult(_engine._vocabulary.Decode(_tokenIds));
        }

        private bool DecodeOneChunk(float[] chunk)
        {
            if (_vadGate?.ShouldDropChunk(chunk) == true)
            {
                return false;
            }

            var melChunk = _featureExtractor.ProcessChunk(chunk);
            var frames = _engine.RunEncoderChunk(melChunk, _languageId, ref _cacheLastChannel, ref _cacheLastTime, ref _cacheLastChannelLen);

            var producedNewTokens = false;
            foreach (var frame in frames)
            {
                var newTokens = _engine.DecodeFrame(frame, ref _lastToken, ref _hState, ref _cState);
                if (newTokens.Count > 0)
                {
                    _tokenIds.AddRange(newTokens);
                    producedNewTokens = true;
                }
            }

            return producedNewTokens;
        }

        private static float[] BytesToFloatSamples(byte[] pcm16Bytes, int sampleCount)
        {
            var samples = new float[sampleCount];
            for (var i = 0; i < sampleCount; i++)
            {
                samples[i] = BitConverter.ToInt16(pcm16Bytes, i * 2) / 32768f;
            }

            return samples;
        }

        public void Dispose() => _vadEngine?.Dispose();
    }
}
