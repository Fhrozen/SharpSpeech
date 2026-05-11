using FastTTSR.Api.Contracts;
using FastTTSR.Api.Options;
using FastTTSR.Api.Services;

var builder = WebApplication.CreateBuilder(args);

builder.Services.Configure<ModelCacheOptions>(builder.Configuration.GetSection(ModelCacheOptions.SectionName));
builder.Services.AddHttpClient();
builder.Services.AddSingleton<IModelCatalog, ModelCatalog>();
builder.Services.AddSingleton<IModelCache, ModelCache>();
builder.Services.AddSingleton<ITtsSynthesizer, SherpaOnnxTtsSynthesizer>();
builder.Services.AddHostedService<ModelWarmupService>();

var app = builder.Build();

app.UseDefaultFiles();
app.UseStaticFiles();

app.MapGet("/health", () => Results.Ok(new { status = "ok" }));

app.MapGet("/api/models", (IModelCatalog modelCatalog) =>
{
    var models = modelCatalog.GetSupportedModels().Select(m => new ModelDefinitionResponse(
        m.Name,
        m.DisplayName,
        m.Description,
        m.SupportedLanguages,
        m.Speakers));

    return Results.Ok(models);
});

app.MapPost("/v1/audio/speech", async (
    OpenAiSpeechRequest request,
    IModelCatalog modelCatalog,
    IModelCache modelCache,
    ITtsSynthesizer synthesizer,
    CancellationToken cancellationToken) =>
{
    if (string.IsNullOrWhiteSpace(request.Model) || !modelCatalog.TryGetModel(request.Model, out var model))
    {
        return Results.BadRequest(new ErrorResponse("unsupported_model", "The selected model is not supported."));
    }

    if (string.IsNullOrWhiteSpace(request.Input))
    {
        return Results.BadRequest(new ErrorResponse("invalid_request", "Input text is required."));
    }

    var modelPath = await modelCache.EnsureModelAsync(model!, cancellationToken);
    var result = await synthesizer.SynthesizeAsync(model!, modelPath, request, cancellationToken);

    return Results.File(result.AudioBytes, result.ContentType, fileDownloadName: result.FileName);
});

app.MapFallbackToFile("/index.html");

app.Run();

public partial class Program;
