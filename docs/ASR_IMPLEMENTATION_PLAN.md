# ASR Implementation Plan (Whisper + Nemotron)

> **Purpose of this file**: a git-tracked, phase-by-phase implementation plan for adding ASR
> (Speech-to-Text) support to SharpAudio alongside the existing TTS feature. It exists so that work
> can be resumed by a different agent/session/person without losing context. If you are picking
> this up fresh, read [docs/LLM_WIKI.md](LLM_WIKI.md) first for architecture context, then come
> back here to see what's done and what's next.
>
> **This file is a condensed summary.** Full unabridged detail (spike findings, root-cause
> narratives, exact file diffs) for every already-implemented phase lives in
> [docs/ASR_IMPLEMENTATION_HISTORY.md](ASR_IMPLEMENTATION_HISTORY.md) — each phase below links to
> its section there. Only open/in-progress phases keep full detail in this file, since that's what
> an implementer picking up the next phase actually needs.

## How to use this document

1. Read [docs/LLM_WIKI.md](LLM_WIKI.md) for the current architecture (updated after every phase).
2. Find the first phase below whose Status is not `✅ Accepted`.
3. Do the work for that phase only. Don't jump ahead.
4. Build via `./dotnet.sh build SharpAudio.slnx` (no local `dotnet` CLI in this dev environment).
5. Update [docs/LLM_WIKI.md](LLM_WIKI.md) (or the relevant `docs/wiki/*.md` detail file) with what
   you built.
6. Update this file: set the phase's Status to `✅ Done (awaiting acceptance)`, fill in "Actual
   output files", and move its full detail into `docs/ASR_IMPLEMENTATION_HISTORY.md` once accepted
   (replace the block here with a short summary + link, following the existing pattern below).
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
- **Worker granularity**: ONE ASR worker executable (`SharpAudio.Worker.Asr`) that internally routes
  Whisper vs Nemotron by `model.Engine`, exactly mirroring how the existing TTS worker
  (`SharpAudio.Worker`) routes Kokoro vs Supertonic. Two worker executables total.
- **Docker packaging**: single `Dockerfile`, 3 selectable final stages
  (`runtime-tts`/`runtime-asr`/`runtime-all`) via `docker build --target <stage>`. Runtime env var
  `SERVER_MODE` (`tts`|`asr`|`both`, default `"tts"` for backward compatibility) controls which
  endpoints/services are active within the built image.
- **Phasing**: implement both Whisper AND Nemotron fully (not stubbed).
- **Process**: implement phase-by-phase, update `docs/LLM_WIKI.md`/`docs/wiki/*.md` after each
  phase, wait for explicit user acceptance before starting the next phase.
- **Streaming (Phase 13+)**: live transcription only needs to work in-process
  (`AsrWorkerOptions__Enabled=false`) for its initial cut; worker-mode gRPC streaming is a
  separate, later set of phases (15-17) — see below.
- **Timestamps (Phase 14)**: offline endpoint only (not the live WebSocket stream); short segments
  are merged into their neighbor (never dropping transcribed text, never silently discarded); new
  request fields reuse the existing `response_format=verbose_json` field (OpenAI-compatible naming)
  plus a new `min_segment_duration` (seconds, default `0.5`) — our own extension, no OpenAI
  equivalent.

## Status tracker

| Phase | Name | Status |
|---|---|---|
| -1 | LLM_WIKI bootstrap | ✅ Accepted — [full detail](ASR_IMPLEMENTATION_HISTORY.md#phase--1--llm_wiki-bootstrap) |
| 0 | Shared groundwork | ✅ Accepted — [full detail](ASR_IMPLEMENTATION_HISTORY.md#phase-0--shared-groundwork) |
| 1 | ASR domain model & config plumbing | ✅ Accepted — [full detail](ASR_IMPLEMENTATION_HISTORY.md#phase-1--asr-domain-model--config-plumbing) |
| 2 | Whisper engine | ✅ Accepted — [full detail](ASR_IMPLEMENTATION_HISTORY.md#phase-2--whisper-engine) |
| 3 | Nemotron engine (spike + implementation) | ✅ Accepted — [full detail](ASR_IMPLEMENTATION_HISTORY.md#phase-3--nemotron-engine-spike--implementation) |
| 4 | ASR worker process + DI wiring | ✅ Accepted — [full detail](ASR_IMPLEMENTATION_HISTORY.md#phase-4--asr-worker-process--di-wiring) |
| 5 | REST endpoints | ✅ Accepted — [full detail](ASR_IMPLEMENTATION_HISTORY.md#phase-5--rest-endpoints) |
| 6 | Docker/Compose packaging | ✅ Accepted — [full detail](ASR_IMPLEMENTATION_HISTORY.md#phase-6--dockercompose-packaging) |
| 7 | Frontend ASR UI | ✅ Accepted — [full detail](ASR_IMPLEMENTATION_HISTORY.md#phase-7--frontend-asr-ui) |
| 8 | Tests | ✅ Done (awaiting acceptance) — [full detail](ASR_IMPLEMENTATION_HISTORY.md#phase-8--tests) |
| 9 | Documentation update (final) | ✅ Done (awaiting acceptance) — [full detail](ASR_IMPLEMENTATION_HISTORY.md#phase-9--documentation-update-final) |
| 10 | Nemotron auto-detect-language fix | ✅ Done (awaiting acceptance) — [full detail](ASR_IMPLEMENTATION_HISTORY.md#phase-10--nemotron-auto-detect-language-fix) |
| 11 | Universal audio input format support (ffmpeg) | ✅ Done (awaiting acceptance) — [full detail](ASR_IMPLEMENTATION_HISTORY.md#phase-11--universal-audio-input-format-support-ffmpeg) |
| 12 | VAD support + language UI polish | ✅ Done (awaiting acceptance) — [full detail](ASR_IMPLEMENTATION_HISTORY.md#phase-12--vad-support-nemotron--language-ui-polish) |
| 13 | Streaming ASR (mic/tab audio, WebSocket) | ✅ Done (awaiting acceptance) — see below |
| 14 | VAD/timestamp segments (offline endpoint) | ✅ Done (awaiting acceptance) — see below |
| 15 | Worker-mode streaming: proto + worker-side | ✅ Done (awaiting acceptance) — see below |
| 16 | Worker-mode streaming: API proxy + unification | ✅ Done (awaiting acceptance) — see below |
| 17 | Worker-mode streaming: tests + docs sync | ✅ Done (awaiting acceptance) — see below |
| 18 | Swagger + documentation sync (round 2) | Not started — see below |

---

## Phase -1 through 12 — summaries

Full detail for every phase below lives in
[docs/ASR_IMPLEMENTATION_HISTORY.md](ASR_IMPLEMENTATION_HISTORY.md); only a short synthesized
summary is kept here.

- **Phase -1 (LLM_WIKI bootstrap)**: created `docs/LLM_WIKI.md` seeded with the pre-ASR
  architecture, plus a "keep this doc updated" maintenance rule.
  [Detail →](ASR_IMPLEMENTATION_HISTORY.md#phase--1--llm_wiki-bootstrap)
- **Phase 0 (Shared groundwork)**: renamed `TtsModelAsset`→`ModelAsset` (shared by TTS/ASR), moved
  `IdleMonitor` to `SharpAudio.Api` so both worker projects can share it, generalized
  `WorkerProcessManager`'s constructor so multiple independently-configured instances can coexist.
  [Detail →](ASR_IMPLEMENTATION_HISTORY.md#phase-0--shared-groundwork)
- **Phase 1 (ASR domain model & config plumbing)**: `AsrModelDefinition`/`IAsrModelCatalog`/
  `IAsrTranscriber`/`TranscriptionResult`/`AudioTranscriptionRequest` — the ASR-side parallel of
  the TTS abstractions — plus a new `"asrModels"` array in `config.json`. No engine/wiring yet.
  [Detail →](ASR_IMPLEMENTATION_HISTORY.md#phase-1--asr-domain-model--config-plumbing)
- **Phase 2 (Whisper engine)**: `WhisperAsrEngine`/`WhisperAsrTranscriber` via Whisper.net (GGML),
  pooled per model+directory like `KokoroTtsEngine`. Surfaced (and later fixed in Phase 6) a
  native-library-load issue and a 16kHz-only input requirement.
  [Detail →](ASR_IMPLEMENTATION_HISTORY.md#phase-2--whisper-engine)
- **Phase 3 (Nemotron engine)**: `NemotronAsrEngine` — cache-aware streaming FastConformer-RNNT via
  3 raw ONNX Runtime sessions (encoder/decoder/joint), tensor contract recovered via a live spike.
  Originally produced ~100% WER; root-caused (via real-audio cosine-similarity diagnostics, not
  guesswork) to a wildly-out-of-range `lang_id` plus wrong mel-scale/config-source/framing — fixed
  using a user-supplied validated Python reference, dropping WER to ~1-2%.
  [Detail →](ASR_IMPLEMENTATION_HISTORY.md#phase-3--nemotron-engine-spike--implementation)
- **Phase 4 (ASR worker process + DI wiring)**: new `SharpAudio.Worker.Asr` executable + `SERVER_MODE`
  env var (`tts`/`asr`/`both`) driving keyed DI registrations (`AsrWorkerProxyTranscriber` in
  worker mode, `AsrTranscriberRouter` in-process). TTS behavior byte-identical when unset.
  [Detail →](ASR_IMPLEMENTATION_HISTORY.md#phase-4--asr-worker-process--di-wiring)
- **Phase 5 (REST endpoints)**: `GET /api/server-info`, `GET /api/asr-models`,
  `POST /v1/audio/transcriptions`; `GET /v1/models` merges TTS+ASR catalogs.
  [Detail →](ASR_IMPLEMENTATION_HISTORY.md#phase-5--rest-endpoints)
- **Phase 6 (Docker/Compose packaging)**: 3 selectable image targets
  (`runtime-tts`/`runtime-asr`/`runtime-all`); fixed the Whisper.net native-lib load failure via
  `LD_LIBRARY_PATH` and added 16kHz resampling before every Whisper call.
  [Detail →](ASR_IMPLEMENTATION_HISTORY.md#phase-6--dockercompose-packaging)
- **Phase 7 (Frontend ASR UI)**: ASR tab/panel, `AudioFileInput`/`TranscriptionResult` components,
  `/api/server-info`-driven tab visibility.
  [Detail →](ASR_IMPLEMENTATION_HISTORY.md#phase-7--frontend-asr-ui)
- **Phase 8 (Tests)**: unit tests (`AsrModelCatalogTests`, `AsrTranscriberRouterTests`) + an opt-in
  real-model "circular" TTS→ASR test suite (`AsrCircularTests`, gated by `ASR_MODEL_TESTS=1`) that
  found and fixed a real `ModelCache` concurrent-download race condition.
  [Detail →](ASR_IMPLEMENTATION_HISTORY.md#phase-8--tests)
- **Phase 9 (Documentation update, final)**: synced README/ARCHITECTURE/API/CONFIGURATION/MODELS/
  DEPLOYMENT/TROUBLESHOOTING docs to the as-built ASR feature.
  [Detail →](ASR_IMPLEMENTATION_HISTORY.md#phase-9--documentation-update-final)
- **Phase 10 (Nemotron auto-detect-language fix)**: `NemotronLanguages.Resolve`'s no-language
  fallback was wrong (`0`/English instead of `101`/the model's real auto-detect slot), causing
  empty transcripts on non-English audio when no `language` was specified (the frontend's actual
  default). Fixed, plus the model's own detected-language tag is now surfaced instead of discarded.
  [Detail →](ASR_IMPLEMENTATION_HISTORY.md#phase-10--nemotron-auto-detect-language-fix)
- **Phase 11 (Universal audio input format support)**: `AudioFormatConverter` shells out to
  `ffmpeg` to normalize any input format to PCM16 WAV; fixed a real bug where piping through a
  non-seekable stdout produced structurally-invalid `0xFFFFFFFF`-sized WAV headers.
  [Detail →](ASR_IMPLEMENTATION_HISTORY.md#phase-11--universal-audio-input-format-support-ffmpeg)
- **Phase 12 (VAD support + language UI polish)**: `SileroVadEngine`/`SileroVadGate` wire the
  previously-unused `silero_vad.onnx` asset into real consecutive-silence chunk-gating for
  Nemotron, opt-in via a `use_vad` request field + a frontend checkbox.
  [Detail →](ASR_IMPLEMENTATION_HISTORY.md#phase-12--vad-support-nemotron--language-ui-polish)

---

## Phase 13 — Streaming ASR (mic/tab audio, WebSocket)
**Status**: ✅ Done (awaiting acceptance) (in-process mode only; worker-mode support is Phases 15-17)

**Inputs**: Phase 5's endpoint infra; Phase 4's `IAsrTranscriber` implementations.

**What's actually already built** (discovered mid-session — this phase's status was stale, it said
"Not started" despite being functional):
- Backend: `app.UseWebSockets()` + `GET /v1/audio/transcriptions/stream` in `Program.cs`
  (in-process mode only) — query params `model` (required)/`language`/`use_vad`; resolves
  `WhisperAsrTranscriber`/`NemotronAsrTranscriber` singletons directly via DI
  (`GetService<T>()`), creates an `IStreamingTranscriptionSession` via `CreateStreamingSession(...)`,
  then `RunStreamingTranscriptionAsync` loops receiving binary PCM16 frames →
  `session.ProcessChunkAsync` → replies with `{"type":"partial","text":...}` JSON frames; a text
  frame `{"type":"end"}` (or socket close) triggers `session.FinishAsync()` → final
  `{"type":"final","text":...}` frame → close. Returns 501 if resolved in worker mode (the
  transcriber singletons aren't registered there).
- Both engines implement `IStreamingTranscriptionSession` (`SampleRate`, `ProcessChunkAsync`,
  `FinishAsync`): `WhisperStreamingSession` (buffer-and-periodically-re-transcribe, no true
  incremental decode) and `NemotronAsrEngine.StreamingSession` (true cache-aware incremental RNNT
  decode, carries encoder cache + LSTM predictor state across calls, threads `SileroVadGate` when
  `enableVad`).
- Frontend: `frontend/src/components/LiveTranscription.vue` (mic via `getUserMedia`, tab/system
  audio via `getDisplayMedia`, `AudioContext`+`AudioWorkletNode` resampling to 16kHz PCM16 via
  `frontend/src/audio-worklets/pcm-capture-processor.js`, WebSocket client), wired into `App.vue`'s
  ASR panel behind `v-if="selectedAsrModel?.supportsStreaming"`.

**Known bugs found this session** (root-caused via live testing against a real deployment) — this
phase's remaining action items:
1. **Mic/tab capture both broken on non-secure origins.** `LiveTranscription.vue`'s
   `startMicrophone()` calls `navigator.mediaDevices.getUserMedia(...)` with **no guard** —
   browsers make `navigator.mediaDevices` itself `undefined` on any non-secure context (plain HTTP
   on a non-`localhost` host, e.g. `http://my.homelab.com:5768`), producing exactly the
   reported `Cannot read properties of undefined (reading 'getUserMedia')`. Not fixable in JS
   alone (browser platform security restriction) — needs a guard + actionable error message
   directing the user to serve over HTTPS or `localhost`.
   **Fix**: add `if (!navigator.mediaDevices?.getUserMedia) { errorMessage.value = '...'; return }`
   to `startMicrophone()` (mirroring `startTabAudio()`'s existing-but-incomplete guard, which only
   checks `.getDisplayMedia` and not `navigator.mediaDevices` itself); message text must name the
   real cause (secure-context requirement) so users don't mistake it for an app bug.
2. **`docker-compose.yml` defaults to worker mode** (`AsrWorkerOptions__Enabled: true`), under
   which streaming always 501s (the transcriber singletons the WS handler resolves via
   `GetService<T>()` are only registered in in-process mode) — yet `AsrModel.SupportsStreaming`
   (from `/api/asr-models`) doesn't reflect this, so the frontend shows a Live Transcription UI
   that cannot work under the documented/default deployment.
   **Fix**: in `Program.cs`'s `/api/asr-models` handler, inject `IOptions<AsrWorkerOptions>` and
   report `SupportsStreaming = catalogValue && !asrWorkerOptions.Value.Enabled`.
3. **`pyscripts/streaming.py`'s `WS_ENDPOINT` (L89) has no `?model=...` query string** — the
   server correctly 404s "model not found" on a blank `model`, which is the literal `HTTP 404` the
   user reported; not a server bug. **Fix**: add `?model=whisper-base` (optionally
   `&language=...&use_vad=...`) to the URL.
4. **No UI separation between offline and live modes** — today `App.vue` shows the file-upload
   (offline) controls and the `<LiveTranscription>` block simultaneously, with no guarantee only
   one is "active". **Fix**: add a local `asrMode: 'offline' | 'live'` ref (default `'offline'`),
   render a small sub-tab-switcher (mirrors the outer TTS/ASR tab-switcher, shown only when
   `selectedAsrModel?.supportsStreaming`), and wrap each mode's UI in `v-if` (not `v-show`) so
   switching away from `'live'` unmounts `LiveTranscription` and triggers its existing
   `onBeforeUnmount` → `stop()` cleanup — this alone guarantees mutual exclusivity.

**Actual output files for this phase's fixes**:
- `frontend/src/App.vue` (modified: `asrMode` ref + `.sub-tab-switcher` + `v-if`/`template v-if`
  wrapping so Offline/Live are mutually exclusive; `syncAsrModelDefaults()` resets `asrMode` to
  `'offline'` when switching to a non-streaming model)
- `frontend/src/components/LiveTranscription.vue` (modified: `startMicrophone()`/`startTabAudio()`
  both guard on `navigator.mediaDevices` itself, not just the specific capture method; a shared
  `SECURE_CONTEXT_ERROR` message explains the real cause)
- `src/SharpAudio.Api/Program.cs` (modified: `/api/asr-models` now injects
  `IOptions<AsrWorkerOptions>` and reports `SupportsStreaming = catalogValue &&
  !asrWorkerOptions.Value.Enabled`)
- `pyscripts/streaming.py` (modified: `WS_ENDPOINT` now includes `?model=whisper-base`)
- `docs/TROUBLESHOOTING.md` (modified: secure-context mic note + WS 404/501 troubleshooting entry)
- `docs/wiki/asr-engines.md` (already documented the as-built streaming contract + known issues
  during Phase 0's doc restructuring, ahead of this phase's fixes)

**Verification**: `./dotnet.sh build`; `cd frontend && npx vue-tsc --noEmit && pnpm run build`;
manual — `AsrWorkerOptions__Enabled=false`: Live Transcription tab visible, mic/tab capture work
over a secure context (`https://` or `http://localhost`), switching to Offline stops any
active live session; `AsrWorkerOptions__Enabled=true` (compose default): Live Transcription tab
hidden; re-run fixed `pyscripts/streaming.py` against an in-process server and confirm no more 404.

Once items 1-4 are done and verified, flip status to `✅ Done (awaiting acceptance)`, then move this
section's detail into `docs/ASR_IMPLEMENTATION_HISTORY.md` per the usual pattern.

---

## Phase 14 — VAD/timestamp segments (offline endpoint only)
**Status**: ✅ Done (awaiting acceptance)

**Inputs**: Phase 5's `/v1/audio/transcriptions` endpoint; Phase 12's VAD support
(`SileroVadGate`); Phase 2's Whisper engine (Whisper.net already computes per-segment start/end
internally, currently discarded).

**Goal**: return per-segment timestamps (OpenAI-style `verbose_json`), gated to segments of at
least a configurable minimum duration (default 0.5s) of detected active audio, displayed in the
frontend as a timestamp+text list.

**Planned changes**:
1. New `Models/TranscriptionSegment.cs`: `record TranscriptionSegment(int Id, double Start, double
   End, string Text)`.
2. `Models/TranscriptionResult.cs`: add a trailing optional `IReadOnlyList<TranscriptionSegment>?
   Segments = null` parameter (backward compatible with existing positional call sites).
3. `Contracts/AudioTranscriptionRequest.cs`: add `bool IncludeSegments` and `double
   MinSegmentDurationSeconds` (default `0.5`).
4. New `Services/SegmentMerger.cs`: `MergeShortSegments(IReadOnlyList<TranscriptionSegment>
   segments, double minDurationSeconds)` — walks segments in order, merging any segment shorter
   than the threshold into the previous one (extending its `End`, appending its `Text`); the very
   first segment merges forward into the next one instead. Renumbers `Id` sequentially. Pure/
   stateless — unit-testable without any model weights.
5. `Services/WhisperAsrEngine.cs`: `TranscribeAsync` also collects `TranscriptionSegment`s from
   Whisper.net's `SegmentData.Start`/`.End`/`.Text` per iteration (already iterating segments, just
   also record them instead of only concatenating text) — always computed, cheap, no extra
   inference cost.
6. `Services/NemotronAsrEngine.cs` (the larger change):
   - `RunEncoderChunks` also returns *region boundaries*: for each contiguous run of non-VAD-dropped
     chunks, track the encoder-output frame index range `[startFrame, endFrame)` plus the
     corresponding real-time bounds (`offset / sampleRate` seconds, from the raw audio chunk
     offsets already being iterated). When VAD is disabled, this collapses to a single region
     spanning all frames.
   - `RunRnntGreedyDecode` additionally returns, per encoder frame, how many tokens it emitted (a
     parallel `int[]` of per-frame token counts) — decode order is already frame-sequential, so
     summing per-frame counts within a region's frame range gives the exact token-slice for that
     region.
   - `Transcribe(...)` builds one `TranscriptionSegment` per region: `Text = vocabulary.Decode`
     over that region's token slice, `Start`/`End` from the region's time bounds; returned as a
     third tuple member alongside `Text`/`DetectedLanguage`.
7. `Services/WhisperAsrTranscriber.cs` / `Services/NemotronAsrTranscriber.cs`: when
   `request.IncludeSegments` is true, run `SegmentMerger.MergeShortSegments(rawSegments,
   request.MinSegmentDurationSeconds)` and populate `TranscriptionResult.Segments`; otherwise leave
   it `null` (no behavior change for existing callers/tests).
8. `Program.cs`'s `/v1/audio/transcriptions` handler: parse `min_segment_duration` form field
   (double, default `0.5`), set `IncludeSegments = responseFormat == "verbose_json"`. Branch the
   response: when `verbose_json`, return `{ text, language, duration, segments: [{ id, start, end,
   text }] }` (abbreviated OpenAI `verbose_json` shape — only the fields actually available);
   otherwise keep the current `{ text }` shape unchanged.
9. Frontend:
   - `frontend/src/types.ts`: add `TranscriptionSegment { id: number; start: number; end: number;
     text: string }`; extend the transcription response type with `segments?: TranscriptionSegment[]`.
   - `App.vue`: standalone "Show timestamps" checkbox in the ASR panel (visible for all models,
     independent of the VAD checkbox — matches OpenAI's independent `timestamp_granularities`),
     plus a "min segment duration" number input (shown only when timestamps are enabled, default
     `0.5`). `transcribe()` sends `response_format=verbose_json` + `min_segment_duration` when
     checked, stores the parsed `segments` array in a new `transcriptionSegments` ref.
   - `frontend/src/components/TranscriptionResult.vue`: accept an optional `segments` prop; when
     present and non-empty, render a list of rows (`mm:ss.s–mm:ss.s` + text) instead of/above the
     flat text block.
10. Docs: `docs/API.md` (new `response_format=verbose_json`/`min_segment_duration` fields +
    response shape), `docs/CONFIGURATION.md`, `docs/MODELS.md` (note Nemotron's segments are only
    VAD-region-based when `use_vad=true`, otherwise one full-span segment).

**Actual output files**:
- `src/SharpAudio.Api/Models/TranscriptionSegment.cs` (new)
- `src/SharpAudio.Api/Models/TranscriptionResult.cs` (modified: trailing optional `Segments`)
- `src/SharpAudio.Api/Contracts/AudioTranscriptionRequest.cs` (modified: `IncludeSegments`,
  `MinSegmentDurationSeconds`)
- `src/SharpAudio.Api/Services/SegmentMerger.cs` (new)
- `src/SharpAudio.Api/Services/WhisperAsrEngine.cs` (modified: `TranscribeAsync` also returns
  `IReadOnlyList<TranscriptionSegment>` from Whisper.net's own per-segment `Start`/`End`)
- `src/SharpAudio.Api/Services/NemotronAsrEngine.cs` (modified: `RunEncoderChunks` tracks contiguous
  active-audio regions, `RunRnntGreedyDecode` returns per-frame token counts, `Transcribe` builds
  `TranscriptionSegment`s via a new `BuildSegments` helper)
- `src/SharpAudio.Api/Services/WhisperAsrTranscriber.cs` / `NemotronAsrTranscriber.cs` (modified:
  apply `SegmentMerger.MergeShortSegments` when `request.IncludeSegments`)
- `src/SharpAudio.Api/Services/WhisperStreamingSession.cs`,
  `src/SharpAudio.Worker.Asr/Services/WorkerTranscriptionService.cs` (modified: updated tuple
  deconstruction for the new 3-element `Transcribe`/`TranscribeAsync` return signatures - neither
  path propagates segments, by design, since streaming (Phase 13) and worker-mode (this phase's
  known limitation) are out of scope)
- `src/SharpAudio.Api/Program.cs` (modified: parses `min_segment_duration`, sets `IncludeSegments`
  from `response_format=verbose_json`, branches the response shape)
- `frontend/src/types.ts` (modified: `TranscriptionSegment`)
- `frontend/src/App.vue` (modified: `showTimestamps`/`minSegmentDuration`/`transcriptionSegments`
  state, standalone "Show timestamps" checkbox + min-duration input in the offline panel)
- `frontend/src/components/TranscriptionResult.vue` (modified: optional `segments` prop, renders a
  timestamped row list instead of the flat text block when present)
- `tests/SharpAudio.Api.Tests/SegmentMergerTests.cs` (new: 5 tests - unchanged-when-all-long,
  merge-into-previous, merge-first-forward, never-drops-text, empty/single-input passthrough)
- `docs/API.md` (modified: `response_format=verbose_json`/`min_segment_duration` fields, response
  shape, worker-mode limitation note)
- `docs/wiki/asr-engines.md` (modified: new "VAD/timestamp segments" section, updated known-issues
  list)

**Known limitation (by design, not a bug)**: segments are only populated in in-process ASR mode
(`AsrWorkerOptions__Enabled=false`) - `transcription.proto`'s `TranscribeResponse` doesn't carry
segment data yet, so worker-mode responses return an empty `segments` array even with
`response_format=verbose_json`. Extending the proto is not scheduled; flag if this becomes a real
need once Phases 15-17 (worker-mode streaming) land, since they touch the same proto file anyway.

**Verification**:
- `./dotnet.sh build SharpAudio.slnx` → 0 errors.
- `./dotnet.sh test tests/SharpAudio.Api.Tests` → 74/74 passed (was 69; +5 new `SegmentMergerTests`).
- `cd frontend && npx vue-tsc --noEmit && pnpm run build` → 0 type errors, build succeeds.
- Not yet run: the opt-in `AsrCircularTests` case exercising `verbose_json` + `use_vad=true` for
  real Nemotron/Whisper weights (no models downloaded in this environment) - recommend running it
  (and a real non-VAD Whisper `verbose_json` case) before flipping this phase to Accepted.

---

## Phase 15 — Worker-mode streaming: proto + worker-side implementation
**Status**: ✅ Done (awaiting acceptance)

**Goal**: extend the ASR worker gRPC protocol with a **bidirectional streaming** RPC, so live
transcription (Phase 13) can eventually work under `AsrWorkerOptions__Enabled=true` too, not just
in-process. This is standard, well-supported grpc-dotnet functionality
(`IAsyncStreamReader<T>`/`IServerStreamWriter<T>`), not a novel technique.

**Planned changes**:
1. Extend `Protos/transcription.proto`: add `rpc TranscribeStream(stream TranscribeStreamChunk)
   returns (stream TranscribeStreamUpdate);`. `TranscribeStreamChunk` carries either a one-time
   "config" payload (model_name, engine, model_path, language, use_vad) on the first message, or
   raw PCM16 audio bytes on subsequent messages — mirrors the existing WS wire protocol so the
   mapping is mechanical. `TranscribeStreamUpdate` carries `{ text, is_final }`.
2. `SharpAudio.Worker.Asr/Services/WorkerTranscriptionService.cs`: implement `TranscribeStream` —
   read the first (config) message, create the right engine's `IStreamingTranscriptionSession`
   (same routing logic already used by the unary `Transcribe` method), then loop:
   `requestStream.MoveNext()` → `session.ProcessChunkAsync(...)` → if text changed,
   `responseStream.WriteAsync(new { text, is_final = false })`; on request-stream completion, call
   `session.FinishAsync()` and write one final `is_final = true` update.

**Actual output files**:
- `src/SharpAudio.Api/Protos/transcription.proto` (modified: added `TranscribeStream` bidirectional
  RPC, `TranscribeStreamChunk` (a `oneof payload { StreamConfig config; bytes audio_chunk; }`),
  `StreamConfig`, `TranscribeStreamUpdate`)
- `src/SharpAudio.Worker.Asr/Services/WorkerTranscriptionService.cs` (modified: implemented
  `TranscribeStream` — reads the first `config` message, builds the right engine's
  `IStreamingTranscriptionSession` via the same `GetOrCreateWhisperEngine`/`GetOrCreateNemotronEngine`
  helpers as the unary `Transcribe` RPC (refactored to take plain `(modelName, modelPath)` params
  instead of a `TranscribeRequest` so both RPCs can share them), then loops `ProcessChunkAsync` per
  audio chunk and writes partial/final `TranscribeStreamUpdate`s)

**Verification**:
- `./dotnet.sh build SharpAudio.slnx` → 0 errors (generated gRPC code for the new RPC/messages
  compiles on both the API's client-side and the worker's server-side proto includes).
- Real end-to-end verification (a standalone worker process + raw `Grpc.Net.Client` test, as
  originally planned) was folded into Phase 17's opt-in integration test instead of a separate
  throwaway harness — see Phase 17 below.

---

## Phase 16 — Worker-mode streaming: API-side proxy + endpoint unification
**Status**: ✅ Done (awaiting acceptance)

**Inputs**: Phase 15's `TranscribeStream` RPC.

**Planned changes**:
1. `src/SharpAudio.Api/Services/AsrWorkerProxyTranscriber.cs`: add a method that opens a duplex
   `TranscribeStream` call on the pooled worker channel and returns a small adapter class
   implementing the existing `IStreamingTranscriptionSession` interface (`ProcessChunkAsync` writes
   to the request stream then reads the next update off a background-drained channel/queue;
   `FinishAsync` completes the request stream and awaits the final update) — a drop-in for the
   same interface the in-process engines already implement.
2. Add `CreateStreamingSession(...)` to `IAsrTranscriber` (or a new marker interface implemented by
   both `AsrTranscriberRouter` and `AsrWorkerProxyTranscriber`) so `Program.cs`'s WS handler can
   stop special-casing in-process mode entirely: resolve the already-injected `IAsrTranscriber` and
   call `CreateStreamingSession(...)` regardless of worker/in-process mode, deleting the current
   `GetService<WhisperAsrTranscriber>()`/`GetService<NemotronAsrTranscriber>()`/501 branch.
3. Remove the Phase-13 runtime-gating fix from `/api/asr-models` (streaming now always works,
   report `SupportsStreaming` from the catalog unconditionally again).

**Actual output files**:
- `src/SharpAudio.Api/Services/IAsrTranscriber.cs` (modified: added
  `Task<IStreamingTranscriptionSession> CreateStreamingSessionAsync(model, modelDirectory,
  language, enableVad, cancellationToken)` to the interface — all 4 implementations updated)
- `src/SharpAudio.Api/Services/AsrWorkerProxyTranscriber.cs` (modified: `CreateStreamingSessionAsync`
  opens a duplex `TranscribeStream` call and returns a new private `WorkerStreamingSession`
  adapter — `ProcessChunkAsync` writes a request-stream chunk then drains the latest buffered
  update off a `System.Threading.Channels.Channel<string>` fed by a background read-loop task;
  `FinishAsync` completes the request stream and awaits the read loop for the final update)
- `src/SharpAudio.Api/Services/AsrTranscriberRouter.cs` (modified: routes
  `CreateStreamingSessionAsync` to `_whisper`/`_nemotron` by engine, same as `TranscribeAsync`)
- `src/SharpAudio.Api/Services/WhisperAsrTranscriber.cs` / `NemotronAsrTranscriber.cs` (modified:
  each gained a thin `CreateStreamingSessionAsync` wrapper around their existing sync
  `CreateStreamingSession` method, to satisfy the interface)
- `src/SharpAudio.Api/Program.cs` (modified: `/v1/audio/transcriptions/stream` now takes an
  `IAsrTranscriber` DI parameter and calls `CreateStreamingSessionAsync(...)` unconditionally —
  deleted the `GetService<WhisperAsrTranscriber>()`/`GetService<NemotronAsrTranscriber>()`/501
  branch entirely; `/api/asr-models` reverted to reporting `SupportsStreaming` from the catalog
  unconditionally, removing the Phase-13 `IOptions<AsrWorkerOptions>` runtime-gating hack)

**Verification**:
- `./dotnet.sh build SharpAudio.slnx` → 0 errors.
- `./dotnet.sh test tests/SharpAudio.Api.Tests` → 74/74 passed (unchanged — confirms the new
  interface member and its 4 implementations didn't regress anything already covered).
- Not yet run: a real `docker compose up` (default worker-mode settings) end-to-end check with
  `pyscripts/streaming.py`/the frontend's Live Transcription button — no Docker build performed
  this pass; recommend doing so before flipping this phase to Accepted (see Phase 17's opt-in test
  for the automated equivalent, which also hasn't been run for real in this environment).

---

## Phase 17 — Worker-mode streaming: tests + docs sync
**Status**: ✅ Done (awaiting acceptance)

**Actual output files**:
- `tests/SharpAudio.Api.IntegrationTests/Support/WorkerAsrExecutableGate.cs` (new): checks whether a
  real published `SharpAudio.Worker.Asr` executable exists next to the API output (mirrors
  `FfmpegTestGate`'s pattern of gating on an external dependency's presence).
- `tests/SharpAudio.Api.IntegrationTests/AsrCircularTests.cs` (modified): new opt-in
  `Streaming_WorkerMode_TranscribesOverWebSocket` test, gated on both `AsrModelTestGate` and
  `WorkerAsrExecutableGate` — builds a `SERVER_MODE=asr`/`AsrWorkerOptions__Enabled=true` factory,
  connects to `/v1/audio/transcriptions/stream` via `TestServer.CreateWebSocketClient()`, streams a
  synthetic 440Hz sine-tone PCM16 clip in 100ms chunks (no TTS dependency, mirrors
  `pyscripts/streaming.py`'s original synthetic-audio helper), sends `{"type":"end"}`, and asserts
  a non-null final transcript arrives — proves the worker-mode duplex gRPC round-trip completes
  without crashing, not transcription accuracy (synthetic tone, not real speech).
- `docs/ASR_IMPLEMENTATION_PLAN.md` (this file, status tracker + Phases 15-17 detail).
- `docs/wiki/asr-engines.md` (modified: worker-mode streaming sections updated, "Known issues"
  entry for live transcription removed since it's now resolved, replaced with a note about the
  new opt-in test's coverage/limitations).
- `docs/TROUBLESHOOTING.md` (modified: removed the "501 requires in-process mode" explanation from
  the WS troubleshooting entry, replaced with a note that both modes now work identically).

**Verification**:
- `./dotnet.sh build tests/SharpAudio.Api.IntegrationTests/SharpAudio.Api.IntegrationTests.csproj` → 0
  errors (this project isn't part of `SharpAudio.slnx`, build/test it directly by path).
- Ran the new test filtered by name without `ASR_MODEL_TESTS`/a published worker executable set →
  correctly shows **Skipped**, confirming the gate is zero-cost in this dev environment (matches
  every other opt-in test in this suite).
- **Not yet run for real**: this test (and Phase 16's manual `docker compose up` check) require a
  real Docker image build where `SharpAudio.Worker.Asr` is actually published next to the API (this
  dev environment only has the plain SDK, no published worker binaries) — recommend building
  `docker build --target runtime-all` and running `ASR_MODEL_TESTS=1 dotnet test ... --filter
  FullyQualifiedName~Streaming_WorkerMode_TranscribesOverWebSocket` (or the equivalent via
  `./tests/run-tests.sh asr-model-tests`) inside that image, plus a manual
  `pyscripts/streaming.py`/frontend Live Transcription check against `docker compose up` with
  default (worker-mode) settings, before flipping Phases 15-17 to Accepted — same caveat pattern
  as Phase 11's ffmpeg fix, which also could only be fully validated inside a real built image.

---

## Phase 18 — Swagger + documentation sync (round 2)
**Status**: Not started

**Goal**: document the widened format support, `use_vad`/`language=auto` fields, the new
`verbose_json`/`min_segment_duration`/segments contract (Phase 14), and the worker-mode-capable
streaming endpoint (Phase 15-17) — via a manual OpenAPI document filter, since Swashbuckle can't
natively represent WebSocket endpoints — in Swagger UI and across `docs/API.md`/
`docs/CONFIGURATION.md`/`docs/TROUBLESHOOTING.md`/`docs/MODELS.md`/`docs/wiki/asr-engines.md`.

---

## Overall verification checklist (run once Phase 4+ exist)
1. `dotnet build SharpAudio.slnx` (via `./dotnet.sh build SharpAudio.slnx`) succeeds.
2. `./dotnet.sh test tests/SharpAudio.Api.Tests` passes, including new Asr tests.
3. Manual: `SERVER_MODE=tts` (default/unset) behaves byte-identical to pre-ASR behavior;
   `/v1/audio/transcriptions` returns 404; ASR UI hidden.
4. Manual: `SERVER_MODE=asr` — TTS endpoints disabled/hidden, ASR endpoints work, only
   `SharpAudio.Worker.Asr` spawns.
5. Manual: `SERVER_MODE=both` — both workers spawn independently on non-overlapping port ranges,
   both UI panels/tabs appear.
6. `docker build --target runtime-tts|runtime-asr|runtime-all -t fastttsr:<tag> .` — confirm each
   image only contains the expected worker executable(s).
7. `./tests/run-tests.sh all` / `docker compose -f docker-compose.test.yml up
   --abort-on-container-exit` for integration coverage including the transcription endpoint.
8. Manual curl: POST a short WAV to `/v1/audio/transcriptions` with `model=whisper-base`, verify
   text output; repeat with `model=nemotron-3.5`.
9. (Phase 13) Manual: mic/tab capture over a secure context works; live/offline tabs are mutually
   exclusive; Live Transcription UI hidden when `AsrWorkerOptions__Enabled=true`.
10. (Phase 14) `POST /v1/audio/transcriptions` with `response_format=verbose_json` returns ordered,
    non-overlapping segments each ≥ `min_segment_duration`.
11. (Phase 15-17) Live transcription works identically under both `AsrWorkerOptions__Enabled=true`
    and `=false`.

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
