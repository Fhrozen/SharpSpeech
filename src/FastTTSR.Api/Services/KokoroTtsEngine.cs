using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using NumSharp;
using FastTTSR.Api.Services.Phonemization;

namespace FastTTSR.Api.Services;

public sealed class KokoroTtsEngine : IDisposable
{
    private readonly InferenceSession _session;
    private readonly EspeakWrapper _espeak;
    private readonly Dictionary<string, int> _symbolToIndex;
    private NDArray? _speakers;
    private bool _disposed;

    public KokoroTtsEngine(string modelPath, string? voicesPath = null, int deviceId = -1)
    {
        var options = new Microsoft.ML.OnnxRuntime.SessionOptions
        {
            LogSeverityLevel = OrtLoggingLevel.ORT_LOGGING_LEVEL_ERROR
        };

        if (deviceId >= 0)
        {
            // CUDA provider (future enhancement)
            // options.AppendExecutionProvider_CUDA(deviceId);
        }

        _session = new InferenceSession(modelPath, options);
        _espeak = new EspeakWrapper();
        _symbolToIndex = GetVocabulary();

        // Load default speaker voices if provided
        if (!string.IsNullOrWhiteSpace(voicesPath) && File.Exists(voicesPath))
        {
            LoadSpeakers(voicesPath);
        }
    }

    public void LoadSpeakers(string voicesPath)
    {
        try
        {
            var data = np.fromfile(voicesPath, np.float32);
            var totalElements = data.size;
            const int embeddingDim = 256;
            
            if (totalElements % embeddingDim != 0)
            {
                throw new Exception($"Voice file size ({totalElements} floats) is not divisible by embedding dimension ({embeddingDim})");
            }
            
            var numSpeakers = totalElements / embeddingDim;
            _speakers = data.reshape(numSpeakers, embeddingDim);
            Console.WriteLine($"Loaded speaker voices from: {voicesPath} ({numSpeakers} speakers, {embeddingDim} dims)");
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Failed to load speakers from {voicesPath}: {ex.Message}");
            _speakers = null;
        }
    }

    public float[] Synthesize(string text, string language = "en-us", float speed = 1.0f)
    {
        // KNOWN LIMITATION: Japanese kanji and katakana are not properly phonemized by espeak-ng.
        // espeak-ng does not have kanji-to-reading conversion, so it may output "character" or
        // incorrect phonemes. Proper Japanese support would require a kanji-to-romaji converter
        // like pykakasi, cutlet, or MeCab before phonemization.
        // For now, Japanese text with kanji/katakana may produce incorrect or English-sounding output.
        
        // Set espeak voice for the language
        _espeak.SetVoice(language);

        // Convert text to phonemes
        var phonemes = _espeak.ConvertToPhonemes(text);
        if (string.IsNullOrWhiteSpace(phonemes))
        {
            return Array.Empty<float>();
        }

        // Convert phonemes to tokens
        var tokens = GetTokens(phonemes);
        var tokenCount = tokens.Length;

        // Get speaker style vector
        var speakerStyle = GetSpeakerStyle(tokenCount);

        // Create input tensors
        var tokensTensor = new DenseTensor<long>(tokens, new[] { 1, tokenCount });
        var styleTensor = new DenseTensor<float>(speakerStyle, new[] { 1, 256 });
        var speedTensor = new DenseTensor<float>(new[] { speed }, new[] { 1 });

        var inputs = new List<NamedOnnxValue>
        {
            NamedOnnxValue.CreateFromTensor("input_ids", tokensTensor),
            NamedOnnxValue.CreateFromTensor("style", styleTensor),
            NamedOnnxValue.CreateFromTensor("speed", speedTensor)
        };

        // Run inference
        using var results = _session.Run(inputs);
        var output = results[0].AsTensor<float>();

        return output.ToArray();
    }

    public byte[] SynthesizeToBytes(string text, string language = "en-us", float speed = 1.0f)
    {
        var waveform = Synthesize(text, language, speed);
        return ConvertToPcm16(waveform);
    }

    private float[] GetSpeakerStyle(int tokenCount)
    {
        if (_speakers == null)
        {
            // Return zero vector if no speakers loaded
            return new float[256];
        }

        if (tokenCount >= _speakers.shape[0])
        {
            // Token count exceeds speaker data, return last speaker
            tokenCount = _speakers.shape[0] - 1;
        }

        return _speakers[tokenCount].ToArray<float>();
    }

    private long[] GetTokens(string phonemes)
    {
        var phonemeChars = SplitStringToChars(phonemes);
        var tokens = new List<long> { 0 }; // Start token

        foreach (var phoneme in phonemeChars)
        {
            if (_symbolToIndex.TryGetValue(phoneme, out var index))
            {
                tokens.Add(index);
            }
            else
            {
                tokens.Add(0); // Unknown token
            }
        }

        tokens.Add(0); // End token
        return tokens.ToArray();
    }

    private static byte[] ConvertToPcm16(float[] waveform)
    {
        var buffer = new byte[waveform.Length * 2];

        for (var i = 0; i < waveform.Length; i++)
        {
            var sample = Math.Clamp(waveform[i], -1.0f, 1.0f);
            var pcmSample = (short)(sample * 32767);
            var bytes = BitConverter.GetBytes(pcmSample);
            buffer[i * 2] = bytes[0];
            buffer[i * 2 + 1] = bytes[1];
        }

        return buffer;
    }

    private static Dictionary<string, int> GetVocabulary()
    {
        var pad = new[] { "$" };
        var punctuation = SplitStringToChars(";:,.!?¡¿—…\"«»\"\" ");
        var letters = SplitStringToChars("ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz");
        var lettersIpa = SplitStringToChars("ɑɐɒæɓʙβɔɕçɗɖðʤəɘɚɛɜɝɞɟʄɡɠɢʛɦɧħɥʜɨɪʝɭɬɫɮʟɱɯɰŋɳɲɴøɵɸθœɶʘɹɺɾɻʀʁɽʂʃʈʧʉʊʋⱱʌɣɤʍχʎʏʑʐʒʔʡʕʢǀǁǂǃˈˌːˑʼʴʰʱʲʷˠˤ˞↓↑→↗↘\u0027̩\u0027ᵻ");

        var allSymbols = pad.Concat(punctuation).Concat(letters).Concat(lettersIpa).ToArray();
        var vocabulary = new Dictionary<string, int>();

        for (var i = 0; i < allSymbols.Length; i++)
        {
            vocabulary[allSymbols[i]] = i;
        }

        return vocabulary;
    }

    private static string[] SplitStringToChars(string text)
    {
        return text.Select(c => c.ToString()).ToArray();
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _session?.Dispose();
        _espeak?.Dispose();
        _disposed = true;
    }
}
