using System.Net;
using System.Net.Http.Json;
using FastTTSR.Api.Contracts;
using FastTTSR.Api.Models;
using FastTTSR.Api.Services;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;

namespace FastTTSR.Api.Tests;

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

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
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
    }

    private sealed class StubTtsSynthesizer : ITtsSynthesizer
    {
        public Task<SynthesisResult> SynthesizeAsync(TtsModelDefinition model, string modelDirectory, OpenAiSpeechRequest request, CancellationToken cancellationToken)
            => Task.FromResult(new SynthesisResult([82, 73, 70, 70], "audio/wav", "test.wav"));
    }
}
