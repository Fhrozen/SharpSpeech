using System.Collections.Concurrent;
using FastTTSR.Api.Contracts;
using FastTTSR.Api.Models;
using FastTTSR.Worker.Grpc;
using Grpc.Net.Client;

namespace FastTTSR.Api.Services;

/// <summary>
/// Proxy synthesizer that routes requests to worker processes via gRPC
/// </summary>
public sealed class WorkerProxySynthesizer : ITtsSynthesizer, IDisposable
{
    private readonly WorkerProcessManager _processManager;
    private readonly IModelCache _modelCache;
    private readonly ILogger<WorkerProxySynthesizer> _logger;
    private readonly ConcurrentDictionary<string, (GrpcChannel Channel, int Port)> _channels = new();
    private bool _disposed;

    public WorkerProxySynthesizer(
        WorkerProcessManager processManager,
        IModelCache modelCache,
        ILogger<WorkerProxySynthesizer> logger)
    {
        _processManager = processManager;
        _modelCache = modelCache;
        _logger = logger;
    }

    public async Task<SynthesisResult> SynthesizeAsync(
        TtsModelDefinition model,
        string modelDirectory,
        OpenAiSpeechRequest request,
        CancellationToken cancellationToken)
    {
        var modelKey = $"{model.Engine}:{model.Name}";

        try
        {
            // Get or spawn worker for this model
            var worker = await _processManager.GetOrSpawnWorkerAsync(modelKey, cancellationToken);

            // Get or create gRPC channel (invalidate if port changed)
            var channel = GetOrCreateChannel(modelKey, worker.Port);
            var client = new WorkerSynthesis.WorkerSynthesisClient(channel);

            // Build gRPC request
            var grpcRequest = new SynthesizeRequest
            {
                ModelName = model.Name,
                Engine = model.Engine,
                ModelPath = Path.Combine(modelDirectory, model.ModelPath),
                VoicesPath = !string.IsNullOrWhiteSpace(model.VoicesPath) 
                    ? Path.Combine(modelDirectory, model.VoicesPath) 
                    : string.Empty,
                TokensPath = !string.IsNullOrWhiteSpace(model.TokensPath)
                    ? Path.Combine(modelDirectory, model.TokensPath)
                    : string.Empty,
                VoiceFileExtension = model.VoiceFileExtension,
                Input = request.Input,
                Voice = request.Voice,
                ResponseFormat = request.ResponseFormat,
                Speed = request.Speed,
                Language = request.Language ?? "en-us"
            };

            _logger.LogInformation("Sending synthesis request to worker {ModelKey} on port {Port}: speed={Speed}, voice={Voice}, input_length={Length}", 
                modelKey, worker.Port, request.Speed, request.Voice, request.Input.Length);

            grpcRequest.SupportedLanguages.AddRange(model.SupportedLanguages);
            grpcRequest.Speakers.AddRange(model.Speakers);

            // Call worker via gRPC with retry logic
            SynthesizeResponse response;
            try
            {
                response = await client.SynthesizeAsync(grpcRequest, cancellationToken: cancellationToken);
            }
            catch (Grpc.Core.RpcException ex) when (ex.StatusCode == Grpc.Core.StatusCode.Unavailable)
            {
                _logger.LogWarning("First connection attempt failed, retrying in 1 second...");
                await Task.Delay(1000, cancellationToken);
                response = await client.SynthesizeAsync(grpcRequest, cancellationToken: cancellationToken);
            }

            // Convert gRPC response to SynthesisResult
            return new SynthesisResult(
                response.AudioBytes.ToByteArray(),
                response.ContentType,
                response.FileName,
                response.ProcessingTimeSeconds,
                response.AudioDurationSeconds,
                response.CharacterCount
            );
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Worker synthesis failed for {ModelKey}", modelKey);
            
            // Return fallback silence
            return GenerateFallbackSilence(model, request);
        }
    }

    private GrpcChannel GetOrCreateChannel(string modelKey, int port)
    {
        // Check if cached channel exists and if port matches
        if (_channels.TryGetValue(modelKey, out var cached))
        {
            if (cached.Port == port)
            {
                _logger.LogDebug("Reusing cached gRPC channel for {ModelKey} on port {Port}", modelKey, port);
                return cached.Channel;
            }
            
            // Port changed, dispose old channel and create new one
            _logger.LogInformation("Worker port changed for {ModelKey}: {OldPort} -> {NewPort}. Recreating channel.", 
                modelKey, cached.Port, port);
            
            try
            {
                cached.Channel.Dispose();
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error disposing old channel for {ModelKey}", modelKey);
            }
            
            _channels.TryRemove(modelKey, out _);
        }

        // Create new channel
        var address = $"http://localhost:{port}";
        _logger.LogInformation("Creating gRPC channel for {ModelKey}: {Address}", modelKey, address);
        
        var channel = GrpcChannel.ForAddress(address, new GrpcChannelOptions
        {
            MaxReceiveMessageSize = 100 * 1024 * 1024, // 100 MB for large audio files
            MaxSendMessageSize = 10 * 1024 * 1024 // 10 MB for requests
        });
        
        _channels[modelKey] = (channel, port);
        return channel;
    }

    private static SynthesisResult GenerateFallbackSilence(TtsModelDefinition model, OpenAiSpeechRequest request)
    {
        const int sampleRate = 24000;
        const short channels = 1;
        const short bitsPerSample = 16;

        var durationSeconds = Math.Clamp(request.Input.Length / 30.0, 0.5, 8.0);
        var sampleCount = (int)(sampleRate * durationSeconds);
        var dataSize = sampleCount * channels * bitsPerSample / 8;

        using var stream = new MemoryStream(44 + dataSize);
        using var writer = new BinaryWriter(stream, System.Text.Encoding.ASCII, leaveOpen: true);

        var byteRate = sampleRate * channels * bitsPerSample / 8;
        var blockAlign = (short)(channels * bitsPerSample / 8);

        // WAV header
        writer.Write(System.Text.Encoding.ASCII.GetBytes("RIFF"));
        writer.Write(36 + dataSize);
        writer.Write(System.Text.Encoding.ASCII.GetBytes("WAVE"));
        writer.Write(System.Text.Encoding.ASCII.GetBytes("fmt "));
        writer.Write(16);
        writer.Write((short)1);
        writer.Write(channels);
        writer.Write(sampleRate);
        writer.Write(byteRate);
        writer.Write(blockAlign);
        writer.Write(bitsPerSample);
        writer.Write(System.Text.Encoding.ASCII.GetBytes("data"));
        writer.Write(dataSize);
        writer.Write(new byte[dataSize]);
        writer.Flush();

        var charCount = request.Input.Length;

        return new SynthesisResult(
            stream.ToArray(),
            "audio/wav",
            $"{model.Name}.wav",
            0.1, // Processing time
            durationSeconds,
            charCount);
    }

    public void Dispose()
    {
        if (_disposed) return;

        foreach (var (channel, _) in _channels.Values)
        {
            try
            {
                channel.Dispose();
            }
            catch
            {
                // Ignore disposal errors
            }
        }

        _channels.Clear();
        _disposed = true;
    }
}
