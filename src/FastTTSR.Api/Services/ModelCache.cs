using FastTTSR.Api.Models;
using FastTTSR.Api.Options;
using Microsoft.Extensions.Options;

namespace FastTTSR.Api.Services;

public sealed class ModelCache : IModelCache
{
    private const string KokoroEngine = "kokoro";

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

        foreach (var asset in model.Assets)
        {
            var outputPath = Path.Combine(targetDirectory, asset.RelativePath);
            Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);

            if (File.Exists(outputPath))
            {
                continue;
            }

            try
            {
                var tempPath = $"{outputPath}.tmp";
                await using var stream = await client.GetStreamAsync(asset.Url, cancellationToken);
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

                _logger.LogWarning(ex, "Failed to download model file {File} for {Model}", asset.RelativePath, model.Name);
            }
        }

        if (string.Equals(model.Engine, KokoroEngine, StringComparison.OrdinalIgnoreCase) &&
            !string.IsNullOrWhiteSpace(model.VoicesPath) &&
            !string.IsNullOrWhiteSpace(model.VoicesBaseUrl))
        {
            await DownloadVoiceFiles(client, model, targetDirectory, cancellationToken);
        }

        if (SupertonicMetadata.IsSupertonic3Engine(model.Engine) &&
            !string.IsNullOrWhiteSpace(model.VoicesPath) &&
            !string.IsNullOrWhiteSpace(model.VoicesBaseUrl))
        {
            await DownloadVoiceFiles(client, model, targetDirectory, cancellationToken);
        }

        return targetDirectory;
    }

    private async Task DownloadVoiceFiles(HttpClient client, TtsModelDefinition model,
        string targetDirectory, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(model.VoicesPath) || string.IsNullOrWhiteSpace(model.VoicesBaseUrl))
        {
            return;
        }

        var voicesDirectory = Path.Combine(targetDirectory, model.VoicesPath);
        Directory.CreateDirectory(voicesDirectory);
        var extension = string.IsNullOrWhiteSpace(model.VoiceFileExtension) ? ".bin" : model.VoiceFileExtension;

        foreach (var speaker in model.Speakers)
        {
            var voiceFileName = $"{speaker}{extension}";
            var outputPath    = Path.Combine(voicesDirectory, voiceFileName);
            if (File.Exists(outputPath))
            {
                continue;
            }

            var voiceUrl = $"{model.VoicesBaseUrl.TrimEnd('/')}/{voiceFileName}";
            var tempPath = $"{outputPath}.tmp";

            try
            {
                await using var stream = await client.GetStreamAsync(voiceUrl, cancellationToken);
                await using (var output = File.Create(tempPath))
                {
                    await stream.CopyToAsync(output, cancellationToken);
                }

                File.Move(tempPath, outputPath, overwrite: true);
            }
            catch (Exception ex)
            {
                if (File.Exists(tempPath))
                {
                    File.Delete(tempPath);
                }

                _logger.LogWarning(ex, "Failed to download voice file {Voice} for {Model}", voiceFileName, model.Name);
            }
        }
    }
}
