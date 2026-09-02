using FastTTSR.Api.Contracts;
using FastTTSR.Api.Options;
using FastTTSR.Api.Services;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;

var builder = WebApplication.CreateBuilder(args);

// Configure URLs from environment variables
var httpPort = Environment.GetEnvironmentVariable("HTTP_PORT") ?? "8080";
var httpsPort = Environment.GetEnvironmentVariable("HTTPS_PORT");

var urls = new List<string> { $"http://+:{httpPort}" };
if (!string.IsNullOrWhiteSpace(httpsPort))
{
    urls.Add($"https://+:{httpsPort}");
    // Kestrel loads the actual certificate from Kestrel:Certificates:Default config (env vars
    // Kestrel__Certificates__Default__Path/Password) - no certificate-loading code needed here.
    builder.Services.AddHttpsRedirection(options => options.HttpsPort = int.Parse(httpsPort));
}

builder.WebHost.UseUrls(urls.ToArray());

// SERVER_MODE: tts (default, preserves pre-ASR behavior) | asr | both.
var serverMode = (Environment.GetEnvironmentVariable("SERVER_MODE") ?? "tts").Trim().ToLowerInvariant();
var ttsEnabled = serverMode is "tts" or "both";
var asrEnabled = serverMode is "asr" or "both";
Console.WriteLine($"[FastTTSR] SERVER_MODE={serverMode} (tts={ttsEnabled}, asr={asrEnabled})");

builder.Services.Configure<ModelCacheOptions>(builder.Configuration.GetSection(ModelCacheOptions.SectionName));
builder.Services.Configure<ModelIdleMonitorOptions>(builder.Configuration.GetSection(ModelIdleMonitorOptions.SectionName));
builder.Services.Configure<WorkerOptions>(builder.Configuration.GetSection(WorkerOptions.SectionName));
builder.Services.Configure<AsrWorkerOptions>(builder.Configuration.GetSection(AsrWorkerOptions.SectionName));
builder.Services.Configure<AsrStreamingOptions>(builder.Configuration.GetSection(AsrStreamingOptions.SectionName));
builder.Services.AddHttpClient();
builder.Services.AddSingleton<IModelCache, ModelCache>();

if (ttsEnabled)
{
    builder.Services.AddSingleton<IModelCatalog, ModelCatalog>();

    // Configure synthesizer based on worker mode
    var workerOptions = builder.Configuration.GetSection(WorkerOptions.SectionName).Get<WorkerOptions>() ?? new WorkerOptions();

    if (workerOptions.Enabled)
    {
        // Worker process mode - use proxy synthesizer
        Console.WriteLine("[FastTTSR] TTS worker mode ENABLED - models will run in separate processes");
        builder.Services.AddSingleton(sp => new WorkerProcessManager(workerOptions, sp.GetRequiredService<ILogger<WorkerProcessManager>>()));
        builder.Services.AddHostedService(sp => sp.GetRequiredService<WorkerProcessManager>());
        builder.Services.AddSingleton<ITtsSynthesizer, WorkerProxySynthesizer>();
    }
    else
    {
        // In-process mode - use direct synthesizers
        Console.WriteLine("[FastTTSR] TTS worker mode DISABLED - models will run in-process");
        builder.Services.AddSingleton<KokoroTtsSynthesizer>();
        builder.Services.AddSingleton<SupertonicTtsSynthesizer>();
        builder.Services.AddSingleton<ITtsSynthesizer, TtsSynthesizerRouter>();
        builder.Services.AddHostedService<ModelIdleMonitorService>();
    }

    builder.Services.AddHostedService<ModelWarmupService>();
}

if (asrEnabled)
{
    builder.Services.AddSingleton<IAsrModelCatalog, AsrModelCatalog>();

    var asrWorkerOptions = builder.Configuration.GetSection(AsrWorkerOptions.SectionName).Get<AsrWorkerOptions>() ?? new AsrWorkerOptions();

    if (asrWorkerOptions.Enabled)
    {
        // Worker process mode - use proxy transcriber, on its own keyed WorkerProcessManager
        // instance/port range so it can run alongside the TTS worker without colliding.
        Console.WriteLine("[FastTTSR] ASR worker mode ENABLED - models will run in separate processes");
        var asrProcessOptions = new WorkerOptions
        {
            ExecutablePath = asrWorkerOptions.ExecutablePath,
            IdleTimeoutSeconds = asrWorkerOptions.IdleTimeoutSeconds,
            PortRangeStart = asrWorkerOptions.PortRangeStart,
            MaxPortAttempts = asrWorkerOptions.MaxPortAttempts,
            StartupTimeoutSeconds = asrWorkerOptions.StartupTimeoutSeconds
        };

        builder.Services.AddKeyedSingleton<WorkerProcessManager>("asr",
            (sp, _) => new WorkerProcessManager(asrProcessOptions, sp.GetRequiredService<ILogger<WorkerProcessManager>>()));
        builder.Services.AddHostedService(sp => sp.GetRequiredKeyedService<WorkerProcessManager>("asr"));
        builder.Services.AddSingleton<IAsrTranscriber, AsrWorkerProxyTranscriber>();
    }
    else
    {
        // In-process mode - use direct transcribers
        Console.WriteLine("[FastTTSR] ASR worker mode DISABLED - models will run in-process");
        builder.Services.AddSingleton<WhisperAsrTranscriber>();
        builder.Services.AddSingleton<NemotronAsrTranscriber>();
        builder.Services.AddSingleton<IAsrTranscriber, AsrTranscriberRouter>();
        builder.Services.AddHostedService<AsrModelIdleMonitorService>();
    }

    builder.Services.AddHostedService<AsrModelWarmupService>();
}

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

if (!string.IsNullOrWhiteSpace(httpsPort))
{
    app.UseHttpsRedirection();
}

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
app.UseWebSockets();

app.MapGet("/health", () => Results.Ok(new { status = "ok" }))
    .WithName("HealthCheck")
    .WithTags("Health")
    .WithSummary("Health check endpoint")
    .WithDescription("Returns the service health status")
    .Produces<object>(200);

app.MapGet("/api/server-info", () => Results.Ok(new ServerInfoResponse(ttsEnabled, asrEnabled)))
    .WithName("GetServerInfo")
    .WithTags("Health")
    .WithSummary("Get server capability info")
    .WithDescription("Returns which task types (TTS speech synthesis, ASR transcription) this server instance was started with, controlled by the SERVER_MODE environment variable")
    .Produces<ServerInfoResponse>(200);

if (ttsEnabled)
{
app.MapGet("/api/models", (IModelCatalog modelCatalog) =>
{
    var models = modelCatalog.GetSupportedModels().Select(m =>
    {
        // Add speaker metadata for Supertonic models
        IReadOnlyList<SpeakerMetadata>? speakerMetadata = null;
        if (SupertonicMetadata.IsSupertonic3Engine(m.Engine))
        {
            speakerMetadata = m.Speakers
                .Select(speakerId => new SpeakerMetadata(
                    speakerId,
                    SupertonicMetadata.GetSpeakerName(speakerId),
                    SupertonicMetadata.GetSpeakerDescription(speakerId)))
                .ToArray();
        }

        return new ModelDefinitionResponse(
            m.Name,
            m.DisplayName,
            m.Description,
            m.SupportedLanguages,
            m.Speakers,
            speakerMetadata);
    });

    return Results.Ok(models);
})
    .WithName("GetModels")
    .WithTags("Models")
    .WithSummary("List available TTS models")
    .WithDescription("Returns detailed information about all available text-to-speech models, including supported languages and speakers")
    .Produces<IEnumerable<ModelDefinitionResponse>>(200);
}

app.MapGet("/v1/models", (HttpContext httpContext) =>
{
    var models = new List<object>();

    var ttsCatalog = httpContext.RequestServices.GetService<IModelCatalog>();
    if (ttsCatalog is not null)
    {
        models.AddRange(ttsCatalog.GetSupportedModels().Select(m => (object)new
        {
            id = m.Name,
            @object = "model",
            created = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
            owned_by = "fastttsr"
        }));
    }

    var asrCatalog = httpContext.RequestServices.GetService<IAsrModelCatalog>();
    if (asrCatalog is not null)
    {
        models.AddRange(asrCatalog.GetSupportedModels().Select(m => (object)new
        {
            id = m.Name,
            @object = "model",
            created = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
            owned_by = "fastttsr"
        }));
    }

    return Results.Ok(new { data = models, @object = "list" });
})
    .WithName("ListModels")
    .WithTags("OpenAI Compatible")
    .WithSummary("List models (OpenAI compatible)")
    .WithDescription("OpenAI-compatible endpoint for listing available TTS and/or ASR models, depending on SERVER_MODE")
    .Produces<object>(200);

if (ttsEnabled)
{
app.MapPost("/v1/audio/speech", async (
    OpenAiSpeechRequest request,
    IModelCatalog modelCatalog,
    IModelCache modelCache,
    ITtsSynthesizer synthesizer,
    HttpContext httpContext,
    ILogger<Program> logger,
    CancellationToken cancellationToken) =>
{
    // logger.LogInformation("[API] Received /v1/audio/speech request: model={Model}, speed={Speed}, voice={Voice}, language={Language}, input_length={Length}",
    //     request.Model, request.Speed, request.Voice, request.Language ?? "null", request.Input?.Length ?? 0);
    
    if (string.IsNullOrWhiteSpace(request.Model) || !modelCatalog.TryGetModel(request.Model, out var model))
    {
        return Results.NotFound(new ErrorResponse("model_not_found", "The requested model was not found."));
    }

    if (string.IsNullOrWhiteSpace(request.Input))
    {
        return Results.BadRequest(new ErrorResponse("invalid_request", "Input text is required."));
    }

    if (KokoroMetadata.IsKokoroEngine(model!.Engine))
    {
        if (!KokoroMetadata.TryNormalizeLanguage(request.Language, out var normalizedLanguage) ||
            !model.SupportedLanguages.Contains(normalizedLanguage, StringComparer.OrdinalIgnoreCase))
        {
            return Results.BadRequest(new ErrorResponse(
                "invalid_request",
                $"Unsupported language '{request.Language}'. Supported languages: {string.Join(", ", model.SupportedLanguages)}"));
        }

        var requestedSpeaker = request.Speaker;
        if (KokoroMetadata.IsDefaultVoiceValue(requestedSpeaker))
        {
            requestedSpeaker = request.Voice;
        }

        string? normalizedSpeaker = null;
        if (!KokoroMetadata.IsDefaultVoiceValue(requestedSpeaker))
        {
            if (!KokoroMetadata.TryNormalizeSpeaker(requestedSpeaker, out var speakerCandidate) ||
                !model.Speakers.Contains(speakerCandidate, StringComparer.OrdinalIgnoreCase))
            {
                return Results.BadRequest(new ErrorResponse(
                    "invalid_request",
                    $"Unsupported speaker '{requestedSpeaker}'. Supported speakers: {string.Join(", ", model.Speakers)}"));
            }

            normalizedSpeaker = speakerCandidate;
        }

        request = new OpenAiSpeechRequest
        {
            Model = request.Model,
            Input = request.Input,
            Voice = normalizedSpeaker ?? request.Voice,
            ResponseFormat = request.ResponseFormat,
            Speed = request.Speed,
            Language = normalizedLanguage,
            Speaker = normalizedSpeaker
        };
    }
    else if (SupertonicMetadata.IsSupertonic3Engine(model!.Engine))
    {
        // Validate language
        if (!SupertonicMetadata.TryNormalizeLanguage(request.Language, out var normalizedLanguage) ||
            !model.SupportedLanguages.Contains(normalizedLanguage, StringComparer.OrdinalIgnoreCase))
        {
            return Results.BadRequest(new ErrorResponse(
                "invalid_request",
                $"Unsupported language '{request.Language}'. Supported languages: {string.Join(", ", model.SupportedLanguages)}"));
        }

        // Validate speaker
        var requestedSpeaker = request.Speaker;
        if (SupertonicMetadata.IsDefaultVoiceValue(requestedSpeaker))
        {
            requestedSpeaker = request.Voice;
        }

        string? normalizedSpeaker = null;
        if (!SupertonicMetadata.IsDefaultVoiceValue(requestedSpeaker))
        {
            if (!SupertonicMetadata.TryNormalizeSpeaker(requestedSpeaker, out var speakerCandidate) ||
                !model.Speakers.Contains(speakerCandidate, StringComparer.OrdinalIgnoreCase))
            {
                return Results.BadRequest(new ErrorResponse(
                    "invalid_request",
                    $"Unsupported speaker '{requestedSpeaker}'. Supported speakers: {string.Join(", ", model.Speakers)}"));
            }

            normalizedSpeaker = speakerCandidate;
        }

        request = new OpenAiSpeechRequest
        {
            Model          = request.Model,
            Input          = request.Input,
            Voice          = normalizedSpeaker ?? request.Voice,
            ResponseFormat = request.ResponseFormat,
            Speed          = request.Speed,
            Language       = normalizedLanguage,
            Speaker        = normalizedSpeaker
        };
    }

    // Sanitize input text to prevent TTS engine crashes from special characters
    var sanitizedInput = TextSanitizer.Sanitize(request.Input, request.Language);
    
    if (string.IsNullOrWhiteSpace(sanitizedInput))
    {
        return Results.BadRequest(new ErrorResponse("invalid_request", "Input text contains only unsupported characters."));
    }
    
    // Create sanitized request
    var sanitizedRequest = new OpenAiSpeechRequest
    {
        Model = request.Model,
        Input = sanitizedInput,
        Voice = request.Voice,
        ResponseFormat = request.ResponseFormat,
        Speed = request.Speed,
        Language = request.Language,
        Speaker = request.Speaker
    };

    // logger.LogInformation("[API] Speech synthesis request: model={Model}, speed={Speed}, voice={Voice}, language={Language}, input_length={Length}",
    //     request.Model, request.Speed, request.Voice, request.Language, request.Input.Length);

    var modelPath = await modelCache.EnsureModelAsync(model!, cancellationToken);
    var result = await synthesizer.SynthesizeAsync(model!, modelPath, sanitizedRequest, cancellationToken);

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
    .WithDescription("Generates audio from the input text using the specified TTS model. OpenAI-compatible endpoint. Supports kokoro-q4 and kokoro-full with full Kokoro speaker catalog and languages: en-us, en-gb, es, fr-fr, hi, it, ja-jp, pt-br, zh-cn (plus accepted aliases). Also supports supertonic-3 with 31 languages and 10 preset voice styles (M1–M5, F1–F5).")
    .Accepts<OpenAiSpeechRequest>("application/json")
    .Produces<byte[]>(200, "audio/wav")
    .Produces<ErrorResponse>(400)
    .Produces<ErrorResponse>(404);
}

if (asrEnabled)
{
app.MapGet("/api/asr-models", (IAsrModelCatalog asrModelCatalog) =>
{
    var models = asrModelCatalog.GetSupportedModels().Select(m => new AsrModelDefinitionResponse(
        m.Name,
        m.DisplayName,
        m.Description,
        m.SupportedLanguages,
        m.SupportsLanguageAutoDetect,
        m.SupportsVad,
        m.SupportsStreaming));

    return Results.Ok(models);
})
    .WithName("GetAsrModels")
    .WithTags("Models")
    .WithSummary("List available ASR models")
    .WithDescription("Returns detailed information about all available speech-to-text models, including supported languages")
    .Produces<IEnumerable<AsrModelDefinitionResponse>>(200);

app.MapPost("/v1/audio/transcriptions", async (
    HttpRequest httpRequest,
    IAsrModelCatalog asrModelCatalog,
    IModelCache modelCache,
    IAsrTranscriber transcriber,
    HttpContext httpContext,
    CancellationToken cancellationToken) =>
{
    if (!httpRequest.HasFormContentType)
    {
        return Results.BadRequest(new ErrorResponse("invalid_request", "Expected multipart/form-data with an audio file."));
    }

    var form = await httpRequest.ReadFormAsync(cancellationToken);
    var file = form.Files.GetFile("file");
    var modelName = form["model"].ToString();
    var language = form["language"].ToString();
    var responseFormat = form["response_format"].ToString();
    var useVadRaw = form["use_vad"].ToString();
    var enableVad = string.Equals(useVadRaw, "true", StringComparison.OrdinalIgnoreCase) || useVadRaw == "1";
    var includeSegments = string.Equals(responseFormat, "verbose_json", StringComparison.OrdinalIgnoreCase);
    var minSegmentDurationRaw = form["min_segment_duration"].ToString();
    var minSegmentDuration = double.TryParse(minSegmentDurationRaw, out var parsedMinDuration) ? parsedMinDuration : 0.5;

    if (file is null || file.Length == 0)
    {
        return Results.BadRequest(new ErrorResponse("invalid_request", "An audio file is required."));
    }

    if (string.IsNullOrWhiteSpace(modelName) || !asrModelCatalog.TryGetModel(modelName, out var model))
    {
        return Results.NotFound(new ErrorResponse("model_not_found", "The requested model was not found."));
    }

    byte[] audioBytes;
    using (var audioStream = new MemoryStream())
    {
        await file.CopyToAsync(audioStream, cancellationToken);
        audioBytes = audioStream.ToArray();
    }

    // Normalize every upload (WAV, FLAC, MP3, OGG, WEBM, M4A, ...) to PCM16 mono WAV up front, so
    // neither engine needs to understand more than one input format.
    try
    {
        audioBytes = await AudioFormatConverter.ToPcm16WavAsync(audioBytes, cancellationToken);
    }
    catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception)
    {
        return Results.BadRequest(new ErrorResponse("invalid_request", $"Could not decode the uploaded audio file: {ex.Message}"));
    }

    var transcriptionRequest = new AudioTranscriptionRequest
    {
        Model = modelName,
        Language = string.IsNullOrWhiteSpace(language) ? null : language,
        ResponseFormat = string.IsNullOrWhiteSpace(responseFormat) ? "json" : responseFormat,
        EnableVad = enableVad,
        IncludeSegments = includeSegments,
        MinSegmentDurationSeconds = minSegmentDuration
    };

    var modelDirectory = await modelCache.EnsureModelAsync(model!, cancellationToken);
    var result = await transcriber.TranscribeAsync(model!, modelDirectory, transcriptionRequest, audioBytes, cancellationToken);

    httpContext.Response.Headers["X-Processing-Time"] = result.ProcessingTimeSeconds.ToString("F3");
    httpContext.Response.Headers["X-Audio-Duration"] = result.AudioDurationSeconds.ToString("F2");
    httpContext.Response.Headers["X-RTF"] = result.Rtf.ToString("F3");
    httpContext.Response.Headers["X-Character-Count"] = result.CharacterCount.ToString();

    if (includeSegments)
    {
        return Results.Ok(new
        {
            text = result.Text,
            language = result.DetectedLanguage,
            duration = result.AudioDurationSeconds,
            segments = (result.Segments ?? []).Select(s => new { id = s.Id, start = s.Start, end = s.End, text = s.Text })
        });
    }

    return Results.Ok(new { text = result.Text });
})
    .WithName("CreateTranscription")
    .WithTags("OpenAI Compatible")
    .WithSummary("Transcribe audio to text")
    .WithDescription("Generates a transcription from the uploaded audio file using the specified ASR model. OpenAI-compatible endpoint. Multipart/form-data fields: file (required, any ffmpeg-decodable format - WAV, FLAC, MP3, OGG, WEBM, M4A, etc. - normalized server-side before transcription), model (required), language (optional), response_format (optional, `json` (default) or `verbose_json` - the latter adds a `segments` array of `{ id, start, end, text }` timestamped segments to the response), min_segment_duration (optional, seconds, default 0.5 - segments shorter than this are merged into a neighboring segment rather than dropped), use_vad (optional, `true`/`1` to enable voice-activity-detection gating - only affects nemotron-3.5, ignored by whisper-base; also makes Nemotron's segments reflect detected speech regions instead of one full-clip segment). Supports whisper-base (multilingual, auto language detection) and nemotron-3.5 (40 language-locales).")
    .Accepts<IFormFile>("multipart/form-data")
    .Produces<object>(200)
    .Produces<ErrorResponse>(400)
    .Produces<ErrorResponse>(404);

app.MapGet("/v1/audio/transcriptions/stream", async (
    HttpContext httpContext,
    IAsrModelCatalog asrModelCatalog,
    IModelCache modelCache,
    IAsrTranscriber transcriber,
    Microsoft.Extensions.Options.IOptions<AsrStreamingOptions> streamingOptions,
    CancellationToken cancellationToken) =>
{
    if (!httpContext.WebSockets.IsWebSocketRequest)
    {
        httpContext.Response.StatusCode = StatusCodes.Status400BadRequest;
        await httpContext.Response.WriteAsync("Expected a WebSocket upgrade request.", cancellationToken);
        return;
    }

    var modelName = httpContext.Request.Query["model"].ToString();
    var language = httpContext.Request.Query["language"].ToString();
    var useVadRaw = httpContext.Request.Query["use_vad"].ToString();
    var enableVad = string.Equals(useVadRaw, "true", StringComparison.OrdinalIgnoreCase) || useVadRaw == "1";

    if (string.IsNullOrWhiteSpace(modelName) || !asrModelCatalog.TryGetModel(modelName, out var model))
    {
        httpContext.Response.StatusCode = StatusCodes.Status404NotFound;
        await httpContext.Response.WriteAsync("The requested model was not found.", cancellationToken);
        return;
    }

    var streamingConfig = streamingOptions.Value;
    var segmentSeconds = streamingConfig.DefaultSegmentSeconds;
    if (double.TryParse(httpContext.Request.Query["segment_seconds"].ToString(), out var requestedSegmentSeconds))
    {
        segmentSeconds = Math.Clamp(requestedSegmentSeconds, streamingConfig.MinSegmentSeconds, streamingConfig.MaxSegmentSeconds);
    }

    // Works identically whether ASR runs in-process or in worker mode - IAsrTranscriber's
    // CreateStreamingSessionAsync hides a duplex gRPC call behind the same interface in worker mode.
    var resolvedLanguage = string.IsNullOrWhiteSpace(language) ? null : language;
    var modelDirectory = await modelCache.EnsureModelAsync(model!, cancellationToken);
    var session = await transcriber.CreateStreamingSessionAsync(model!, modelDirectory, resolvedLanguage, enableVad, segmentSeconds, cancellationToken);

    using var _ = session;
    using var webSocket = await httpContext.WebSockets.AcceptWebSocketAsync();
    await RunStreamingTranscriptionAsync(webSocket, session, cancellationToken);
})
    .WithName("StreamTranscription")
    .WithTags("OpenAI Compatible")
    .WithSummary("Stream live speech-to-text over WebSocket")
    .WithDescription("WebSocket upgrade endpoint for real-time transcription. Query params: model (required), " +
        "language (optional), use_vad (optional, nemotron-3.5 only), segment_seconds (optional, default 10, " +
        "clamped to [5,30] - max seconds of audio before the current segment is force-committed; committed " +
        "sooner on sentence-ending punctuation). After upgrading, send binary WebSocket frames of raw 16-bit " +
        "PCM mono audio at 16kHz (no WAV/container header) - client-captured microphone or tab/system audio " +
        "must be resampled to this format before sending. The server replies with JSON text frames, each " +
        "scoped to the CURRENT segment only (never the whole conversation, so message size stays bounded " +
        "regardless of utterance length): `{\"type\":\"partial\",\"text\":\"...\"}` as the current segment's " +
        "text becomes available (replace the currently-displayed in-progress line), and " +
        "`{\"type\":\"segment\",\"text\":\"...\"}` once a segment is committed (append it to permanent " +
        "history and start a new in-progress line). Nemotron uses true cache-aware incremental decoding; " +
        "Whisper has no incremental API and instead periodically re-transcribes its current segment's " +
        "buffered audio, which is trimmed down at each commit. Send a text frame `{\"type\":\"end\"}` (or " +
        "close the socket) to finish - the server replies once more with " +
        "`{\"type\":\"final\",\"text\":\"...\"}` (the trailing, not-yet-committed segment's text) before " +
        "closing. Works in both in-process and worker mode. Swagger UI cannot exercise WebSocket endpoints " +
        "via \"Try it out\"; this description documents the protocol only.");
}

app.MapFallbackToFile("/index.html");

app.Run();

static async Task RunStreamingTranscriptionAsync(WebSocket webSocket, IStreamingTranscriptionSession session, CancellationToken cancellationToken)
{
    var receiveBuffer = new byte[16 * 1024];

    while (webSocket.State == WebSocketState.Open)
    {
        using var messageStream = new MemoryStream();
        WebSocketReceiveResult result;

        do
        {
            result = await webSocket.ReceiveAsync(receiveBuffer, cancellationToken);
            if (result.MessageType == WebSocketMessageType.Close)
            {
                break;
            }

            messageStream.Write(receiveBuffer, 0, result.Count);
        }
        while (!result.EndOfMessage);

        if (result.MessageType == WebSocketMessageType.Close)
        {
            break;
        }

        if (result.MessageType == WebSocketMessageType.Text)
        {
            var text = Encoding.UTF8.GetString(messageStream.ToArray());
            if (text.Contains("\"end\"", StringComparison.OrdinalIgnoreCase))
            {
                break;
            }

            continue;
        }

        var updates = await session.ProcessChunkAsync(messageStream.ToArray(), cancellationToken);
        foreach (var update in updates)
        {
            await SendJsonAsync(webSocket, new { type = update.IsSegmentFinal ? "segment" : "partial", text = update.Text }, cancellationToken);
        }
    }

    var finalText = await session.FinishAsync(cancellationToken);
    await SendJsonAsync(webSocket, new { type = "final", text = finalText }, cancellationToken);

    if (webSocket.State == WebSocketState.Open)
    {
        await webSocket.CloseAsync(WebSocketCloseStatus.NormalClosure, "done", CancellationToken.None);
    }
}

static async Task SendJsonAsync(WebSocket webSocket, object payload, CancellationToken cancellationToken)
{
    var bytes = JsonSerializer.SerializeToUtf8Bytes(payload);
    await webSocket.SendAsync(bytes, WebSocketMessageType.Text, endOfMessage: true, cancellationToken);
}

public partial class Program;
