namespace SharpAudio.Api.Options;

public sealed class ModelCacheOptions
{
    public const string SectionName = "ModelCache";

    public string CacheDirectory { get; set; } = Environment.GetEnvironmentVariable("MODEL_CACHE_DIR")
                                                  ?? Path.Combine(Path.GetTempPath(), "sharp-audio-cache");
}
