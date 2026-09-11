using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;

namespace SharpAudio.Api.Services;

/// <summary>
/// Holds parsed voice style tensors loaded from a Supertonic-3 voice style JSON file.
/// Each voice style file contains two float tensors: style_ttl (for the text encoder)
/// and style_dp (for the duration predictor).
/// </summary>
public sealed class SupertonicStyle
{
    public float[] Ttl { get; }
    public int[] TtlShape { get; }
    public float[] Dp { get; }
    public int[] DpShape { get; }

    public SupertonicStyle(float[] ttl, int[] ttlShape, float[] dp, int[] dpShape)
    {
        Ttl = ttl;
        TtlShape = ttlShape;
        Dp = dp;
        DpShape = dpShape;
    }
}

/// <summary>
/// Core ONNX inference engine for Supertonic-3. Owns the four ONNX sessions and all
/// inference logic: text preprocessing, Unicode tokenization, noisy-latent sampling,
/// the iterative flow-matching denoising loop, and vocoder decoding.
/// </summary>
public sealed class SupertonicTtsEngine : IDisposable
{
    // ONNX sessions
    private readonly InferenceSession _dpOrt;         // duration predictor
    private readonly InferenceSession _textEncOrt;    // text encoder
    private readonly InferenceSession _vectorEstOrt;  // vector estimator (denoiser)
    private readonly InferenceSession _vocoderOrt;    // vocoder

    // Runtime config (from tts.json)
    public int SampleRate { get; }
    private readonly int _baseChunkSize;
    private readonly int _chunkCompressFactor;
    private readonly int _latentDim;

    // Unicode tokenizer (maps code-point indices → model token IDs)
    private readonly long[] _unicodeIndexer;

    // Sentence-boundary regex, respects common abbreviations
    private static readonly Regex SentenceRegex = new(
        @"(?<!Mr\.|Mrs\.|Ms\.|Dr\.|Prof\.|Sr\.|Jr\.|Ph\.D\.|etc\.|e\.g\.|i\.e\.|vs\.|Inc\.|Ltd\.|Co\.|Corp\.|St\.|Ave\.|Blvd\.)(?<!\b[A-Z]\.)(?<=[.!?])\s+",
        RegexOptions.Compiled);

    private static readonly Regex ParagraphRegex = new(@"\n\s*\n+", RegexOptions.Compiled);
    private static readonly Regex ExtraSpaceRegex = new(@"\s+", RegexOptions.Compiled);

    private bool _disposed;

    public SupertonicTtsEngine(string onnxDir, int deviceId = -1)
    {
        var opts = new Microsoft.ML.OnnxRuntime.SessionOptions
        {
            LogSeverityLevel = OrtLoggingLevel.ORT_LOGGING_LEVEL_ERROR
        };

        // Load config
        var cfgPath = Path.Combine(onnxDir, "tts.json");
        var cfgJson = File.ReadAllText(cfgPath);
        using var cfgDoc = JsonDocument.Parse(cfgJson);
        var root = cfgDoc.RootElement;
        SampleRate           = root.GetProperty("ae").GetProperty("sample_rate").GetInt32();
        _baseChunkSize       = root.GetProperty("ae").GetProperty("base_chunk_size").GetInt32();
        _chunkCompressFactor = root.GetProperty("ttl").GetProperty("chunk_compress_factor").GetInt32();
        _latentDim           = root.GetProperty("ttl").GetProperty("latent_dim").GetInt32();

        // Load unicode indexer
        var indexerPath = Path.Combine(onnxDir, "unicode_indexer.json");
        var indexerJson = File.ReadAllText(indexerPath);
        _unicodeIndexer = JsonSerializer.Deserialize<long[]>(indexerJson)
            ?? throw new Exception("Failed to load unicode_indexer.json");

        // Load ONNX sessions
        _dpOrt        = new InferenceSession(Path.Combine(onnxDir, "duration_predictor.onnx"), opts);
        _textEncOrt   = new InferenceSession(Path.Combine(onnxDir, "text_encoder.onnx"), opts);
        _vectorEstOrt = new InferenceSession(Path.Combine(onnxDir, "vector_estimator.onnx"), opts);
        _vocoderOrt   = new InferenceSession(Path.Combine(onnxDir, "vocoder.onnx"), opts);

        Console.WriteLine($"[Supertonic-3] Loaded engine from: {onnxDir}  (sr={SampleRate})");
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Public synthesis API
    // ──────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Synthesizes speech for a single text string, chunking long inputs automatically.
    /// Returns a float waveform array.
    /// </summary>
    public float[] Synthesize(string text, string lang, SupertonicStyle style,
        int totalStep = 8, float speed = 1.05f, float silenceDuration = 0.3f)
    {
        var maxLen = (lang is "ko" or "ja") ? 120 : 300;
        var chunks = ChunkText(text, maxLen);

        var wavParts = new List<float[]>();
        foreach (var chunk in chunks)
        {
            var wav = Infer([chunk], [lang], style, totalStep, speed);
            wavParts.Add(wav);
        }

        if (wavParts.Count == 1)
        {
            return wavParts[0];
        }

        // Concatenate with silence gaps
        var silenceLen = (int)(silenceDuration * SampleRate);
        var silence = new float[silenceLen];
        var total = wavParts.Sum(p => p.Length) + silence.Length * (wavParts.Count - 1);
        var result = new float[total];
        int offset = 0;
        for (int i = 0; i < wavParts.Count; i++)
        {
            wavParts[i].CopyTo(result, offset);
            offset += wavParts[i].Length;
            if (i < wavParts.Count - 1)
            {
                silence.CopyTo(result, offset);
                offset += silenceLen;
            }
        }

        return result;
    }

    /// <summary>
    /// Synthesizes and converts the waveform to 16-bit PCM bytes.
    /// </summary>
    public byte[] SynthesizeToBytes(string text, string lang, SupertonicStyle style,
        int totalStep = 8, float speed = 1.05f)
    {
        var waveform = Synthesize(text, lang, style, totalStep, speed);
        return ConvertToPcm16(waveform);
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Core ONNX inference
    // ──────────────────────────────────────────────────────────────────────────

    private float[] Infer(List<string> textList, List<string> langList, SupertonicStyle style,
        int totalStep, float speed)
    {
        int bsz = textList.Count;

        // Preprocess and tokenize
        var processedTexts = textList.Select((t, i) => PreprocessText(t, langList[i])).ToList();
        var textIdsLengths = processedTexts.Select(t => (long)t.Length).ToArray();
        long maxLen = textIdsLengths.Max();

        // Console.WriteLine($"[SupertonicEngine.Infer] batch_size={bsz}, processed_texts_count={processedTexts.Count}, max_length={maxLen}");
        // Console.Error.WriteLine($"[SupertonicEngine.Infer] batch_size={bsz}, processed_texts_count={processedTexts.Count}, max_length={maxLen}");
        // Console.WriteLine($"[SupertonicEngine.Infer] text_ids_lengths=[{string.Join(", ", textIdsLengths)}]");
        // Console.Error.WriteLine($"[SupertonicEngine.Infer] text_ids_lengths=[{string.Join(", ", textIdsLengths)}]");

        // Build textIds 2D array [bsz, maxLen]
        var textIdsFlat = new long[bsz * maxLen];
        for (int b = 0; b < bsz; b++)
        {
            var codePoints = processedTexts[b].Select(c => (int)c).ToArray();
            for (int j = 0; j < codePoints.Length; j++)
            {
                int cp = codePoints[j];
                long tokenId = (cp >= 0 && cp < _unicodeIndexer.Length) ? _unicodeIndexer[cp] : 0L;
                textIdsFlat[b * maxLen + j] = tokenId;
            }
        }

        // Build textMask [bsz, 1, maxLen]
        var textMaskFlat = BuildLengthMask(textIdsLengths, maxLen);

        var textIdsShape  = new[] { bsz, (int)maxLen };
        var textMaskShape = new[] { bsz, 1, (int)maxLen };
        var textIdsTensor  = new DenseTensor<long>(textIdsFlat, textIdsShape);
        var textMaskTensor = new DenseTensor<float>(textMaskFlat, textMaskShape);
        var styleTtlTensor = new DenseTensor<float>(style.Ttl, style.TtlShape);
        var styleDpTensor  = new DenseTensor<float>(style.Dp, style.DpShape);

        // ── Duration predictor ──
        var dpInputs = new List<NamedOnnxValue>
        {
            NamedOnnxValue.CreateFromTensor("text_ids",  textIdsTensor),
            NamedOnnxValue.CreateFromTensor("style_dp",  styleDpTensor),
            NamedOnnxValue.CreateFromTensor("text_mask", textMaskTensor)
        };
        float[] duration;
        using (var dpOutputs = _dpOrt.Run(dpInputs))
        {
            var durationTensor = dpOutputs.First(o => o.Name == "duration").AsTensor<float>();
            duration = durationTensor.ToArray();
            
            // Console.WriteLine($"[SupertonicEngine.Infer] Duration predictor output: shape=[{string.Join("x", durationTensor.Dimensions.ToArray())}], length={duration.Length}");
            // Console.Error.WriteLine($"[SupertonicEngine.Infer] Duration predictor output: shape=[{string.Join("x", durationTensor.Dimensions.ToArray())}], length={duration.Length}");
            
            // if (duration.Length <= 10)
            // {
            //     Console.WriteLine($"[SupertonicEngine.Infer] Duration values: [{string.Join(", ", duration.Select(d => d.ToString("F4")))}]");
            //     Console.Error.WriteLine($"[SupertonicEngine.Infer] Duration values: [{string.Join(", ", duration.Select(d => d.ToString("F4")))}]");
            // }
            // else
            // {
            //     Console.WriteLine($"[SupertonicEngine.Infer] First 10 duration values: [{string.Join(", ", duration.Take(10).Select(d => d.ToString("F4")))}]");
            //     Console.Error.WriteLine($"[SupertonicEngine.Infer] First 10 duration values: [{string.Join(", ", duration.Take(10).Select(d => d.ToString("F4")))}]");
            // }
        }

        // Apply speed factor
        // Console.WriteLine($"[SupertonicEngine] Applying speed factor: speed={speed}, duration_count={duration.Length}");
        // Console.Error.WriteLine($"[SupertonicEngine] Applying speed factor: speed={speed}, duration_count={duration.Length}");
        
        // if (duration.Length >= 3)
        // {
        //     Console.WriteLine($"[SupertonicEngine] First 3 durations before: [{duration[0]:F4}, {duration[1]:F4}, {duration[2]:F4}]");
        //     Console.Error.WriteLine($"[SupertonicEngine] First 3 durations before: [{duration[0]:F4}, {duration[1]:F4}, {duration[2]:F4}]");
        // }
        
        for (int i = 0; i < duration.Length; i++)
        {
            duration[i] /= speed;
        }
        
        // if (duration.Length >= 3)
        // {
        //     Console.WriteLine($"[SupertonicEngine] First 3 durations after: [{duration[0]:F4}, {duration[1]:F4}, {duration[2]:F4}]");
        //     Console.Error.WriteLine($"[SupertonicEngine] First 3 durations after: [{duration[0]:F4}, {duration[1]:F4}, {duration[2]:F4}]");
        // }

        // ── Text encoder ──
        var textEncInputs = new List<NamedOnnxValue>
        {
            NamedOnnxValue.CreateFromTensor("text_ids",  textIdsTensor),
            NamedOnnxValue.CreateFromTensor("style_ttl", styleTtlTensor),
            NamedOnnxValue.CreateFromTensor("text_mask", textMaskTensor)
        };
        float[] textEmb;
        int[] textEmbShape;
        using (var textEncOutputs = _textEncOrt.Run(textEncInputs))
        {
            var textEmbTensor = textEncOutputs.First(o => o.Name == "text_emb").AsTensor<float>();
            textEmb = textEmbTensor.ToArray();
            textEmbShape = textEmbTensor.Dimensions.ToArray();
        }

        // ── Sample noisy latent ──
        float wavLenMax = duration.Max() * SampleRate;
        var wavLengths  = duration.Select(d => (long)(d * SampleRate)).ToArray();
        int chunkSize   = _baseChunkSize * _chunkCompressFactor;
        int latentLen   = (int)((wavLenMax + chunkSize - 1) / chunkSize);
        int latentDim   = _latentDim * _chunkCompressFactor;

        var xt = SampleGaussian(bsz * latentDim * latentLen);
        var latentMaskFlat = BuildLatentMask(wavLengths, _baseChunkSize, _chunkCompressFactor, latentLen);

        var latentShape     = new[] { bsz, latentDim, latentLen };
        var latentMaskShape = new[] { bsz, 1, latentLen };

        // Apply mask to noisy latent
        for (int b = 0; b < bsz; b++)
        {
            for (int d = 0; d < latentDim; d++)
            {
                for (int t = 0; t < latentLen; t++)
                {
                    xt[b * latentDim * latentLen + d * latentLen + t] *=
                        latentMaskFlat[b * latentLen + t];
                }
            }
        }

        var totalStepArray   = Enumerable.Repeat((float)totalStep, bsz).ToArray();

        // ── Iterative denoising loop ──
        for (int step = 0; step < totalStep; step++)
        {
            var currentStepArray = Enumerable.Repeat((float)step, bsz).ToArray();

            var xtCopy = (float[])xt.Clone();

            var vectorEstInputs = new List<NamedOnnxValue>
            {
                NamedOnnxValue.CreateFromTensor("noisy_latent",
                    new DenseTensor<float>(xtCopy, latentShape)),
                NamedOnnxValue.CreateFromTensor("text_emb",
                    new DenseTensor<float>(textEmb, textEmbShape)),
                NamedOnnxValue.CreateFromTensor("style_ttl", styleTtlTensor),
                NamedOnnxValue.CreateFromTensor("text_mask", textMaskTensor),
                NamedOnnxValue.CreateFromTensor("latent_mask",
                    new DenseTensor<float>(latentMaskFlat, latentMaskShape)),
                NamedOnnxValue.CreateFromTensor("total_step",
                    new DenseTensor<float>(totalStepArray, new[] { bsz })),
                NamedOnnxValue.CreateFromTensor("current_step",
                    new DenseTensor<float>(currentStepArray, new[] { bsz }))
            };

            using var vectorEstOutputs = _vectorEstOrt.Run(vectorEstInputs);
            var denoisedTensor = vectorEstOutputs.First(o => o.Name == "denoised_latent").AsTensor<float>();
            xt = denoisedTensor.ToArray();
        }

        // ── Vocoder ──
        var vocoderInputs = new List<NamedOnnxValue>
        {
            NamedOnnxValue.CreateFromTensor("latent", new DenseTensor<float>(xt, latentShape))
        };
        using var vocoderOutputs = _vocoderOrt.Run(vocoderInputs);
        return vocoderOutputs.First(o => o.Name == "wav_tts").AsTensor<float>().ToArray();
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Text preprocessing
    // ──────────────────────────────────────────────────────────────────────────

    private static string PreprocessText(string text, string lang)
    {
        // Unicode normalization
        text = text.Normalize(NormalizationForm.FormKD);

        // Remove emojis
        text = RemoveEmojis(text);

        // Symbol replacements
        text = text
            .Replace("\u2013", "-")   // en dash
            .Replace("\u2011", "-")   // non-breaking hyphen
            .Replace("\u2014", "-")   // em dash
            .Replace("_", " ")
            .Replace("\u201C", "\"")  // left double quote
            .Replace("\u201D", "\"")  // right double quote
            .Replace("\u2018", "'")   // left single quote
            .Replace("\u2019", "'")   // right single quote
            .Replace("\u00B4", "'")   // acute accent
            .Replace("`", "'")
            .Replace("[", " ")
            .Replace("]", " ")
            .Replace("|", " ")
            .Replace("/", " ")
            .Replace("#", " ")
            .Replace("\u2192", " ")   // right arrow
            .Replace("\u2190", " ");  // left arrow

        // Remove special symbols
        text = Regex.Replace(text, @"[♥☆♡©\\]", "");

        // Common expression replacements
        text = text
            .Replace("@", " at ")
            .Replace("e.g.,", "for example, ")
            .Replace("i.e.,", "that is, ");

        // Fix spacing around punctuation
        text = Regex.Replace(text, @" ,",  ",");
        text = Regex.Replace(text, @" \.", ".");
        text = Regex.Replace(text, @" !",  "!");
        text = Regex.Replace(text, @" \?", "?");
        text = Regex.Replace(text, @" ;",  ";");
        text = Regex.Replace(text, @" :",  ":");
        text = Regex.Replace(text, @" '",  "'");

        // Remove duplicate quotes
        while (text.Contains("\"\"")) text = text.Replace("\"\"", "\"");
        while (text.Contains("''"))   text = text.Replace("''", "'");

        // Collapse whitespace
        text = ExtraSpaceRegex.Replace(text, " ").Trim();

        // Ensure ends with punctuation
        if (!Regex.IsMatch(text, @"[.!?;:,'""\)\]}…。」』】〉》›»]$"))
        {
            text += ".";
        }

        // Wrap with language tags
        return $"<{lang}>{text}</{lang}>";
    }

    private static string RemoveEmojis(string text)
    {
        var sb = new StringBuilder(text.Length);
        for (int i = 0; i < text.Length; i++)
        {
            int cp;
            if (char.IsHighSurrogate(text[i]) && i + 1 < text.Length && char.IsLowSurrogate(text[i + 1]))
            {
                cp = char.ConvertToUtf32(text[i], text[i + 1]);
                i++;
            }
            else
            {
                cp = text[i];
            }

            bool isEmoji = (cp >= 0x1F600 && cp <= 0x1F64F) ||
                           (cp >= 0x1F300 && cp <= 0x1F5FF) ||
                           (cp >= 0x1F680 && cp <= 0x1F6FF) ||
                           (cp >= 0x1F700 && cp <= 0x1F77F) ||
                           (cp >= 0x1F780 && cp <= 0x1F7FF) ||
                           (cp >= 0x1F800 && cp <= 0x1F8FF) ||
                           (cp >= 0x1F900 && cp <= 0x1F9FF) ||
                           (cp >= 0x1FA00 && cp <= 0x1FA6F) ||
                           (cp >= 0x1FA70 && cp <= 0x1FAFF) ||
                           (cp >= 0x2600  && cp <= 0x26FF)  ||
                           (cp >= 0x2700  && cp <= 0x27BF)  ||
                           (cp >= 0x1F1E6 && cp <= 0x1F1FF);

            if (!isEmoji)
            {
                sb.Append(cp > 0xFFFF ? char.ConvertFromUtf32(cp) : ((char)cp).ToString());
            }
        }

        return sb.ToString();
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Tensor helpers
    // ──────────────────────────────────────────────────────────────────────────

    private static float[] BuildLengthMask(long[] lengths, long maxLen)
    {
        int bsz = lengths.Length;
        var mask = new float[bsz * maxLen];
        for (int b = 0; b < bsz; b++)
        {
            for (long t = 0; t < maxLen; t++)
            {
                mask[b * maxLen + t] = t < lengths[b] ? 1.0f : 0.0f;
            }
        }

        return mask;
    }

    private static float[] BuildLatentMask(long[] wavLengths, int baseChunkSize,
        int chunkCompressFactor, int latentLen)
    {
        int bsz = wavLengths.Length;
        int latentSize = baseChunkSize * chunkCompressFactor;
        var latentLengths = wavLengths.Select(len => (len + latentSize - 1) / latentSize).ToArray();

        var mask = new float[bsz * latentLen];
        for (int b = 0; b < bsz; b++)
        {
            for (int t = 0; t < latentLen; t++)
            {
                mask[b * latentLen + t] = t < latentLengths[b] ? 1.0f : 0.0f;
            }
        }

        return mask;
    }

    private static float[] SampleGaussian(int size)
    {
        var result = new float[size];
        for (int i = 0; i < size; i += 2)
        {
            double u1 = 1.0 - Random.Shared.NextDouble();
            double u2 = 1.0 - Random.Shared.NextDouble();
            double r  = Math.Sqrt(-2.0 * Math.Log(u1));
            double theta = 2.0 * Math.PI * u2;
            result[i] = (float)(r * Math.Cos(theta));
            if (i + 1 < size)
            {
                result[i + 1] = (float)(r * Math.Sin(theta));
            }
        }

        return result;
    }

    private static byte[] ConvertToPcm16(float[] waveform)
    {
        var buffer = new byte[waveform.Length * 2];
        for (int i = 0; i < waveform.Length; i++)
        {
            var sample    = Math.Clamp(waveform[i], -1.0f, 1.0f);
            var pcmSample = (short)(sample * 32767);
            var bytes     = BitConverter.GetBytes(pcmSample);
            buffer[i * 2]     = bytes[0];
            buffer[i * 2 + 1] = bytes[1];
        }

        return buffer;
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Text chunking
    // ──────────────────────────────────────────────────────────────────────────

    private static List<string> ChunkText(string text, int maxLen)
    {
        var chunks = new List<string>();

        var paragraphs = ParagraphRegex.Split(text.Trim())
            .Select(p => p.Trim())
            .Where(p => !string.IsNullOrEmpty(p))
            .ToList();

        foreach (var paragraph in paragraphs)
        {
            var sentences = SentenceRegex.Split(paragraph);
            string current = "";

            foreach (var sentence in sentences)
            {
                if (string.IsNullOrEmpty(sentence)) continue;

                if (current.Length + sentence.Length + 1 <= maxLen)
                {
                    current = string.IsNullOrEmpty(current) ? sentence : current + " " + sentence;
                }
                else
                {
                    if (!string.IsNullOrEmpty(current))
                    {
                        chunks.Add(current.Trim());
                    }

                    current = sentence;
                }
            }

            if (!string.IsNullOrEmpty(current))
            {
                chunks.Add(current.Trim());
            }
        }

        if (chunks.Count == 0)
        {
            chunks.Add(text.Trim());
        }

        return chunks;
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Voice style loading (static helper)
    // ──────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Loads a single voice style JSON file into a <see cref="SupertonicStyle"/> with
    /// batch size 1. The JSON format is:
    /// <code>
    /// {
    ///   "style_ttl": { "dims": [1, D1, D2], "data": [[[...]]] },
    ///   "style_dp":  { "dims": [1, D1, D2], "data": [[[...]]] }
    /// }
    /// </code>
    /// </summary>
    public static SupertonicStyle LoadVoiceStyle(string voiceStylePath)
    {
        var json = File.ReadAllText(voiceStylePath);
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        var (ttlFlat, ttlShape) = ParseStyleTensor(root.GetProperty("style_ttl"));
        var (dpFlat,  dpShape)  = ParseStyleTensor(root.GetProperty("style_dp"));

        return new SupertonicStyle(ttlFlat, ttlShape, dpFlat, dpShape);
    }

    private static (float[] flat, int[] shape) ParseStyleTensor(JsonElement element)
    {
        var dims = element.GetProperty("dims").EnumerateArray()
                          .Select(d => (int)d.GetInt64()).ToArray();

        var flatList = new List<float>();
        foreach (var batch in element.GetProperty("data").EnumerateArray())
        {
            foreach (var row in batch.EnumerateArray())
            {
                foreach (var val in row.EnumerateArray())
                {
                    flatList.Add(val.GetSingle());
                }
            }
        }

        return (flatList.ToArray(), dims);
    }

    // ──────────────────────────────────────────────────────────────────────────
    // IDisposable
    // ──────────────────────────────────────────────────────────────────────────

    public void Dispose()
    {
        if (_disposed) return;
        _dpOrt.Dispose();
        _textEncOrt.Dispose();
        _vectorEstOrt.Dispose();
        _vocoderOrt.Dispose();
        _disposed = true;
    }
}
