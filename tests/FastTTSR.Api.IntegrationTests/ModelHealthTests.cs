using System.Net;
using System.Net.Http.Json;
using FastTTSR.Api.Contracts;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace FastTTSR.Api.IntegrationTests;

/// <summary>
/// Integration tests that verify model configuration and health
/// These tests run against the API with actual model files
/// </summary>
public class ModelHealthTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly HttpClient _client;
    private readonly string _modelCacheDir;

    public ModelHealthTests(WebApplicationFactory<Program> factory)
    {
        _client = factory.CreateClient();
        _modelCacheDir = Environment.GetEnvironmentVariable("MODEL_CACHE_DIR") ?? "/cache";
    }

    [Fact]
    public async Task GetModels_ShouldReturnAvailableModels()
    {
        // Act
        var response = await _client.GetAsync("/v1/models");

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var models = await response.Content.ReadFromJsonAsync<ModelsResponse>();
        Assert.NotNull(models);
        Assert.NotEmpty(models.Data);
    }

    [Theory]
    [InlineData("kokoro-q4")]
    [InlineData("kokoro-full")]
    [InlineData("supertonic-3")]
    public async Task ModelConfiguration_ShouldHaveRequiredFiles(string modelName)
    {
        // Arrange
        var modelDir = Path.Combine(_modelCacheDir, modelName);

        // Act - Get model details from API
        var response = await _client.GetAsync("/v1/models");
        var models = await response.Content.ReadFromJsonAsync<ModelsResponse>();
        var model = models?.Data.FirstOrDefault(m => m.Id == modelName);

        // Assert model exists
        Assert.NotNull(model);
        
        // Assert directory exists
        Assert.True(Directory.Exists(modelDir), $"Model directory {modelDir} does not exist");

        // Check for model file
        var modelPath = Path.Combine(modelDir, GetModelPath(modelName));
        Assert.True(File.Exists(modelPath), $"Model file {modelPath} does not exist");

        // Check for tokens.txt (all models require this)
        var tokensPath = Path.Combine(modelDir, "tokens.txt");
        Assert.True(File.Exists(tokensPath), $"Tokens file {tokensPath} does not exist");

        // For Kokoro models, also check voices directory
        if (modelName.StartsWith("kokoro"))
        {
            var voicesDir = Path.Combine(modelDir, "voices");
            Assert.True(Directory.Exists(voicesDir), $"Voices directory {voicesDir} does not exist");
            
            // Check for at least one voice file
            var voiceFiles = Directory.GetFiles(voicesDir, "*.bin");
            Assert.NotEmpty(voiceFiles);
        }
    }

    [Theory]
    [InlineData("kokoro-q4", "tokens.txt")]
    [InlineData("kokoro-full", "tokens.txt")]
    [InlineData("supertonic-3", "tokens.txt")]
    public async Task TokensFile_ShouldExistAndBeReadable(string modelName, string tokensFileName)
    {
        // Arrange
        var tokensPath = Path.Combine(_modelCacheDir, modelName, tokensFileName);

        // Act
        var exists = File.Exists(tokensPath);
        var content = exists ? await File.ReadAllTextAsync(tokensPath) : null;

        // Assert
        Assert.True(exists, $"Tokens file {tokensPath} does not exist");
        Assert.NotNull(content);
        Assert.NotEmpty(content);
    }

    private static string GetModelPath(string modelName) => modelName switch
    {
        "kokoro-q4" => "onnx/model_q4.onnx",
        "kokoro-full" => "onnx/model.onnx",
        "supertonic-3" => "model.onnx",
        _ => throw new ArgumentException($"Unknown model: {modelName}")
    };

    private sealed class ModelsResponse
    {
        public List<ModelInfo> Data { get; init; } = [];
    }

    private sealed class ModelInfo
    {
        public string Id { get; init; } = "";
        public string Object { get; init; } = "";
    }
}
