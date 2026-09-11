namespace FastTTSR.Api.IntegrationTests.Support;

/// <summary>Opt-in gate for the real-model ASR/TTS circular tests (AsrCircularTests).</summary>
public static class AsrModelTestGate
{
    public static bool IsEnabled
    {
        get
        {
            var value = Environment.GetEnvironmentVariable("ASR_MODEL_TESTS");
            return string.Equals(value, "1", StringComparison.Ordinal) ||
                   string.Equals(value, "true", StringComparison.OrdinalIgnoreCase);
        }
    }

    public const string SkipReason =
        "Skipped by default: downloads real TTS/ASR models and runs real inference. " +
        "Set ASR_MODEL_TESTS=1 (or run ./tests/run-tests.sh asr-model-tests) to opt in.";
}
