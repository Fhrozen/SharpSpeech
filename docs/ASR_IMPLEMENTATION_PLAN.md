# ASR Implementation Plan (Whisper + Nemotron)

> **Purpose of this file**: a git-tracked, phase-by-phase implementation plan for adding ASR
> (Speech-to-Text) support to FastTTSR alongside the existing TTS feature. It exists so that work
> can be resumed by a different agent/session/person without losing context. If you are picking
> this up fresh, read [docs/LLM_WIKI.md](LLM_WIKI.md) first for architecture context, then come
> back here to see what's done and what's next.

## How to use this document

1. Read [docs/LLM_WIKI.md](LLM_WIKI.md) for the current architecture (updated after every phase).
2. Find the first phase below whose Status is not `✅ Accepted`.
3. Do the work for that phase only. Don't jump ahead.
4. Build via `./dotnet.sh build FastTTSR.slnx` (no local `dotnet` CLI in this dev environment).
5. Update [docs/LLM_WIKI.md](LLM_WIKI.md)'s relevant section with what you built.
6. Update this file: set the phase's Status to `✅ Done (awaiting acceptance)` and fill in
   "Actual output files" with the real paths you touched.
7. **Stop and wait for the user to explicitly accept the phase** before starting the next one.
   Once accepted, flip Status to `✅ Accepted`.

## Decisions locked in with the user (do not re-litigate without asking)

- **Whisper engine**: [Whisper.net](https://github.com/sandrohanea/whisper.net) (whisper.cpp/GGML
  models) — minimal dependencies, chosen over `Microsoft.ML.OnnxRuntimeGenAI`.
- **Nemotron engine**: raw `Microsoft.ML.OnnxRuntime`, 3 chained sessions
  (`encoder.onnx`/`decoder.onnx`/`joint.onnx`), mirroring the existing `SupertonicTtsEngine`
  multi-session pattern — **not** `Microsoft.ML.OnnxRuntimeGenAI`, even though the model's own
  README suggests it. Model:
  https://huggingface.co/onnx-community/nemotron-3.5-asr-streaming-0.6b-onnx-int4
  Ships: `encoder.onnx(+.data)`, `decoder.onnx(+.data)`, `joint.onnx(+.data)`,
  `audio_processor_config.json`, `genai_config.json` (**not used**), `model_config.json`,
  `tokenizer.json`, `vocab.txt`, `silero_vad.onnx` (optional VAD). RNNT cache-aware streaming
  architecture (560ms chunk export).
  **Known risk**: exact cache tensor names/shapes for the encoder are not knowable ahead of time —
  they must be inspected from the ONNX graph itself (`InferenceSession.InputMetadata`/
  `OutputMetadata`) during Phase 3's spike step. User explicitly accepted this "spike + iterate"
  risk.
- **Worker granularity**: ONE ASR worker executable (`FastTTSR.Worker.Asr`) that internally routes
  Whisper vs Nemotron by `model.Engine`, exactly mirroring how the existing TTS worker
  (`FastTTSR.Worker`) routes Kokoro vs Supertonic. Two worker executables total.
- **Docker packaging**: single `Dockerfile`, build `ARG SERVER_MODE` (`tts`|`asr`|`all`, default
  `all`) selects which worker(s) are copied into 3 named final stages
  (`runtime-tts`/`runtime-asr`/`runtime-all`), final `FROM runtime-${SERVER_MODE}`. Runtime env var
  `SERVER_MODE` (`tts`|`asr`|`both`, default `"tts"` for backward compatibility) controls which
  endpoints/services are active within the built image.
- **Phasing**: implement both Whisper AND Nemotron fully (not stubbed).
- **Process**: implement phase-by-phase, update `docs/LLM_WIKI.md` after each phase, wait for
  explicit user acceptance before starting the next phase (this replaces the earlier "implement
  everything in one pass" approach).

## Status tracker

| Phase | Name | Status |
|---|---|---|
| -1 | LLM_WIKI bootstrap | ✅ Accepted |
| 0 | Shared groundwork | ✅ Accepted |
| 1 | ASR domain model & config plumbing | ✅ Accepted |
| 2 | Whisper engine | ✅ Accepted |
| 3 | Nemotron engine (spike + implementation) | ✅ Accepted |
| 4 | ASR worker process + DI wiring | ✅ Accepted |
| 5 | REST endpoints | ✅ Accepted |
| 6 | Docker/Compose packaging | ✅ Accepted |
| 7 | Frontend ASR UI | ✅ Done (awaiting acceptance) |
| 8 | Tests | 🚧 In progress (circular tests done) |
| 9 | Documentation update (final) | Not started |

---

## Phase -1 — LLM_WIKI bootstrap
**Status**: ✅ Accepted

**Inputs**: none (first phase).

**Goal**: create an agent-optimized architecture reference so future sessions don't have to
re-discover the codebase from scratch.

**Changes**: created `docs/LLM_WIKI.md` seeded with the pre-ASR architecture (Program.cs/DI wiring,
worker process lifecycle, gRPC contract, core abstractions, model definitions/config.json schema,
env var table, endpoints, Docker packaging, frontend structure, testing, extension points), plus a
"Keeping this doc updated" maintenance rule section.

**Actual output files**:
- `docs/LLM_WIKI.md` (created)

---

## Phase 0 — Shared groundwork
**Status**: ✅ Accepted

**Inputs**: Phase -1's LLM_WIKI (for reference only, not a code dependency).

**Goal**: make small, low-risk refactors to existing TTS code so ASR can reuse it without
duplication, before any ASR-specific code exists.

**Changes**:
1. Renamed `Models/TtsModelAsset.cs` → `Models/ModelAsset.cs` (generic asset descriptor: `Url`,
   `RelativePath`), shared by both TTS and ASR model definitions. Updated all references in
   `TtsModelDefinition.cs` and `ModelCatalog.cs`.
2. Moved `IdleMonitor` from `FastTTSR.Worker/Services/IdleMonitor.cs` to
   `FastTTSR.Api/Services/IdleMonitor.cs` so both worker projects can share it via their existing
   `ProjectReference` to `FastTTSR.Api`. Updated `using` in `FastTTSR.Worker/Program.cs`.
3. Generalized `WorkerProcessManager`'s constructor to accept a plain `WorkerOptions` value
   (instead of `IOptions<WorkerOptions>`), so multiple independently-configured instances (one per
   task type) can be constructed manually in `Program.cs`. Updated the registration in
   `Program.cs` to resolve `IOptions<WorkerOptions>` and pass `.Value` explicitly.

**Actual output files**:
- `src/FastTTSR.Api/Models/ModelAsset.cs` (created, replaces `TtsModelAsset.cs`)
- `src/FastTTSR.Api/Models/TtsModelDefinition.cs` (modified: `ModelAsset` type)
- `src/FastTTSR.Api/Services/ModelCatalog.cs` (modified: `ModelAsset` type)
- `src/FastTTSR.Api/Services/IdleMonitor.cs` (created, moved from Worker project)
- `src/FastTTSR.Worker/Program.cs` (modified: `using FastTTSR.Api.Services;`)
- `src/FastTTSR.Api/Services/WorkerProcessManager.cs` (modified constructor)
- `src/FastTTSR.Api/Program.cs` (modified: `WorkerProcessManager` registration)

**Verification**: `./dotnet.sh build FastTTSR.slnx` → 0 errors.

---

## Phase 1 — ASR domain model & config plumbing
**Status**: ✅ Accepted

**Inputs**: Phase 0's `ModelAsset` type and generalized `IModelCache` seam.

**Goal**: define the ASR-side domain model, catalog, and contracts — the parallel of
`TtsModelDefinition`/`IModelCatalog`/`ITtsSynthesizer` for ASR — without yet implementing any
engine or wiring anything into `Program.cs`.

**Changes**:
1. `Models/AsrModelDefinition.cs`: `Name`, `DisplayName`, `Description`, `Engine`
   (`"whisper"`|`"nemotron-3.5"`), `ModelPath`, `Assets` (`IReadOnlyList<ModelAsset>`),
   `SupportedLanguages`, `SupportsLanguageAutoDetect`.
2. `Services/IAsrModelCatalog.cs` + `Services/AsrModelCatalog.cs` (mirrors `ModelCatalog`): loads a
   new `"asrModels"` array from the same `config.json` (same `MODEL_CONFIG_PATH` env var), falls
   back to hardcoded defaults (`whisper-base`, `nemotron-3.5`) if config is missing/empty.
3. `config.json`: added a sibling top-level `"asrModels"` array with 2 entries — `whisper-base`
   (engine `"whisper"`, single GGML asset from `ggerganov/whisper.cpp`) and `nemotron-3.5` (engine
   `"nemotron-3.5"`, 11 assets from `onnx-community/nemotron-3.5-asr-streaming-0.6b-onnx-int4`).
4. `Contracts/AudioTranscriptionRequest.cs` (`Model`, `Language?`, `ResponseFormat` — the audio
   file itself is passed separately as a `byte[]`), `Contracts/AsrModelDefinitionResponse.cs`
   (mirrors `ModelDefinitionResponse`), `Contracts/ServerInfoResponse.cs`
   (`{ TtsEnabled, AsrEnabled }`, will back a future `GET /api/server-info` endpoint).
5. `Models/TranscriptionResult.cs`: `Text`, `DetectedLanguage`, `ProcessingTimeSeconds`,
   `AudioDurationSeconds`, `CharacterCount`, computed `Rtf`.
6. `Services/IAsrTranscriber.cs` (mirrors `ITtsSynthesizer`):
   `TranscribeAsync(AsrModelDefinition model, string modelDirectory, AudioTranscriptionRequest
   request, byte[] audioBytes, CancellationToken ct) -> Task<TranscriptionResult>`.
   `Services/IIdleTrackingTranscriber.cs` mirrors `IIdleTrackingSynthesizer` exactly.
7. `IModelCache`/`ModelCache` gained a second overload: `EnsureModelAsync(AsrModelDefinition, ct)`.
   Extracted a shared private `DownloadAssetsAsync(...)` helper used by both the Tts and Asr
   overloads (the Tts overload additionally does Kokoro/Supertonic voice-file downloads that don't
   apply to ASR models — kept as TTS-only logic, not generalized).

**Actual output files**:
- `src/FastTTSR.Api/Models/AsrModelDefinition.cs` (created)
- `src/FastTTSR.Api/Models/TranscriptionResult.cs` (created)
- `src/FastTTSR.Api/Services/IAsrModelCatalog.cs` (created)
- `src/FastTTSR.Api/Services/AsrModelCatalog.cs` (created)
- `src/FastTTSR.Api/Services/IAsrTranscriber.cs` (created)
- `src/FastTTSR.Api/Services/IIdleTrackingTranscriber.cs` (created)
- `src/FastTTSR.Api/Contracts/AudioTranscriptionRequest.cs` (created)
- `src/FastTTSR.Api/Contracts/AsrModelDefinitionResponse.cs` (created)
- `src/FastTTSR.Api/Contracts/ServerInfoResponse.cs` (created)
- `src/FastTTSR.Api/config.json` (modified: added `asrModels` array)
- `src/FastTTSR.Api/Services/IModelCache.cs` (modified: new overload)
- `src/FastTTSR.Api/Services/ModelCache.cs` (modified: new overload + shared helper)
- `tests/FastTTSR.Api.Tests/SpeechEndpointTests.cs` (modified: `StubModelCache` implements new
  interface member)

**Verification**: `./dotnet.sh build FastTTSR.slnx` → 0 errors.

---

## Phase 2 — Whisper engine
**Status**: ✅ Accepted

**Inputs**: Phase 1's `AsrModelDefinition`, `IAsrTranscriber`, `IIdleTrackingTranscriber`,
`AudioTranscriptionRequest`, `TranscriptionResult`.

**Goal**: a fully working Whisper-based ASR engine, not yet wired into DI/endpoints/worker (that's
Phase 4/5).

**Changes**:
1. Added `Whisper.net` + `Whisper.net.Runtime` (v1.8.1) NuGet packages to
   `FastTTSR.Api.csproj` (engine code lives in `Api/Services`, same convention as Kokoro/Supertonic,
   reused later by the worker project via its `ProjectReference`).
2. `Services/WhisperAsrEngine.cs`: wraps one GGML model file via `WhisperFactory.FromPath(...)`.
   `TranscribeAsync(byte[] wavBytes, string? language, ct)` builds a processor
   (`.CreateBuilder().WithLanguage(language ?? "auto").Build()`), feeds the WAV bytes as a
   `MemoryStream` to `processor.ProcessAsync(...)` (`IAsyncEnumerable<SegmentData>`), concatenates
   `segment.Text` across all segments, and uses `segment.Language` as the detected language when
   auto-detecting.
3. `Services/WhisperAsrTranscriber.cs`: implements `IAsrTranscriber` + `IIdleTrackingTranscriber`,
   pools one `WhisperAsrEngine` per `"{model.Name}:{modelDirectory}"` key in a
   `ConcurrentDictionary` (mirrors `KokoroTtsSynthesizer.GetOrCreateEngine` exactly). Rejects any
   model whose `Engine` isn't `"whisper"`.
4. `Services/WavAudioUtils.cs`: new small shared helper — reads a RIFF/WAV header (no external
   audio library) to compute `AudioDurationSeconds` for the *input* recording (ASR needs this for
   metrics, unlike TTS where duration is computed from generated PCM at a known fixed sample rate).

**Actual output files**:
- `src/FastTTSR.Api/FastTTSR.Api.csproj` (modified: 2 new package refs)
- `src/FastTTSR.Api/Services/WhisperAsrEngine.cs` (created)
- `src/FastTTSR.Api/Services/WhisperAsrTranscriber.cs` (created)
- `src/FastTTSR.Api/Services/WavAudioUtils.cs` (created)

**Verification**: `./dotnet.sh build FastTTSR.slnx` → 0 errors.

**Open item carried forward to Phase 6**: verify `Whisper.net.Runtime`'s native binaries are
correctly included for linux-x64 when `FastTTSR.Worker.Asr` is published inside the Docker image.
**Update from Phase 5 diagnostics**: confirmed via a live smoke test that `WhisperFactory.FromPath`
throws `"Failed to load native whisper library... PInvokeError: Success"` on
`mcr.microsoft.com/dotnet/sdk:10.0-preview` even though `runtimes/linux-x64/libwhisper.so` (and its
sibling `libggml-*.so` files) ARE present in the build/publish output (confirmed present both for a
plain `dotnet build` and an explicit `dotnet publish -r linux-x64`). Root-caused one contributing
factor: `libggml-cpu-whisper.so` depends on `libgomp.so.1` (GNU OpenMP), which is **not** installed
in the base image (parallel to why the Dockerfile already needs `apt-get install libespeak-ng1` for
Kokoro) - installing `libgomp1` was confirmed via `ldd` to resolve that specific missing dependency.
However, **installing `libgomp1` alone did not fix the load failure** - the same generic error
persisted after a live re-test, meaning there is at least one more unresolved issue (most likely:
Whisper.net's internal native-library probing/RID-matching logic not locating the sibling `.so`
files at runtime the way `ldd`+`LD_LIBRARY_PATH` did in manual testing). **Phase 6 must**: (a) add
`libgomp1` (and likely `libstdc++6`, usually already present) to the Dockerfile's `apt-get install`
line for any image variant that includes `FastTTSR.Worker.Asr`; (b) further investigate/resolve the
remaining native-load failure - candidates to try: setting `LD_LIBRARY_PATH` to the app's
`runtimes/linux-x64` folder via a Dockerfile `ENV`, using a self-contained publish with explicit
`-r linux-x64`, or checking for a newer/different Whisper.net.Runtime package split (some versions
split desktop-Linux support into a separate `Whisper.net.Runtime.Linux` package) - and confirm with
a real transcription request before considering ASR Whisper support production-ready.

---

## Phase 3 — Nemotron engine (spike + implementation)
**Status**: ✅ Accepted

**Inputs**: Phase 1's `AsrModelDefinition`/`IAsrTranscriber`/`IIdleTrackingTranscriber`; the
`nemotron-3.5` asset set downloaded via `IModelCache.EnsureModelAsync(AsrModelDefinition, ct)`
(Phase 1); `SupertonicTtsEngine.cs` as the multi-session pattern to mirror.

**Goal**: a fully working Nemotron-based ASR engine.

**Spike findings (confirmed via a throwaway C# console app using
`Microsoft.ML.OnnxRuntime.InferenceSession.InputMetadata`/`OutputMetadata`, run through
`./dotnet.sh run`; the app and downloaded model files were deleted afterward, not committed — only
the small `.onnx` graph files plus real `decoder.onnx.data`/`joint.onnx.data` and a sparse
zero-filled placeholder for the 690MB `encoder.onnx.data` were needed, since ONNX Runtime requires
*a* file to be present at the external-data path but does not validate its contents just to report
graph metadata):**

- **Encoder** (`encoder.onnx`) — cache-aware streaming FastConformer, 24 layers, hidden=1024:
  - Inputs: `audio_signal` float32 `[1,65,128]` (batch, frames, mel_bins — one chunk of log-mel
    features), `length` int64 `[1]`, `cache_last_channel` float32 `[1,24,56,1024]`,
    `cache_last_time` float32 `[1,24,1024,8]`, `cache_last_channel_len` int64 `[1]`, `lang_id`
    int64 `[1]`.
  - Outputs: `outputs` float32 `[1,7,1024]` (7 encoded frames per 65-frame input chunk — matches
    `subsampling_factor=8`: 56 new frames/8≈7), `encoded_lengths` int64 `[1]`,
    `cache_last_channel_next`/`cache_last_time_next`/`cache_last_channel_len_next` (same shapes as
    the `cache_*` inputs — fed back in as next chunk's cache state).
  - `65 = 56 + 9`: 56 new hop-windows per chunk (`chunk_samples=8960` / `hop_length=160`) plus
    `pre_encode_cache_size=9` frames of look-back carried from the previous chunk.
- **Decoder/predictor** (`decoder.onnx`) — **LSTM-based**, not attention-based, 2 layers,
  hidden=640:
  - Inputs: `targets` int64 `[-1,-1]` (batch, previously-emitted tokens), `h_in`/`c_in` float32
    `[2,-1,640]`.
  - Outputs: `decoder_output` float32 `[-1,640,-1]` — **note the (batch, hidden, seq) channel-first
    layout**, `h_out`/`c_out` float32 `[2,-1,640]`.
- **Joint** (`joint.onnx`):
  - Inputs: `encoder_output` float32 `[-1,-1,1024]` (batch, T, hidden — matches encoder `outputs`
    directly, no transpose needed), `decoder_output` float32 `[-1,-1,640]` (batch, U, hidden —
    **decoder.onnx's raw output must be transposed from (B,640,U) to (B,U,640) before feeding
    here**).
  - Output: `joint_output` float32 `[-1,-1,-1,13088]` (batch, T, U, vocab).
- `vocab.txt` (already an asset) is a plain id→piece list, one UTF-8 piece per line, 13088 lines
  (0-indexed, matches `vocab_size`/`blank_id=13087` exactly — last line is literally `<blank>`).
  Pieces use SentencePiece `▁` (U+2581) word-boundary prefix convention. It also contains language
  tag tokens (e.g. `<bg-BG>`) — these double as the `lang_id` encoder input (looked up by scanning
  vocab for a `<xx-XX>`-shaped line matching the requested language), so **no separate language-id
  table or `tokenizer.json` parsing is needed** — `vocab.txt` alone is sufficient for both token
  decoding and language conditioning.
- Confirmed from `genai_config.json` (already read, not re-derived): `blank_id=13087`,
  `max_symbols_per_step=10`, `chunk_samples=8960` (560ms @16kHz), and the mel params mirrored in
  `audio_processor_config.json` (`n_mels=128`, `fft_size=512`, `hop_length=160`, `win_length=400`,
  `preemph=0.97`, `sample_rate=16000`).
- **Known accuracy caveat (unverified without real end-to-end audio testing)**: the mel-scale
  formula (HTK vs Slaney), frame-centering/padding mode, and `dither` are not fully pinned down by
  the config alone — implemented with reasonable standard defaults (HTK mel scale, zero-pad
  centering, dither skipped). May need tuning once real transcription output can be checked against
  ground truth.

**Planned changes (revised after spike)**:
1. `Services/NemotronVocabulary.cs`: loads `vocab.txt` (id→piece list), decodes token id sequences
   to text (SentencePiece `▁`→space convention, skips `<...>` control/language tags), and resolves
   a language code to its `lang_id` vocab index by scanning for a matching `<xx-XX>` tag.
2. `Services/NemotronFeatureExtractor.cs`: log-mel spectrogram (FFT + Hann window + mel filterbank)
   per `audio_processor_config.json` params — no external DSP library.
3. `Services/NemotronAsrEngine.cs`: owns 3 `InferenceSession`s (encoder/decoder/joint), mirrors
   `SupertonicTtsEngine`'s multi-session-in-one-class pattern. Implements the chunked cache-aware
   encoder loop (65-frame windows, cache state threaded between chunks) and the RNNT greedy decode
   loop (LSTM predictor + joint combiner, up to `max_symbols_per_step` emissions per encoder frame,
   stopping on `blank_id`), using the two helpers above.
4. `Services/NemotronAsrTranscriber.cs`: implements `IAsrTranscriber` + `IIdleTrackingTranscriber`,
   pools `NemotronAsrEngine` per model+directory (same pattern as `WhisperAsrTranscriber`).

**Actual output files**:
- `src/FastTTSR.Api/Services/NemotronVocabulary.cs` (created)
- `src/FastTTSR.Api/Services/NemotronFeatureExtractor.cs` (created)
- `src/FastTTSR.Api/Services/NemotronAsrEngine.cs` (created)
- `src/FastTTSR.Api/Services/NemotronAsrTranscriber.cs` (created)
- `src/FastTTSR.Api/Services/WavAudioUtils.cs` (modified: added `ReadMonoFloat`/resampling, and
  `TryReadHeader` now also returns the data chunk offset)
- `src/FastTTSR.Api/config.json` (modified: added `genai_config.json` as a `nemotron-3.5` asset)
- `src/FastTTSR.Api/Services/AsrModelCatalog.cs` (modified: same, in the hardcoded defaults)

**Verification**: `./dotnet.sh build FastTTSR.slnx` → 0 errors. The spike (encoder/decoder/joint
graph metadata) was validated by actually loading the real ONNX graphs via a throwaway C# console
app (`./dotnet.sh run`), not just read from docs — see spike findings above. **Real-weights smoke
test** (also via a throwaway C# console app, `ProjectReference` to `FastTTSR.Api.csproj`, deleted
afterward): downloaded the full real `encoder.onnx.data`/`decoder.onnx.data`/`joint.onnx.data`
(~770MB total), constructed a synthetic 3-second 16kHz mono sine-tone WAV, and called
`NemotronAsrEngine.Transcribe` directly. **Result: SUCCESS in 2.3s** — the full pipeline (multi-chunk
cache-aware encoder loop, RNNT greedy decode, vocabulary decode, language-id resolution) ran to
completion with real weights with no exceptions and produced plausible-shaped (if nonsensical,
since the input wasn't real speech) output text. This confirms the tensor shapes/session wiring are
structurally correct end-to-end. **Still unverified**: transcription *accuracy* — that requires
real speech audio + a known ground-truth transcript to check against, which wasn't available in
this environment; the mel-scale/framing caveat above still stands.

**Fallback if raw-ORT approach proves infeasible mid-spike**: `Microsoft.ML.OnnxRuntimeGenAI` using
the model's provided `genai_config.json` (adds one dependency scoped to `Worker.Asr`/`Api`) — only
if agreed with the user, since it contradicts the locked-in decision above.

---

## Phase 4 — ASR worker process + DI wiring
**Status**: ✅ Accepted

**Inputs**: Phase 2 (`WhisperAsrTranscriber`) and Phase 3 (`NemotronAsrTranscriber`) both
implementing `IAsrTranscriber`/`IIdleTrackingTranscriber`.

**Goal**: a second worker executable (`FastTTSR.Worker.Asr`) that hosts both ASR engines behind
gRPC, plus `SERVER_MODE`-aware DI wiring in `Program.cs` so TTS/ASR/both can be toggled at runtime.

**Planned changes**:
1. `Protos/transcription.proto`: `service WorkerTranscription { rpc Transcribe(...); rpc
   HealthCheck(...); }`; messages `TranscribeRequest` (model_name, engine, model_path, audio_bytes,
   audio_format, language) / `TranscribeResponse` (text, language_detected,
   processing_time_seconds, audio_duration_seconds).
2. New project `src/FastTTSR.Worker.Asr/FastTTSR.Worker.Asr.csproj` (mirrors
   `FastTTSR.Worker.csproj`): `ProjectReference` to `FastTTSR.Api.csproj`, `Protobuf` include of
   `transcription.proto` (`GrpcServices="Server"`), `Grpc.AspNetCore`.
   - `Program.cs`: same `--port/--model-key/--idle-timeout` arg parsing + Kestrel HTTP/2 as
     `Worker/Program.cs`.
   - `Services/WorkerTranscriptionService.cs` (mirrors `WorkerSynthesisService`): routes by
     `request.Engine` (`"whisper"` vs `"nemotron-3.5"`) to `WhisperAsrEngine`/`NemotronAsrEngine`,
     lazy-loads, idle-tracked via the shared `IdleMonitor` (Phase 0).
3. Add the new project to `FastTTSR.slnx`.
4. `Options/AsrWorkerOptions.cs` (mirrors `WorkerOptions`): section `"AsrWorkerOptions"`,
   `ExecutablePath` default `"./worker-asr/FastTTSR.Worker.Asr"`, `PortRangeStart` default `50151`
   (distinct range from TTS's `50051+`), `IdleTimeoutSeconds`, `MaxPortAttempts`,
   `StartupTimeoutSeconds`, `Enabled`.
5. `Services/AsrWorkerProxyTranscriber.cs` (mirrors `WorkerProxySynthesizer`): implements
   `IAsrTranscriber`, talks gRPC to the ASR `WorkerProcessManager`'s spawned process.
6. `Program.cs` changes:
   - Parse `SERVER_MODE` env var (`tts`|`asr`|`both`, default `"tts"`) into two booleans
     (`ttsEnabled`, `asrEnabled`).
   - Register `IAsrModelCatalog`/`AsrModelCatalog` when `asrEnabled`.
   - Use keyed DI (`AddKeyedSingleton<WorkerProcessManager>("tts"/"asr", ...)`) so two
     independently-configured `WorkerProcessManager` instances can coexist.
   - In-process (non-worker) ASR mode: register `WhisperAsrTranscriber` + `NemotronAsrTranscriber`
     singletons + an `AsrTranscriberRouter` (mirrors `TtsSynthesizerRouter`) as `IAsrTranscriber`.
   - Idle monitoring: extend/parallel `ModelIdleMonitorService` to also cover
     `IIdleTrackingTranscriber` instances when running in-process.
   - Gate TTS's existing registrations behind `ttsEnabled` (must remain byte-identical behavior
     when `SERVER_MODE` is unset/`"tts"`).

**Expected output files**:
- `src/FastTTSR.Api/Protos/transcription.proto` (new)
- `src/FastTTSR.Worker.Asr/FastTTSR.Worker.Asr.csproj` (new)
- `src/FastTTSR.Worker.Asr/Program.cs` (new)
- `src/FastTTSR.Worker.Asr/Services/WorkerTranscriptionService.cs` (new)
- `src/FastTTSR.Api/Options/AsrWorkerOptions.cs` (new)
- `src/FastTTSR.Api/Services/AsrWorkerProxyTranscriber.cs` (new)
- `src/FastTTSR.Api/Services/AsrTranscriberRouter.cs` (new, in-process mode)
- `src/FastTTSR.Api/Program.cs` (modified: SERVER_MODE parsing, keyed DI, ASR registrations)
- `FastTTSR.slnx` (modified: add project)

**Actual output files** (matches expected, plus 2 warmup/idle-monitor services not explicitly
called out in the original plan but needed to mirror the TTS side completely):
- `src/FastTTSR.Api/Protos/transcription.proto` (created) - own `csharp_namespace`
  (`FastTTSR.Worker.Asr.Grpc`, distinct from synthesis.proto's `FastTTSR.Worker.Grpc`) to avoid
  type-name collisions.
- `src/FastTTSR.Api/FastTTSR.Api.csproj` (modified: added `Protobuf Include="Protos/transcription.proto" GrpcServices="Client"`)
- `src/FastTTSR.Worker.Asr/FastTTSR.Worker.Asr.csproj` (created, mirrors `FastTTSR.Worker.csproj`)
- `src/FastTTSR.Worker.Asr/Program.cs` (created, mirrors `Worker/Program.cs`: same
  `--port/--model-key/--idle-timeout` args, `app.RunAsync()` + 500ms delay + `READY:{port}` stdout
  signal, default port 50151)
- `src/FastTTSR.Worker.Asr/Services/WorkerTranscriptionService.cs` (created, mirrors
  `WorkerSynthesisService`: single-active-engine-with-lock pattern, routes by `request.Engine`)
- `src/FastTTSR.Api/Options/AsrWorkerOptions.cs` (created)
- `src/FastTTSR.Api/Services/AsrWorkerProxyTranscriber.cs` (created, mirrors
  `WorkerProxySynthesizer`, resolves its `WorkerProcessManager` via `[FromKeyedServices("asr")]`)
- `src/FastTTSR.Api/Services/AsrTranscriberRouter.cs` (created, mirrors `TtsSynthesizerRouter`)
- `src/FastTTSR.Api/Services/AsrModelWarmupService.cs` (created, mirrors `ModelWarmupService`)
- `src/FastTTSR.Api/Services/AsrModelIdleMonitorService.cs` (created, mirrors
  `ModelIdleMonitorService`; shares the same `ModelIdleMonitorOptions`/`MODEL_IDLE_TIMEOUT_SECONDS`
  config as TTS rather than adding a new env var)
- `src/FastTTSR.Api/Program.cs` (modified): added `SERVER_MODE` env var parsing
  (`tts`(default)/`asr`/`both` → `ttsEnabled`/`asrEnabled` booleans); wrapped the existing TTS
  registration block in `if (ttsEnabled)` **unchanged internally** (byte-identical when
  `SERVER_MODE` unset); added a parallel `if (asrEnabled)` block registering
  `IAsrModelCatalog`/`IAsrTranscriber` (worker-mode via a **keyed** `"asr"` `WorkerProcessManager`
  instance so it can coexist with TTS's own non-keyed instance without collision, or in-process mode
  via `AsrTranscriberRouter`).
- `FastTTSR.slnx` (modified: added `FastTTSR.Worker.Asr` project)

**Verification**: `./dotnet.sh build FastTTSR.slnx` → 0 errors, 0 warnings.
`./dotnet.sh test tests/FastTTSR.Api.Tests/FastTTSR.Api.Tests.csproj` → all 43 existing tests still
pass (confirms default `SERVER_MODE=tts` behavior is unaffected). Endpoints for ASR don't exist yet
(Phase 5), so `IAsrTranscriber`/`IAsrModelCatalog` aren't exercised end-to-end through HTTP yet -
only compile-time/DI-graph correctness has been verified this phase.

---

## Phase 5 — REST endpoints
**Status**: ✅ Accepted

**Inputs**: Phase 4's DI wiring (`IAsrTranscriber`, `IAsrModelCatalog` resolvable when
`asrEnabled`).

**Planned changes**:
1. `GET /api/server-info` → `ServerInfoResponse` (always mapped, reflects `SERVER_MODE`).
2. `GET /api/asr-models` → list of `AsrModelDefinitionResponse` (mapped only if `asrEnabled`).
3. `POST /v1/audio/transcriptions` (OpenAI-compatible name/shape): multipart/form-data binding
   (`IFormFile file`, `string model`, `string? language`, `string? response_format`). Validates
   model exists in `IAsrModelCatalog`, reads file into `byte[]`, calls
   `modelCache.EnsureModelAsync` + `asrTranscriber.TranscribeAsync`, returns `{ text }` JSON with
   metrics in response headers mirroring the TTS pattern (`X-Processing-Time`, `X-Audio-Duration`,
   `X-RTF`). Reuses `ErrorResponse` for 400/404, mirroring `/v1/audio/speech`'s validation style.
4. Extend `GET /v1/models` to include ASR model ids too when `asrEnabled`.
5. Guard TTS endpoints (`/v1/audio/speech`, `/api/models`) behind `ttsEnabled` the same way.

**Actual output files**:
- `src/FastTTSR.Api/Program.cs` (modified: `/api/server-info` added unconditionally; `/api/models`
  and `/v1/audio/speech` wrapped in `if (ttsEnabled)` with no internal changes; `/v1/models`
  rewritten to merge `IModelCatalog`/`IAsrModelCatalog` via `httpContext.RequestServices.GetService<T>()`
  (returns null gracefully if a catalog isn't registered, rather than throwing); new
  `if (asrEnabled)` block adds `/api/asr-models` and `/v1/audio/transcriptions`).

**Verification**:
- `./dotnet.sh build FastTTSR.slnx` → 0 errors, 0 warnings.
- `./dotnet.sh test tests/FastTTSR.Api.Tests` → all 43 existing tests still pass.
- **Live smoke test** (in-process mode, `SERVER_MODE=both`, real running server, curl'd via
  `docker exec` since the dev container publishes no host ports):
  - `GET /api/server-info` → `{"ttsEnabled":true,"asrEnabled":true}` ✅
  - `GET /api/asr-models` → correctly returns both `whisper-base` and `nemotron-3.5` ✅
  - `GET /v1/models` → correctly merges all 5 model ids (3 TTS + 2 ASR) ✅
  - `POST /v1/audio/transcriptions` (multipart, real downloaded `whisper-base` GGML weights, a
    synthetic WAV generated via a throwaway C# helper) → **500 Internal Server Error**, but NOT a
    Phase 5 bug: the exception occurs inside `WhisperFactory.FromPath` (Whisper.net's native
    library loader), i.e. the endpoint's own logic (multipart parsing, model lookup, cache
    resolution, request/response shape) executed correctly up to the point of engine construction.
    Root-caused and carried forward to Phase 6 - see the updated note at the end of the Phase 2
    section above.
- All Phase 5 code (routing, validation, response shaping) is confirmed correct; the one open
  issue is an environment/native-library packaging concern that Phase 6 (Docker packaging) already
  owned.

**Expected output files**:
- `src/FastTTSR.Api/Program.cs` (modified: new endpoint mappings)

---

## Phase 6 — Docker/Compose packaging
**Status**: ✅ Accepted

**Inputs**: Phase 4 producing both worker executables (`FastTTSR.Worker`, `FastTTSR.Worker.Asr`).

**Planned changes**:
1. `Dockerfile`: add `ARG SERVER_MODE=all` (build-time). `backend-build` stage restores/publishes
   all 3 projects always. Add native deps for `Whisper.net.Runtime` (verify what's needed for
   linux-x64) + keep existing `libespeak-ng1`/`espeak-ng-data` for Kokoro. Replace the single final
   stage with 3 named stages sharing a common `runtime-base`:
   - `runtime-tts`: copies api + worker(tts) only, `ENV SERVER_MODE=tts`.
   - `runtime-asr`: copies api + worker-asr only, `ENV SERVER_MODE=asr`.
   - `runtime-all`: copies api + both workers, `ENV SERVER_MODE=both`.
   Final line: `FROM runtime-${SERVER_MODE}`.
2. `docker-compose.yml`: keep default single service (build args `SERVER_MODE=all`, runtime env
   `SERVER_MODE=both`), pass through new `AsrWorkerOptions__*` env vars; add commented example
   snippets for tts-only/asr-only variants.
3. `docker-compose.test.yml`: extend similarly if integration tests need an ASR-enabled container.

**Deviation from plan (improvement)**: used Docker's native `--target <stage>` mechanism instead of
an `ARG SERVER_MODE` + `FROM runtime-${SERVER_MODE}` trick. This is the standard, better-supported
Docker idiom for exactly this "one Dockerfile, multiple selectable final images" use case - no ARG
needed at all. `docker build --target runtime-tts|runtime-asr|runtime-all -t <tag> .`; omitting
`--target` builds `runtime-all` by default (it's the last stage in the file).

**Two real bugs were found and fixed while validating this in a real packaged image** (not just
`dotnet run`/`dotnet build` as in earlier phases) - both were previously-tracked open risks from
Phase 2/5, now resolved:
1. **Whisper.net native library load failure** (`"Cannot load the library on this platform ...
   PInvokeError: Success"`, tracked since Phase 2/5): root-caused to `Whisper.net.Runtime` shipping
   native `.so` files under `runtimes/<rid>/` instead of the standard `runtimes/<rid>/native/`
   layout, so the dynamic linker never finds sibling dependencies (`libggml-*.so`) without help -
   `libgomp1` alone (already added) was necessary but not sufficient. **Fixed** by adding
   `ENV LD_LIBRARY_PATH=/app/runtimes/linux-x64:/app/worker-asr/runtimes/linux-x64` to the
   `runtime-asr`/`runtime-all` stages (covers both the API's own copy, used in in-process mode, and
   the ASR worker's copy, used in worker mode). **Confirmed fixed** via a real packaged-image test
   (see Validation below) - Whisper now transcribes correctly, e.g. `"Hello, this is a test of the
   Whisper transcription pipeline."` for a matching TTS-generated input.
2. **Whisper.net requires exactly 16kHz input and does not resample internally** (new finding, not
   previously known - only surfaced once the native-library issue above was fixed and a real
   transcription could actually be attempted): threw `Whisper.net.Wave.NotSupportedWaveException:
   Only 16KHz sample rate is supported` for Kokoro-generated audio (24kHz). **Fixed**:
   `WhisperAsrEngine.TranscribeAsync` now calls a new `WavAudioUtils.ResampleToMono16kWav(wavBytes)`
   (reuses the existing `ReadMonoFloat` resampler, re-encodes as a 16kHz mono PCM16 WAV) before
   handing audio to Whisper.net, mirroring what Nemotron's pipeline already did internally.

**Validation performed** (against the real built `fastttsr:all` image, `docker build --target
runtime-all`, both in-process AND full worker mode):
- `./dotnet.sh build FastTTSR.slnx` → 0 errors after the `WhisperAsrEngine`/`WavAudioUtils` fix.
- `docker build --target runtime-all -t fastttsr:all .` → succeeds (frontend + all 3 .NET projects
  publish with `-r linux-x64 --self-contained false`).
- In-process mode (`WorkerOptions__Enabled=false`, `AsrWorkerOptions__Enabled=false`): Whisper
  transcription of a Kokoro-generated WAV → 200, correct text. Nemotron transcription → 200,
  garbled text (still the known, tracked accuracy caveat - not a packaging issue).
- **Full worker mode** (production default, no env overrides): `/v1/audio/speech` (Supertonic-3),
  `/v1/audio/transcriptions` with `whisper-base` (correct text), and `/v1/audio/transcriptions`
  with `nemotron-3.5` (garbled but non-crashing, same caveat) all returned 200 - confirms the ASR
  worker subprocess correctly inherits `LD_LIBRARY_PATH` from its parent process.

**Expected output files**:
- `Dockerfile` (modified)
- `docker-compose.yml` (modified)
- `docker-compose.test.yml` (modified, if needed)

**Actual output files**:
- `Dockerfile` (rewritten: publishes all 3 projects with `-r linux-x64`, shared `runtime-base`
  stage + `runtime-tts`/`runtime-asr`/`runtime-all` leaf stages, `libgomp1` added,
  `LD_LIBRARY_PATH` set in ASR-capable stages)
- `docker-compose.yml` (modified: `SERVER_MODE` + `AsrWorkerOptions__*` env passthrough, commented
  tts-only/asr-only example services using `target:`)
- `src/FastTTSR.Api/Services/WavAudioUtils.cs` (modified: new `ResampleToMono16kWav` helper)
- `src/FastTTSR.Api/Services/WhisperAsrEngine.cs` (modified: resamples to 16kHz before Whisper.net)
- `docker-compose.test.yml`/`Dockerfile.tests` already updated in the Phase 8 circular-tests work
  (added earlier, ahead of this phase, at the user's request)

---

## Phase 7 — Frontend ASR UI
**Status**: ✅ Done (awaiting acceptance)

**Inputs**: Phase 5's endpoint contracts (`/api/server-info`, `/api/asr-models`,
`/v1/audio/transcriptions`).

**Planned changes**:
1. `frontend/src/types.ts`: add `AsrModel`, `TranscriptionMetrics`, `ServerInfo` interfaces.
2. `frontend/src/App.vue`: on mount, fetch `GET /api/server-info`; if both `ttsEnabled`/`asrEnabled`,
   render a simple tab switcher between "Text to Speech" (unchanged) and "Speech to Text" (new); if
   only one enabled, show that panel directly without tabs.
3. New `frontend/src/components/AudioFileInput.vue`: file picker (`accept="audio/*"`).
4. New `frontend/src/components/TranscriptionResult.vue`: displays returned text (copyable),
   metrics row styled like `AudioPlayer`'s metrics.
5. Reuse `ModelSelector.vue`/`LanguageSelector.vue`; call `POST /v1/audio/transcriptions` via
   `fetch` with `FormData`.

**Actual output files**:
- `frontend/src/types.ts` (modified: added `AsrModel`, `TranscriptionMetrics`, `ServerInfo`)
- `frontend/src/components/ModelSelector.vue` (modified: `models` prop loosened to a structural
  `{ name, displayName }[]` type so it can be reused for both `TtsModel[]` and `AsrModel[]`)
- `frontend/src/components/AudioFileInput.vue` (new: styled file picker, emits `update:modelValue`)
- `frontend/src/components/TranscriptionResult.vue` (new: metrics row + copyable text block,
  reuses `AudioPlayer`'s metric CSS class names for visual consistency)
- `frontend/src/App.vue` (modified: fetches `/api/server-info` on mount and sets `activeTab`
  accordingly; tab switcher rendered only when both `ttsEnabled`/`asrEnabled`; new ASR panel wired
  to `asrForm` state, `loadAsrModels()`, and `transcribe()` which POSTs `FormData` to
  `/v1/audio/transcriptions` and reads `X-Processing-Time`/`X-RTF`/`X-Audio-Duration`/
  `X-Character-Count` response headers into `transcriptionMetrics`; added `.tab-switcher`/
  `.tab-btn`/`.tab-panel`/`.demo-generate-btn` global styles matching the existing dark theme)
- `frontend/pnpm-lock.yaml` (regenerated: pre-existing drift where `package.json` already listed
  `typescript`/`vue-tsc` devDependencies not reflected in the lockfile; refreshed while installing
  to run the build/type-check below, unrelated to the ASR feature itself)

**Verification performed**:
- `pnpm install` (via `npm install -g pnpm`, no local pnpm previously) succeeded.
- `npx vue-tsc --noEmit` — zero type errors.
- `pnpm run build` (`vue-tsc && vite build`) — succeeded, 43 modules transformed, output bundle
  produced (`dist/assets/index-*.js` ~100kB, `dist/assets/index-*.css` ~14kB).
- Not yet tested against a live backend with `SERVER_MODE=both`/`asr` (no browser/manual smoke
  test performed this phase) — build/type-check only.

---



## Phase 8 — Tests
**Status**: 🚧 In progress (circular TTS->ASR model tests implemented ahead of schedule, between
Phase 5 and Phase 6, at the user's request; unit-test-level coverage below is still pending)

**Inputs**: functioning engines (Phase 2/3), endpoints (Phase 5).

**Planned changes**:
1. `tests/FastTTSR.Api.Tests/AsrModelCatalogTests.cs` (mirrors `ModelCatalogTests.cs`).
2. `tests/FastTTSR.Api.Tests/AsrTranscriberRouterTests.cs`.
3. `tests/FastTTSR.Api.IntegrationTests/SpeechTranscriptionTests.cs` (mirrors
   `SpeechSynthesisTests.cs`): POST small sample WAV to `/v1/audio/transcriptions` for the whisper
   model, assert 200 + non-empty text; add a Nemotron case once Phase 3 is confirmed working.
4. Extend `ModelHealthTests.cs` pattern for `/api/server-info`.

**Expected output files** (still pending):
- `tests/FastTTSR.Api.Tests/AsrModelCatalogTests.cs` (new)
- `tests/FastTTSR.Api.Tests/AsrTranscriberRouterTests.cs` (new)
- `tests/FastTTSR.Api.IntegrationTests/ModelHealthTests.cs` (modified)

### Circular TTS->ASR model tests (done, implemented ahead of schedule)

**Goal**: real end-to-end evaluation - synthesize real audio via Supertonic-3 (multiple preset
speakers), feed it into Whisper/Nemotron, and check transcription quality - as an opt-in suite that
never runs in CI (downloads real multi-hundred-MB-to-GB models and runs real inference).

**Design**:
- `tests/FastTTSR.Api.IntegrationTests/TestData/test_text.md`: human-readable corpus - 3 short
  (2-5 sentence) samples, 2 long (~150-250 word) paragraph samples, and 1 long ~20-turn
  conversation script (alternating speakers F1/M2), each tagged with `## <id> (type: ..., speaker:
  ...)` headings. The conversation is used for the "streaming" test: concatenating many
  independently-synthesized turns into one long, multi-minute, multi-chunk audio stresses
  Nemotron's cache-aware chunk-to-chunk decoding far more than a single short clip does (there is
  no separate incremental/streaming HTTP API yet - both engines currently only expose whole-file
  batch transcription - so "streaming" here means exercising the *engine's internal* chunked
  cache-threading logic across many consecutive chunks via one long audio file, not a new
  incremental request API).
- `Support/AsrTestCorpus.cs`: parses `test_text.md` into `AsrTestSample`/`AsrConversationSample`
  records (simple regex-based heading/turn parsing, no markdown library).
- `Support/WordErrorRate.cs`: small self-contained word-level edit-distance (WER) calculator (no
  external NLP library).
- `Support/WavTestUtils.cs`: concatenates several PCM16 WAV clips (with a short silence gap) into
  one long WAV, for building the conversation audio from individually-synthesized turns.
- `Support/AsrModelTestGate.cs`: opt-in gate - `AsrModelTestGate.IsEnabled` reads env var
  `ASR_MODEL_TESTS=1`/`true`.
- `AsrCircularTests.cs` (`[Trait("Category", "AsrModelTests")]`, uses `Xunit.SkippableFact`'s
  `[SkippableTheory]`/`Skip.IfNot(...)` so tests show as **Skipped** - not silently passed or
  failed - when not opted in): each test builds its own `WebApplicationFactory<Program>` (only
  *after* the skip check passes, so nothing expensive happens when skipped) with
  `SERVER_MODE=both`, `WorkerOptions__Enabled=false`, `AsrWorkerOptions__Enabled=false` (in-process
  mode - avoids needing separately-built worker executables reachable on disk).
  - `Offline_TextSample_TranscribesToReasonablyMatchingText` (Theory, every short/long sample ×
    {whisper-base, nemotron-3.5}): synthesizes via `POST /v1/audio/speech` (model=supertonic-3),
    transcribes via `POST /v1/audio/transcriptions`, asserts non-empty transcript and WER ≤ 0.9.
  - `Streaming_LongConversation_TranscribesWithoutCrashingAndProducesText` (Theory over both ASR
    models): synthesizes every conversation turn individually (different speaker per turn),
    concatenates into one long WAV via `WavTestUtils`, transcribes once, asserts non-empty
    transcript with a plausible word count (not a strict WER, since the point is surviving many
    chunks without crashing/degenerating, not exact accuracy).

**A real bug was found and fixed while validating this**: `ModelCache.EnsureModelAsync` had no
per-model locking, so a cold-cache run raced the background `AsrModelWarmupService` download
against the on-demand download triggered by the test's own HTTP request - both writing the same
asset files concurrently - causing an intermittent 500 (this affected TTS models too, not just ASR,
it just hadn't been hit before). **Fixed**: `ModelCache` now uses a per-model-name `SemaphoreSlim`
to serialize concurrent ensure-calls for the same model.

**Validation performed**:
- `./dotnet.sh build FastTTSR.slnx` → 0 errors. `./dotnet.sh test tests/FastTTSR.Api.Tests` → 43/43
  still pass (ModelCache fix didn't regress anything).
- Ran `AsrCircularTests` unfiltered without `ASR_MODEL_TESTS` set → all 12 cases correctly show as
  **Skipped**, 0 downloads triggered, ~60ms total (confirms the gate is truly zero-cost by default).
- Ran one opt-in case for real (`short-1` × `nemotron-3.5`) against a **warm** model cache (to
  isolate from the download-race bug above, which is now fixed but wasn't yet at test time): the
  full pipeline (TTS synthesis → HTTP → ASR transcription → WER check) executed correctly
  end-to-end in ~6s with **no crash**. The test still *fails* its WER assertion (100% WER, garbled
  output) - this is the **already-documented, tracked Nemotron accuracy caveat** (mel-scale/framing
  uncertainty, see Phase 3), not a bug in the test itself. This is expected and desired: the test
  correctly detects the known accuracy gap and will start passing once that's tuned.
- Whisper cases are expected to still hit the Phase 5/6-tracked native-library-load issue in
  container environments until that's fixed.

**Actual output files**:
- `tests/FastTTSR.Api.IntegrationTests/TestData/test_text.md` (new)
- `tests/FastTTSR.Api.IntegrationTests/Support/AsrTestCorpus.cs` (new)
- `tests/FastTTSR.Api.IntegrationTests/Support/WordErrorRate.cs` (new)
- `tests/FastTTSR.Api.IntegrationTests/Support/WavTestUtils.cs` (new)
- `tests/FastTTSR.Api.IntegrationTests/Support/AsrModelTestGate.cs` (new)
- `tests/FastTTSR.Api.IntegrationTests/AsrCircularTests.cs` (new)
- `tests/FastTTSR.Api.IntegrationTests/FastTTSR.Api.IntegrationTests.csproj` (modified: added
  `Xunit.SkippableFact` package + `TestData/test_text.md` copy-to-output)
- `src/FastTTSR.Api/Services/ModelCache.cs` (modified: per-model-name locking, bug fix)
- `tests/run-tests.sh` (modified: new `asr-model-tests` mode)
- `docker-compose.test.yml` (modified: new `asr-model-tests` service, self-hosting, no `fastttsr`
  dependency, `ASR_MODEL_TESTS=1`)
- `Dockerfile.tests` (modified: added `libgomp1` so Whisper's native lib has a chance to load)
- `.github/workflows/ci-tests.yml` (modified: integration-tests job now runs `dotnet test ... --filter "Category!=AsrModelTests"`, explicit belt-and-suspenders exclusion alongside the env-var gate)

**How to run**: `ASR_MODEL_TESTS=1 dotnet test tests/FastTTSR.Api.IntegrationTests/... --filter
"Category=AsrModelTests"`, or `./tests/run-tests.sh asr-model-tests` (docker-compose-based).

---

## Phase 9 — Documentation update (final)
**Status**: Not started

**Inputs**: all prior phases complete.

**Planned changes**:
1. Final `docs/LLM_WIKI.md` pass: as-built ASR architecture end-to-end + an "Adding a new engine"
   walkthrough for both TTS and ASR.
2. Update `README.md` (feature list, endpoints table, env vars, models table).
3. Update `docs/ARCHITECTURE.md`, `docs/API.md`, `docs/CONFIGURATION.md`, `docs/MODELS.md`,
   `docs/DEPLOYMENT.md` (multi-image build/run instructions), `docs/TROUBLESHOOTING.md`.
4. Confirm no doc references stale info (e.g. "single worker" or "TTS-only" assumptions).
5. Mark this plan document's status tracker fully `✅ Accepted` and add a short "complete" banner
   at the top.

---

## Overall verification checklist (run once Phase 4+ exist)
1. `dotnet build FastTTSR.slnx` (via `./dotnet.sh build FastTTSR.slnx`) succeeds.
2. `./dotnet.sh test tests/FastTTSR.Api.Tests` passes, including new Asr tests.
3. Manual: `SERVER_MODE=tts` (default/unset) behaves byte-identical to pre-ASR behavior;
   `/v1/audio/transcriptions` returns 404; ASR UI hidden.
4. Manual: `SERVER_MODE=asr` — TTS endpoints disabled/hidden, ASR endpoints work, only
   `FastTTSR.Worker.Asr` spawns.
5. Manual: `SERVER_MODE=both` — both workers spawn independently on non-overlapping port ranges,
   both UI panels/tabs appear.
6. `docker build --build-arg SERVER_MODE=tts|asr|all -t fastttsr:<tag> .` — confirm each image only
   contains the expected worker executable(s).
7. `./tests/run-tests.sh all` / `docker compose -f docker-compose.test.yml up
   --abort-on-container-exit` for integration coverage including the transcription endpoint.
8. Manual curl: POST a short WAV to `/v1/audio/transcriptions` with `model=whisper-base`, verify
   text output; repeat with `model=nemotron-3.5`.

## Reference files (existing code whose patterns are mirrored)
- `src/FastTTSR.Api/Services/TtsSynthesizerRouter.cs` — pattern for `AsrTranscriberRouter`.
- `src/FastTTSR.Api/Services/SupertonicTtsEngine.cs` — multi-ONNX-session pattern for
  `NemotronAsrEngine`.
- `src/FastTTSR.Api/Services/KokoroTtsEngine.cs` / `KokoroTtsSynthesizer.cs` — single-session
  pooling pattern, followed exactly by `WhisperAsrEngine`/`WhisperAsrTranscriber`.
- `src/FastTTSR.Api/Services/WorkerProcessManager.cs`, `Services/WorkerProxySynthesizer.cs`,
  `src/FastTTSR.Worker/Program.cs`, `Services/WorkerSynthesisService.cs` — worker process pattern
  for `FastTTSR.Worker.Asr`.
- `src/FastTTSR.Api/Protos/synthesis.proto` — pattern for `transcription.proto`.
- `src/FastTTSR.Api/Program.cs` — current DI wiring/endpoint mapping to extend.
