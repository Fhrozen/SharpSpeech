using System.ComponentModel.DataAnnotations;
using System.ComponentModel;

namespace FastTTSR.Api.Contracts;

/// <summary>
/// Request to generate speech from text
/// </summary>
public sealed class OpenAiSpeechRequest
{
    /// <summary>
    /// The TTS model to use. Available: kokoro-q4, kokoro-full
    /// </summary>
    [Required]
    [DefaultValue("kokoro-q4")]
    public string Model { get; init; } = string.Empty;

    /// <summary>
    /// The text to generate audio for. Maximum length depends on the model.
    /// </summary>
    [Required]
    [DefaultValue("Hello, this is a test of the text to speech system.")]
    public string Input { get; init; } = string.Empty;

    /// <summary>
    /// The voice to use for speech synthesis (e.g., af_bella, af_nicole, am_adam)
    /// </summary>
    [DefaultValue("af_bella")]
    public string Voice { get; init; } = "default";

    /// <summary>
    /// The audio format. Currently only 'wav' is supported.
    /// </summary>
    [DefaultValue("wav")]
    public string ResponseFormat { get; init; } = "wav";

    /// <summary>
    /// The speed of the generated audio. Range: 0.5 to 2.0
    /// </summary>
    [Range(0.5, 2.0)]
    [DefaultValue(1.0)]
    public float Speed { get; init; } = 1.0f;

    /// <summary>
    /// The language code for synthesis (e.g., en-us, ja-jp). Defaults to en-us if not specified.
    /// </summary>
    [DefaultValue("en-us")]
    public string? Language { get; init; }

    /// <summary>
    /// Alternative to 'Voice' parameter. Specifies the speaker for the model.
    /// </summary>
    public string? Speaker { get; init; }
}
