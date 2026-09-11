# ASR Implementation History (Phases -1 through 12, full detail)

> This file holds the **full, unabridged** phase-by-phase detail for every ASR phase that is
> already `✅ Accepted`/`✅ Done`. It exists so [docs/ASR_IMPLEMENTATION_PLAN.md](ASR_IMPLEMENTATION_PLAN.md)
> can stay short (a synthesized 2-4 sentence summary + link per phase, from which you arrived
> here). Read a phase's detail here only if you need the low-level "why"/exact file list/root-cause
> narrative behind it — the summary in the main plan doc is enough for most orientation purposes.

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
2. Moved `IdleMonitor` from `SharpAudio.Worker/Services/IdleMonitor.cs` to
   `SharpAudio.Api/Services/IdleMonitor.cs` so both worker projects can share it via their existing
   `ProjectReference` to `SharpAudio.Api`. Updated `using` in `SharpAudio.Worker/Program.cs`.
3. Generalized `WorkerProcessManager`'s constructor to accept a plain `WorkerOptions` value
   (instead of `IOptions<WorkerOptions>`), so multiple independently-configured instances (one per
   task type) can be constructed manually in `Program.cs`. Updated the registration in
   `Program.cs` to resolve `IOptions<WorkerOptions>` and pass `.Value` explicitly.

**Actual output files**:
- `src/SharpAudio.Api/Models/ModelAsset.cs` (created, replaces `TtsModelAsset.cs`)
- `src/SharpAudio.Api/Models/TtsModelDefinition.cs` (modified: `ModelAsset` type)
- `src/SharpAudio.Api/Services/ModelCatalog.cs` (modified: `ModelAsset` type)
- `src/SharpAudio.Api/Services/IdleMonitor.cs` (created, moved from Worker project)
- `src/SharpAudio.Worker/Program.cs` (modified: `using SharpAudio.Api.Services;`)
- `src/SharpAudio.Api/Services/WorkerProcessManager.cs` (modified constructor)
- `src/SharpAudio.Api/Program.cs` (modified: `WorkerProcessManager` registration)

**Verification**: `./dotnet.sh build SharpAudio.slnx` → 0 errors.

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
- `src/SharpAudio.Api/Models/AsrModelDefinition.cs` (created)
- `src/SharpAudio.Api/Models/TranscriptionResult.cs` (created)
- `src/SharpAudio.Api/Services/IAsrModelCatalog.cs` (created)
- `src/SharpAudio.Api/Services/AsrModelCatalog.cs` (created)
- `src/SharpAudio.Api/Services/IAsrTranscriber.cs` (created)
- `src/SharpAudio.Api/Services/IIdleTrackingTranscriber.cs` (created)
- `src/SharpAudio.Api/Contracts/AudioTranscriptionRequest.cs` (created)
- `src/SharpAudio.Api/Contracts/AsrModelDefinitionResponse.cs` (created)
- `src/SharpAudio.Api/Contracts/ServerInfoResponse.cs` (created)
- `src/SharpAudio.Api/config.json` (modified: added `asrModels` array)
- `src/SharpAudio.Api/Services/IModelCache.cs` (modified: new overload)
- `src/SharpAudio.Api/Services/ModelCache.cs` (modified: new overload + shared helper)
- `tests/SharpAudio.Api.Tests/SpeechEndpointTests.cs` (modified: `StubModelCache` implements new
  interface member)

**Verification**: `./dotnet.sh build SharpAudio.slnx` → 0 errors.

---

## Phase 2 — Whisper engine
**Status**: ✅ Accepted

**Inputs**: Phase 1's `AsrModelDefinition`, `IAsrTranscriber`, `IIdleTrackingTranscriber`,
`AudioTranscriptionRequest`, `TranscriptionResult`.

**Goal**: a fully working Whisper-based ASR engine, not yet wired into DI/endpoints/worker (that's
Phase 4/5).

**Changes**:
1. Added `Whisper.net` + `Whisper.net.Runtime` (v1.8.1) NuGet packages to
   `SharpAudio.Api.csproj` (engine code lives in `Api/Services`, same convention as Kokoro/Supertonic,
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
- `src/SharpAudio.Api/SharpAudio.Api.csproj` (modified: 2 new package refs)
- `src/SharpAudio.Api/Services/WhisperAsrEngine.cs` (created)
- `src/SharpAudio.Api/Services/WhisperAsrTranscriber.cs` (created)
- `src/SharpAudio.Api/Services/WavAudioUtils.cs` (created)

**Verification**: `./dotnet.sh build SharpAudio.slnx` → 0 errors.

**Open item carried forward to Phase 6**: verify `Whisper.net.Runtime`'s native binaries are
correctly included for linux-x64 when `SharpAudio.Worker.Asr` is published inside the Docker image.
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
line for any image variant that includes `SharpAudio.Worker.Asr`; (b) further investigate/resolve the
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
- `src/SharpAudio.Api/Services/NemotronVocabulary.cs` (created)
- `src/SharpAudio.Api/Services/NemotronFeatureExtractor.cs` (created)
- `src/SharpAudio.Api/Services/NemotronAsrEngine.cs` (created)
- `src/SharpAudio.Api/Services/NemotronAsrTranscriber.cs` (created)
- `src/SharpAudio.Api/Services/WavAudioUtils.cs` (modified: added `ReadMonoFloat`/resampling, and
  `TryReadHeader` now also returns the data chunk offset)
- `src/SharpAudio.Api/config.json` (modified: added `genai_config.json` as a `nemotron-3.5` asset)
- `src/SharpAudio.Api/Services/AsrModelCatalog.cs` (modified: same, in the hardcoded defaults)

**Verification**: `./dotnet.sh build SharpAudio.slnx` → 0 errors. The spike (encoder/decoder/joint
graph metadata) was validated by actually loading the real ONNX graphs via a throwaway C# console
app (`./dotnet.sh run`), not just read from docs — see spike findings above. **Real-weights smoke
test** (also via a throwaway C# console app, `ProjectReference` to `SharpAudio.Api.csproj`, deleted
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

**Update (post-Phase-8): accuracy caveat resolved.** The "unverified accuracy" caveat above turned
out to hide real bugs, not just unvalidated defaults. Root-caused after the user supplied a
validated standalone Python reference implementation (`pyscripts/nemotron_speech_ort_only.py`,
using only `onnxruntime`+`numpy`+`soundfile`, reverse-engineered from `onnxruntime-genai`'s own
C++ source for this model). Diagnosis method: real-audio evidence, not guesswork - a throwaway
reflection-based harness compared `NemotronAsrEngine`'s internal encoder output for real
synthesized speech vs. total silence of the same length; cosine similarity was ~0.9995 (nearly
identical), proving the encoder was almost completely ignoring actual audio content. Comparing
against the Python reference found the root cause: **`lang_id` was being resolved from
`vocab.txt`'s `<en-US>` tag line index (2947) instead of the small fixed integer (0) the model's
language embedding actually expects** - an out-of-range id injected into every single chunk,
drowning out the real acoustic signal. Several compounding bugs were fixed at the same time:
- `lang_id`: now a fixed lookup table (`Services/NemotronLanguages.cs`), not derived from
  `vocab.txt` tags at all.
- Config source: switched entirely from `audio_processor_config.json` to `genai_config.json`
  (`log_eps` was `1e-10` from the wrong file; the correct value is `5.96e-08`).
- Mel scale: Slaney (librosa/NeMo default), not the classic HTK formula originally implemented.
- Hann window: centered within the FFT frame (zero-padded on both sides), not left-aligned.
- Framing: true stateful streaming (raw-audio chunks of `chunk_samples`, carrying `nFft/2` samples
  of real left-context audio + the previous chunk's trailing log-mel frames across calls) instead
  of computing log-mel for the whole utterance up front and slicing it.
- Encoder's `length` input: total frames fed (cache + new), not just new frames.

**Verified via the opt-in circular tests** (`AsrCircularTests`, real Supertonic-3-synthesized
speech, real Nemotron weights): WER dropped from **100% → ~1-2%** on multi-sentence paragraph
samples, and the 20-turn, multi-minute conversation streaming test now produces near-perfect
output (297 words emitted vs. 296 reference words). `docs/MODELS.md`/`docs/LLM_WIKI.md`/
`docs/TROUBLESHOOTING.md` updated to remove the now-resolved caveat language.

---

## Phase 4 — ASR worker process + DI wiring
**Status**: ✅ Accepted

**Inputs**: Phase 2 (`WhisperAsrTranscriber`) and Phase 3 (`NemotronAsrTranscriber`) both
implementing `IAsrTranscriber`/`IIdleTrackingTranscriber`.

**Goal**: a second worker executable (`SharpAudio.Worker.Asr`) that hosts both ASR engines behind
gRPC, plus `SERVER_MODE`-aware DI wiring in `Program.cs` so TTS/ASR/both can be toggled at runtime.

**Planned changes**:
1. `Protos/transcription.proto`: `service WorkerTranscription { rpc Transcribe(...); rpc
   HealthCheck(...); }`; messages `TranscribeRequest` (model_name, engine, model_path, audio_bytes,
   audio_format, language) / `TranscribeResponse` (text, language_detected,
   processing_time_seconds, audio_duration_seconds).
2. New project `src/SharpAudio.Worker.Asr/SharpAudio.Worker.Asr.csproj` (mirrors
   `SharpAudio.Worker.csproj`): `ProjectReference` to `SharpAudio.Api.csproj`, `Protobuf` include of
   `transcription.proto` (`GrpcServices="Server"`), `Grpc.AspNetCore`.
   - `Program.cs`: same `--port/--model-key/--idle-timeout` arg parsing + Kestrel HTTP/2 as
     `Worker/Program.cs`.
   - `Services/WorkerTranscriptionService.cs` (mirrors `WorkerSynthesisService`): routes by
     `request.Engine` (`"whisper"` vs `"nemotron-3.5"`) to `WhisperAsrEngine`/`NemotronAsrEngine`,
     lazy-loads, idle-tracked via the shared `IdleMonitor` (Phase 0).
3. Add the new project to `SharpAudio.slnx`.
4. `Options/AsrWorkerOptions.cs` (mirrors `WorkerOptions`): section `"AsrWorkerOptions"`,
   `ExecutablePath` default `"./worker-asr/SharpAudio.Worker.Asr"`, `PortRangeStart` default `50151`
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
- `src/SharpAudio.Api/Protos/transcription.proto` (new)
- `src/SharpAudio.Worker.Asr/SharpAudio.Worker.Asr.csproj` (new)
- `src/SharpAudio.Worker.Asr/Program.cs` (new)
- `src/SharpAudio.Worker.Asr/Services/WorkerTranscriptionService.cs` (new)
- `src/SharpAudio.Api/Options/AsrWorkerOptions.cs` (new)
- `src/SharpAudio.Api/Services/AsrWorkerProxyTranscriber.cs` (new)
- `src/SharpAudio.Api/Services/AsrTranscriberRouter.cs` (new, in-process mode)
- `src/SharpAudio.Api/Program.cs` (modified: SERVER_MODE parsing, keyed DI, ASR registrations)
- `SharpAudio.slnx` (modified: add project)

**Actual output files** (matches expected, plus 2 warmup/idle-monitor services not explicitly
called out in the original plan but needed to mirror the TTS side completely):
- `src/SharpAudio.Api/Protos/transcription.proto` (created) - own `csharp_namespace`
  (`SharpAudio.Worker.Asr.Grpc`, distinct from synthesis.proto's `SharpAudio.Worker.Grpc`) to avoid
  type-name collisions.
- `src/SharpAudio.Api/SharpAudio.Api.csproj` (modified: added `Protobuf Include="Protos/transcription.proto" GrpcServices="Client"`)
- `src/SharpAudio.Worker.Asr/SharpAudio.Worker.Asr.csproj` (created, mirrors `SharpAudio.Worker.csproj`)
- `src/SharpAudio.Worker.Asr/Program.cs` (created, mirrors `Worker/Program.cs`: same
  `--port/--model-key/--idle-timeout` args, `app.RunAsync()` + 500ms delay + `READY:{port}` stdout
  signal, default port 50151)
- `src/SharpAudio.Worker.Asr/Services/WorkerTranscriptionService.cs` (created, mirrors
  `WorkerSynthesisService`: single-active-engine-with-lock pattern, routes by `request.Engine`)
- `src/SharpAudio.Api/Options/AsrWorkerOptions.cs` (created)
- `src/SharpAudio.Api/Services/AsrWorkerProxyTranscriber.cs` (created, mirrors
  `WorkerProxySynthesizer`, resolves its `WorkerProcessManager` via `[FromKeyedServices("asr")]`)
- `src/SharpAudio.Api/Services/AsrTranscriberRouter.cs` (created, mirrors `TtsSynthesizerRouter`)
- `src/SharpAudio.Api/Services/AsrModelWarmupService.cs` (created, mirrors `ModelWarmupService`)
- `src/SharpAudio.Api/Services/AsrModelIdleMonitorService.cs` (created, mirrors
  `ModelIdleMonitorService`; shares the same `ModelIdleMonitorOptions`/`MODEL_IDLE_TIMEOUT_SECONDS`
  config as TTS rather than adding a new env var)
- `src/SharpAudio.Api/Program.cs` (modified): added `SERVER_MODE` env var parsing
  (`tts`(default)/`asr`/`both` → `ttsEnabled`/`asrEnabled` booleans); wrapped the existing TTS
  registration block in `if (ttsEnabled)` **unchanged internally** (byte-identical when
  `SERVER_MODE` unset); added a parallel `if (asrEnabled)` block registering
  `IAsrModelCatalog`/`IAsrTranscriber` (worker-mode via a **keyed** `"asr"` `WorkerProcessManager`
  instance so it can coexist with TTS's own non-keyed instance without collision, or in-process mode
  via `AsrTranscriberRouter`).
- `SharpAudio.slnx` (modified: added `SharpAudio.Worker.Asr` project)

**Verification**: `./dotnet.sh build SharpAudio.slnx` → 0 errors, 0 warnings.
`./dotnet.sh test tests/SharpAudio.Api.Tests/SharpAudio.Api.Tests.csproj` → all 43 existing tests still
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
- `src/SharpAudio.Api/Program.cs` (modified: `/api/server-info` added unconditionally; `/api/models`
  and `/v1/audio/speech` wrapped in `if (ttsEnabled)` with no internal changes; `/v1/models`
  rewritten to merge `IModelCatalog`/`IAsrModelCatalog` via `httpContext.RequestServices.GetService<T>()`
  (returns null gracefully if a catalog isn't registered, rather than throwing); new
  `if (asrEnabled)` block adds `/api/asr-models` and `/v1/audio/transcriptions`).

**Verification**:
- `./dotnet.sh build SharpAudio.slnx` → 0 errors, 0 warnings.
- `./dotnet.sh test tests/SharpAudio.Api.Tests` → all 43 existing tests still pass.
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
- `src/SharpAudio.Api/Program.cs` (modified: new endpoint mappings)

---

## Phase 6 — Docker/Compose packaging
**Status**: ✅ Accepted

**Inputs**: Phase 4 producing both worker executables (`SharpAudio.Worker`, `SharpAudio.Worker.Asr`).

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

**Validation performed** (against the real built `sharp-audio:all` image, `docker build --target
runtime-all`, both in-process AND full worker mode):
- `./dotnet.sh build SharpAudio.slnx` → 0 errors after the `WhisperAsrEngine`/`WavAudioUtils` fix.
- `docker build --target runtime-all -t sharp-audio:all .` → succeeds (frontend + all 3 .NET projects
  publish with `-r linux-x64 --self-contained false`).
- In-process mode (`WorkerOptions__Enabled=false`, `AsrWorkerOptions__Enabled=false`): Whisper
  transcription of a Kokoro-generated WAV → 200, correct text. Nemotron transcription → 200,
  garbled text (known accuracy caveat at the time - since fixed, see Phase 3's "Update" note).
- **Full worker mode** (production default, no env overrides): `/v1/audio/speech` (Supertonic-3),
  `/v1/audio/transcriptions` with `whisper-base` (correct text), and `/v1/audio/transcriptions`
  with `nemotron-3.5` (garbled but non-crashing, same caveat - since fixed) all returned 200 -
  confirms the ASR worker subprocess correctly inherits `LD_LIBRARY_PATH` from its parent process.

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
- `src/SharpAudio.Api/Services/WavAudioUtils.cs` (modified: new `ResampleToMono16kWav` helper)
- `src/SharpAudio.Api/Services/WhisperAsrEngine.cs` (modified: resamples to 16kHz before Whisper.net)
- `docker-compose.test.yml`/`Dockerfile.tests` already updated in the Phase 8 circular-tests work
  (added earlier, ahead of this phase, at the user's request)

---

## Phase 7 — Frontend ASR UI
**Status**: ✅ Accepted

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
  accordingly; renders nothing but a loading placeholder until `server-info` resolves (default
  `serverInfo` is `{ ttsEnabled: false, asrEnabled: false }`, so no panel flashes before load);
  shows an error message if a misconfigured server reports neither `ttsEnabled` nor `asrEnabled`;
  tab switcher rendered only when both `ttsEnabled`/`asrEnabled`; TTS/ASR panels are each wrapped
  in `v-if="serverInfo.ttsEnabled"`/`v-if="serverInfo.asrEnabled"` so only the panel(s) the running
  server actually serves are ever mounted; header subtitle is computed from `serverInfo` (TTS-only/
  ASR-only/both wording); new ASR panel wired to `asrForm` state, `loadAsrModels()`, and
  `transcribe()` which POSTs `FormData` to `/v1/audio/transcriptions` and reads
  `X-Processing-Time`/`X-RTF`/`X-Audio-Duration`/`X-Character-Count` response headers into
  `transcriptionMetrics`; added `.tab-switcher`/`.tab-btn`/`.tab-panel`/`.demo-generate-btn`/
  `.demo-placeholder` global styles matching the existing dark theme)
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
**Status**: ✅ Done (awaiting acceptance)

**Inputs**: functioning engines (Phase 2/3), endpoints (Phase 5).

**Planned changes**:
1. `tests/SharpAudio.Api.Tests/AsrModelCatalogTests.cs` (mirrors `ModelCatalogTests.cs`).
2. `tests/SharpAudio.Api.Tests/AsrTranscriberRouterTests.cs`.
3. ~~`tests/SharpAudio.Api.IntegrationTests/SpeechTranscriptionTests.cs`~~ — **superseded**: this
   would have downloaded real Whisper/Nemotron weights inside the always-run `integration-tests`
   Docker profile, which contradicts the later, explicit user decision that ASR-with-real-models
   testing must be opt-in only and excluded from CI (see "Circular TTS->ASR model tests" below,
   which already exercises `/v1/audio/transcriptions` end-to-end with real models for both
   engines and is the intended home for this kind of coverage).
4. Extend `ModelHealthTests.cs` pattern for `/api/server-info`.

**Actual output files**:
- `tests/SharpAudio.Api.Tests/AsrModelCatalogTests.cs` (new): catalog contains `whisper-base`/
  `nemotron-3.5`, correct `Engine` strings, Whisper's `ModelPath` is one of its own `Assets`,
  Nemotron exposes >10 `SupportedLanguages` including `en`/`ja`, Nemotron declares all 3
  encoder/decoder/joint `.onnx`+`.onnx.data` assets plus config/vocab files, unknown model name
  returns `false`, `GetSupportedModels()` returns exactly both entries.
- `tests/SharpAudio.Api.Tests/AsrTranscriberRouterTests.cs` (new): instantiates the real
  `AsrTranscriberRouter` with real (but otherwise idle) `WhisperAsrTranscriber`/
  `NemotronAsrTranscriber` and a bogus model directory — no real model weights needed since each
  engine fails fast (Whisper's explicit `FileNotFoundException` on the missing `.bin` file vs.
  Nemotron's `InferenceSession` constructor failing while opening the missing `encoder.onnx`) and
  the failure's identity itself proves which branch the router picked. Covers: `engine="whisper"`
  routes to Whisper, `engine="nemotron-3.5"` (and case-insensitively `"NEMOTRON-3.5"`) routes to
  Nemotron, an unrecognized engine falls through to Whisper's default branch (which then rejects
  it with its own `InvalidOperationException` — proving it was *routed* there, even though Whisper
  itself is strict about the engine string matching exactly `"whisper"`), and `GetLoadedEngines()`
  starts empty.
- `tests/SharpAudio.Api.IntegrationTests/ModelHealthTests.cs` (modified): added
  `ServerInfo_ShouldReflectConfiguredServerMode`, which re-derives the expected
  `ttsEnabled`/`asrEnabled` from the `SERVER_MODE` env var the same way `Program.cs` does, then
  asserts `GET /api/server-info` matches — correct under whichever mode a given test run/container
  is actually configured for, no real ASR models required.

**Verification performed**:
- `./dotnet.sh build SharpAudio.slnx` — 0 errors.
- `./dotnet.sh test tests/SharpAudio.Api.Tests/SharpAudio.Api.Tests.csproj` — 54/54 passed (was 43
  before this phase; +11 new ASR tests), 0 failed, 0 skipped.
- Did not re-run `tests/SharpAudio.Api.IntegrationTests` (Docker-based, requires downloaded TTS
  models) or the opt-in `AsrCircularTests` this phase — no production code changed, only test
  additions plus one pre-existing test file edit that doesn't touch ASR-model-requiring code
  paths.

### Circular TTS->ASR model tests (done, implemented ahead of schedule)

**Goal**: real end-to-end evaluation - synthesize real audio via Supertonic-3 (multiple preset
speakers), feed it into Whisper/Nemotron, and check transcription quality - as an opt-in suite that
never runs in CI (downloads real multi-hundred-MB-to-GB models and runs real inference).

**Design**:
- `tests/SharpAudio.Api.IntegrationTests/TestData/test_text.md`: human-readable corpus - 3 short
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
asset files concurrently - causing an intermittent 500 (this affected TTS models too, not just
ASR, it just hadn't been hit before). **Fixed**: `ModelCache` now uses a per-model-name
`SemaphoreSlim` to serialize concurrent ensure-calls for the same model.

**Validation performed**:
- `./dotnet.sh build SharpAudio.slnx` → 0 errors. `./dotnet.sh test tests/SharpAudio.Api.Tests` → 43/43
  still pass (ModelCache fix didn't regress anything).
- Ran `AsrCircularTests` unfiltered without `ASR_MODEL_TESTS` set → all 12 cases correctly show as
  **Skipped**, 0 downloads triggered, ~60ms total (confirms the gate is truly zero-cost by default).
- Ran one opt-in case for real (`short-1` × `nemotron-3.5`) against a **warm** model cache (to
  isolate from the download-race bug above, which is now fixed but wasn't yet at test time): the
  full pipeline (TTS synthesis → HTTP → ASR transcription → WER check) executed correctly
  end-to-end in ~6s with **no crash**. The test *failed* its WER assertion at the time (100% WER,
  garbled output) - this was the Nemotron accuracy caveat tracked in Phase 3, since root-caused and
  fixed (see Phase 3's "Update" note) - all `nemotron-3.5` circular test cases now pass (~1-2% WER).
  This is what the test infrastructure was designed to catch: it correctly detected the accuracy
  gap when it existed, and now confirms the fix.
- Whisper cases are expected to still hit the Phase 5/6-tracked native-library-load issue in
  container environments until that's fixed.

**Actual output files**:
- `tests/SharpAudio.Api.IntegrationTests/TestData/test_text.md` (new)
- `tests/SharpAudio.Api.IntegrationTests/Support/AsrTestCorpus.cs` (new)
- `tests/SharpAudio.Api.IntegrationTests/Support/WordErrorRate.cs` (new)
- `tests/SharpAudio.Api.IntegrationTests/Support/WavTestUtils.cs` (new)
- `tests/SharpAudio.Api.IntegrationTests/Support/AsrModelTestGate.cs` (new)
- `tests/SharpAudio.Api.IntegrationTests/AsrCircularTests.cs` (new)
- `tests/SharpAudio.Api.IntegrationTests/SharpAudio.Api.IntegrationTests.csproj` (modified: added
  `Xunit.SkippableFact` package + `TestData/test_text.md` copy-to-output)
- `src/SharpAudio.Api/Services/ModelCache.cs` (modified: per-model-name locking, bug fix)
- `tests/run-tests.sh` (modified: new `asr-model-tests` mode)
- `docker-compose.test.yml` (modified: new `asr-model-tests` service, self-hosting, no `sharp-audio`
  dependency, `ASR_MODEL_TESTS=1`)
- `Dockerfile.tests` (modified: added `libgomp1` so Whisper's native lib has a chance to load)
- `.github/workflows/ci-tests.yml` (modified: integration-tests job now runs `dotnet test ... --filter "Category!=AsrModelTests"`, explicit belt-and-suspenders exclusion alongside the env-var gate)

**How to run**: `ASR_MODEL_TESTS=1 dotnet test tests/SharpAudio.Api.IntegrationTests/... --filter
"Category=AsrModelTests"`, or `./tests/run-tests.sh asr-model-tests` (docker-compose-based).

---

## Phase 9 — Documentation update (final)
**Status**: ✅ Done (awaiting acceptance)

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

**Actual output files**:
- `README.md` (modified): ASR-aware intro/features, `SERVER_MODE` quick-start example, ASR
  endpoints in the endpoint table, ASR models table, `SERVER_MODE` env var, opt-in
  `ASR_MODEL_TESTS` testing example, architecture diagram pointer.
- `docs/ARCHITECTURE.md` (modified): intro/overview note about `SERVER_MODE`, new "ASR Layer
  (parallel to TTS)" subsection under Core Components with a TTS↔ASR component-mapping table and
  engine descriptions, updated endpoint list, updated "Adding New Models" extension point.
- `docs/API.md` (modified): new `GET /api/server-info`, `GET /api/asr-models`, and
  `POST /v1/audio/transcriptions` endpoint docs (request/response/headers/errors), cURL and
  Python (`requests` + OpenAI SDK) transcription examples.
- `docs/CONFIGURATION.md` (modified): `SERVER_MODE` env var, new "Worker Process Configuration"
  section (`WorkerOptions__*`/`AsrWorkerOptions__*`), ASR model config.json fields + Whisper/
  Nemotron examples, `SERVER_MODE`/`AsrWorkerOptions__*` added to the docker-compose.yml and
  `.env` examples.
- `docs/MODELS.md` (modified): retitled to "TTS & ASR Models Documentation", new ASR row in the
  Model Overview table, new "ASR Models" section (Whisper Base + Nemotron 3.5 ASR, including the
  documented feature-extraction accuracy caveat), ASR licensing entries.
- `docs/DEPLOYMENT.md` (modified): `SERVER_MODE`/`AsrWorkerOptions__*` added to the production
  docker-compose example (with a memory-sizing note for ASR), new "Choosing a Server Mode / Image
  Variant" subsection (`docker build --target runtime-tts/-asr/-all`), production checklist items
  for `SERVER_MODE` and `/api/server-info` verification.
- `docs/TROUBLESHOOTING.md` (modified): new "ASR Issues" section covering the real bugs found
  during implementation (Whisper native-lib/`LD_LIBRARY_PATH` failure, 16kHz-input requirement,
  `ModelCache` concurrent-download race, Nemotron accuracy caveat, `SERVER_MODE`-gated 404s), plus
  a `SERVER_MODE` check added to the existing 404 troubleshooting steps.
- `docs/LLM_WIKI.md`: already kept current phase-by-phase throughout Phases -1-8 (per the
  established maintenance rule) - no further changes needed for Phase 9 beyond what was already
  recorded.

**Verification performed**:
- Manual review pass across all six docs for stale "TTS-only"/single-worker phrasing - none found
  beyond what was intentionally left as historical context (e.g. `SUPPORTED` model badges).
- No build step applies to Markdown docs; cross-checked all newly-documented env vars
  (`SERVER_MODE`, `AsrWorkerOptions__*`) and endpoint contracts (`/api/server-info`,
  `/api/asr-models`, `/v1/audio/transcriptions`) directly against `Program.cs`/`Options/*.cs`/
  `Contracts/*.cs` source to avoid drift.

---

## Phase 10 — Nemotron auto-detect-language fix
**Status**: ✅ Done (awaiting acceptance)

**Inputs**: the working (per Phase 3/8) Nemotron engine; a user report that Nemotron returns an
empty transcript for audio where Whisper succeeds.

**Root cause**: confirmed against HuggingFace's own `transformers` docs for
`nvidia/nemotron-3.5-asr-streaming-0.6b` - `Nemotron3_5AsrConfig.default_prompt_id=101` is the
model's own default and is the "auto-detect" language-prompt slot, matching our own
`NemotronLanguages.CodeToId["auto"]=101`. But `NemotronLanguages.Resolve` fell back to `0`
(English) whenever no language was given/recognized - and the frontend sends no `language` param
by default (`asrForm.language = ''` in `App.vue`). For non-English audio, this wrong language
conditioning desensitizes the RNNT decoder, which then emits mostly/only blank tokens -> empty
text. Whisper doesn't have this failure mode since it truly auto-detects.

**Changes**:
1. `Services/NemotronLanguages.cs`: `Resolve`'s fallback changed from `0` to `101` for
   null/empty/unrecognized language input.
2. `Services/NemotronVocabulary.cs`: added a `Decode(tokenIds, out string? detectedLanguageTag)`
   overload that extracts the leading `<xx-XX>`-shaped language tag the model emits in auto-detect
   mode (previously silently discarded along with other control tokens) via a small regex match;
   the existing no-out-param `Decode` now delegates to it, discarding the tag.
3. `Services/NemotronAsrEngine.cs`'s `Transcribe()`: uses the new decode overload so
   `DetectedLanguage` reflects the model's own detection in auto mode instead of just echoing back
   the (possibly null) input language.
4. `frontend/src/App.vue`/`frontend/src/components/LanguageSelector.vue`: added an explicit
   "Auto-detect" chip (prepended to the language list whenever `supportsLanguageAutoDetect` is
   true, including for Whisper which previously had no visible language selector at all since its
   `SupportedLanguages` array is empty), made it the real default via `syncAsrModelDefaults()`
   (`asrForm.language = 'auto'` instead of `''`), and sent it through explicitly to the server
   rather than omitting the field - both paths now converge to the same fixed `101` lang_id.

**Actual output files**:
- `src/SharpAudio.Api/Services/NemotronLanguages.cs` (modified: default fallback)
- `src/SharpAudio.Api/Services/NemotronVocabulary.cs` (modified: language-tag-aware decode overload)
- `src/SharpAudio.Api/Services/NemotronAsrEngine.cs` (modified: uses new decode overload)
- `frontend/src/App.vue` (modified: `asrLanguageOptions` computed, `syncAsrModelDefaults`,
  `transcribe()` comment)
- `frontend/src/components/LanguageSelector.vue` (modified: `'auto': 'Auto-detect'` display label)
- `tests/SharpAudio.Api.Tests/NemotronLanguagesTests.cs` (new)
- `tests/SharpAudio.Api.Tests/NemotronVocabularyTests.cs` (new)

**Verification**:
- `./dotnet.sh build SharpAudio.slnx` → 0 errors.
- `./dotnet.sh test tests/SharpAudio.Api.Tests` → 66/66 passed (was 54; +12 new tests).
- `cd frontend && npx vue-tsc --noEmit && pnpm run build` → 0 type errors, build succeeds.
- Not yet re-run: the opt-in `AsrCircularTests`/a live non-English-audio smoke test against real
  Nemotron weights (no model weights available in this pass) - the fix is verified by
  build/unit-test only so far; recommend a real-audio check before flipping to Accepted.

---

## Phase 11 — Universal audio input format support (ffmpeg)
**Status**: ✅ Done (awaiting acceptance)

**Goal**: fix the crash/`NotSupportedException` when a non-WAV file (FLAC, MP3, etc.) is uploaded
to `/v1/audio/transcriptions`, by normalizing every upload to PCM16 mono WAV before either ASR
engine sees the bytes.

**Library decision**: evaluated NAudio and pure-managed alternatives - NAudio's real codecs are
Windows-only (ACM/Media Foundation), and managed decoders like NLayer only cover MP3, not
FLAC/OGG/WEBM/M4A. Chose `ffmpeg`, shelled out via `ProcessStartInfo` mirroring the existing
pattern in `Services/WorkerProcessManager.cs`.

**Real bug found and fixed during verification (not just a hypothetical)**: initially, piping
ffmpeg's WAV output straight through `pipe:1` produced files that `WavAudioUtils`'s strict parser
rejected as "no data" for every single conversion, including ones that should have worked -
confirmed via a real Docker-image test run (not just unit tests), then root-caused by inspecting
the raw output bytes: **ffmpeg cannot seek on a non-seekable stdout pipe, so it writes a
placeholder `0xFFFFFFFF` for both the RIFF chunk size and the `data` chunk size** (it doesn't know
the true length until done and can't go back to patch the header). `WavAudioUtils.TryReadHeader`'s
`dataSize > 0` check then rejected the file (`0xFFFFFFFF` reads as `-1` via `BitConverter.ToInt32`).
This would have made every upload fail once wired in, not just FLAC/MP3 - fixed by having
`AudioFormatConverter` patch the RIFF/data chunk size fields itself once the true output length is
known (trivial since the bytes are already fully buffered in memory before returning).

**Changes**:
1. New `Services/AudioFormatConverter.cs`: `ToPcm16WavAsync(byte[], CancellationToken)` pipes
   bytes to `ffmpeg -i pipe:0 -vn -ac 1 -acodec pcm_s16le -f wav pipe:1` via redirected
   stdin/stdout (stdout/stderr drained concurrently with the stdin write to avoid a pipe
   deadlock), throws with ffmpeg's stderr on failure, and patches the RIFF/data chunk sizes in the
   returned bytes (see bug above).
2. `Program.cs`'s `/v1/audio/transcriptions` handler: calls this unconditionally right after
   reading the uploaded file bytes, before `IModelCache.EnsureModelAsync`/`IAsrTranscriber.
   TranscribeAsync`; catches `InvalidOperationException`/`Win32Exception` (e.g. ffmpeg missing)
   and returns a `400 invalid_request` instead of a 500.
3. `Dockerfile`: `ffmpeg` added to the `runtime-asr`/`runtime-all` stages only (not `runtime-tts`).
4. `Dockerfile.tests`: `ffmpeg` added so integration tests can exercise real conversion.
5. Docs: `docs/API.md`/`docs/TROUBLESHOOTING.md` updated to describe the widened format support
   and document the fixed bugs.

**Actual output files**:
- `src/SharpAudio.Api/Services/AudioFormatConverter.cs` (new)
- `src/SharpAudio.Api/Program.cs` (modified: wiring + error handling + endpoint description)
- `Dockerfile` (modified: `ffmpeg` in `runtime-asr`/`runtime-all`)
- `Dockerfile.tests` (modified: `ffmpeg` added)
- `tests/SharpAudio.Api.IntegrationTests/Support/FfmpegTestGate.cs` (new)
- `tests/SharpAudio.Api.IntegrationTests/Support/FfmpegTestEncoder.cs` (new, test-only reverse
  encoder used to build FLAC/MP3 fixtures)
- `tests/SharpAudio.Api.IntegrationTests/AudioFormatConverterTests.cs` (new: FLAC/MP3 round-trip,
  gated only on ffmpeg's presence, no model weights needed)
- `tests/SharpAudio.Api.IntegrationTests/AsrCircularTests.cs` (modified: added
  `Offline_NonWavUpload_TranscribesSuccessfully`, an opt-in real end-to-end FLAC upload test
  gated on both `AsrModelTestGate` and `FfmpegTestGate`; `TranscribeAsync` gained optional
  `fileName`/`contentType` params)
- `docs/API.md`, `docs/TROUBLESHOOTING.md` (modified)

**Verification**:
- `./dotnet.sh build SharpAudio.slnx` → 0 errors.
- `./dotnet.sh test tests/SharpAudio.Api.Tests` → 66/66 passed (unchanged).
- `./dotnet.sh test tests/SharpAudio.Api.IntegrationTests --filter FullyQualifiedName~AudioFormatConverterTests`
  → correctly **Skipped** (no ffmpeg in the plain SDK image `dotnet.sh` uses), proving the gate
  works.
- **Real verification, not just skip-checking**: built `Dockerfile.tests` (now includes `ffmpeg`)
  into a throwaway image and ran the same test filter inside it for real - initially **failed**
  (caught the pipe-header bug above), then **passed** after the fix (both FLAC and MP3
  round-trips produced a normalized WAV with the correct ~1.0s duration). Also re-ran the full
  unit test suite (66/66) inside that image to confirm no regressions. Throwaway image deleted
  afterward.
- Not yet run: the new opt-in `Offline_NonWavUpload_TranscribesSuccessfully` (needs real
  downloaded model weights, `ASR_MODEL_TESTS=1`) - not exercised in this pass since no models were
  downloaded in this environment; recommend running it before flipping this phase to Accepted.

---

## Phase 12 — VAD support (Nemotron) + language UI polish
**Status**: ✅ Done (awaiting acceptance)

**Goal**: wire the currently-downloaded-but-unused `silero_vad.onnx` asset into real chunk-gating
during Nemotron transcription (ported from the validated Python reference's `VadGate`/
`SileroVadOrt` classes in `pyscripts/nemotron_speech_ort_only.py`), exposed as an opt-in
`use_vad` request field and a frontend checkbox shown only for models with `SupportsVad=true`.

**Changes**:
1. `Models/AsrModelDefinition.cs` + `Contracts/AsrModelDefinitionResponse.cs`: new `SupportsVad`
   bool field (`true` for `nemotron-3.5`, `false` for `whisper-base`), set in both `config.json`'s
   `asrModels` array and `Services/AsrModelCatalog.cs`'s hardcoded defaults.
2. New `Services/ISileroVadEngine.cs`/`Services/SileroVadEngine.cs`: raw Silero VAD ONNX session
   wrapper (`Reset()`, `ContainsSpeech(samples, threshold)`), ported from `SileroVadOrt` - fixed
   `input`/`state`/`sr` → `output`/`stateN` tensor contract (Silero's own ONNX export, not
   config-driven), `(2,1,128)` LSTM state, windowed processing with a carried-context buffer.
   Exposed behind an `ISileroVadEngine` interface so the chunk-gating policy is unit-testable
   without needing real model weights.
3. New `Services/SileroVadGate.cs`: consecutive-silence chunk-gating policy, ported from `VadGate`
   (`ShouldDropChunk`), thresholds computed from `genai_config.json`'s `vad` section
   (`threshold`, `silence_duration_ms`, `prefix_padding_ms`, with the same fallback defaults as
   the Python reference for configs/models predating that section).
4. `Contracts/AudioTranscriptionRequest.cs`: new `EnableVad` bool (form field `use_vad`,
   `true`/`1`). `Program.cs` parses it; `NemotronAsrTranscriber` passes it into
   `NemotronAsrEngine.Transcribe(wavBytes, language, enableVad)`. `NemotronAsrEngine` lazily
   constructs its `SileroVadEngine` (and resets its state fresh per utterance, mirroring how
   `NemotronFeatureExtractor.Reset()` already works) only when VAD is actually requested, and its
   `RunEncoderChunks` loop calls `SileroVadGate.ShouldDropChunk` per raw-audio chunk, `continue`-ing
   past the mel/encoder/decoder work entirely for gated (silent) chunks - matching the Python
   reference's semantics exactly (a skipped chunk doesn't update the streaming feature extractor's
   left-context/mel-cache state either, only the encoder's cache tensors are preserved as last
   known good). Whisper: `EnableVad` is a no-op (ignored, mirrors how an unsupported `language`
   value already behaves) - `WhisperAsrTranscriber` doesn't read the field at all.
5. Frontend: `types.ts`'s `AsrModel` gains `supportsVad`; `App.vue`'s `asrForm` gains
   `enableVad` (reset to `false` in `syncAsrModelDefaults()` when switching to a non-VAD model);
   a checkbox (`.vad-toggle`) rendered only when `selectedAsrModel?.supportsVad`; `transcribe()`
   appends `use_vad=true` to the FormData only when checked (omitted otherwise, matching the
   `language` field's omit-when-empty convention).
6. `docs/API.md`/`docs/CONFIGURATION.md`/`docs/MODELS.md` updated with the new field/response
   property and VAD behavior description.

**Actual output files**:
- `src/SharpAudio.Api/Services/SileroVadEngine.cs` (new, includes `ISileroVadEngine`)
- `src/SharpAudio.Api/Services/SileroVadGate.cs` (new)
- `src/SharpAudio.Api/Services/NemotronAsrEngine.cs` (modified: vad config parsing, lazy VAD engine,
  gating in `RunEncoderChunks`, `Transcribe` gains `enableVad` param, `Dispose` disposes the VAD
  engine if created)
- `src/SharpAudio.Api/Services/NemotronAsrTranscriber.cs` (modified: passes `request.EnableVad`)
- `src/SharpAudio.Api/Contracts/AudioTranscriptionRequest.cs` (modified: `EnableVad`)
- `src/SharpAudio.Api/Models/AsrModelDefinition.cs`,
  `src/SharpAudio.Api/Contracts/AsrModelDefinitionResponse.cs` (modified: `SupportsVad`)
- `src/SharpAudio.Api/Services/AsrModelCatalog.cs`, `src/SharpAudio.Api/config.json` (modified:
  `supportsVad` per model)
- `src/SharpAudio.Api/Program.cs` (modified: `use_vad` form parsing, `/api/asr-models` response,
  endpoint description)
- `frontend/src/types.ts`, `frontend/src/App.vue` (modified: VAD checkbox + state)
- `tests/SharpAudio.Api.Tests/SileroVadGateTests.cs` (new: fake-engine-based policy tests, no real
  weights needed)
- `tests/SharpAudio.Api.Tests/AsrModelCatalogTests.cs` (modified: `SupportsVad` assertions)
- `tests/SharpAudio.Api.IntegrationTests/AsrCircularTests.cs` (modified: new opt-in
  `Offline_NemotronWithVad_TranscribesWithoutCrashing` case; `TranscribeAsync` gained an
  `enableVad` param)
- `docs/API.md`, `docs/CONFIGURATION.md`, `docs/MODELS.md` (modified)

**Verification**:
- `./dotnet.sh build SharpAudio.slnx` → 0 errors.
- `./dotnet.sh test tests/SharpAudio.Api.Tests` → 69/69 passed (was 66; +3 new `SileroVadGateTests`
  covering never-drop-during-speech, drop-after-configured-silence-duration, and
  counter-reset-on-speech-return).
- `cd frontend && npx vue-tsc --noEmit && pnpm run build` → 0 type errors, build succeeds.
- `./dotnet.sh test tests/SharpAudio.Api.IntegrationTests --filter Category=AsrModelTests` → all 14
  cases (including the new VAD case) correctly show as **Skipped** by default - confirms the gate
  is still zero-cost.
- Not yet run: the opt-in `Offline_NemotronWithVad_TranscribesWithoutCrashing` (needs real
  downloaded Nemotron + Silero VAD weights) - not exercised in this pass since no models were
  downloaded in this environment; recommend running it before flipping this phase to Accepted.

---

## Reference files (existing code whose patterns are mirrored)
- `src/SharpAudio.Api/Services/TtsSynthesizerRouter.cs` — pattern for `AsrTranscriberRouter`.
- `src/SharpAudio.Api/Services/SupertonicTtsEngine.cs` — multi-ONNX-session pattern for
  `NemotronAsrEngine`.
- `src/SharpAudio.Api/Services/KokoroTtsEngine.cs` / `KokoroTtsSynthesizer.cs` — single-session
  pooling pattern, followed exactly by `WhisperAsrEngine`/`WhisperAsrTranscriber`.
- `src/SharpAudio.Api/Services/WorkerProcessManager.cs`, `Services/WorkerProxySynthesizer.cs`,
  `src/SharpAudio.Worker/Program.cs`, `Services/WorkerSynthesisService.cs` — worker process pattern
  for `SharpAudio.Worker.Asr`.
- `src/SharpAudio.Api/Protos/synthesis.proto` — pattern for `transcription.proto`.
- `src/SharpAudio.Api/Program.cs` — current DI wiring/endpoint mapping to extend.
