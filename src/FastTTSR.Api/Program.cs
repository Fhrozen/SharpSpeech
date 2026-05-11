using FastTTSR.Api.Contracts;
using FastTTSR.Api.Options;
using FastTTSR.Api.Services;

var builder = WebApplication.CreateBuilder(args);

builder.Services.Configure<ModelCacheOptions>(builder.Configuration.GetSection(ModelCacheOptions.SectionName));
builder.Services.AddHttpClient();
builder.Services.AddSingleton<IModelCatalog, ModelCatalog>();
builder.Services.AddSingleton<IModelCache, ModelCache>();
builder.Services.AddSingleton<ITtsSynthesizer, KokoroTtsSynthesizer>();
builder.Services.AddHostedService<ModelWarmupService>();

// Add Swagger/OpenAPI support
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
    options.SwaggerDoc("v1", new Microsoft.OpenApi.Models.OpenApiInfo
    {
        Title = "FastTTSR API",
        Version = "v1",
        Description = "Text-to-Speech REST API with OpenAI-compatible endpoints. Powered by Kokoro TTS models with custom OnnxRuntime inference and espeak-ng phonemization.",
        Contact = new Microsoft.OpenApi.Models.OpenApiContact
        {
            Name = "FastTTSR",
            Url = new Uri("https://github.com/yourusername/FastTTSR")
        }
    });

    // Include XML comments if available
    var xmlFilename = $"{System.Reflection.Assembly.GetExecutingAssembly().GetName().Name}.xml";
    var xmlPath = Path.Combine(AppContext.BaseDirectory, xmlFilename);
    if (File.Exists(xmlPath))
    {
        options.IncludeXmlComments(xmlPath);
    }
});

var app = builder.Build();

// Enable Swagger in all environments
app.UseSwagger();
app.UseSwaggerUI(options =>
{
    options.SwaggerEndpoint("/swagger/v1/swagger.json", "FastTTSR API v1");
    options.RoutePrefix = "swagger";
    options.DocumentTitle = "FastTTSR API Documentation";
});

app.UseDefaultFiles();
app.UseStaticFiles();

app.MapGet("/health", () => Results.Ok(new { status = "ok" }))
    .WithName("HealthCheck")
    .WithTags("Health")
    .WithSummary("Health check endpoint")
    .WithDescription("Returns the service health status")
    .Produces<object>(200);

app.MapGet("/api/models", (IModelCatalog modelCatalog) =>
{
    var models = modelCatalog.GetSupportedModels().Select(m => new ModelDefinitionResponse(
        m.Name,
        m.DisplayName,
        m.Description,
        m.SupportedLanguages,
        m.Speakers));

    return Results.Ok(models);
})
    .WithName("GetModels")
    .WithTags("Models")
    .WithSummary("List available TTS models")
    .WithDescription("Returns detailed information about all available text-to-speech models, including supported languages and speakers")
    .Produces<IEnumerable<ModelDefinitionResponse>>(200);

app.MapGet("/v1/models", (IModelCatalog modelCatalog) =>
{
    var models = modelCatalog.GetSupportedModels().Select(m => new
    {
        id = m.Name,
        @object = "model",
        created = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
        owned_by = "fastttsr"
    });

    return Results.Ok(new { data = models, @object = "list" });
})
    .WithName("ListModels")
    .WithTags("OpenAI Compatible")
    .WithSummary("List models (OpenAI compatible)")
    .WithDescription("OpenAI-compatible endpoint for listing available TTS models")
    .Produces<object>(200);

app.MapPost("/v1/audio/speech", async (
    OpenAiSpeechRequest request,
    IModelCatalog modelCatalog,
    IModelCache modelCache,
    ITtsSynthesizer synthesizer,
    HttpContext httpContext,
    CancellationToken cancellationToken) =>
{
    if (string.IsNullOrWhiteSpace(request.Model) || !modelCatalog.TryGetModel(request.Model, out var model))
    {
        return Results.NotFound(new ErrorResponse("model_not_found", "The requested model was not found."));
    }

    if (string.IsNullOrWhiteSpace(request.Input))
    {
        return Results.BadRequest(new ErrorResponse("invalid_request", "Input text is required."));
    }

    var modelPath = await modelCache.EnsureModelAsync(model!, cancellationToken);
    var result = await synthesizer.SynthesizeAsync(model!, modelPath, request, cancellationToken);

    // Add metrics to response headers
    httpContext.Response.Headers["X-Processing-Time"] = result.ProcessingTimeSeconds.ToString("F3");
    httpContext.Response.Headers["X-Chars-Per-Second"] = result.CharsPerSecond.ToString("F1");
    httpContext.Response.Headers["X-RTF"] = result.RTF.ToString("F3");
    httpContext.Response.Headers["X-Audio-Duration"] = result.AudioDurationSeconds.ToString("F2");
    httpContext.Response.Headers["X-Character-Count"] = result.CharacterCount.ToString();

    return Results.File(result.AudioBytes, result.ContentType, fileDownloadName: result.FileName);
})
    .WithName("CreateSpeech")
    .WithTags("OpenAI Compatible")
    .WithSummary("Generate speech from text")
    .WithDescription("Generates audio from the input text using the specified TTS model. OpenAI-compatible endpoint. Supports kokoro-q4 and kokoro-full models with voices: af_bella, af_nicole, am_adam. Languages: en-us, ja-jp, and more via espeak-ng.")
    .Accepts<OpenAiSpeechRequest>("application/json")
    .Produces<byte[]>(200, "audio/wav")
    .Produces<ErrorResponse>(400)
    .Produces<ErrorResponse>(404);

app.MapFallbackToFile("/index.html");

app.Run();

public partial class Program;
