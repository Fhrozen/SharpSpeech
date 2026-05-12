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

            return parsed?.Models?
                .Where(m => !string.IsNullOrWhiteSpace(m.Name))
                .Select(ApplyCanonicalMetadata)
                .ToArray() ?? [];
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Failed to load model config: {ex.Message}");
            return [];
        }
    }

    private static IReadOnlyList<TtsModelDefinition> GetDefaultModels() =>
    [
        ApplyCanonicalMetadata(new()
        {
            Name = "supertonic-3",
            DisplayName = "Supertonic 3",
            Description = "Supertonic-3 multilingual ONNX TTS model — 31 languages, 10 preset voice styles, flow-matching inference.",
            Engine = "supertonic-3",
            ModelPath = "onnx",
            VoicesPath = "voice_styles",
            VoiceFileExtension = ".json",
            Assets =
            [
                new TtsModelAsset { RelativePath = "onnx/text_encoder.onnx",       Url = "https://huggingface.co/Supertone/supertonic-3/resolve/main/onnx/text_encoder.onnx" },
                new TtsModelAsset { RelativePath = "onnx/duration_predictor.onnx", Url = "https://huggingface.co/Supertone/supertonic-3/resolve/main/onnx/duration_predictor.onnx" },
                new TtsModelAsset { RelativePath = "onnx/vector_estimator.onnx",   Url = "https://huggingface.co/Supertone/supertonic-3/resolve/main/onnx/vector_estimator.onnx" },
                new TtsModelAsset { RelativePath = "onnx/vocoder.onnx",            Url = "https://huggingface.co/Supertone/supertonic-3/resolve/main/onnx/vocoder.onnx" },
                new TtsModelAsset { RelativePath = "onnx/tts.json",                Url = "https://huggingface.co/Supertone/supertonic-3/resolve/main/onnx/tts.json" },
                new TtsModelAsset { RelativePath = "onnx/unicode_indexer.json",    Url = "https://huggingface.co/Supertone/supertonic-3/resolve/main/onnx/unicode_indexer.json" }
            ]
        }),
        ApplyCanonicalMetadata(new()
        {
            Name = "kokoro-q4",
            DisplayName = "Kokoro Q4",
            Description = "Kokoro ONNX Q4 model with direct OnnxRuntime inference.",
            Engine = "kokoro",
            ModelPath = "onnx/model_q4.onnx",
            TokensPath = "/app/assets/tokens.txt",
            VoicesPath = "voices",
            VoicesBaseUrl = "https://huggingface.co/onnx-community/Kokoro-82M-v1.0-ONNX/resolve/main/voices",
            VoiceFileExtension = ".bin",
            Assets =
            [
                new TtsModelAsset
                {
                    RelativePath = "onnx/model_q4.onnx",
                    Url = "https://huggingface.co/onnx-community/Kokoro-82M-v1.0-ONNX/resolve/main/onnx/model_q4.onnx"
                }
            ]
        }),
        ApplyCanonicalMetadata(new()
        {
            Name = "kokoro-full",
            DisplayName = "Kokoro Full",
            Description = "Kokoro ONNX full model with direct OnnxRuntime inference.",
            Engine = "kokoro",
            ModelPath = "onnx/model.onnx",
            TokensPath = "/app/assets/tokens.txt",
            VoicesPath = "voices",
            VoicesBaseUrl = "https://huggingface.co/onnx-community/Kokoro-82M-v1.0-ONNX/resolve/main/voices",
            VoiceFileExtension = ".bin",
            Assets =
            [
                new TtsModelAsset
                {
                    RelativePath = "onnx/model.onnx",
                    Url = "https://huggingface.co/onnx-community/Kokoro-82M-v1.0-ONNX/resolve/main/onnx/model.onnx"
                }
            ]
        })
    ];

    private static TtsModelDefinition ApplyCanonicalMetadata(TtsModelDefinition model)
    {
        if (SupertonicMetadata.IsSupertonic3Engine(model.Engine))
        {
            return new TtsModelDefinition
            {
                Name               = model.Name,
                DisplayName        = model.DisplayName,
                Description        = model.Description,
                Engine             = model.Engine,
                ModelPath          = model.ModelPath,
                VoicesPath         = model.VoicesPath,
                VoicesBaseUrl      = model.VoicesBaseUrl,
                VoiceFileExtension = model.VoiceFileExtension,
                TokensPath         = model.TokensPath,
                Assets             = model.Assets,
                SupportedLanguages = SupertonicMetadata.SupportedLanguages,
                Speakers           = SupertonicMetadata.SupportedSpeakers
            };
        }

        if (!KokoroMetadata.IsKokoroEngine(model.Engine))
        {
            return model;
        }

        return new TtsModelDefinition
        {
            Name = model.Name,
            DisplayName = model.DisplayName,
            Description = model.Description,
            Engine = model.Engine,
            ModelPath = model.ModelPath,
            VoicesPath = model.VoicesPath,
            VoicesBaseUrl = model.VoicesBaseUrl,
            VoiceFileExtension = model.VoiceFileExtension,
            TokensPath = model.TokensPath,
            Assets = model.Assets,
            SupportedLanguages = KokoroMetadata.SupportedLanguages,
            Speakers = KokoroMetadata.SupportedSpeakers
        };
    }

    private sealed class ModelCatalogConfig
    {
        public IReadOnlyList<TtsModelDefinition> Models { get; init; } = [];
    }
}
