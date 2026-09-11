using System.Net;
using System.Net.Http.Json;
using SharpAudio.Api.Contracts;
using SharpAudio.Api.Models;
using SharpAudio.Api.Services;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;

namespace SharpAudio.Api.Tests;

public sealed class SpeechEndpointTests
{
    [Fact]
    public async Task Returns_bad_request_for_unsupported_model()
    {
        await using var app = new ApiFactory();
        using var client = app.CreateClient();

        var response = await client.PostAsJsonAsync("/v1/audio/speech", new OpenAiSpeechRequest
        {
            Model = "unknown",
            Input = "hello"
        });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Returns_audio_for_supported_model()
    {
        await using var app = new ApiFactory();
        using var client = app.CreateClient();

        var response = await client.PostAsJsonAsync("/v1/audio/speech", new OpenAiSpeechRequest
        {
            Model = "kokoro-q4",
            Input = "hello"
        });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("audio/wav", response.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task Returns_bad_request_for_unsupported_language()
    {
        await using var app = new ApiFactory();
        using var client = app.CreateClient();

        var response = await client.PostAsJsonAsync("/v1/audio/speech", new OpenAiSpeechRequest
        {
            Model = "kokoro-q4",
            Input = "hello",
            Language = "ko-kr"
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Returns_bad_request_for_unsupported_speaker()
    {
        await using var app = new ApiFactory();
        using var client = app.CreateClient();

        var response = await client.PostAsJsonAsync("/v1/audio/speech", new OpenAiSpeechRequest
        {
            Model = "kokoro-q4",
            Input = "hello",
            Speaker = "not_a_speaker"
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Returns_audio_for_japanese_input_and_language()
    {
        await using var app = new ApiFactory();
        using var client = app.CreateClient();

        var response = await client.PostAsJsonAsync("/v1/audio/speech", new OpenAiSpeechRequest
        {
            Model = "kokoro-q4",
            Input = "こんにちは、世界です。",
            Language = "ja-jp",
            Speaker = "jf_alpha"
        });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("audio/wav", response.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task Api_models_endpoint_exposes_full_kokoro_metadata()
    {
        await using var app = new ApiFactory();
        using var client = app.CreateClient();

        var response = await client.GetAsync("/api/models");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        await using var stream = await response.Content.ReadAsStreamAsync();
        using var document = await JsonDocument.ParseAsync(stream);
        var first = document.RootElement[0];
        var languages = first.GetProperty("supportedLanguages").EnumerateArray().Select(v => v.GetString()).ToArray();
        var speakers = first.GetProperty("speakers").EnumerateArray().Select(v => v.GetString()).ToArray();

        Assert.Contains("ja-jp", languages);
        Assert.Contains("zh-cn", languages);
        Assert.Contains("jf_alpha", speakers);
        Assert.Contains("zm_yunyang", speakers);
    }

    private sealed class ApiFactory : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<IHostedService>();
                services.RemoveAll<IModelCache>();
                services.RemoveAll<ITtsSynthesizer>();

                services.AddSingleton<IModelCache, StubModelCache>();
                services.AddSingleton<ITtsSynthesizer, StubTtsSynthesizer>();
            });
        }
    }

    private sealed class StubModelCache : IModelCache
    {
        public Task<string> EnsureModelAsync(TtsModelDefinition model, CancellationToken cancellationToken) => Task.FromResult("/tmp/models");
        public Task<string> EnsureModelAsync(AsrModelDefinition model, CancellationToken cancellationToken) => Task.FromResult("/tmp/models");
    }

    private sealed class StubTtsSynthesizer : ITtsSynthesizer
    {
        public Task<SynthesisResult> SynthesizeAsync(TtsModelDefinition model, string modelDirectory, OpenAiSpeechRequest request, CancellationToken cancellationToken)
            => Task.FromResult(new SynthesisResult([82, 73, 70, 70], "audio/wav", "test.wav", 0.01, 0.5, request.Input.Length));
    }
}
