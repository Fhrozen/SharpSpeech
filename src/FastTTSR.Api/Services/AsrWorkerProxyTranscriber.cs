using System.Collections.Concurrent;
using System.Threading.Channels;
using FastTTSR.Api.Contracts;
using FastTTSR.Api.Models;
using FastTTSR.Worker.Asr.Grpc;
using Google.Protobuf;
using Grpc.Core;
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

    public async Task<IStreamingTranscriptionSession> CreateStreamingSessionAsync(
        AsrModelDefinition model, string modelDirectory, string? language, bool enableVad, CancellationToken cancellationToken)
    {
        var modelKey = $"{model.Engine}:{model.Name}";

        var worker = await _processManager.GetOrSpawnWorkerAsync(modelKey, cancellationToken);
        var channel = GetOrCreateChannel(modelKey, worker.Port);
        var client = new WorkerTranscription.WorkerTranscriptionClient(channel);

        var call = client.TranscribeStream(cancellationToken: cancellationToken);
        await call.RequestStream.WriteAsync(new TranscribeStreamChunk
        {
            Config = new StreamConfig
            {
                ModelName = model.Name,
                Engine = model.Engine,
                ModelPath = Path.Combine(modelDirectory, model.ModelPath),
                Language = language ?? string.Empty,
                UseVad = enableVad
            }
        }, cancellationToken);

        return new WorkerStreamingSession(call);
    }

    /// <summary>Adapts a duplex TranscribeStream gRPC call to <see cref="IStreamingTranscriptionSession"/> -
    /// a drop-in for the in-process streaming sessions, so Program.cs's WebSocket handler doesn't
    /// need to know whether ASR is running in-process or in worker mode.</summary>
    private sealed class WorkerStreamingSession : IStreamingTranscriptionSession
    {
        private readonly AsyncDuplexStreamingCall<TranscribeStreamChunk, TranscribeStreamUpdate> _call;
        private readonly Channel<string> _updates = Channel.CreateUnbounded<string>();
        private readonly Task _readLoopTask;
        private string _lastFinalText = string.Empty;

        // Both engines' streaming sessions operate on 16kHz PCM16 mono audio - see WhisperStreamingSession/NemotronAsrEngine.
        public int SampleRate => 16000;

        public WorkerStreamingSession(AsyncDuplexStreamingCall<TranscribeStreamChunk, TranscribeStreamUpdate> call)
        {
            _call = call;
            _readLoopTask = ReadLoopAsync();
        }

        private async Task ReadLoopAsync()
        {
            try
            {
                await foreach (var update in _call.ResponseStream.ReadAllAsync())
                {
                    if (update.IsFinal)
                    {
                        _lastFinalText = update.Text;
                    }

                    await _updates.Writer.WriteAsync(update.Text);
                }
            }
            catch (Exception)
            {
                // Connection dropped/cancelled - FinishAsync falls back to the last known text.
            }
            finally
            {
                _updates.Writer.TryComplete();
            }
        }

        public async Task<string?> ProcessChunkAsync(byte[] pcm16Chunk, CancellationToken cancellationToken)
        {
            await _call.RequestStream.WriteAsync(new TranscribeStreamChunk { AudioChunk = ByteString.CopyFrom(pcm16Chunk) }, cancellationToken);
            return DrainLatestUpdate();
        }

        public async Task<string> FinishAsync(CancellationToken cancellationToken)
        {
            await _call.RequestStream.CompleteAsync();
            await _readLoopTask;
            return DrainLatestUpdate() ?? _lastFinalText;
        }

        private string? DrainLatestUpdate()
        {
            string? latest = null;
            while (_updates.Reader.TryRead(out var next))
            {
                latest = next;
            }

            return latest;
        }

        public void Dispose() => _call.Dispose();
    }
}
