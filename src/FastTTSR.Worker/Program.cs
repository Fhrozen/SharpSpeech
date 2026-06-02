using FastTTSR.Worker.Services;

var builder = WebApplication.CreateBuilder(args);

// Parse command-line arguments
var port = 50051; // Default port
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

Console.WriteLine($"[Worker] Starting on port {port} for model: {modelKey}");
Console.WriteLine($"[Worker] Idle timeout: {idleTimeoutSeconds} seconds");

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
        Console.WriteLine($"[Worker] Idle timeout reached ({idleTimeoutSeconds}s). Shutting down...");
        Environment.Exit(0);
    }));

builder.Services.AddSingleton<WorkerSynthesisService>();

var app = builder.Build();

// Map gRPC service
app.MapGrpcService<WorkerSynthesisService>();

// Signal readiness
Console.WriteLine($"[Worker] READY:{port}");

app.Run();
