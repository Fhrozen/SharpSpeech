using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace SharpAudio.Api.IntegrationTests;

/// <summary>
/// Integration tests for speech synthesis endpoint
/// Tests actual TTS generation with Docker-deployed models
/// </summary>
public class SpeechSynthesisTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly HttpClient _client;

    public SpeechSynthesisTests(WebApplicationFactory<Program> factory)
    {
        _client = factory.CreateClient();
    }

    [Theory]
    [InlineData("kokoro-q4", "af_bella")]
    [InlineData("kokoro-full", "af_nicole")]
    public async Task SynthesizeSpeech_WithKokoroModel_ShouldReturnAudioData(string model, string voice)
    {
        // Arrange
        var request = new
        {
            model,
            input = "Hello, this is a test of the speech synthesis system.",
            voice,
            speed = 1.0
        };

        // Act
        var response = await _client.PostAsJsonAsync("/v1/audio/speech", request);

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        
        var contentType = response.Content.Headers.ContentType?.MediaType;
        Assert.NotNull(contentType);
        Assert.Contains("audio", contentType);

        var audioData = await response.Content.ReadAsByteArrayAsync();
        Assert.NotEmpty(audioData);
        
        // WAV files start with "RIFF" header
        var header = System.Text.Encoding.ASCII.GetString(audioData.Take(4).ToArray());
        Assert.Equal("RIFF", header);
    }

    [Fact]
    public async Task SynthesizeSpeech_WithInvalidModel_ShouldReturnNotFound()
    {
        // Arrange
        var request = new
        {
            model = "non-existent-model",
            input = "Test text",
            voice = "default"
        };

        // Act
        var response = await _client.PostAsJsonAsync("/v1/audio/speech", request);

        // Assert
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task SynthesizeSpeech_WithEmptyInput_ShouldReturnBadRequest()
    {
        // Arrange
        var request = new
        {
            model = "kokoro-q4",
            input = "",
            voice = "af_bella"
        };

        // Act
        var response = await _client.PostAsJsonAsync("/v1/audio/speech", request);

        // Assert
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Theory]
    [InlineData(0.5)]
    [InlineData(1.0)]
    [InlineData(1.5)]
    [InlineData(2.0)]
    public async Task SynthesizeSpeech_WithDifferentSpeeds_ShouldSucceed(double speed)
    {
        // Arrange
        var request = new
        {
            model = "kokoro-q4",
            input = "Testing different speeds.",
            voice = "af_bella",
            speed
        };

        // Act
        var response = await _client.PostAsJsonAsync("/v1/audio/speech", request);

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        
        var audioData = await response.Content.ReadAsByteArrayAsync();
        Assert.NotEmpty(audioData);
    }

    [Fact]
    public async Task SynthesizeSpeech_WithLongText_ShouldHandleMultipleSentences()
    {
        // Arrange
        var longText = "This is the first sentence. " +
                      "This is the second sentence. " +
                      "This is the third sentence. " +
                      "This is the fourth sentence.";
        
        var request = new
        {
            model = "kokoro-q4",
            input = longText,
            voice = "af_bella",
            speed = 1.0
        };

        // Act
        var response = await _client.PostAsJsonAsync("/v1/audio/speech", request);

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        
        var audioData = await response.Content.ReadAsByteArrayAsync();
        Assert.NotEmpty(audioData);
        
        // Longer text should produce larger audio file (very rough estimate)
        Assert.True(audioData.Length > 10000, "Audio data seems too small for the input text");
    }

    [Theory]
    [InlineData("kokoro-q4", "af_bella")]
    [InlineData("kokoro-q4", "af_nicole")]
    [InlineData("kokoro-q4", "am_adam")]
    public async Task SynthesizeSpeech_WithDifferentVoices_ShouldSucceed(string model, string voice)
    {
        // Arrange
        var request = new
        {
            model,
            input = "Testing different voice options.",
            voice,
            speed = 1.0
        };

        // Act
        var response = await _client.PostAsJsonAsync("/v1/audio/speech", request);

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        
        var audioData = await response.Content.ReadAsByteArrayAsync();
        Assert.NotEmpty(audioData);
    }

    [Fact]
    public async Task HealthCheck_ShouldReturnHealthy()
    {
        // Act
        var response = await _client.GetAsync("/health");

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }
}
