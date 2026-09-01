# FastTTSR Wiki — Testing

> Linked from [docs/LLM_WIKI.md](../LLM_WIKI.md).

- `tests/FastTTSR.Api.Tests`: unit tests (`ModelCatalogTests`, `KokoroMetadataTests`,
  `SherpaOnnxTtsSynthesizerTests`, `SpeechEndpointTests`, `TextSanitizerTests`,
  `AsrModelCatalogTests`, `AsrTranscriberRouterTests`, `NemotronLanguagesTests`,
  `NemotronVocabularyTests`, `SileroVadGateTests`). The ASR router test instantiates the real
  `AsrTranscriberRouter`/`WhisperAsrTranscriber`/`NemotronAsrTranscriber` with a bogus model
  directory — no real weights needed, since each engine fails fast in a distinguishable way
  (Whisper's own `FileNotFoundException` vs. Nemotron's `InferenceSession` failing to open a
  missing `encoder.onnx`), which is enough to prove routing went to the right engine.
  `SileroVadGateTests` uses a fake `ISileroVadEngine` to unit-test the consecutive-silence gating
  policy without needing real Silero weights.
- `tests/FastTTSR.Api.IntegrationTests`: full-stack tests via Docker (`SpeechSynthesisTests`,
  `ModelHealthTests` — including a `SERVER_MODE`-aware `/api/server-info` check,
  `JapaneseConcurrencyTests`, `AudioFormatConverterTests` — FLAC/MP3 round-trip, gated on ffmpeg's
  presence).
- Run: `dotnet test tests/FastTTSR.Api.Tests`, or `./tests/run-tests.sh all` /
  `docker compose -f docker-compose.test.yml up --abort-on-container-exit` for integration.
- Frontend: `cd frontend && pnpm install && pnpm run build` (runs `vue-tsc --noEmit` then `vite
  build`); no pnpm preinstalled in the dev container - install via `npm install -g pnpm` first.

## Circular TTS→ASR model tests (opt-in, real models, not run in CI)
`tests/FastTTSR.Api.IntegrationTests/AsrCircularTests.cs` synthesizes real audio via Supertonic-3
(`test_text.md` corpus — short/long samples + a long multi-speaker conversation script) and feeds
it into Whisper/Nemotron via the real HTTP endpoints, checking transcription quality (word error
rate) and crash-resistance on long multi-chunk audio. **Opt-in only**: gated by
`AsrModelTestGate`/`ASR_MODEL_TESTS=1` env var (via `Xunit.SkippableFact`, shows as Skipped by
default — zero cost, no downloads, when not enabled) and tagged `[Trait("Category",
"AsrModelTests")]`; CI's `ci-tests.yml` explicitly excludes this category. Run locally with
`ASR_MODEL_TESTS=1 dotnet test ... --filter "Category=AsrModelTests"` or `./tests/run-tests.sh
asr-model-tests`. See
[docs/ASR_IMPLEMENTATION_HISTORY.md#phase-8](../ASR_IMPLEMENTATION_HISTORY.md#phase-8--tests) for
full design notes, including the real concurrency bug this suite found and fixed in `ModelCache`
(see below). Also covers non-WAV uploads (`Offline_NonWavUpload_TranscribesSuccessfully`) and VAD
(`Offline_NemotronWithVad_TranscribesWithoutCrashing`).

## Known bug fixed: ModelCache concurrent-download race
`ModelCache.EnsureModelAsync` (both Tts/Asr overloads) serializes per-model downloads via a
`ConcurrentDictionary<string, SemaphoreSlim>` keyed by model name. Previously, a background
warmup service (`ModelWarmupService`/`AsrModelWarmupService`) racing an on-demand request for the
same not-yet-cached model could write the same asset file concurrently, corrupting it / causing
intermittent 500s — this affected TTS models too, not just ASR, it just hadn't been exercised
before the circular ASR tests were added.
