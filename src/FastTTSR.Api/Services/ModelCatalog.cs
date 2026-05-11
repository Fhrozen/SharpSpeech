using FastTTSR.Api.Models;

namespace FastTTSR.Api.Services;

public sealed class ModelCatalog : IModelCatalog
{
    private static readonly StringComparer Comparer = StringComparer.OrdinalIgnoreCase;

    private readonly IReadOnlyDictionary<string, TtsModelDefinition> _models =
        new[]
        {
            new TtsModelDefinition(
                Name: "kokoro-tts",
                DisplayName: "Kokoro TTS",
                Description: "Kokoro ONNX model loaded through Sherpa-ONNX runtime.",
                HuggingFaceRepository: "hexgrad/Kokoro-82M",
                Files:
                [
                    "kokoro-v1.0.onnx",
                    "voices-v1.0.bin"
                ],
                SupportedLanguages: ["en-us", "ja-jp"],
                Speakers: ["af_bella", "af_nicole", "am_adam"]),
            new TtsModelDefinition(
                Name: "supertonic-3",
                DisplayName: "Supertonic 3",
                Description: "Supertonic 3 ONNX model loaded through Sherpa-ONNX runtime.",
                HuggingFaceRepository: "Supertone/supertonic-3",
                Files:
                [
                    "model.onnx",
                    "tokens.txt",
                    "speakers.json"
                ],
                SupportedLanguages: ["en", "ko"],
                Speakers: ["alloy", "aria", "nova"]) 
        }
        .ToDictionary(m => m.Name, Comparer);

    public IReadOnlyCollection<TtsModelDefinition> GetSupportedModels() => _models.Values.ToArray();

    public bool TryGetModel(string modelName, out TtsModelDefinition? model) => _models.TryGetValue(modelName, out model);
}
