using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using SharpAudio.Api.IntegrationTests.Support;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;
using Xunit.Abstractions;

namespace SharpAudio.Api.IntegrationTests;

/// <summary>
/// Circular TTS -> ASR tests: synthesizes real audio via the Supertonic-3 TTS model, then feeds it
/// into the Whisper/Nemotron ASR engines and checks the transcription against the known reference
/// text. Opt-in only (see AsrModelTestGate) - downloads real models and runs real inference, so
/// these are skipped by default and excluded from the GitHub Actions CI workflow.
///
/// NOTE: as of Phase 5, Whisper cases are expected to fail in container environments missing a
/// working native whisper.cpp library load (see docs/ASR_IMPLEMENTATION_PLAN.md Phase 6) - that is
/// a known, tracked packaging issue, not a bug in these tests.
/// </summary>
[Trait("Category", "AsrModelTests")]
public class AsrCircularTests
{
    private static readonly (IReadOnlyList<AsrTestSample> Samples, IReadOnlyList<AsrConversationSample> Conversations) Corpus
        = AsrTestCorpus.Load();

    private static readonly string[] AsrModels = ["whisper-base", "nemotron-3.5"];

    private readonly ITestOutputHelper _output;

    public AsrCircularTests(ITestOutputHelper output)
    {
        _output = output;
    }

    public static IEnumerable<object[]> OfflineSampleCases()
    {
        foreach (var sample in Corpus.Samples)
        {
            foreach (var model in AsrModels)
            {
                yield return new object[] { sample.Id, model };
            }
        }
    }

    [SkippableTheory]
    [MemberData(nameof(OfflineSampleCases))]
    public async Task Offline_TextSample_TranscribesToReasonablyMatchingText(string sampleId, string asrModel)
    {
        Skip.IfNot(AsrModelTestGate.IsEnabled, AsrModelTestGate.SkipReason);

        var sample = Corpus.Samples.First(s => s.Id == sampleId);
        using var factory = CreateFactory();
        var client = factory.CreateClient();

        var wavBytes = await SynthesizeAsync(client, sample.Text, sample.Speaker ?? "F1");
        var transcript = await TranscribeAsync(client, wavBytes, asrModel);
        var wer = WordErrorRate.Compute(sample.Text, transcript);

        _output.WriteLine($"[{asrModel}] sample={sampleId} WER={wer:P0}");
        _output.WriteLine($"  reference:  \"{sample.Text}\"");
        _output.WriteLine($"  transcript: \"{transcript}\"");

        Assert.False(string.IsNullOrWhiteSpace(transcript), $"[{asrModel}] produced an empty transcript for sample '{sampleId}'.");
        Assert.True(wer <= 0.9, $"[{asrModel}] WER {wer:P0} too high for sample '{sampleId}'.");
    }

    [SkippableTheory]
    [MemberData(nameof(AsrModelNames))]
    public async Task Streaming_LongConversation_TranscribesWithoutCrashingAndProducesText(string asrModel)
    {
        Skip.IfNot(AsrModelTestGate.IsEnabled, AsrModelTestGate.SkipReason);

        var conversation = Corpus.Conversations.First();
        using var factory = CreateFactory();
        var client = factory.CreateClient();

        var turnClips = new List<byte[]>();
        foreach (var turn in conversation.Turns)
        {
            turnClips.Add(await SynthesizeAsync(client, turn.Text, turn.Speaker));
        }

        var longWav = WavTestUtils.Concatenate(turnClips);
        var referenceText = string.Join(' ', conversation.Turns.Select(t => t.Text));
        var referenceWordCount = referenceText.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length;

        var transcript = await TranscribeAsync(client, longWav, asrModel);
        var transcriptWordCount = transcript.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length;

        _output.WriteLine($"[{asrModel}] conversation turns={conversation.Turns.Count}, audio_bytes={longWav.Length}");
        _output.WriteLine($"  reference word count:  {referenceWordCount}");
        _output.WriteLine($"  transcript word count: {transcriptWordCount}");
        _output.WriteLine($"  transcript: \"{transcript}\"");

        // A long multi-chunk conversation stresses cache-aware streaming decode; the main risk is
        // crashing or degenerating to empty/near-empty output, not exact word accuracy.
        Assert.False(string.IsNullOrWhiteSpace(transcript), $"[{asrModel}] produced an empty transcript for the long conversation.");
        Assert.True(transcriptWordCount > referenceWordCount / 4,
            $"[{asrModel}] transcript suspiciously short ({transcriptWordCount} words vs {referenceWordCount} reference words).");
    }

    [SkippableFact]
    public async Task Offline_NonWavUpload_TranscribesSuccessfully()
    {
        Skip.IfNot(AsrModelTestGate.IsEnabled, AsrModelTestGate.SkipReason);
        Skip.IfNot(FfmpegTestGate.IsAvailable, FfmpegTestGate.SkipReason);

        var sample = Corpus.Samples.First();
        using var factory = CreateFactory();
        var client = factory.CreateClient();

        var wavBytes = await SynthesizeAsync(client, sample.Text, sample.Speaker ?? "F1");
        var flacBytes = await FfmpegTestEncoder.EncodeAsync(wavBytes, "flac");

        var transcript = await TranscribeAsync(client, flacBytes, "whisper-base", fileName: "sample.flac", contentType: "audio/flac");

        _output.WriteLine($"FLAC upload transcript: \"{transcript}\"");
        Assert.False(string.IsNullOrWhiteSpace(transcript), "FLAC upload produced an empty transcript.");
    }

    [SkippableFact]
    public async Task Offline_NemotronWithVad_TranscribesWithoutCrashing()
    {
        Skip.IfNot(AsrModelTestGate.IsEnabled, AsrModelTestGate.SkipReason);

        var sample = Corpus.Samples.FirstOrDefault(s => s.Type == "long") ?? Corpus.Samples.First();
        using var factory = CreateFactory();
        var client = factory.CreateClient();

        var wavBytes = await SynthesizeAsync(client, sample.Text, sample.Speaker ?? "F1");
        var transcript = await TranscribeAsync(client, wavBytes, "nemotron-3.5", enableVad: true);
        var wer = WordErrorRate.Compute(sample.Text, transcript);

        _output.WriteLine($"[nemotron-3.5 + VAD] sample={sample.Id} WER={wer:P0}");
        _output.WriteLine($"  reference:  \"{sample.Text}\"");
        _output.WriteLine($"  transcript: \"{transcript}\"");

        Assert.False(string.IsNullOrWhiteSpace(transcript), "VAD-enabled Nemotron transcription produced an empty transcript.");
        Assert.True(wer <= 0.9, $"VAD-enabled Nemotron WER {wer:P0} too high for sample '{sample.Id}'.");
    }

    [SkippableFact]
    public async Task Streaming_WorkerMode_TranscribesOverWebSocket()
    {
        Skip.IfNot(AsrModelTestGate.IsEnabled, AsrModelTestGate.SkipReason);
        Skip.IfNot(WorkerAsrExecutableGate.IsAvailable, WorkerAsrExecutableGate.SkipReason);

        Environment.SetEnvironmentVariable("SERVER_MODE", "asr");
        Environment.SetEnvironmentVariable("AsrWorkerOptions__Enabled", "true");
        using var factory = new WebApplicationFactory<Program>();
        var wsClient = factory.Server.CreateWebSocketClient();

        var wsUri = new Uri(factory.Server.BaseAddress, "/v1/audio/transcriptions/stream?model=whisper-base");
        using var socket = await wsClient.ConnectAsync(new Uri(wsUri.ToString().Replace("http", "ws")), CancellationToken.None);

        // Synthetic tone, not real speech - this test proves the worker-mode duplex gRPC round-trip
        // completes without crashing, not transcription accuracy (mirrors Phase 3's initial engine smoke test).
        var pcm = GenerateSyntheticPcm16(durationSeconds: 2.0);
        const int chunkBytes = 3200; // 100ms @ 16kHz mono PCM16
        for (var offset = 0; offset < pcm.Length; offset += chunkBytes)
        {
            var length = Math.Min(chunkBytes, pcm.Length - offset);
            await socket.SendAsync(new ArraySegment<byte>(pcm, offset, length), WebSocketMessageType.Binary, endOfMessage: true, CancellationToken.None);
        }

        await socket.SendAsync(Encoding.UTF8.GetBytes("{\"type\":\"end\"}"), WebSocketMessageType.Text, endOfMessage: true, CancellationToken.None);

        string? finalText = null;
        var buffer = new byte[16 * 1024];
        while (socket.State == WebSocketState.Open)
        {
            var result = await socket.ReceiveAsync(buffer, CancellationToken.None);
            if (result.MessageType == WebSocketMessageType.Close)
            {
                break;
            }

            var json = Encoding.UTF8.GetString(buffer, 0, result.Count);
            _output.WriteLine($"[worker-mode WS] {json}");
            var message = JsonSerializer.Deserialize<JsonElement>(json);
            if (message.GetProperty("type").GetString() == "final")
            {
                finalText = message.GetProperty("text").GetString();
                break;
            }
        }

        Assert.NotNull(finalText);
    }

    private static byte[] GenerateSyntheticPcm16(double durationSeconds, int sampleRate = 16000)
    {
        var sampleCount = (int)(durationSeconds * sampleRate);
        var pcm = new byte[sampleCount * 2];
        for (var i = 0; i < sampleCount; i++)
        {
            var sample = (short)(0.5 * short.MaxValue * Math.Sin(2 * Math.PI * 440 * i / sampleRate));
            BitConverter.GetBytes(sample).CopyTo(pcm, i * 2);
        }

        return pcm;
    }

    public static IEnumerable<object[]> AsrModelNames() => AsrModels.Select(m => new object[] { m });

    private static WebApplicationFactory<Program> CreateFactory()
    {
        Environment.SetEnvironmentVariable("SERVER_MODE", "both");
        Environment.SetEnvironmentVariable("WorkerOptions__Enabled", "false");
        Environment.SetEnvironmentVariable("AsrWorkerOptions__Enabled", "false");
        return new WebApplicationFactory<Program>();
    }

    private static async Task<byte[]> SynthesizeAsync(HttpClient client, string text, string speaker)
    {
        var response = await client.PostAsJsonAsync("/v1/audio/speech", new
        {
            model = "supertonic-3",
            input = text,
            voice = speaker,
            response_format = "wav",
            speed = 1.0
        });

        response.EnsureSuccessStatusCode();
        return await response.Content.ReadAsByteArrayAsync();
    }

    private static async Task<string> TranscribeAsync(HttpClient client, byte[] audioBytes, string asrModel, string fileName = "sample.wav", string contentType = "audio/wav", bool enableVad = false)
    {
        using var content = new MultipartFormDataContent();
        var audioContent = new ByteArrayContent(audioBytes);
        audioContent.Headers.ContentType = new MediaTypeHeaderValue(contentType);
        content.Add(audioContent, "file", fileName);
        content.Add(new StringContent(asrModel), "model");
        if (enableVad)
        {
            content.Add(new StringContent("true"), "use_vad");
        }

        var response = await client.PostAsync("/v1/audio/transcriptions", content);
        response.EnsureSuccessStatusCode();

        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        return json.GetProperty("text").GetString() ?? string.Empty;
    }
}
