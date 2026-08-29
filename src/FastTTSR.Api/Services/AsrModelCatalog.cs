using System.Text.Json;
using FastTTSR.Api.Models;

namespace FastTTSR.Api.Services;

public sealed class AsrModelCatalog : IAsrModelCatalog
{
    private static readonly StringComparer Comparer = StringComparer.OrdinalIgnoreCase;

    private readonly IReadOnlyDictionary<string, AsrModelDefinition> _models;

    public AsrModelCatalog()
    {
        var configuredModels = LoadConfiguredModels();
        var models = configuredModels.Count > 0 ? configuredModels : GetDefaultModels();
        _models = models.ToDictionary(m => m.Name, Comparer);
    }

    public IReadOnlyCollection<AsrModelDefinition> GetSupportedModels() => _models.Values.ToArray();

    public bool TryGetModel(string modelName, out AsrModelDefinition? model) => _models.TryGetValue(modelName, out model);

    private static IReadOnlyList<AsrModelDefinition> LoadConfiguredModels()
    {
        try
        {
            var path = Environment.GetEnvironmentVariable("MODEL_CONFIG_PATH")
                       ?? Path.Combine(AppContext.BaseDirectory, "config.json");

            if (!File.Exists(path))
            {
                return [];
            }

            var json = File.ReadAllText(path);
            var parsed = JsonSerializer.Deserialize<AsrModelCatalogConfig>(json, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });

            return parsed?.AsrModels?
                .Where(m => !string.IsNullOrWhiteSpace(m.Name))
                .ToArray() ?? [];
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Failed to load ASR model config: {ex.Message}");
            return [];
        }
    }

    private static IReadOnlyList<AsrModelDefinition> GetDefaultModels() =>
    [
        new()
        {
            Name = "whisper-base",
            DisplayName = "Whisper Base",
            Description = "OpenAI Whisper base multilingual model (GGML), run via whisper.cpp.",
            Engine = "whisper",
            ModelPath = "ggml-base.bin",
            Assets =
            [
                new ModelAsset
                {
                    RelativePath = "ggml-base.bin",
                    Url = "https://huggingface.co/ggerganov/whisper.cpp/resolve/main/ggml-base.bin"
                }
            ],
            SupportedLanguages = [],
            SupportsLanguageAutoDetect = true,
            SupportsVad = false
        },
        new()
        {
            Name = "nemotron-3.5",
            DisplayName = "Nemotron 3.5 ASR",
            Description = "NVIDIA Nemotron 3.5 streaming ASR (cache-aware FastConformer-RNNT, INT4 ONNX).",
            Engine = "nemotron-3.5",
            ModelPath = "",
            Assets =
            [
                new ModelAsset { RelativePath = "encoder.onnx", Url = "https://huggingface.co/onnx-community/nemotron-3.5-asr-streaming-0.6b-onnx-int4/resolve/main/encoder.onnx" },
                new ModelAsset { RelativePath = "encoder.onnx.data", Url = "https://huggingface.co/onnx-community/nemotron-3.5-asr-streaming-0.6b-onnx-int4/resolve/main/encoder.onnx.data" },
                new ModelAsset { RelativePath = "decoder.onnx", Url = "https://huggingface.co/onnx-community/nemotron-3.5-asr-streaming-0.6b-onnx-int4/resolve/main/decoder.onnx" },
                new ModelAsset { RelativePath = "decoder.onnx.data", Url = "https://huggingface.co/onnx-community/nemotron-3.5-asr-streaming-0.6b-onnx-int4/resolve/main/decoder.onnx.data" },
                new ModelAsset { RelativePath = "joint.onnx", Url = "https://huggingface.co/onnx-community/nemotron-3.5-asr-streaming-0.6b-onnx-int4/resolve/main/joint.onnx" },
                new ModelAsset { RelativePath = "joint.onnx.data", Url = "https://huggingface.co/onnx-community/nemotron-3.5-asr-streaming-0.6b-onnx-int4/resolve/main/joint.onnx.data" },
                new ModelAsset { RelativePath = "audio_processor_config.json", Url = "https://huggingface.co/onnx-community/nemotron-3.5-asr-streaming-0.6b-onnx-int4/resolve/main/audio_processor_config.json" },
                new ModelAsset { RelativePath = "model_config.json", Url = "https://huggingface.co/onnx-community/nemotron-3.5-asr-streaming-0.6b-onnx-int4/resolve/main/model_config.json" },
                new ModelAsset { RelativePath = "genai_config.json", Url = "https://huggingface.co/onnx-community/nemotron-3.5-asr-streaming-0.6b-onnx-int4/resolve/main/genai_config.json" },
                new ModelAsset { RelativePath = "tokenizer.json", Url = "https://huggingface.co/onnx-community/nemotron-3.5-asr-streaming-0.6b-onnx-int4/resolve/main/tokenizer.json" },
                new ModelAsset { RelativePath = "vocab.txt", Url = "https://huggingface.co/onnx-community/nemotron-3.5-asr-streaming-0.6b-onnx-int4/resolve/main/vocab.txt" },
                new ModelAsset { RelativePath = "silero_vad.onnx", Url = "https://huggingface.co/onnx-community/nemotron-3.5-asr-streaming-0.6b-onnx-int4/resolve/main/silero_vad.onnx" }
            ],
            SupportedLanguages =
            [
                "en", "es", "fr", "it", "pt", "nl", "de", "tr", "ru", "ar", "hi", "ja", "ko", "vi", "uk",
                "pl", "sv", "cs", "nb", "da", "bg", "fi", "hr", "sk", "zh", "hu", "ro", "et",
                "el", "lt", "lv", "mt", "sl", "he", "th", "nn"
            ],
            SupportsLanguageAutoDetect = true,
            SupportsVad = true
        }
    ];

    private sealed class AsrModelCatalogConfig
    {
        public IReadOnlyList<AsrModelDefinition> AsrModels { get; init; } = [];
    }
}
