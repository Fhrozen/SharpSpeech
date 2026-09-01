# FastTTSR Wiki — Frontend

> Linked from [docs/LLM_WIKI.md](../LLM_WIKI.md). Covers `frontend/`: structure, components, state
> management, and capability discovery.

Vue 3 + TypeScript, Composition API, no UI framework/router/state library (deliberately minimal).

## `App.vue`
On mount, fetches `GET /api/server-info` first (`{ ttsEnabled, asrEnabled }`); sets `activeTab` to
whichever is enabled (defaults to `'tts'` if both, `'asr'` if only ASR). Default local state before
that fetch resolves is `{ ttsEnabled: false, asrEnabled: false }` plus a `serverInfoLoaded` flag, so
neither panel flashes before the real capabilities are known — a loading placeholder is shown
instead; if the fetch fails, it falls back to TTS-only (the pre-ASR default). If a server reports
neither flag (misconfiguration), an error message is shown instead of a blank page. The TTS panel
is wrapped in `v-if="serverInfo.ttsEnabled"` and the ASR panel in `v-if="serverInfo.asrEnabled"` —
only the panel(s) the running server actually serves are ever mounted, matching `SERVER_MODE`.
Then conditionally fetches `GET /api/models` (if `ttsEnabled`) and/or `GET /api/asr-models` (if
`asrEnabled`). A `.tab-switcher` (two buttons, gold-accent active underline) is rendered only when
both flags are true; otherwise the single enabled panel shows directly with no tabs. The header
subtitle is computed from `serverInfo` (TTS-only/ASR-only/both wording).

- **TTS panel** (unchanged since pre-ASR): form state (`model`, `speaker`, `language`, `speed`,
  `input`, `quality`) in a `reactive()`, calls `POST /v1/audio/speech` via plain `fetch()`, reads
  metrics from response headers.
- **ASR panel**: `asrForm` reactive (`model`, `language`, `enableVad`, `file: File | null`);
  `transcribe()` builds a `FormData` (`file`, `model`, optional `language`, `use_vad`) and `POST`s
  to `/v1/audio/transcriptions`; reads `X-Processing-Time`/`X-RTF`/`X-Audio-Duration`/
  `X-Character-Count` response headers into `transcriptionMetrics`, and the JSON body's `text`
  field into `transcriptionText`. Also renders a `<LiveTranscription>` block for models with
  `supportsStreaming` — see [docs/wiki/asr-engines.md](asr-engines.md) for the streaming
  contract/known issues; `LiveTranscription.vue` handles mic/tab audio capture and the WebSocket
  client itself.

## Components (`frontend/src/components/`)
| Component | Purpose | Scope |
|---|---|---|
| `ModelSelector.vue` | Dropdown; prop type loosened to a structural `{ name, displayName }[]` so it's reused for both `TtsModel[]` and `AsrModel[]` | both |
| `LanguageSelector.vue` | Clickable tag list; reused for ASR models' `supportedLanguages`, shows an "Auto-detect" chip when `supportsLanguageAutoDetect` | both |
| `SpeakerSelector.vue` | Clickable tag list | TTS |
| `TextInput.vue` | Contenteditable + presets | TTS |
| `ParameterControls.vue` | Speed/quality sliders + generate button | TTS |
| `AudioPlayer.vue` | Playback + metrics + download | TTS |
| `AudioFileInput.vue` | `accept="audio/*"` file picker, `v-model="File \| null"` | ASR |
| `TranscriptionResult.vue` | Copyable text block + metrics row; reuses `AudioPlayer`'s `.audio-result-metrics`/`.metric`/`.metric-value`/`.metric-label` CSS class names for visual consistency (Vue scoped styles don't cascade cross-component) | ASR |
| `LiveTranscription.vue` | Mic/tab audio capture (`getUserMedia`/`getDisplayMedia`), `AudioWorkletNode` PCM16 resampling, WebSocket client for `/v1/audio/transcriptions/stream` | ASR |
| `StatusMessage.vue` | Alert banner, one instance per tab | both |
| `AppHeader.vue` | Title/subtitle | both |

`frontend/src/audio-worklets/pcm-capture-processor.js`: `AudioWorkletProcessor` that resamples
captured mono float audio to 16kHz (linear interpolation) and posts raw PCM16 back to the main
thread for the WebSocket send — no WAV/container header, matching the server's expected wire
format.

## Types (`frontend/src/types.ts`)
`TtsModel`, `SpeakerMetadata`, `SynthesisRequest`, `SynthesisMetrics`, `TextPreset`, `StatusType`,
`AsrModel` (`name`, `displayName`, `description`, `supportedLanguages`,
`supportsLanguageAutoDetect`, `supportsVad`, `supportsStreaming`), `TranscriptionMetrics`
(`processingTime`, `rtf`, `audioDuration`, `characterCount`), `ServerInfo` (`ttsEnabled`,
`asrEnabled`), `StreamMessage` (`type: 'partial' | 'final'`, `text`).

## Build/dev
`vite.config.js` proxies `/api/*` and `/v1/*` to the backend in dev (WebSocket connections are
**not** proxied by Vite — they connect directly from the browser to `window.location.host`). Build:
`vue-tsc && vite build` → `dist/`, copied into the API's `wwwroot` at Docker build time. No pnpm
preinstalled in the dev container — `npm install -g pnpm` first, then `pnpm install && pnpm run
build`.

## Capability discovery
`GET /api/server-info` drives which panel(s)/tabs are shown; each panel's model list still comes
from its own `/api/models` or `/api/asr-models` call; each ASR model's `supportsStreaming`/
`supportsVad`/`supportsLanguageAutoDetect` flags drive which sub-controls appear for that model.
