using System.Net;
using System.Net.Http.Json;
using FastTTSR.Api.Contracts;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace FastTTSR.Api.IntegrationTests;

/// <summary>
/// Tests for Japanese text synthesis under concurrent load to verify espeak-ng thread safety.
/// The espeak_TextToPhonemes() function uses a static global buffer that gets reallocated,
/// causing "free(): invalid pointer" crashes without proper synchronization.
/// </summary>
public class JapaneseConcurrencyTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    public JapaneseConcurrencyTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task Concurrent_Japanese_requests_should_not_crash()
    {
        // Arrange
        var client = _factory.CreateClient();
        var japaneseTexts = new[]
        {
            "こんにちは、世界です。",
            "これは日本語のテストです。",
            "東京は日本の首都です。",
            "富士山は美しい山です。",
            "桜の花が咲いています。",
            "日本語の文字は難しいです。",
            "ひらがな、カタカナ、漢字があります。",
            "今日は良い天気ですね。",
            "お元気ですか？",
            "ありがとうございます。"
        };

        // Act - Send 10 concurrent Japanese TTS requests
        var tasks = japaneseTexts.Select(text => 
            client.PostAsJsonAsync("/v1/audio/speech", new OpenAiSpeechRequest
            {
                Model = "kokoro-q4",
                Input = text,
                Language = "ja-jp",
                Speaker = "jf_alpha",
                Speed = 1.0f
            })
        ).ToArray();

        var responses = await Task.WhenAll(tasks);

        // Assert - All requests should succeed
        foreach (var response in responses)
        {
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Equal("audio/wav", response.Content.Headers.ContentType?.MediaType);
            
            var audioData = await response.Content.ReadAsByteArrayAsync();
            Assert.True(audioData.Length > 0, "Audio data should not be empty");
        }
    }

    [Fact]
    public async Task Stress_test_20_concurrent_Japanese_requests()
    {
        // Arrange
        var client = _factory.CreateClient();
        var japaneseText = "こんにちは、これは並行処理のテストです。エスピークエンジーは安全に動作する必要があります。";
        var requestCount = 20;

        // Act - Send 20 identical concurrent requests
        var tasks = Enumerable.Range(0, requestCount).Select(_ =>
            client.PostAsJsonAsync("/v1/audio/speech", new OpenAiSpeechRequest
            {
                Model = "kokoro-q4",
                Input = japaneseText,
                Language = "ja-jp",
                Speaker = "jf_alpha",
                Speed = 1.0f
            })
        ).ToArray();

        var responses = await Task.WhenAll(tasks);

        // Assert - All requests should succeed
        var successCount = responses.Count(r => r.StatusCode == HttpStatusCode.OK);
        Assert.Equal(requestCount, successCount);
        
        foreach (var response in responses)
        {
            var audioData = await response.Content.ReadAsByteArrayAsync();
            Assert.True(audioData.Length > 0, "Audio data should not be empty");
        }
    }

    [Fact]
    public async Task Mixed_language_concurrent_requests_should_succeed()
    {
        // Arrange
        var client = _factory.CreateClient();
        var requests = new[]
        {
            new { Text = "Hello world", Language = "en-us", Speaker = "af_bella" },
            new { Text = "こんにちは、世界", Language = "ja-jp", Speaker = "jf_alpha" },
            new { Text = "你好世界", Language = "zh-cn", Speaker = "zf_xiaobei" },
            new { Text = "Bonjour le monde", Language = "fr-fr", Speaker = "ff_siwis" },
            new { Text = "Hola mundo", Language = "es", Speaker = "ef_dora" },
            new { Text = "こんにちは", Language = "ja-jp", Speaker = "jm_kumo" },
            new { Text = "さようなら", Language = "ja-jp", Speaker = "jf_gongitsune" },
            new { Text = "Hello again", Language = "en-us", Speaker = "am_adam" }
        };

        // Act - Send mixed language requests concurrently
        var tasks = requests.Select(req =>
            client.PostAsJsonAsync("/v1/audio/speech", new OpenAiSpeechRequest
            {
                Model = "kokoro-q4",
                Input = req.Text,
                Language = req.Language,
                Speaker = req.Speaker,
                Speed = 1.0f
            })
        ).ToArray();

        var responses = await Task.WhenAll(tasks);

        // Assert
        foreach (var response in responses)
        {
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Equal("audio/wav", response.Content.Headers.ContentType?.MediaType);
            
            var audioData = await response.Content.ReadAsByteArrayAsync();
            Assert.True(audioData.Length > 0, "Audio data should not be empty");
        }
    }

    [Fact]
    public async Task Sequential_Japanese_requests_should_succeed()
    {
        // Arrange
        var client = _factory.CreateClient();
        var japaneseTexts = new[]
        {
            "一つ目のテスト",
            "二つ目のテスト",
            "三つ目のテスト",
            "四つ目のテスト",
            "五つ目のテスト"
        };

        // Act - Send requests sequentially (control test)
        foreach (var text in japaneseTexts)
        {
            var response = await client.PostAsJsonAsync("/v1/audio/speech", new OpenAiSpeechRequest
            {
                Model = "kokoro-q4",
                Input = text,
                Language = "ja-jp",
                Speaker = "jf_alpha",
                Speed = 1.0f
            });

            // Assert
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            var audioData = await response.Content.ReadAsByteArrayAsync();
            Assert.True(audioData.Length > 0, "Audio data should not be empty");
        }
    }

    [Fact]
    public async Task Japanese_with_complex_characters_should_not_crash()
    {
        // Arrange
        var client = _factory.CreateClient();
        var complexTexts = new[]
        {
            "漢字、ひらがな、カタカナ混在テスト",
            "🗾日本の絵文字テスト🏯",
            "「引用符」と（括弧）のテスト",
            "数字：１２３４５６７８９０",
            "記号：！？、。・：；"
        };

        // Act
        var tasks = complexTexts.Select(text =>
            client.PostAsJsonAsync("/v1/audio/speech", new OpenAiSpeechRequest
            {
                Model = "kokoro-q4",
                Input = text,
                Language = "ja-jp",
                Speaker = "jf_alpha",
                Speed = 1.0f
            })
        ).ToArray();

        var responses = await Task.WhenAll(tasks);

        // Assert
        foreach (var response in responses)
        {
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            var audioData = await response.Content.ReadAsByteArrayAsync();
            Assert.True(audioData.Length > 0, "Audio data should not be empty");
        }
    }
}
