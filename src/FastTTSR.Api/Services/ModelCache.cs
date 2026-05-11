using FastTTSR.Api.Models;
using FastTTSR.Api.Options;
using Microsoft.Extensions.Options;

namespace FastTTSR.Api.Services;

public sealed class ModelCache : IModelCache
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly string _cacheDirectory;
    private readonly ILogger<ModelCache> _logger;

    public ModelCache(IHttpClientFactory httpClientFactory, IOptions<ModelCacheOptions> options, ILogger<ModelCache> logger)
    {
        _httpClientFactory = httpClientFactory;
        _cacheDirectory = options.Value.CacheDirectory;
        _logger = logger;
    }

    public async Task<string> EnsureModelAsync(TtsModelDefinition model, CancellationToken cancellationToken)
    {
        var targetDirectory = Path.Combine(_cacheDirectory, model.Name);
        Directory.CreateDirectory(targetDirectory);

        var client = _httpClientFactory.CreateClient();

        foreach (var file in model.Files)
        {
            var outputPath = Path.Combine(targetDirectory, file);
            if (File.Exists(outputPath))
            {
                continue;
            }

            var uri = $"https://huggingface.co/{model.HuggingFaceRepository}/resolve/main/{file}";

            try
            {
                var tempPath = $"{outputPath}.tmp";
                await using var stream = await client.GetStreamAsync(uri, cancellationToken);
                await using (var output = File.Create(tempPath))
                {
                    await stream.CopyToAsync(output, cancellationToken);
                }

                File.Move(tempPath, outputPath, overwrite: true);
            }
            catch (Exception ex)
            {
                var tempPath = $"{outputPath}.tmp";
                if (File.Exists(tempPath))
                {
                    File.Delete(tempPath);
                }

                _logger.LogWarning(ex, "Failed to download model file {File} for {Model}", file, model.Name);
            }
        }

        return targetDirectory;
    }
}
