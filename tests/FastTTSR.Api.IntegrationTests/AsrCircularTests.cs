using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using FastTTSR.Api.IntegrationTests.Support;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;
using Xunit.Abstractions;

namespace FastTTSR.Api.IntegrationTests;

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

    private static async Task<string> TranscribeAsync(HttpClient client, byte[] audioBytes, string asrModel, string fileName = "sample.wav", string contentType = "audio/wav")
    {
        using var content = new MultipartFormDataContent();
        var audioContent = new ByteArrayContent(audioBytes);
        audioContent.Headers.ContentType = new MediaTypeHeaderValue(contentType);
        content.Add(audioContent, "file", fileName);
        content.Add(new StringContent(asrModel), "model");

        var response = await client.PostAsync("/v1/audio/transcriptions", content);
        response.EnsureSuccessStatusCode();

        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        return json.GetProperty("text").GetString() ?? string.Empty;
    }
}
