# FastTTSR — LLM Wiki

A condensed, agent-optimized architecture reference for FastTTSR. This file is meant to let an
agent ramp up quickly without re-discovering the codebase from scratch. It links to the detailed
human-oriented docs in `docs/*.md` rather than duplicating them — read those for deep detail on a
specific topic.

## Keeping this doc updated

**Rule: update the relevant section of this file as part of any change that affects architecture,
config surface, endpoints, or conventions — do this alongside the code change, not as a deferred
separate pass.** If you add a new engine, endpoint, env var, or worker type, add/adjust the
matching section below in the same PR/session.

## What this project is

FastTTSR is a .NET 10 REST API (OpenAI-compatible) for Text-to-Speech, with a Vue 3 frontend and
Docker packaging. Each TTS model runs inference in an isolated, disposable OS worker process for
memory isolation (spawned on demand, killed when idle).

## Backend architecture

### Request flow
```
HTTP request → Program.cs minimal API endpoint → IModelCatalog (validate model) →
IModelCache (ensure model files downloaded) → ITtsSynthesizer (do the work) → response
```

### Worker-mode vs in-process mode
Controlled by `WorkerOptions.Enabled` (config section `WorkerOptions`, default `true`):
- **Worker mode (default)**: `WorkerProcessManager` (singleton + `IHostedService`) spawns/pools a
  separate `FastTTSR.Worker` OS process per model; `WorkerProxySynthesizer` is registered as
  `ITtsSynthesizer` and talks to the worker over gRPC.
- **In-process mode**: `KokoroTtsSynthesizer` + `SupertonicTtsSynthesizer` singletons are
  registered, and `TtsSynthesizerRouter` (registered as `ITtsSynthesizer`) routes each request to
  the right one by `model.Engine`. `ModelIdleMonitorService` (hosted service) periodically releases
  idle in-process engines.

### Worker process lifecycle (`src/FastTTSR.Api/Services/WorkerProcessManager.cs`)
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

### gRPC contract (`src/FastTTSR.Api/Protos/synthesis.proto`)
- `service WorkerSynthesis { rpc Synthesize(...); rpc HealthCheck(...); }`
- `SynthesizeRequest`: model_name, engine, model_path, voices_path, tokens_path,
  voice_file_extension, supported_languages, speakers, input, voice, response_format, speed,
  language.
- `SynthesizeResponse`: audio_bytes, content_type, file_name, processing_time_seconds,
  audio_duration_seconds, character_count.
- `FastTTSR.Worker.csproj` has a `ProjectReference` to `FastTTSR.Api.csproj` and includes the same
  `.proto` file (`GrpcServices="Server"`) — this is how the worker reuses `KokoroTtsEngine` /
  `SupertonicTtsEngine`, which physically live under `src/FastTTSR.Api/Services/`.
- `Worker/Services/WorkerSynthesisService.cs` implements the gRPC service: routes by
  `request.Engine` string (`"kokoro"` vs `"supertonic"`/`"supertonic-3"`), lazily creates and pools
  the engine, and reports activity to `IdleMonitor` on every call.

### Core abstractions (`src/FastTTSR.Api/Services/`)
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
  directory path.
- **Strategy/Factory pattern**: `TtsSynthesizerRouter` picks an engine by `model.Engine` string
  (`SupertonicMetadata.IsSupertonic3Engine(...)` else falls back to Kokoro). This is the pattern to
  mirror when adding new task types/engines.

### Model definition & config (`src/FastTTSR.Api/Models/TtsModelDefinition.cs`, `config.json`)
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
per engine (`KokoroMetadata`, `SupertonicMetadata`).

### Engines
- **Kokoro** (`KokoroTtsEngine.cs`/`KokoroTtsSynthesizer.cs`): single ONNX session via
  `Microsoft.ML.OnnxRuntime`, phonemization via `EspeakWrapper` (wraps espeak-ng native lib),
  24000 Hz output, pooled per model+directory in a `ConcurrentDictionary`.
- **Supertonic-3** (`SupertonicTtsEngine.cs`/`SupertonicTtsSynthesizer.cs`): **4 chained ONNX
  sessions** (text_encoder, duration_predictor, vector_estimator/denoiser, vocoder), iterative
  flow-matching denoising loop, 22050 Hz output, config read from `tts.json` inside the model dir.
  **This multi-session-in-one-class pattern is the template for any future ONNX-chain engine** (see
  ASR/Nemotron below).

### Config/env var surface (as of TTS-only baseline)
| Env var | Purpose | Default |
|---|---|---|
| `HTTP_PORT` / `HTTPS_PORT` | Kestrel listen ports | 8080 / (none) |
| `MODEL_CACHE_DIR` | Model download cache dir | `{TempPath}/fastttsr-cache` |
| `MODEL_CONFIG_PATH` | Path to `config.json` | `{BaseDirectory}/config.json` |
| `MODEL_IDLE_TIMEOUT_SECONDS` | In-process engine idle release timeout | 60 |
| `ESPEAK_DATA_DIR` | espeak-ng phoneme data dir (Kokoro) | — |
| `WorkerOptions__Enabled` | worker-mode vs in-process | true |
| `WorkerOptions__ExecutablePath` | TTS worker binary path | `./FastTTSR.Worker` |
| `WorkerOptions__PortRangeStart` | first gRPC port to try | 50051 |
| `WorkerOptions__IdleTimeoutSeconds` | worker self-termination timeout | 60 |
| `ASPNETCORE_ENVIRONMENT` | environment name | Production |

### Endpoints (`src/FastTTSR.Api/Program.cs`)
| Route | Method | Purpose |
|---|---|---|
| `/health` | GET | health check |
| `/api/models` | GET | list TTS models w/ languages/speakers/metadata |
| `/v1/models` | GET | OpenAI-compatible model listing |
| `/v1/audio/speech` | POST | OpenAI-compatible TTS synthesis (JSON body: model, input, voice,
  response_format, speed, language, speaker); returns audio bytes + metrics in response headers
  (`X-Processing-Time`, `X-Chars-Per-Second`, `X-RTF`, `X-Audio-Duration`, `X-Character-Count`) |
| `/swagger` | GET | interactive API docs |

Static frontend files are served from `wwwroot` (built by the frontend and copied in at Docker
build time); `MapFallbackToFile("/index.html")` handles SPA routing.

## Docker packaging
Multi-stage `Dockerfile`: `frontend-build` (node:24-alpine, pnpm build) → `backend-build` (dotnet
SDK, publishes `FastTTSR.Api` + `FastTTSR.Worker`, copies frontend dist into `wwwroot`) → final
`mcr.microsoft.com/dotnet/aspnet` runtime image (installs `libespeak-ng1`/`espeak-ng-data`, copies
published API + worker, sets `WorkerOptions__ExecutablePath=/app/worker/FastTTSR.Worker`,
`ENTRYPOINT dotnet FastTTSR.Api.dll`). `docker-compose.yml` defines a single `fastttsr` service with
port mapping, env passthrough, and `./model-cache:/cache` + `./assets:/app/assets:ro` volumes.

## Frontend (`frontend/`)
Vue 3 + TypeScript, Composition API, no UI framework/router/state library (deliberately minimal).
- `App.vue`: fetches `GET /api/models` once on mount, holds form state (`model`, `speaker`,
  `language`, `speed`, `input`, `quality`) in a `reactive()`, calls `POST /v1/audio/speech` via
  plain `fetch()`, reads metrics from response headers.
- Components: `ModelSelector` (dropdown), `LanguageSelector`/`SpeakerSelector` (clickable tag
  lists), `TextInput` (contenteditable + presets), `ParameterControls` (speed/quality sliders +
  generate button), `AudioPlayer` (playback + metrics + download), `StatusMessage` (alert banner),
  `AppHeader`.
- `types.ts`: `TtsModel`, `SpeakerMetadata`, `SynthesisRequest`, `SynthesisMetrics`, `TextPreset`,
  `StatusType`.
- `vite.config.js` proxies `/api/*` and `/v1/*` to the backend in dev. Build: `vue-tsc && vite
  build` → `dist/`, copied into the API's `wwwroot` at Docker build time.
- Capability discovery today is implicit: the app just calls `/api/models` and renders whatever
  comes back. (When ASR ships, a `GET /api/server-info` endpoint will drive which UI
  sections/tabs are shown — see ASR section once implemented.)

## Testing
- `tests/FastTTSR.Api.Tests`: unit tests (`ModelCatalogTests`, `KokoroMetadataTests`,
  `SherpaOnnxTtsSynthesizerTests`, `SpeechEndpointTests`, `TextSanitizerTests`).
- `tests/FastTTSR.Api.IntegrationTests`: full-stack tests via Docker (`SpeechSynthesisTests`,
  `ModelHealthTests`, `JapaneseConcurrencyTests`).
- Run: `dotnet test tests/FastTTSR.Api.Tests`, or `./tests/run-tests.sh all` /
  `docker compose -f docker-compose.test.yml up --abort-on-container-exit` for integration.

## Extension points

### Adding a new TTS engine
1. Add a `{Engine}TtsEngine.cs` (owns ONNX session(s), pooled per model+directory — mirror
   `KokoroTtsEngine` for single-session or `SupertonicTtsEngine` for multi-session chains) and a
   `{Engine}TtsSynthesizer.cs` implementing `ITtsSynthesizer`/`IIdleTrackingSynthesizer`.
2. Add routing for the new `Engine` string in `TtsSynthesizerRouter` and
   `Worker/Services/WorkerSynthesisService.cs`.
3. Add model entries to `config.json` `"models"` array.
4. Register the synthesizer singleton in `Program.cs` (in-process branch).

### ASR (Whisper + Nemotron) — in progress
Parallel abstraction to the TTS one, added alongside it (not replacing it):
- `Models/AsrModelDefinition.cs`: Name, DisplayName, Description, Engine (`"whisper"` |
  `"nemotron-3.5"`), ModelPath, Assets (`IReadOnlyList<ModelAsset>`, shared type with TTS),
  SupportedLanguages, SupportsLanguageAutoDetect.
- `Services/IAsrModelCatalog.cs` + `AsrModelCatalog.cs` (mirrors `ModelCatalog`): loads the
  `"asrModels"` array from the same `config.json` (same `MODEL_CONFIG_PATH` env var), falls back to
  hardcoded defaults (`whisper-base`, `nemotron-3.5`) if config is missing.
- `Services/IAsrTranscriber.cs`: `TranscribeAsync(AsrModelDefinition model, string modelDirectory,
  AudioTranscriptionRequest request, byte[] audioBytes, CancellationToken ct) ->
  Task<TranscriptionResult>` — the ASR equivalent of `ITtsSynthesizer`.
- `Services/IIdleTrackingTranscriber.cs` — ASR equivalent of `IIdleTrackingSynthesizer`.
- `Models/TranscriptionResult.cs`: Text, DetectedLanguage, ProcessingTimeSeconds,
  AudioDurationSeconds, CharacterCount, computed `Rtf`.
- `Contracts/AudioTranscriptionRequest.cs` (Model, Language?, ResponseFormat — the file itself is
  passed as a separate `byte[]`), `Contracts/AsrModelDefinitionResponse.cs`,
  `Contracts/ServerInfoResponse.cs` (`{ TtsEnabled, AsrEnabled }`, will back a future
  `GET /api/server-info` endpoint).
- `IModelCache` gained a second overload: `EnsureModelAsync(AsrModelDefinition, ct)`. `ModelCache`
  extracts a shared private `DownloadAssetsAsync(...)` helper used by both the Tts and Asr
  overloads (the Tts overload additionally does Kokoro/Supertonic voice-file downloads that don't
  apply to ASR models).
- `config.json` gained a sibling top-level `"asrModels"` array (same shape philosophy as
  `"models"`): `whisper-base` (engine `"whisper"`, single GGML asset) and `nemotron-3.5` (engine
  `"nemotron-3.5"`, encoder/decoder/joint ONNX + audio processor config + tokenizer + silero VAD,
  from `onnx-community/nemotron-3.5-asr-streaming-0.6b-onnx-int4`).

**Still to be added** (worker process, engines, endpoints, SERVER_MODE, frontend) — this section
will be filled in incrementally as those phases land; see the implementation log below for what
exists so far.

## Build tooling note
No local `dotnet` CLI in the dev container — use `./dotnet.sh <args>` (Docker-based wrapper) for
build/test/publish, e.g. `./dotnet.sh build FastTTSR.slnx`, `./dotnet.sh test tests/FastTTSR.Api.Tests`.

## Implementation log (shared/groundwork changes made ahead of ASR feature work)
- `Models/TtsModelAsset.cs` renamed to `Models/ModelAsset.cs` (generic asset descriptor shared by
  TTS and ASR model definitions).
- `Services/IdleMonitor.cs` moved from `FastTTSR.Worker/Services/` to `FastTTSR.Api/Services/` so
  both worker projects can share it via their existing `ProjectReference` to `FastTTSR.Api`.
- `WorkerProcessManager` constructor now takes a plain `WorkerOptions` value (not
  `IOptions<WorkerOptions>`), so multiple independently-configured instances (one per task type)
  can be constructed manually in `Program.cs`.
