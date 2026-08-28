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

**Still to be added** (worker process, endpoints, SERVER_MODE, frontend) — this section will be
filled in incrementally as those phases land; see the implementation log below for what exists so
far.

#### Whisper engine (Phase 2 — done)
- NuGet: `Whisper.net` + `Whisper.net.Runtime` (added to `FastTTSR.Api.csproj`; native whisper.cpp
  binaries ship inside `Whisper.net.Runtime`, no external dependency needed).
- `Services/WhisperAsrEngine.cs`: wraps one GGML model file via `WhisperFactory.FromPath(...)`;
  `TranscribeAsync(byte[] wavBytes, string? language, ct)` builds a processor
  (`.CreateBuilder().WithLanguage(language ?? "auto").Build()`), feeds the WAV bytes as a
  `MemoryStream` to `processor.ProcessAsync(...)` (an `IAsyncEnumerable<SegmentData>`), concatenates
  `segment.Text` across all segments, and picks up `segment.Language` as the detected language when
  auto-detecting.
- `Services/WhisperAsrTranscriber.cs`: implements `IAsrTranscriber` + `IIdleTrackingTranscriber`,
  pools one `WhisperAsrEngine` per `"{model.Name}:{modelDirectory}"` key in a
  `ConcurrentDictionary` (mirrors `KokoroTtsSynthesizer`'s `GetOrCreateEngine` pattern exactly).
  Rejects any model whose `Engine` isn't `"whisper"`.
- `Services/WavAudioUtils.cs`: new small shared helper — reads a RIFF/WAV header (no external audio
  library) to compute `AudioDurationSeconds` for the *input* recording (needed for ASR metrics,
  unlike TTS where duration is computed from generated PCM at a known fixed sample rate).
- Not yet wired into DI/endpoints/worker — that lands in Phase 4/5. Native runtime packaging for
  the container (linux-x64) is still an open verification item for Phase 6.

#### Nemotron engine (Phase 3 — done)
Cache-aware streaming FastConformer-RNNT via raw `Microsoft.ML.OnnxRuntime` (3 chained sessions),
mirrors `SupertonicTtsEngine`'s multi-session-in-one-class pattern. Exact tensor contract was
recovered via a throwaway C# spike (`InferenceSession.InputMetadata`/`OutputMetadata`, run with
`./dotnet.sh run` against the real ONNX graphs) — full details and caveats are in
[docs/ASR_IMPLEMENTATION_PLAN.md](ASR_IMPLEMENTATION_PLAN.md)'s Phase 3 section, summarized here:
- **Encoder** (24 layers, hidden=1024): consumes fixed 65-frame log-mel chunks (128 mels; 56 new
  frames + 9 cached lookback frames), cache-aware via `cache_last_channel`/`cache_last_time`/
  `cache_last_channel_len` tensors threaded chunk-to-chunk, conditioned by a `lang_id` token that
  is itself just a vocab index (see below). Emits 7 encoded frames/chunk.
- **Decoder/predictor** (`Services/NemotronAsrEngine.cs`'s `RunDecoderStep`): LSTM-based (2 layers,
  hidden=640), not attention-based. Its raw output is (batch, hidden, seq) and must be transposed
  before the joint step.
- **Joint** (`RunJoint`): combines one encoder frame + one decoder step, argmax over 13088 vocab
  entries; RNNT greedy decode loop lives in `RunRnntGreedyDecode` (up to `max_symbols_per_step`
  emissions per encoder frame, stops on `blank_id`).
- `Services/NemotronVocabulary.cs`: `vocab.txt` (id-per-line) doubles as both the token→text
  decoder (SentencePiece `▁` convention) and the language-tag→`lang_id` lookup (e.g. `<en-US>`) —
  no separate tokenizer file needed.
- `Services/NemotronFeatureExtractor.cs`: self-contained log-mel spectrogram (own radix-2 FFT +
  triangular mel filterbank + Hann window), no external DSP library.
- `Services/WavAudioUtils.cs` gained `ReadMonoFloat(wavBytes, targetSampleRate)` (PCM16 decode +
  linear-interpolation resample to 16kHz mono) alongside the existing duration helper.
- `config.json`'s `nemotron-3.5` entry also downloads `genai_config.json` now (read at runtime for
  `blank_id`/`max_symbols_per_step`, NOT for `Microsoft.ML.OnnxRuntimeGenAI` — that library is
  intentionally not used).
- **Open risk**: mel-scale formula (HTK vs Slaney) and frame-centering/dither details aren't fully
  pinned down by config alone. A real-weights smoke test (throwaway harness, real
  encoder/decoder/joint weights, synthetic sine-tone WAV input) ran the full pipeline **end-to-end
  successfully with no exceptions** (multi-chunk encoder loop, RNNT greedy decode, vocab decode all
  executed correctly) — this validates shapes/wiring, but **transcription accuracy is still
  unverified** since there was no real speech + ground-truth transcript available to check against.
- Not yet wired into DI/endpoints/worker — Phase 4/5.

#### Worker process, DI wiring & SERVER_MODE (Phase 4 — done)
- **`SERVER_MODE`** env var (`tts` (default) | `asr` | `both`) parsed at the top of `Program.cs`
  into `ttsEnabled`/`asrEnabled` booleans. The entire pre-existing TTS registration block is now
  wrapped in `if (ttsEnabled)` **with no internal changes** — behavior with `SERVER_MODE` unset is
  byte-identical to before ASR existed (verified: all 43 pre-existing tests still pass). A parallel
  `if (asrEnabled)` block mirrors it for ASR.
- **Two independent worker types** can now run simultaneously: TTS's `WorkerProcessManager` stays
  registered as a plain (non-keyed) singleton exactly as before; ASR's is registered as a
  **keyed singleton** (`AddKeyedSingleton<WorkerProcessManager>("asr", ...)`) with its own
  `WorkerOptions` (mapped from `AsrWorkerOptions`, `Options/AsrWorkerOptions.cs`) so it uses a
  distinct port range (`50151+` vs TTS's `50051+`) and executable path
  (`./worker-asr/FastTTSR.Worker.Asr`) — no changes needed to `WorkerProcessManager` itself beyond
  the Phase 0 constructor generalization.
- **`FastTTSR.Worker.Asr`** (new project, mirrors `FastTTSR.Worker` exactly): same
  `--port/--model-key/--idle-timeout` CLI args, same `app.RunAsync()` + 500ms delay + `READY:{port}`
  stdout signal Kestrel-HTTP/2 startup pattern. `Services/WorkerTranscriptionService.cs` mirrors
  `WorkerSynthesisService`'s single-active-engine-with-lock pattern (only one of
  `WhisperAsrEngine`/`NemotronAsrEngine` loaded at a time, swapped on model change), routing by
  `request.Engine`.
- **`Protos/transcription.proto`** (new, own `csharp_namespace = "FastTTSR.Worker.Asr.Grpc"` to
  avoid clashing with synthesis.proto's `FastTTSR.Worker.Grpc`): `WorkerTranscription` service with
  `Transcribe`/`HealthCheck` RPCs. `TranscribeRequest.model_path` is computed identically to TTS's
  `SynthesizeRequest.model_path` (`Path.Combine(modelDirectory, model.ModelPath)`) — this
  transparently yields a file path for Whisper (`model.ModelPath` = ggml filename) or a directory
  path for Nemotron (`model.ModelPath` = `""`), matching what each engine constructor expects.
- **`Services/AsrWorkerProxyTranscriber.cs`** (mirrors `WorkerProxySynthesizer`): implements
  `IAsrTranscriber`, resolves its `WorkerProcessManager` via `[FromKeyedServices("asr")]`
  constructor injection.
- **`Services/AsrTranscriberRouter.cs`** (in-process/non-worker mode, mirrors
  `TtsSynthesizerRouter`): routes by `model.Engine == "nemotron-3.5"` else defaults to Whisper.
- **`Services/AsrModelWarmupService.cs`** / **`Services/AsrModelIdleMonitorService.cs`** mirror
  `ModelWarmupService`/`ModelIdleMonitorService`. The idle monitor intentionally reuses the same
  `ModelIdleMonitorOptions`/`MODEL_IDLE_TIMEOUT_SECONDS` config as TTS rather than introducing a
  new env var.
- Not yet exposed via HTTP — no `/v1/audio/transcriptions` endpoint exists yet, so
  `IAsrTranscriber`/`IAsrModelCatalog` are wired into DI but unreachable until Phase 5.

#### REST endpoints (Phase 5 — done)
- `GET /api/server-info` (always mapped) → `{ ttsEnabled, asrEnabled }`, reflects `SERVER_MODE`.
- `GET /api/asr-models` (mapped only if `asrEnabled`) → list of `AsrModelDefinitionResponse`.
- `POST /v1/audio/transcriptions` (mapped only if `asrEnabled`, OpenAI-compatible name/shape):
  multipart/form-data (`file`, `model`, `language?`, `response_format?`), validates the model via
  `IAsrModelCatalog`, resolves the model directory via `IModelCache.EnsureModelAsync`, calls
  `IAsrTranscriber.TranscribeAsync`, returns `{ text }` JSON with metrics in response headers
  (`X-Processing-Time`, `X-Audio-Duration`, `X-RTF`, `X-Character-Count`) mirroring the
  `/v1/audio/speech` pattern.
- `GET /v1/models` was changed to merge `IModelCatalog` and `IAsrModelCatalog` results via
  `httpContext.RequestServices.GetService<T>()` (gracefully returns null instead of throwing if a
  catalog isn't registered for the current `SERVER_MODE`) rather than requiring both as direct DI
  parameters.
- `/api/models` and `/v1/audio/speech` are now wrapped in `if (ttsEnabled)` with **zero internal
  changes** - verified byte-identical via the full existing test suite.
- **Verified live** (in-process mode smoke test, `SERVER_MODE=both`): `/api/server-info`,
  `/api/asr-models`, and `/v1/models` all return correct results. `/v1/audio/transcriptions`
  routing/validation/model-resolution all execute correctly, but the actual Whisper transcription
  call currently fails - see the open native-library issue below (this is an environment/packaging
  issue, not an endpoint bug).

### Open issue: Whisper.net native library fails to load (carried to Phase 6)
Confirmed via a live test on `mcr.microsoft.com/dotnet/sdk:10.0-preview`: `WhisperFactory.FromPath`
throws `"Failed to load native whisper library... PInvokeError: Success"` even though
`runtimes/linux-x64/libwhisper.so` (and siblings) are present in both `dotnet build` and
`dotnet publish -r linux-x64` output. One confirmed contributing cause: `libggml-cpu-whisper.so`
depends on `libgomp.so.1` (GNU OpenMP), which is not installed in the base image (same category of
issue as Kokoro needing `apt-get install libespeak-ng1`) - but installing `libgomp1` alone did
**not** fully resolve the failure; a live re-test after installing it still failed identically.
**Phase 6 must**: add `libgomp1`/`libstdc++6` to the Dockerfile, and further investigate the
remaining native-load failure (candidates: explicit `LD_LIBRARY_PATH` pointing at the app's
`runtimes/linux-x64` folder, a self-contained `-r linux-x64` publish, or checking whether a newer/
split Whisper.net.Runtime package is needed for desktop Linux) before considering Whisper ASR
production-ready.

### Updated env var table (as of Phase 4)
| Env var | Purpose | Default |
|---|---|---|
| `SERVER_MODE` | `tts`\|`asr`\|`both` — which task type(s) this instance serves | `tts` |
| `AsrWorkerOptions__Enabled` | ASR worker-mode vs in-process | true |
| `AsrWorkerOptions__ExecutablePath` | ASR worker binary path | `./worker-asr/FastTTSR.Worker.Asr` |
| `AsrWorkerOptions__PortRangeStart` | first ASR gRPC port to try | 50151 |
| `AsrWorkerOptions__IdleTimeoutSeconds` | ASR worker self-termination timeout | 60 |
| (all pre-existing TTS env vars, unchanged — see table above) | | |

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
