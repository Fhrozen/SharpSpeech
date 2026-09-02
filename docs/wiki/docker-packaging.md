# FastTTSR Wiki — Docker Packaging

> Linked from [docs/LLM_WIKI.md](../LLM_WIKI.md).

Multi-stage `Dockerfile`, 3 selectable final images sharing a common `runtime-base` stage:
`docker build --target runtime-tts|runtime-asr|runtime-all -t <tag> .` (omitting `--target` builds
`runtime-all`, the last/default stage). `backend-build` publishes `FastTTSR.Api`,
`FastTTSR.Worker`, and `FastTTSR.Worker.Asr` with `-r linux-x64 --self-contained false`.
`runtime-base` installs `libespeak-ng1`/`espeak-ng-data` (Kokoro) + `libgomp1` (Whisper.net's
native `ggml-cpu` library needs OpenMP). `runtime-asr`/`runtime-all` additionally set
`ENV LD_LIBRARY_PATH=/app/runtimes/linux-x64:/app/worker-asr/runtimes/linux-x64` — **required** for
Whisper.net's native libs to load at all (see "Known fixes" below), and install `ffmpeg` (universal
audio input format support — see [docs/wiki/asr-engines.md](asr-engines.md)). `docker-compose.yml`
passes through `SERVER_MODE` + `AsrWorkerOptions__*` env vars and has commented example services
for TTS-only/ASR-only deployments using `target: runtime-tts`/`runtime-asr`. It also maps an
optional HTTPS port (`HOST_HTTPS_PORT:HTTPS_PORT`, both default `5769`) and mounts `./certs:/certs:ro`
plus `Kestrel__Certificates__Default__Path`/`Password` env vars (only meaningful once `HTTPS_PORT`
is set) - see `./generate-cert.sh` and the "HTTPS / TLS" section of `docs/CONFIGURATION.md` for the
self-signed LAN-hostname cert flow; `Program.cs` auto-redirects HTTP → HTTPS once `HTTPS_PORT` is set.

Used Docker's native `--target <stage>` mechanism (not a build `ARG` + `FROM runtime-${ARG}`
trick) — the standard, better-supported idiom for "one Dockerfile, multiple selectable final
images".

## Known fixes (found while validating against a real packaged image, not just `dotnet build`)
1. **Whisper.net native library load failure** (`"Cannot load the library... PInvokeError:
   Success"`): `Whisper.net.Runtime` ships native `.so` files under `runtimes/<rid>/` instead of the
   standard `runtimes/<rid>/native/` layout, so sibling dependencies (`libggml-*.so`) aren't found
   without help. Fixed via the `LD_LIBRARY_PATH` `ENV` above (installing `libgomp1` alone was
   necessary but insufficient). Confirmed fixed in both in-process and full worker mode.
2. **Whisper.net requires exactly 16kHz mono input, no internal resampling**: `WhisperAsrEngine`
   calls `WavAudioUtils.ResampleToMono16kWav(wavBytes)` before handing audio to Whisper.net
   (mirrors what Nemotron's pipeline already did). Without this, any non-16kHz source (e.g. Kokoro
   at 24kHz, Supertonic at 22050Hz) throws `NotSupportedWaveException`.
3. **ffmpeg stdout-pipe WAV headers**: piping ffmpeg's WAV output through a non-seekable
   `pipe:1` makes it write placeholder `0xFFFFFFFF` RIFF/data chunk sizes — see
   [docs/wiki/asr-engines.md](asr-engines.md)'s "Universal audio input format support" section for
   the fix (`AudioFormatConverter` patches the header itself).

## Full detail
See [docs/ASR_IMPLEMENTATION_HISTORY.md#phase-6](../ASR_IMPLEMENTATION_HISTORY.md#phase-6--dockercompose-packaging)
for the complete validation log (exact commands run, both in-process and full-worker-mode test
results).
