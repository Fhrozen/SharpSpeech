# SharpAudio — LLM Wiki

A condensed, agent-optimized architecture reference for SharpAudio. This file is meant to let an
agent ramp up quickly without re-discovering the codebase from scratch.

**This file is a short summary/index.** Full topic-level detail lives in `docs/wiki/*.md` (linked
below) — read those only when you need the low-level detail for the topic you're working on.

## Keeping this doc updated

**Rule: update the relevant `docs/wiki/*.md` detail file (and this file's one-line summary/link if
the topic itself is new) as part of any change that affects architecture, config surface,
endpoints, or conventions — do this alongside the code change, not as a deferred separate pass.**
If you add a new engine, endpoint, env var, or worker type, update the matching detail file in the
same PR/session.

## What this project is

SharpAudio is a .NET 10 REST API (OpenAI-compatible) for Text-to-Speech and Speech-to-Text (ASR),
with a Vue 3 frontend and Docker packaging. Each model runs inference in an isolated, disposable OS
worker process for memory isolation by default (spawned on demand, killed when idle), with an
in-process mode also available. Which task type(s) (`tts`/`asr`/`both`) a given running instance
serves is controlled by the `SERVER_MODE` env var (default `tts`, preserving pre-ASR behavior).

## How this doc is organized

| File | What's in it |
|---|---|
| [docs/wiki/backend-architecture.md](wiki/backend-architecture.md) | Request flow, worker-mode vs in-process mode, worker process lifecycle, gRPC contract (TTS), core abstractions (`ITtsSynthesizer`/`IModelCatalog`/`IModelCache`), model definition/`config.json` schema, TTS engines (Kokoro/Supertonic), TTS env var/endpoint tables, "Adding a new TTS engine" |
| [docs/wiki/asr-engines.md](wiki/asr-engines.md) | ASR domain model/abstractions, Whisper engine, Nemotron engine (incl. bugfix history), universal audio format support (ffmpeg), VAD support, ASR worker process/DI wiring, REST + streaming endpoints, live transcription frontend, known issues/roadmap, ASR env var table, "Adding a new ASR engine" |
| [docs/wiki/frontend.md](wiki/frontend.md) | Vue app structure, `App.vue` state/behavior, component table, types, build/dev, capability discovery |
| [docs/wiki/docker-packaging.md](wiki/docker-packaging.md) | Multi-stage `Dockerfile`, 3 selectable image targets, known native-library/packaging fixes |
| [docs/wiki/testing.md](wiki/testing.md) | Unit/integration test layout, opt-in circular TTS→ASR tests, known `ModelCache` race-condition bugfix |

For the ASR feature's phase-by-phase build log (what was done, why, and what's next), see
[docs/ASR_IMPLEMENTATION_PLAN.md](ASR_IMPLEMENTATION_PLAN.md) (condensed summary + status tracker)
and [docs/ASR_IMPLEMENTATION_HISTORY.md](ASR_IMPLEMENTATION_HISTORY.md) (full unabridged detail per
completed phase).

## Quick architecture summary
```
HTTP request → Program.cs minimal API endpoint → IModelCatalog (validate model) →
IModelCache (ensure model files downloaded) → ITtsSynthesizer/IAsrTranscriber (do the work) → response
```
Worker mode (default, `WorkerOptions.Enabled`/`AsrWorkerOptions.Enabled` = `true`): a
`WorkerProcessManager` spawns/pools a separate OS process (`SharpAudio.Worker`/`SharpAudio.Worker.Asr`)
per model, communicating over gRPC. In-process mode: engines run as singletons in the API process
itself, routed by a `*Router` class, with idle engines released periodically. See
[docs/wiki/backend-architecture.md](wiki/backend-architecture.md) for the full detail.

## Build tooling note
No local `dotnet` CLI in this dev environment — use `./dotnet.sh <args>` (Docker-based wrapper) for
build/test/publish, e.g. `./dotnet.sh build SharpAudio.slnx`, `./dotnet.sh test
tests/SharpAudio.Api.Tests`.
