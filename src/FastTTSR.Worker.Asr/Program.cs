using FastTTSR.Api.Services;
using FastTTSR.Worker.Asr.Services;

var builder = WebApplication.CreateBuilder(args);

// Parse command-line arguments
var port = 50151; // Default port (ASR range, distinct from TTS's 50051+)
var modelKey = string.Empty;
var idleTimeoutSeconds = 60; // Default 60 seconds

for (int i = 0; i < args.Length; i++)
{
    if (args[i] == "--port" && i + 1 < args.Length)
    {
        port = int.Parse(args[i + 1]);
        i++;
    }
    else if (args[i] == "--model-key" && i + 1 < args.Length)
    {
        modelKey = args[i + 1];
        i++;
    }
    else if (args[i] == "--idle-timeout" && i + 1 < args.Length)
    {
        idleTimeoutSeconds = int.Parse(args[i + 1]);
        i++;
    }
}

Console.WriteLine($"[Worker.Asr] Starting on port {port} for model: {modelKey}");
Console.WriteLine($"[Worker.Asr] Idle timeout: {idleTimeoutSeconds} seconds");

// Configure Kestrel to listen on specified port
builder.WebHost.ConfigureKestrel(options =>
{
    options.ListenLocalhost(port, listenOptions =>
    {
        listenOptions.Protocols = Microsoft.AspNetCore.Server.Kestrel.Core.HttpProtocols.Http2;
    });
});

// Add gRPC services
builder.Services.AddGrpc();

// Register idle monitor
builder.Services.AddSingleton<IdleMonitor>(sp => new IdleMonitor(
    TimeSpan.FromSeconds(idleTimeoutSeconds),
    () =>
    {
        Console.WriteLine($"[Worker.Asr] Idle timeout reached ({idleTimeoutSeconds}s). Shutting down...");
        Environment.Exit(0);
    }));

builder.Services.AddSingleton<WorkerTranscriptionService>();

var app = builder.Build();

// Map gRPC service
app.MapGrpcService<WorkerTranscriptionService>();

// Start the server in background
var runTask = app.RunAsync();

// Wait a moment for Kestrel to start listening
await Task.Delay(500);

// Signal readiness after server is listening
Console.WriteLine($"[Worker.Asr] READY:{port}");

await runTask;
