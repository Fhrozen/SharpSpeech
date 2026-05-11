using System.Text.Json;
using FastTTSR.Api.Models;

namespace FastTTSR.Api.Services;

public sealed class ModelCatalog : IModelCatalog
{
    private static readonly StringComparer Comparer = StringComparer.OrdinalIgnoreCase;

    private readonly IReadOnlyDictionary<string, TtsModelDefinition> _models;

    public ModelCatalog()
    {
        var configuredModels = LoadConfiguredModels();
        var models = configuredModels.Count > 0 ? configuredModels : GetDefaultModels();
        _models = models.ToDictionary(m => m.Name, Comparer);
    }

    public IReadOnlyCollection<TtsModelDefinition> GetSupportedModels() => _models.Values.ToArray();

    public bool TryGetModel(string modelName, out TtsModelDefinition? model) => _models.TryGetValue(modelName, out model);

    private static IReadOnlyList<TtsModelDefinition> LoadConfiguredModels()
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
            var parsed = JsonSerializer.Deserialize<ModelCatalogConfig>(json, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });

            return parsed?.Models?.Where(m => !string.IsNullOrWhiteSpace(m.Name)).ToArray() ?? [];
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Failed to load model config: {ex.Message}");
            return [];
        }
    }

    private static IReadOnlyList<TtsModelDefinition> GetDefaultModels() =>
    [
        new()
        {
            Name = "kokoro-q4",
            DisplayName = "Kokoro Q4",
            Description = "Kokoro ONNX Q4 model loaded through Sherpa-ONNX runtime.",
            Engine = "kokoro",
            ModelPath = "onnx/model_q4.onnx",
            VoicesPath = "voices.bin",
            Assets =
            [
                new TtsModelAsset
                {
                    RelativePath = "onnx/model_q4.onnx",
                    Url = "https://huggingface.co/onnx-community/Kokoro-82M-v1.0-ONNX/resolve/main/onnx/model_q4.onnx"
                },
                new TtsModelAsset
                {
                    RelativePath = "voices.bin",
                    Url = "https://huggingface.co/onnx-community/Kokoro-82M-v1.0-ONNX/resolve/main/voices.bin"
                }
            ],
            SupportedLanguages = ["en-us", "ja-jp"],
            Speakers = ["af_bella", "af_nicole", "am_adam"]
        },
        new()
        {
            Name = "kokoro-full",
            DisplayName = "Kokoro Full",
            Description = "Kokoro ONNX full model loaded through Sherpa-ONNX runtime.",
            Engine = "kokoro",
            ModelPath = "onnx/model.onnx",
            VoicesPath = "voices.bin",
            Assets =
            [
                new TtsModelAsset
                {
                    RelativePath = "onnx/model.onnx",
                    Url = "https://huggingface.co/onnx-community/Kokoro-82M-v1.0-ONNX/resolve/main/onnx/model.onnx"
                },
                new TtsModelAsset
                {
                    RelativePath = "voices.bin",
                    Url = "https://huggingface.co/onnx-community/Kokoro-82M-v1.0-ONNX/resolve/main/voices.bin"
                }
            ],
            SupportedLanguages = ["en-us", "ja-jp"],
            Speakers = ["af_bella", "af_nicole", "am_adam"]
        },
        new()
        {
            Name = "supertonic-3",
            DisplayName = "Supertonic 3",
            Description = "Supertonic 3 ONNX model loaded through Sherpa-ONNX runtime.",
            Engine = "vits",
            ModelPath = "model.onnx",
            TokensPath = "tokens.txt",
            Assets =
            [
                new TtsModelAsset
                {
                    RelativePath = "model.onnx",
                    Url = "https://huggingface.co/Supertone/supertonic-3/resolve/main/model.onnx"
                },
                new TtsModelAsset
                {
                    RelativePath = "tokens.txt",
                    Url = "https://huggingface.co/Supertone/supertonic-3/resolve/main/tokens.txt"
                },
                new TtsModelAsset
                {
                    RelativePath = "speakers.json",
                    Url = "https://huggingface.co/Supertone/supertonic-3/resolve/main/speakers.json"
                }
            ],
            SupportedLanguages = ["en", "ko"],
            Speakers = ["alloy", "aria", "nova"]
        }
    ];

    private sealed class ModelCatalogConfig
    {
        public IReadOnlyList<TtsModelDefinition> Models { get; init; } = [];
    }
}
