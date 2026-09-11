# SharpAudio Wiki — Backend Architecture (TTS baseline)

> Linked from [docs/LLM_WIKI.md](../LLM_WIKI.md). Covers the pre-ASR / TTS-side architecture:
> request flow, worker-mode vs in-process mode, the worker process lifecycle, the gRPC contract,
> core abstractions, the model definition/config schema, the TTS engines themselves, and the
> TTS-only env var/endpoint tables. For the ASR-side equivalents, see
> [docs/wiki/asr-engines.md](asr-engines.md).

## Request flow
```
HTTP request → Program.cs minimal API endpoint → IModelCatalog (validate model) →
IModelCache (ensure model files downloaded) → ITtsSynthesizer (do the work) → response
```

## Worker-mode vs in-process mode
Controlled by `WorkerOptions.Enabled` (config section `WorkerOptions`, default `true`):
- **Worker mode (default)**: `WorkerProcessManager` (singleton + `IHostedService`) spawns/pools a
  separate `SharpAudio.Worker` OS process per model; `WorkerProxySynthesizer` is registered as
  `ITtsSynthesizer` and talks to the worker over gRPC.
- **In-process mode**: `KokoroTtsSynthesizer` + `SupertonicTtsSynthesizer` singletons are
  registered, and `TtsSynthesizerRouter` (registered as `ITtsSynthesizer`) routes each request to
  the right one by `model.Engine`. `ModelIdleMonitorService` (hosted service) periodically releases
  idle in-process engines.

## Worker process lifecycle (`src/SharpAudio.Api/Services/WorkerProcessManager.cs`)
- Pools **one process per model**, keyed by `modelKey = "{model.Engine}:{model.Name}"`.
- Spawns via `ProcessStartInfo(FileName = WorkerOptions.ExecutablePath, Arguments = "--port {p}
  --model-key \"{k}\" --idle-timeout {t}")`.
- Waits for the worker to print `READY:{port}` to stdout (regex-matched) before considering it up.
- Ports allocated starting at `WorkerOptions.PortRangeStart` (default 50051), up to
  `MaxPortAttempts` (default 100).
- Worker self-terminates after `IdleTimeoutSeconds` (default 60) of inactivity (see
  `Api/Services/IdleMonitor.cs`, shared with other worker projects via ProjectReference).
- Communication is gRPC over HTTP/2, `MaxReceiveMessageSize` 100MB / `MaxSendMessageSize` 10MB
  (large audio payloads).
- Its constructor takes a plain `WorkerOptions` value (not `IOptions<WorkerOptions>`), so multiple
  independently-configured instances (one per task type — TTS vs ASR) can be constructed manually
  in `Program.cs` and registered via keyed DI.

## gRPC contract (`src/SharpAudio.Api/Protos/synthesis.proto`)
- `service WorkerSynthesis { rpc Synthesize(...); rpc HealthCheck(...); }`
- `SynthesizeRequest`: model_name, engine, model_path, voices_path, tokens_path,
  voice_file_extension, supported_languages, speakers, input, voice, response_format, speed,
  language.
- `SynthesizeResponse`: audio_bytes, content_type, file_name, processing_time_seconds,
  audio_duration_seconds, character_count.
- `SharpAudio.Worker.csproj` has a `ProjectReference` to `SharpAudio.Api.csproj` and includes the same
  `.proto` file (`GrpcServices="Server"`) — this is how the worker reuses `KokoroTtsEngine` /
  `SupertonicTtsEngine`, which physically live under `src/SharpAudio.Api/Services/`.
- `Worker/Services/WorkerSynthesisService.cs` implements the gRPC service: routes by
  `request.Engine` string (`"kokoro"` vs `"supertonic"`/`"supertonic-3"`), lazily creates and pools
  the engine, and reports activity to `IdleMonitor` on every call.

## Core abstractions (`src/SharpAudio.Api/Services/`)
- `ITtsSynthesizer.SynthesizeAsync(TtsModelDefinition model, string modelDirectory,
  OpenAiSpeechRequest request, CancellationToken ct) -> Task<SynthesisResult>` — the single
  synthesis contract, implemented by `WorkerProxySynthesizer`, `TtsSynthesizerRouter`,
  `KokoroTtsSynthesizer`, `SupertonicTtsSynthesizer`.
- `IIdleTrackingSynthesizer { GetLoadedEngines(); TryReleaseEngine(key); }` — lets
  `ModelIdleMonitorService` release in-process engines that have been idle.
- `IModelCatalog { GetSupportedModels(); TryGetModel(name, out model); }` — implemented by
  `ModelCatalog`, which loads model definitions from `config.json` (see below).
- `IModelCache.EnsureModelAsync(TtsModelDefinition model, CancellationToken ct) -> Task<string>` —
  downloads/verifies a model's asset files into `MODEL_CACHE_DIR/{model.Name}/`, returns the local
  directory path. Has a second overload for `AsrModelDefinition` (see asr-engines.md). Uses a
  per-model-name `SemaphoreSlim` internally to serialize concurrent ensure-calls for the same model
  (fixes a real concurrent-download race — see [docs/wiki/testing.md](testing.md)).
- **Strategy/Factory pattern**: `TtsSynthesizerRouter` picks an engine by `model.Engine` string
  (`SupertonicMetadata.IsSupertonic3Engine(...)` else falls back to Kokoro). This is the pattern to
  mirror when adding new task types/engines.

## Model definition & config (`src/SharpAudio.Api/Models/TtsModelDefinition.cs`, `config.json`)
```csharp
public sealed class TtsModelDefinition
{
    public string Name { get; init; }              // "kokoro-q4", "supertonic-3"
    public string DisplayName { get; init; }
    public string Description { get; init; }
    public string Engine { get; init; }             // "kokoro", "supertonic-3"
    public string ModelPath { get; init; }           // relative path within model cache dir
    public string? VoicesPath { get; init; }
    public string? VoicesBaseUrl { get; init; }
    public string VoiceFileExtension { get; init; }
    public string? TokensPath { get; init; }
    public IReadOnlyList<ModelAsset> Assets { get; init; }   // ONNX/asset files to download (shared type)
    public IReadOnlyList<string> SupportedLanguages { get; init; }
    public IReadOnlyList<string> Speakers { get; init; }
}
```
`ModelCatalog` loads from `MODEL_CONFIG_PATH` env var, or falls back to
`{AppContext.BaseDirectory}/config.json`, top-level `"models"` array; falls back to hardcoded
defaults if the file is missing. `ApplyCanonicalMetadata()` enriches entries with languages/speakers
per engine (`KokoroMetadata`, `SupertonicMetadata`). `ModelAsset` (`Url`, `RelativePath`) is a
generic asset descriptor shared by both TTS and ASR model definitions.

## Engines
- **Kokoro** (`KokoroTtsEngine.cs`/`KokoroTtsSynthesizer.cs`): single ONNX session via
  `Microsoft.ML.OnnxRuntime`, phonemization via `EspeakWrapper` (wraps espeak-ng native lib),
  24000 Hz output, pooled per model+directory in a `ConcurrentDictionary`.
- **Supertonic-3** (`SupertonicTtsEngine.cs`/`SupertonicTtsSynthesizer.cs`): **4 chained ONNX
  sessions** (text_encoder, duration_predictor, vector_estimator/denoiser, vocoder), iterative
  flow-matching denoising loop, 22050 Hz output, config read from `tts.json` inside the model dir.
  **This multi-session-in-one-class pattern is the template for any future ONNX-chain engine** (see
  Nemotron in [docs/wiki/asr-engines.md](asr-engines.md)).

## Config/env var surface (TTS baseline)
| Env var | Purpose | Default |
|---|---|---|
| `HTTP_PORT` / `HTTPS_PORT` | Kestrel listen ports | 8080 / (none) |
| `MODEL_CACHE_DIR` | Model download cache dir | `{TempPath}/fastttsr-cache` |
| `MODEL_CONFIG_PATH` | Path to `config.json` | `{BaseDirectory}/config.json` |
| `MODEL_IDLE_TIMEOUT_SECONDS` | In-process engine idle release timeout | 60 |
| `ESPEAK_DATA_DIR` | espeak-ng phoneme data dir (Kokoro) | — |
| `WorkerOptions__Enabled` | worker-mode vs in-process | true |
| `WorkerOptions__ExecutablePath` | TTS worker binary path | `./SharpAudio.Worker` |
| `WorkerOptions__PortRangeStart` | first gRPC port to try | 50051 |
| `WorkerOptions__IdleTimeoutSeconds` | worker self-termination timeout | 60 |
| `ASPNETCORE_ENVIRONMENT` | environment name | Production |
| `SERVER_MODE` | `tts`\|`asr`\|`both` — which task type(s) this instance serves | `tts` |

See [docs/wiki/asr-engines.md](asr-engines.md) for the ASR-specific env vars
(`AsrWorkerOptions__*`).

## Endpoints (`src/SharpAudio.Api/Program.cs`) — TTS + always-on
| Route | Method | Purpose |
|---|---|---|
| `/health` | GET | health check |
| `/api/server-info` | GET | `{ ttsEnabled, asrEnabled }`, reflects `SERVER_MODE` |
| `/api/models` | GET | list TTS models w/ languages/speakers/metadata (only if `ttsEnabled`) |
| `/v1/models` | GET | OpenAI-compatible model listing, merges TTS+ASR catalogs |
| `/v1/audio/speech` | POST | OpenAI-compatible TTS synthesis (only if `ttsEnabled`); returns audio
  bytes + metrics in response headers (`X-Processing-Time`, `X-Chars-Per-Second`, `X-RTF`,
  `X-Audio-Duration`, `X-Character-Count`) |
| `/swagger` | GET | interactive API docs |

See [docs/wiki/asr-engines.md](asr-engines.md) for the ASR-specific endpoints
(`/api/asr-models`, `/v1/audio/transcriptions`, `/v1/audio/transcriptions/stream`).

Static frontend files are served from `wwwroot` (built by the frontend and copied in at Docker
build time); `MapFallbackToFile("/index.html")` handles SPA routing.

## Extension point: Adding a new TTS engine
1. Add a `{Engine}TtsEngine.cs` (owns ONNX session(s), pooled per model+directory — mirror
   `KokoroTtsEngine` for single-session or `SupertonicTtsEngine` for multi-session chains) and a
   `{Engine}TtsSynthesizer.cs` implementing `ITtsSynthesizer`/`IIdleTrackingSynthesizer`.
2. Add routing for the new `Engine` string in `TtsSynthesizerRouter` and
   `Worker/Services/WorkerSynthesisService.cs`.
3. Add model entries to `config.json` `"models"` array.
4. Register the synthesizer singleton in `Program.cs` (in-process branch).

## Build tooling note
No local `dotnet` CLI in this dev environment — use `./dotnet.sh <args>` (Docker-based wrapper) for
build/test/publish, e.g. `./dotnet.sh build SharpAudio.slnx`, `./dotnet.sh test
tests/SharpAudio.Api.Tests`.
