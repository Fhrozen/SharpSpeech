using System.Collections.Concurrent;
using FastTTSR.Api.Contracts;
using FastTTSR.Api.Models;
using FastTTSR.Worker.Asr.Grpc;
using Google.Protobuf;
using Grpc.Net.Client;
using Microsoft.Extensions.DependencyInjection;

namespace FastTTSR.Api.Services;

/// <summary>
/// Proxy transcriber that routes requests to the ASR worker process via gRPC (mirrors
/// <see cref="WorkerProxySynthesizer"/>).
/// </summary>
public sealed class AsrWorkerProxyTranscriber : IAsrTranscriber, IDisposable
{
    private readonly WorkerProcessManager _processManager;
    private readonly ILogger<AsrWorkerProxyTranscriber> _logger;
    private readonly ConcurrentDictionary<string, (GrpcChannel Channel, int Port)> _channels = new();
    private bool _disposed;

    public AsrWorkerProxyTranscriber(
        [FromKeyedServices("asr")] WorkerProcessManager processManager,
        ILogger<AsrWorkerProxyTranscriber> logger)
    {
        _processManager = processManager;
        _logger = logger;
    }

    public async Task<TranscriptionResult> TranscribeAsync(
        AsrModelDefinition model,
        string modelDirectory,
        AudioTranscriptionRequest request,
        byte[] audioBytes,
        CancellationToken cancellationToken)
    {
        var modelKey = $"{model.Engine}:{model.Name}";

        var worker = await _processManager.GetOrSpawnWorkerAsync(modelKey, cancellationToken);
        var channel = GetOrCreateChannel(modelKey, worker.Port);
        var client = new WorkerTranscription.WorkerTranscriptionClient(channel);

        var grpcRequest = new TranscribeRequest
        {
            ModelName = model.Name,
            Engine = model.Engine,
            ModelPath = Path.Combine(modelDirectory, model.ModelPath),
            AudioBytes = ByteString.CopyFrom(audioBytes),
            AudioFormat = "wav",
            Language = request.Language ?? string.Empty
        };

        _logger.LogInformation("Sending transcription request to worker {ModelKey} on port {Port}: audio_bytes={Length}",
            modelKey, worker.Port, audioBytes.Length);

        TranscribeResponse response;
        try
        {
            response = await client.TranscribeAsync(grpcRequest, cancellationToken: cancellationToken);
        }
        catch (Grpc.Core.RpcException ex) when (ex.StatusCode == Grpc.Core.StatusCode.Unavailable)
        {
            _logger.LogWarning("First connection attempt failed, retrying in 1 second...");
            await Task.Delay(1000, cancellationToken);
            response = await client.TranscribeAsync(grpcRequest, cancellationToken: cancellationToken);
        }

        return new TranscriptionResult(
            response.Text,
            string.IsNullOrEmpty(response.LanguageDetected) ? null : response.LanguageDetected,
            response.ProcessingTimeSeconds,
            response.AudioDurationSeconds,
            response.Text.Length);
    }

    private GrpcChannel GetOrCreateChannel(string modelKey, int port)
    {
        if (_channels.TryGetValue(modelKey, out var cached))
        {
            if (cached.Port == port)
            {
                return cached.Channel;
            }

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

        var address = $"http://localhost:{port}";
        _logger.LogInformation("Creating gRPC channel for {ModelKey}: {Address}", modelKey, address);

        var channel = GrpcChannel.ForAddress(address, new GrpcChannelOptions
        {
            MaxReceiveMessageSize = 10 * 1024 * 1024, // 10 MB for transcribed text responses
            MaxSendMessageSize = 100 * 1024 * 1024 // 100 MB for uploaded audio files
        });

        _channels[modelKey] = (channel, port);
        return channel;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        foreach (var (channel, _) in _channels.Values)
        {
            channel.Dispose();
        }

        _disposed = true;
    }
}
