# FastTTSR Wiki — ASR Engines (Whisper + Nemotron)

> Linked from [docs/LLM_WIKI.md](../LLM_WIKI.md). Covers the full ASR-side architecture: domain
> model/catalog/contracts, the Whisper and Nemotron engines (including their bugfix narratives),
> universal audio format support, VAD support, worker process/DI wiring, REST + streaming
> endpoints, and the current known issues/roadmap. For the TTS-side equivalents, see
> [docs/wiki/backend-architecture.md](backend-architecture.md). For the phase-by-phase build log
> this section summarizes, see [docs/ASR_IMPLEMENTATION_PLAN.md](../ASR_IMPLEMENTATION_PLAN.md)
> and [docs/ASR_IMPLEMENTATION_HISTORY.md](../ASR_IMPLEMENTATION_HISTORY.md).

## Domain model & abstractions
Parallel abstraction to the TTS one, added alongside it (not replacing it):
- `Models/AsrModelDefinition.cs`: `Name`, `DisplayName`, `Description`, `Engine` (`"whisper"` |
  `"nemotron-3.5"`), `ModelPath`, `Assets` (`IReadOnlyList<ModelAsset>`, shared type with TTS),
  `SupportedLanguages`, `SupportsLanguageAutoDetect`, `SupportsVad`, `SupportsStreaming`.
- `Services/IAsrModelCatalog.cs` + `AsrModelCatalog.cs` (mirrors `ModelCatalog`): loads the
  `"asrModels"` array from the same `config.json` (same `MODEL_CONFIG_PATH` env var), falls back to
  hardcoded defaults (`whisper-base`, `nemotron-3.5`) if config is missing.
- `Services/IAsrTranscriber.cs`: `TranscribeAsync(AsrModelDefinition model, string modelDirectory,
  AudioTranscriptionRequest request, byte[] audioBytes, CancellationToken ct) ->
  Task<TranscriptionResult>` — the ASR equivalent of `ITtsSynthesizer`.
- `Services/IIdleTrackingTranscriber.cs` — ASR equivalent of `IIdleTrackingSynthesizer`.
- `Models/TranscriptionResult.cs`: `Text`, `DetectedLanguage`, `ProcessingTimeSeconds`,
  `AudioDurationSeconds`, `CharacterCount`, computed `Rtf`.
- `Contracts/AudioTranscriptionRequest.cs` (`Model`, `Language?`, `ResponseFormat`, `EnableVad` —
  the file itself is passed as a separate `byte[]`), `Contracts/AsrModelDefinitionResponse.cs`,
  `Contracts/ServerInfoResponse.cs` (`{ TtsEnabled, AsrEnabled }`, backs `GET /api/server-info`).
- `IModelCache` gained a second overload: `EnsureModelAsync(AsrModelDefinition, ct)`, sharing the
  same per-model-name locking as the TTS overload (see [docs/wiki/testing.md](testing.md)).
- `config.json` has a sibling top-level `"asrModels"` array (same shape philosophy as `"models"`):
  `whisper-base` (engine `"whisper"`, single GGML asset) and `nemotron-3.5` (engine
  `"nemotron-3.5"`, encoder/decoder/joint ONNX + audio processor config + tokenizer + silero VAD,
  from `onnx-community/nemotron-3.5-asr-streaming-0.6b-onnx-int4`).

## Whisper engine
- NuGet: `Whisper.net` + `Whisper.net.Runtime` (added to `FastTTSR.Api.csproj`; native whisper.cpp
  binaries ship inside `Whisper.net.Runtime`).
- `Services/WhisperAsrEngine.cs`: wraps one GGML model file via `WhisperFactory.FromPath(...)`;
  `TranscribeAsync(byte[] wavBytes, string? language, ct)` resamples to 16kHz mono via
  `WavAudioUtils.ResampleToMono16kWav` (Whisper.net requires exactly 16kHz, no internal
  resampling), builds a processor (`.CreateBuilder().WithLanguage(language ?? "auto").Build()`),
  feeds the WAV to `processor.ProcessAsync(...)` (an `IAsyncEnumerable<SegmentData>`), concatenates
  `segment.Text` across all segments, and picks up `segment.Language` as the detected language when
  auto-detecting.
- `Services/WhisperAsrTranscriber.cs`: implements `IAsrTranscriber` + `IIdleTrackingTranscriber`,
  pools one `WhisperAsrEngine` per `"{model.Name}:{modelDirectory}"` key (mirrors
  `KokoroTtsSynthesizer`'s pooling pattern exactly). Rejects any model whose `Engine` isn't
  `"whisper"`.
- `Services/WavAudioUtils.cs`: shared helper — reads a RIFF/WAV header (no external audio library)
  for duration metrics, `ReadMonoFloat`/resampling, `ResampleToMono16kWav`.
- Streaming: `WhisperStreamingSession` implements `IStreamingTranscriptionSession` via a
  buffer-and-periodically-re-transcribe approach (no true incremental decode API in Whisper.net).

## Nemotron engine
Cache-aware streaming FastConformer-RNNT via raw `Microsoft.ML.OnnxRuntime` (3 chained sessions:
encoder/decoder/joint), mirrors `SupertonicTtsEngine`'s multi-session-in-one-class pattern. Exact
tensor contract was recovered via a throwaway C# spike (`InferenceSession.InputMetadata`/
`OutputMetadata`) — full details in
[docs/ASR_IMPLEMENTATION_HISTORY.md#phase-3](../ASR_IMPLEMENTATION_HISTORY.md#phase-3--nemotron-engine-spike--implementation).

- **Encoder** (24 layers, hidden=1024): consumes fixed-size log-mel chunks (128 mels;
  `chunk_samples` raw audio per chunk from `genai_config.json`, prefixed with 9 cached lookback
  frames), cache-aware via `cache_last_channel`/`cache_last_time`/`cache_last_channel_len` tensors
  threaded chunk-to-chunk, conditioned by a `lang_id` integer resolved via
  `Services/NemotronLanguages.cs`'s fixed lookup table (default `101` = auto-detect). Emits ~7
  encoded frames/chunk.
- **Decoder/predictor** (`RunDecoderStep`): LSTM-based (2 layers, hidden=640), not
  attention-based. Its raw output is (batch, hidden, seq) and must be transposed before the joint
  step.
- **Joint** (`RunJoint`): combines one encoder frame + one decoder step, argmax over 13088 vocab
  entries; RNNT greedy decode loop (`RunRnntGreedyDecode`, up to `max_symbols_per_step` emissions
  per encoder frame, stops on `blank_id`).
- `Services/NemotronVocabulary.cs`: `vocab.txt` (id-per-line) is the token→text decoder
  (SentencePiece `▁` convention); `Decode(tokenIds, out detectedLanguageTag)` also extracts the
  leading `<xx-XX>` auto-detect tag the model emits.
- `Services/NemotronFeatureExtractor.cs`: self-contained **streaming** log-mel spectrogram (own
  radix-2 FFT + Slaney-scale mel filterbank + Hann window centered within the FFT frame), stateful
  across chunks (carries `nFft/2` samples of left-context audio + the previous chunk's trailing
  log-mel frames).
- **All hyperparameters are read from `genai_config.json`** (not `audio_processor_config.json`,
  which is missing several needed fields).
- `Services/NemotronAsrEngine.cs` also owns VAD gating (see below) and the streaming session
  (`NemotronAsrEngine.StreamingSession`, true cache-aware incremental decode, carries encoder cache
  + LSTM predictor state across `ProcessChunkAsync` calls).

### Bugfix history (Nemotron)
1. **Feature-extraction/lang_id bug** (root-caused via real-audio cosine-similarity diagnostics
   comparing encoder output for real speech vs. silence — proved the encoder was ignoring audio
   content): `lang_id` was resolved from `vocab.txt`'s `<en-US>` tag *line index* (2947) instead of
   the small fixed integer (0) the model's language embedding actually expects. Also fixed:
   config source (`genai_config.json` not `audio_processor_config.json`, wrong `log_eps`), mel
   scale (Slaney not HTK), Hann window centering, and true per-chunk streaming framing (not a
   whole-utterance-at-once STFT). **Result**: WER 100% → ~1-2% on paragraphs, near-perfect on a
   20-turn conversation. Full narrative:
   [ASR_IMPLEMENTATION_HISTORY.md#phase-3](../ASR_IMPLEMENTATION_HISTORY.md#phase-3--nemotron-engine-spike--implementation).
2. **Wrong auto-detect default**: `NemotronLanguages.Resolve` defaulted unset/unrecognized language
   to lang_id `0` (English) instead of `101` (the model's real auto-detect slot per HuggingFace's
   own `transformers` docs, `default_prompt_id=101`). Since the frontend sends no `language` by
   default, non-English audio got wrong conditioning → the decoder emitted mostly blank → empty
   transcript. Fixed: default is now `101`; the model's own emitted `<xx-XX>` tag is surfaced as
   `DetectedLanguage` instead of being discarded. Full narrative:
   [ASR_IMPLEMENTATION_HISTORY.md#phase-10](../ASR_IMPLEMENTATION_HISTORY.md#phase-10--nemotron-auto-detect-language-fix).

## Universal audio input format support (ffmpeg)
`/v1/audio/transcriptions` doesn't assume WAV input: `Program.cs` calls
`Services/AudioFormatConverter.cs`'s `ToPcm16WavAsync(byte[], CancellationToken)` unconditionally
right after reading the uploaded file, before either engine sees the bytes. Shells out to `ffmpeg`
(`-i pipe:0 -vn -ac 1 -acodec pcm_s16le -f wav pipe:1`), so any ffmpeg-decodable format (FLAC, MP3,
OGG, WEBM, M4A, WAV, ...) works. `ffmpeg` is installed in the `Dockerfile`'s `runtime-asr`/
`runtime-all` stages (not `runtime-tts`) and in `Dockerfile.tests`. If ffmpeg is missing or the
input is undecodable, the endpoint returns `400 invalid_request` instead of a 500.

**Real bug found during verification**: ffmpeg can't seek on a non-seekable stdout pipe, so it
writes a placeholder `0xFFFFFFFF` for the RIFF/`data` chunk sizes instead of the real ones —
`WavAudioUtils`'s strict parser rejected every converted file as a result. This wasn't caught by
plain unit tests (skip when ffmpeg isn't on PATH) — only caught by building `Dockerfile.tests` into
a real image and running the tests inside it. Fixed: `AudioFormatConverter` patches the RIFF/data
chunk size fields itself once the true output length is known. Full narrative:
[ASR_IMPLEMENTATION_HISTORY.md#phase-11](../ASR_IMPLEMENTATION_HISTORY.md#phase-11--universal-audio-input-format-support-ffmpeg).

## VAD support (Nemotron only)
`AsrModelDefinition`/`AsrModelDefinitionResponse` have `SupportsVad` (`nemotron-3.5`: true,
`whisper-base`: false). `Services/SileroVadEngine.cs` (behind an `ISileroVadEngine` interface, for
unit-testability without real weights) wraps the bundled `silero_vad.onnx` asset — `(2,1,128)` LSTM
state carried across windowed calls. `Services/SileroVadGate.cs` implements a consecutive-silence
chunk-gating policy (thresholds from `genai_config.json`'s `vad` section). Wired via
`Contracts/AudioTranscriptionRequest.cs`'s `EnableVad` (form field `use_vad`) →
`NemotronAsrEngine.Transcribe(..., enableVad)`: when enabled, `RunEncoderChunks` calls
`SileroVadGate.ShouldDropChunk` per raw-audio chunk and `continue`s past mel/encoder/decoder work
entirely for gated (silent) chunks — a skipped chunk doesn't advance the streaming feature
extractor's left-context/mel-cache state either. Whisper ignores `EnableVad` entirely (no-op).
Frontend: `AsrModel.supportsVad` gates a `.vad-toggle` checkbox in the ASR panel.

## Worker process, DI wiring & SERVER_MODE
- **`SERVER_MODE`** env var (`tts` (default) | `asr` | `both`) parsed at the top of `Program.cs`
  into `ttsEnabled`/`asrEnabled` booleans. The pre-existing TTS registration block is wrapped in
  `if (ttsEnabled)` **with no internal changes** (byte-identical when `SERVER_MODE` unset). A
  parallel `if (asrEnabled)` block mirrors it for ASR.
- **Two independent worker types** run simultaneously: TTS's `WorkerProcessManager` stays a plain
  (non-keyed) singleton; ASR's is a **keyed singleton** (`AddKeyedSingleton<WorkerProcessManager>
  ("asr", ...)`) with its own `WorkerOptions` (mapped from `AsrWorkerOptions`,
  `Options/AsrWorkerOptions.cs`) — distinct port range (`50151+` vs TTS's `50051+`) and executable
  path (`./worker-asr/FastTTSR.Worker.Asr`).
- **`FastTTSR.Worker.Asr`** (mirrors `FastTTSR.Worker`): same CLI args, Kestrel-HTTP/2 startup
  pattern. `Services/WorkerTranscriptionService.cs` mirrors `WorkerSynthesisService`'s
  single-active-engine-with-lock pattern, routing by `request.Engine`.
- **`Protos/transcription.proto`** (own `csharp_namespace = "FastTTSR.Worker.Asr.Grpc"`):
  `WorkerTranscription` service with `Transcribe`/`HealthCheck` unary RPCs, plus `TranscribeStream`
  - a bidirectional streaming RPC (`stream TranscribeStreamChunk` in, `stream TranscribeStreamUpdate`
  out) used for live transcription in worker mode. The first request message must set `config`
  (`StreamConfig`: model_name/engine/model_path/language/use_vad/segment_seconds); every subsequent
  message sets `audio_chunk` (raw PCM16 mono bytes). `WorkerTranscriptionService.TranscribeStream`
  creates the right engine's `IStreamingTranscriptionSession` (same routing as the unary RPC) and
  streams back `{ text, is_final, is_segment_final }` updates - `text` is always scoped to the
  CURRENT segment only (never the whole conversation), `is_segment_final` marks a mid-stream
  segment commit, `is_final` marks the true end of the call.
- **`Services/AsrWorkerProxyTranscriber.cs`** (mirrors `WorkerProxySynthesizer`): implements
  `IAsrTranscriber`, resolves its `WorkerProcessManager` via `[FromKeyedServices("asr")]`.
  `CreateStreamingSessionAsync` opens a duplex `TranscribeStream` call on the pooled worker channel
  and wraps it in a private `WorkerStreamingSession` adapter implementing
  `IStreamingTranscriptionSession` (`ProcessChunkAsync` writes a chunk then drains buffered updates
  from a background read-loop task, collapsing consecutive in-progress updates to the latest one
  but forwarding every segment-commit exactly once; `FinishAsync` completes the request stream and
  returns the final `is_final` update's text) - a drop-in for the same interface the in-process
  engines implement, so the WS handler in `Program.cs` doesn't need to know which mode is active.
- **`Services/AsrTranscriberRouter.cs`** (in-process mode, mirrors `TtsSynthesizerRouter`): routes
  by `model.Engine == "nemotron-3.5"` else defaults to Whisper.
- **`Services/AsrModelWarmupService.cs`** / **`Services/AsrModelIdleMonitorService.cs`** mirror the
  TTS equivalents; the idle monitor reuses the same `ModelIdleMonitorOptions`/
  `MODEL_IDLE_TIMEOUT_SECONDS` config as TTS.

## REST + streaming endpoints
- `GET /api/server-info` (always mapped) → `{ ttsEnabled, asrEnabled }`.
- `GET /api/asr-models` (only if `asrEnabled`) → list of `AsrModelDefinitionResponse`.
- `POST /v1/audio/transcriptions` (only if `asrEnabled`, OpenAI-compatible): multipart/form-data
  (`file`, `model`, `language?`, `response_format?`, `min_segment_duration?`, `use_vad?`), returns
  `{ text }` JSON by default, or `{ text, language, duration, segments: [{id,start,end,text}] }`
  when `response_format=verbose_json` (see "VAD/timestamp segments" below), with metrics in
  response headers (`X-Processing-Time`, `X-Audio-Duration`, `X-RTF`, `X-Character-Count`).
- `GET /v1/audio/transcriptions/stream` (only if `asrEnabled`, WebSocket upgrade): works in both
  in-process and worker mode - query params `model` (required)/`language`/`use_vad`/
  `segment_seconds` (optional, default 10, clamped to `[AsrStreamingOptions.MinSegmentSeconds,
  MaxSegmentSeconds]` = `[5,30]`); binary PCM16 16kHz mono frames in, JSON frames out. Every
  outbound message is scoped to the CURRENT segment only, never the whole conversation, so message
  size stays bounded regardless of utterance length: `{"type":"partial","text":...}` as the
  in-progress segment's text updates, `{"type":"segment","text":...}` once a segment is committed
  (time limit or sentence-ending punctuation - see `SegmentBoundaryPolicy`), and
  `{"type":"final","text":...}` (the trailing, not-yet-committed segment) once the session ends. A
  `{"type":"end"}` text frame (or socket close) triggers the final message. Resolved via
  `IAsrTranscriber.CreateStreamingSessionAsync(...)`, which hides the in-process-vs-worker-mode
  distinction behind a duplex gRPC call in worker mode (see `AsrWorkerProxyTranscriber` below).
  Nemotron's cache-aware encoder/decoder state is never reset at a segment boundary (only the
  emitted/decoded text window is bounded); Whisper's retained PCM buffer IS trimmed down to a
  ~0.5s overlap at each commit, since its per-pass cost is a real function of buffer size.
- `GET /v1/models` merges `IModelCatalog`/`IAsrModelCatalog` results.
- `/api/models` and `/v1/audio/speech` are wrapped in `if (ttsEnabled)` with zero internal changes.

## Live transcription (frontend)
`frontend/src/components/LiveTranscription.vue`: mic capture via `getUserMedia`, tab/system audio
via `getDisplayMedia`, resampled to 16kHz PCM16 in an `AudioWorkletNode`
(`frontend/src/audio-worklets/pcm-capture-processor.js`), streamed over the WebSocket endpoint
above (including a `segment_seconds` query param sourced from a numeric input next to the VAD
toggle in `App.vue`, default 10). Maintains two pieces of state: `committedLines` (one entry per
`segment`/`final` message, each timestamped client-side at receipt time and rendered on its own
line, high-contrast/`#e5e5e5`) and `liveText` (the in-progress `partial` segment, rendered in gray/
`#888888` right after the committed lines) - gives a visual distinction between already-committed
and still-being-transcribed text, with no backend timestamp support needed. Wired into `App.vue`'s
ASR panel behind `v-if="selectedAsrModel?.supportsStreaming"`.

## VAD/timestamp segments (offline endpoint only)
`Models/TranscriptionSegment.cs` (`Id`, `Start`, `End`, `Text`) + `TranscriptionResult.Segments`.
Requested via `response_format=verbose_json` (+ optional `min_segment_duration`, default `0.5s`) -
mirrors OpenAI's `verbose_json`/`timestamp_granularities` naming, decoupled from the `use_vad`
checkbox (matches OpenAI's independent fields), though VAD is what makes Nemotron's segments
meaningful:
- **Whisper**: segments come directly from Whisper.net's own per-segment `Start`/`End`/`Text` -
  always computed, no extra cost.
- **Nemotron**: `RunEncoderChunks` tracks contiguous "active audio" regions (maximal runs of
  non-VAD-dropped chunks when `use_vad=true`, or one region spanning the whole clip when it's
  `false`); `RunRnntGreedyDecode` additionally returns a per-encoder-frame token count so each
  region's frame range can be sliced out of the flat token stream and decoded into its own segment
  text.
- `Services/SegmentMerger.cs`: segments shorter than `min_segment_duration` are merged into a
  neighboring segment (extends its time range + appends text) rather than dropped, so no
  transcribed text is ever silently lost.
- **Known limitation**: segments are only populated when running ASR in-process
  (`AsrWorkerOptions__Enabled=false`) - the worker gRPC protocol (`transcription.proto`) doesn't
  carry segment data yet, so worker-mode responses return an empty `segments` array even with
  `response_format=verbose_json`.
- Frontend: a standalone "Show timestamps" checkbox (independent of the VAD checkbox) + a
  "min segment duration" number input in the offline ASR panel; `TranscriptionResult.vue` renders
  a timestamped row list instead of the flat text block when segments are present.

## Known issues / roadmap (as of this writing)
- **Segment timestamps only work in-process** (`AsrWorkerOptions__Enabled=false`) -
  `transcription.proto`'s `TranscribeResponse` doesn't carry segment data yet, so worker-mode
  `verbose_json` responses return an empty `segments` array. Extending the proto to carry segments
  isn't scheduled; it would follow the same pattern as `TranscribeStream`'s `StreamConfig`/
  `TranscribeStreamUpdate` messages.
- Live transcription's worker-mode path (`AsrWorkerProxyTranscriber.CreateStreamingSessionAsync`) is
  covered by an opt-in integration test
  (`AsrCircularTests.Streaming_WorkerMode_TranscribesOverWebSocket`) gated on both real model
  weights (`ASR_MODEL_TESTS=1`) and a published `FastTTSR.Worker.Asr` executable being present next
  to the API output - it isn't exercised by a plain `./dotnet.sh test` run in this dev environment
  and should be validated against a real Docker image before being fully trusted in production.

## Updated ASR env var table
| Env var | Purpose | Default |
|---|---|---|
| `SERVER_MODE` | `tts`\|`asr`\|`both` — which task type(s) this instance serves | `tts` |
| `AsrWorkerOptions__Enabled` | ASR worker-mode vs in-process (**must be `false` for live streaming
  to work today**) | true |
| `AsrWorkerOptions__ExecutablePath` | ASR worker binary path | `./worker-asr/FastTTSR.Worker.Asr` |
| `AsrWorkerOptions__PortRangeStart` | first ASR gRPC port to try | 50151 |
| `AsrWorkerOptions__IdleTimeoutSeconds` | ASR worker self-termination timeout | 60 |

## Extension point: Adding a new ASR engine
1. Add a `{Engine}AsrEngine.cs` (owns model session(s), pooled per model+directory — mirror
   `WhisperAsrEngine` for a single-session wrapper or `NemotronAsrEngine` for a multi-session
   chain) and a `{Engine}AsrTranscriber.cs` implementing `IAsrTranscriber`/
   `IIdleTrackingTranscriber` (+ `IStreamingTranscriptionSession` if streaming should be
   supported).
2. Add routing for the new `Engine` string in `AsrTranscriberRouter` and
   `Worker.Asr/Services/WorkerTranscriptionService.cs`.
3. Add model entries to `config.json`'s `"asrModels"` array (and `AsrModelCatalog`'s hardcoded
   defaults).
4. Register the transcriber singleton in `Program.cs` (in-process branch).
