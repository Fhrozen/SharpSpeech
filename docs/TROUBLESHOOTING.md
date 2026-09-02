# Troubleshooting Guide

This guide covers common issues and their solutions when working with FastTTSR.

## Table of Contents

- [Installation Issues](#installation-issues)
- [Runtime Errors](#runtime-errors)
- [Performance Issues](#performance-issues)
- [API Issues](#api-issues)
- [Model Issues](#model-issues)
- [ASR Issues](#asr-issues)
- [Docker Issues](#docker-issues)
- [Audio Quality Issues](#audio-quality-issues)
- [Debugging Tools](#debugging-tools)

---

## Installation Issues

### espeak-ng Not Found

**Error:**
```
Unable to load shared library 'libespeak-ng.so.1' or one of its dependencies
```

**Cause:** espeak-ng library not installed or not in library path.

**Solution (Linux):**
```bash
sudo apt-get update
sudo apt-get install -y libespeak-ng1 espeak-ng-data
```

**Solution (macOS):**
```bash
brew install espeak-ng
```

**Solution (Windows):**
```powershell
choco install espeak-ng
# Or download from: https://github.com/espeak-ng/espeak-ng/releases
```

**Verify Installation:**
```bash
which espeak-ng
espeak-ng --version
```

### .NET SDK Not Found

**Error:**
```
The command 'dotnet' was not found
```

**Solution:**
1. Download .NET 10 SDK: https://dotnet.microsoft.com/download/dotnet/10.0
2. Verify installation:
```bash
dotnet --version
```

### pnpm Not Found

**Error:**
```
pnpm: command not found
```

**Solution:**
```bash
# Enable corepack (recommended)
corepack enable pnpm

# Or install globally
npm install -g pnpm
```

---

## Runtime Errors

### "free(): invalid pointer" Crash

**Error:**
```
free(): invalid pointer
Aborted (core dumped)
```

**Cause:** Thread safety issue with espeak-ng when processing concurrent requests or multi-byte UTF-8 text (Japanese, Chinese).

**Solution:** Already fixed in current version with thread-safe locking. If still occurring:

1. **Verify you're using latest version:**
```bash
docker pull fhrozen/fast-ttsr:latest
```

2. **Check if CJK punctuation is being normalized:**
   - See [TEXT_SANITIZATION.md](TEXT_SANITIZATION.md)
   - Ensure `TextSanitizer` is being called

3. **Reduce concurrency temporarily:**
```yaml
deploy:
  resources:
    limits:
      cpus: '1.0'  # Limit to single core
```

### Model Download Fails

**Error:**
```
Failed to download model from https://huggingface.co/...
```

**Possible Causes:**
1. No internet connection
2. Hugging Face is down
3. Disk space full
4. Permissions issue

**Solutions:**

**Check internet connectivity:**
```bash
curl -I https://huggingface.co
```

**Check disk space:**
```bash
df -h
```

**Check permissions:**
```bash
ls -la model-cache/
chmod 777 model-cache/  # For Docker
```

**Manual download:**
```bash
cd model-cache/kokoro-q4/onnx/
wget https://huggingface.co/onnx-community/Kokoro-82M-v1.0-ONNX/resolve/main/onnx/model_q4.onnx
```

### Out of Memory Error

**Error:**
```
System.OutOfMemoryException: Insufficient memory to continue the execution of the program
```

**Cause:** Too many models loaded or insufficient system memory.

**Solutions:**

**1. Enable idle unloading:**
```json
{
  "ModelIdleMonitor": {
    "IdleTimeoutSeconds": 60,
    "CheckIntervalSeconds": 10
  }
}
```

**2. Increase Docker memory limit:**
```yaml
deploy:
  resources:
    limits:
      memory: 8G  # Increase from 4G
```

**3. Use quantized models:**
```json
{
  "model": "kokoro-q4"  // Instead of kokoro-full
}
```

**4. Check memory usage:**
```bash
# System memory
free -h

# Docker container
docker stats fastttsr
```

### Port Already in Use

**Error:**
```
Unable to bind to http://0.0.0.0:5768: address already in use
```

**Solution:**

**1. Find process using port:**
```bash
# Linux/macOS
lsof -i :5768
netstat -tuln | grep 5768

# Windows
netstat -ano | findstr :5768
```

**2. Kill the process:**
```bash
kill -9 <PID>
```

**3. Or change port:**
```bash
HTTP_PORT=8080 dotnet run
```

---

## Performance Issues

### Slow Synthesis (> 1 second)

**Symptoms:** Synthesis takes longer than expected.

**Diagnosis:**

**1. Check model being used:**
```bash
curl http://localhost:5768/api/models
```

**2. Enable detailed logging:**
```json
{
  "Logging": {
    "LogLevel": {
      "Default": "Debug"
    }
  }
}
```

**3. Check resource usage:**
```bash
docker stats
htop
```

**Common Causes:**

**1. Using full-precision model:**
- **Solution:** Switch to `kokoro-q4` for faster synthesis

**2. First request (model loading):**
- **Solution:** Implement warmup or pre-load models

**3. CPU throttling:**
- **Solution:** Increase CPU allocation
```yaml
deploy:
  resources:
    limits:
      cpus: '2.0'
```

**4. Disk I/O bottleneck:**
- **Solution:** Use SSD instead of HDD, or increase disk throughput

**5. espeak-ng lock contention:**
- **Solution:** Already serialized; may need to scale horizontally

### High Memory Usage

**Symptoms:** Memory usage grows over time or is unexpectedly high.

**Diagnosis:**
```bash
# Monitor memory over time
watch -n 1 'docker stats --no-stream fastttsr'
```

**Solutions:**

**1. Enable idle unloading:**
```bash
MODEL_IDLE_TIMEOUT_SECONDS=60
```

**2. Use quantized models:**
```json
{"model": "kokoro-q4"}  // ~1.5GB vs 3.5GB
```

**3. Limit concurrent requests:**
```nginx
# In nginx
limit_req_zone $binary_remote_addr zone=tts:10m rate=10r/s;
```

**4. Restart service periodically:**
```bash
# Cron job to restart daily
0 3 * * * docker compose restart fastttsr
```

### High CPU Usage

**Symptoms:** CPU usage constantly high.

**Diagnosis:**
```bash
top
docker stats
```

**Solutions:**

**1. Horizontal scaling:**
- Deploy multiple instances
- Use load balancer

**2. Optimize concurrency:**
- Limit concurrent requests
- Add queue management

**3. Use GPU acceleration** (future feature):
- ONNX Runtime supports CUDA
- Requires GPU-enabled container

---

## API Issues

### 404 Not Found

**Error:**
```json
{
  "error": {
    "code": "model_not_found",
    "message": "The requested model was not found."
  }
}
```

**Solution:**

**1. Check model name:**
```bash
curl http://localhost:5768/api/models | jq '.[] | .name'
```

**2. Verify config.json:**
```bash
cat src/FastTTSR.Api/config.json
```

**3. Check model is loaded:**
```bash
ls -la model-cache/
```

**4. Check `/v1/audio/transcriptions` or `/api/asr-models` returning 404 for the whole route (not
just "model not found"):**
This means the server wasn't started with ASR enabled. Check `SERVER_MODE`:
```bash
curl http://localhost:5768/api/server-info
# {"ttsEnabled": true, "asrEnabled": false}  <- ASR routes aren't mapped at all
```
Restart with `SERVER_MODE=asr` or `SERVER_MODE=both`.

### 400 Bad Request

**Error:**
```json
{
  "error": {
    "code": "invalid_request",
    "message": "Unsupported language 'xyz'"
  }
}
```

**Solution:**

**1. Check supported languages:**
```bash
curl http://localhost:5768/api/models | jq '.[] | .supportedLanguages'
```

**2. Use correct language code:**
```json
{
  "language": "en-us"  // Not "english" or "en"
}
```

**3. Check for typos:**
- `ja-jp` not `ja-JA`
- `zh-cn` not `zh_cn`

### 500 Internal Server Error

**Error:**
```json
{
  "error": {
    "code": "synthesis_failure",
    "message": "TTS synthesis failed"
  }
}
```

**Note:** Service returns valid silent WAV on synthesis failure for graceful degradation.

**Diagnosis:**

**1. Check logs:**
```bash
docker compose logs -f fastttsr
```

**2. Look for exceptions:**
- ONNX Runtime errors
- espeak-ng errors
- Memory allocation errors

**3. Test with simple input:**
```bash
curl -X POST http://localhost:5768/v1/audio/speech \
  -H 'Content-Type: application/json' \
  -d '{"model":"kokoro-q4","input":"Test","voice":"af"}' \
  --output test.wav
```

**Solutions:**

**1. Restart service:**
```bash
docker compose restart
```

**2. Clear model cache:**
```bash
rm -rf model-cache/*
```

**3. Check for corrupt models:**
```bash
# Re-download models
rm -rf model-cache/kokoro-q4/
```

### CORS Errors (Frontend)

**Error (Browser Console):**
```
Access to fetch at 'http://localhost:5768/v1/audio/speech' from origin 'http://localhost:5173' 
has been blocked by CORS policy
```

**Solution:**

**1. Add CORS configuration** in `Program.cs`:
```csharp
builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
    {
        policy.AllowAnyOrigin()
              .AllowAnyMethod()
              .AllowAnyHeader();
    });
});

app.UseCors();
```

**2. Or configure nginx:**
```nginx
add_header Access-Control-Allow-Origin * always;
add_header Access-Control-Allow-Methods "GET, POST, OPTIONS" always;
add_header Access-Control-Allow-Headers "Content-Type" always;
```

---

## Model Issues

### Model Not Loading

**Symptoms:** Model exists but fails to load.

**Diagnosis:**

**1. Check file integrity:**
```bash
ls -lh model-cache/kokoro-q4/onnx/model_q4.onnx
```

**2. Verify ONNX file:**
```bash
# Install onnx
pip install onnx

# Validate model
python -c "import onnx; model = onnx.load('model-cache/kokoro-q4/onnx/model_q4.onnx'); onnx.checker.check_model(model)"
```

**Solutions:**

**1. Re-download model:**
```bash
rm model-cache/kokoro-q4/onnx/model_q4.onnx
# Restart service to trigger download
docker compose restart
```

**2. Check ONNX Runtime version:**
```bash
dotnet list package | grep OnnxRuntime
```

**3. Verify model path in config.json:**
```json
{
  "modelPath": "onnx/model_q4.onnx"  // Relative to model cache dir
}
```

### Voice File Missing

**Error:**
```
Failed to load voice file: af_bella.bin
```

**Solution:**

**1. Check if voice exists:**
```bash
ls -la model-cache/kokoro-q4/voices/
```

**2. Download voice manually:**
```bash
cd model-cache/kokoro-q4/voices/
wget https://huggingface.co/onnx-community/Kokoro-82M-v1.0-ONNX/resolve/main/voices/af_bella.bin
```

**3. Use default voice:**
```json
{
  "voice": "af"  // Use base voice
}
```

### Poor Pronunciation

**Symptoms:** Words mispronounced or incorrect accent.

**Solutions:**

**1. Verify language setting:**
```json
{
  "language": "ja-jp"  // For Japanese text
}
```

**2. Try different voice:**
```json
{
  "voice": "bf_emma"  // British accent for British English
}
```

**3. Adjust text:**
- Remove special characters
- Use phonetic spelling for difficult words
- Break long sentences into shorter ones

**4. Check espeak-ng data:**
```bash
ls -la /usr/share/espeak-ng-data/
```

---

## ASR Issues

### Whisper: "Cannot load the library on this platform... PInvokeError: Success"

**Cause:** `Whisper.net.Runtime` ships its native `.so` files under `runtimes/<rid>/` instead of the
standard `runtimes/<rid>/native/` layout .NET's native library resolver expects, and the native
library itself depends on OpenMP (`libgomp.so.1`), which isn't installed by default in slim
Docker base images.

**Solution:**
1. Install `libgomp1` in the runtime image (`apt-get install -y libgomp1`).
2. Point `LD_LIBRARY_PATH` at wherever the published output placed the Whisper native libs, e.g.:
```dockerfile
ENV LD_LIBRARY_PATH=/app/runtimes/linux-x64:/app/worker-asr/runtimes/linux-x64
```
Both are already applied in the shipped `Dockerfile`'s `runtime-asr`/`runtime-all` stages - if
you're building a custom image or running the worker outside Docker, replicate both steps.

### Whisper: "Only 16KHz sample rate is supported" / `NotSupportedWaveException`

**Cause:** Whisper.net requires exactly 16kHz mono PCM input and does not resample internally.
TTS-generated audio (Kokoro is 24kHz, Supertonic-3 is 22050Hz) or arbitrary user uploads will not
match this.

**Solution:** Already handled - `WhisperAsrEngine` calls `WavAudioUtils.ResampleToMono16kWav()` on
the uploaded audio before transcription. If you see this error, verify you're running a version
that includes this fix (see [docs/wiki/docker-packaging.md](wiki/docker-packaging.md)'s "Known
fixes" section).

### Uploading FLAC/MP3/OGG/etc. to `/v1/audio/transcriptions` fails or crashes (fixed)

**Cause:** Both ASR engines originally assumed a 16-bit PCM RIFF/WAV container; any other format
threw `NotSupportedException` from `WavAudioUtils`.

**Solution:** Already fixed - the endpoint now normalizes every upload to PCM16 mono WAV via
`Services/AudioFormatConverter.cs` (shells out to `ffmpeg`) before either engine sees the bytes, so
any ffmpeg-decodable format works. Requires the `ffmpeg` binary in the runtime image - already
installed in the shipped `Dockerfile`'s `runtime-asr`/`runtime-all` stages. If you're running the
API outside Docker or in a custom image, install `ffmpeg` yourself; without it, uploads fail with
a `400 invalid_request` ("Could not decode the uploaded audio file") instead of a crash.

### Nemotron returns an empty transcript for non-English audio (fixed)

**Cause:** `NemotronLanguages.Resolve` defaulted an unset/unrecognized `language` to lang_id `0`
(English) instead of `101` (the model's actual auto-detect slot, per HuggingFace's
`Nemotron3_5AsrConfig.default_prompt_id=101`). Since the frontend sends no `language` by default,
non-English audio got wrong language conditioning and the RNNT decoder emitted mostly/only blank
tokens.

**Solution:** Already fixed - `Resolve` now defaults to `101`, and `NemotronVocabulary` extracts
the model's own emitted `<xx-XX>` language tag in auto mode so `DetectedLanguage` reflects real
detection. If you still see empty output, confirm you're running a build that includes this fix
and that the frontend's "Auto-detect" language chip (or an explicit correct `language` value) is
selected.

### Intermittent 500s when downloading a model for the first time under load

**Cause:** A background warmup service (`ModelWarmupService`/`AsrModelWarmupService`) racing an
on-demand request for the same not-yet-cached model could concurrently write the same asset
file, corrupting it. Affects both TTS and ASR models.

**Solution:** Already fixed - `ModelCache.EnsureModelAsync` serializes per-model-name downloads via
a `ConcurrentDictionary<string, SemaphoreSlim>`. If you still see this on an older build, update to
a version that includes the fix.

### Nemotron transcription was garbled/inaccurate (fixed)

**Cause (root-caused via real-audio diagnostics, see below):** the original implementation sourced
feature-extraction parameters from `audio_processor_config.json` and derived the encoder's
`lang_id` conditioning input from `vocab.txt`'s `<xx-YY>` tag line indices (e.g. `2947` for
`<en-US>`). Both were wrong: `lang_id` actually expects a small fixed integer (`0` for English),
not a vocab index - feeding a huge out-of-range id into every chunk desensitized the encoder to
the actual audio almost entirely (confirmed by comparing encoder output for real speech vs. total
silence: cosine similarity ~0.9995, i.e. nearly identical). The mel-scale (HTK instead of Slaney),
`log_eps` value, window centering, and per-chunk framing algorithm were also all subtly wrong
relative to what the model was actually trained/exported with.

**Solution:** Already fixed - `NemotronAsrEngine`/`NemotronFeatureExtractor` now source all
hyperparameters from `genai_config.json` and use a fixed `lang_id` lookup table
(`Services/NemotronLanguages.cs`), ported from a validated Python/onnxruntime reference
implementation. Confirmed via the opt-in circular tests: WER dropped from 100% to ~1-2% on
multi-sentence paragraphs, and a 20-turn conversation transcribed at near-perfect accuracy
(297 words produced vs. 296 reference words). If you still see garbled Nemotron output, confirm
you're running a build that includes this fix.

### `/api/asr-models` or `/v1/audio/transcriptions` return 404 for the whole route

**Cause:** The server wasn't started with ASR enabled.

**Solution:**
```bash
curl http://localhost:5768/api/server-info
# {"ttsEnabled": true, "asrEnabled": false}
```
Restart the container with `SERVER_MODE=asr` or `SERVER_MODE=both`.

### Live Transcription (mic/tab audio) fails with "Cannot read properties of undefined (reading 'getUserMedia')"

**Cause:** Browsers only expose `navigator.mediaDevices` on secure contexts - HTTPS, or
`http://localhost`. Serving the frontend over plain HTTP on any other hostname (e.g.
`http://your-lan-host:5768`) makes `navigator.mediaDevices` itself `undefined`, not just its
capture methods - this is a browser platform security restriction, not an app bug.

**Solution:** Serve the frontend over HTTPS - either the built-in Kestrel-native flow (run
`./generate-cert.sh <your-lan-hostname-or-ip>`, set `HTTPS_PORT`/`HOST_HTTPS_PORT`/`CERT_PASSWORD`
in `.env`, see "HTTPS / TLS" in [docs/CONFIGURATION.md](CONFIGURATION.md)) or a reverse proxy with
a TLS certificate (nginx example also in that doc), or access it via `http://localhost:5768` if
the browser and server are on the same machine. `LiveTranscription.vue` now shows this exact
explanation instead of a raw JS error when it detects the missing API.

### `GET /v1/audio/transcriptions/stream` (live transcription) returns 404

**404 ("The requested model was not found")**: the `model` query parameter was missing or didn't
match a real model name - the endpoint requires `?model=<name>` (e.g.
`?model=whisper-base`), it is not optional the way it is for some other fields. Double-check any
client script's WebSocket URL includes it.

**Note**: live transcription now works identically in both in-process mode
(`AsrWorkerOptions__Enabled=false`) and worker mode (`AsrWorkerOptions__Enabled=true`, the
`docker-compose.yml` default) - the ASR worker process implements a bidirectional streaming gRPC
RPC (`TranscribeStream`) that the API transparently proxies. If you're on an older build that still
returns `501`, update to a version that includes worker-mode streaming support.

### Live Transcription WebSocket fails in every browser, but a raw script (curl/Python) against the same URL works fine

**Cause:** an antivirus/security suite's "scan encrypted connections" (HTTPS-inspection/MITM)
feature - Kaspersky is the most commonly reported one, but similar features exist in other
suites - is intercepting the TLS connection and mishandling the WebSocket upgrade handshake.
Symptoms that confirm this rather than a server bug:
- The page itself (and its self-signed cert warning) loads fine over HTTPS, but
  `new WebSocket('wss://...')` fails immediately with a generic, reason-less "failed" in every
  browser (Chrome, Brave, etc.) on that machine.
- The exact same URL works from a plain script (Python `websockets`, `curl`) on the same machine -
  AV HTTPS-scanning is typically applied per-browser/OS-proxy, not to arbitrary script processes.
- Server-side logs show zero activity for the failed attempt - the request never reaches the
  container at all, because the AV's local proxy drops/mishandles it before forwarding.
- After accepting any "untrusted site" interstitial from the AV, the browser's padlock shows the
  connection as secure/"certificate is valid" - that's the AV's own re-issued certificate (signed
  by its locally-installed root CA), not the original self-signed one from `generate-cert.sh`.

**Solution:** add an exclusion for the FastTTSR hostname/port in the antivirus's HTTPS/encrypted-
connections-scanning settings (e.g. in Kaspersky: Settings → Network Settings → Encrypted
connections scanning → add the hostname to exclusions, or temporarily set it to "Do not scan"
to confirm the diagnosis), then retry. This is a client-side network security product limitation,
not a FastTTSR bug.

### Live Transcription WebSocket still fails after fixing antivirus interception, and Chrome shows "Not secure"/"you disabled security warnings for this site"

**Cause:** a plain `openssl`-generated self-signed certificate (`generate-cert.sh`'s fallback path)
was manually "clicked through" in the browser rather than genuinely trusted. Chrome's bypass for
an untrusted-cert warning applies to page navigation/subresource loads, but a `new WebSocket(...)`
call opens its own independent TLS handshake and re-validates the certificate - the bypass doesn't
reliably extend to it, so the WebSocket keeps failing even though the page itself loads.

**Solution:** use a genuinely trusted certificate instead of a bypassed self-signed one - re-run
`./generate-cert.sh <hostname>` with either `mkcert` installed locally, or with just `docker`
available (it auto-detects and uses `./docker/mkcert` for you, no local `mkcert` install/`sudo`
needed). Either way, **also import the resulting `./certs/mkcert-ca/rootCA.pem`** into the trust
store of the machine that runs the browser (not just the machine running the Docker container, if
they're different) - mkcert's local CA is only trusted on whichever machine actually installs it.
See "HTTPS / TLS" in [docs/CONFIGURATION.md](CONFIGURATION.md) for the full flow.

---


## Docker Issues

### Container Won't Start

**Error:**
```
Error response from daemon: Container ... is not running
```

**Diagnosis:**
```bash
docker compose logs fastttsr
docker inspect fastttsr
```

**Solutions:**

**1. Check image exists:**
```bash
docker images | grep fastttsr
```

**2. Re-pull image:**
```bash
docker compose pull
docker compose up -d
```

**3. Check volumes:**
```bash
ls -la model-cache/
ls -la assets/
```

**4. Check port availability:**
```bash
netstat -tuln | grep 5768
```

### Volume Mount Permission Denied

**Error:**
```
Permission denied: '/cache/kokoro-q4'
```

**Solution:**

**1. Fix permissions:**
```bash
chmod 777 model-cache
chown -R $(whoami) model-cache
```

**2. Or run with user:**
```yaml
services:
  fastttsr:
    user: "${UID}:${GID}"
```

**3. SELinux context (if on RHEL/CentOS):**
```bash
chcon -Rt svirt_sandbox_file_t model-cache/
```

### Container Out of Memory

**Error:**
```
Killed
```

**Solution:**

**1. Increase memory limit:**
```yaml
deploy:
  resources:
    limits:
      memory: 8G
```

**2. Check Docker daemon limits:**
```bash
docker info | grep Memory
```

**3. Increase Docker Desktop memory** (Settings → Resources → Memory)

---

## Audio Quality Issues

### Audio Sounds Robotic

**Possible Causes:**
1. Using quantized model
2. Speed setting too high/low
3. Input text has special characters

**Solutions:**

**1. Use full-precision model:**
```json
{
  "model": "kokoro-full"  // Better quality
}
```

**2. Adjust speed:**
```json
{
  "speed": 1.0  // Default, try 0.9-1.1
}
```

**3. Clean input text:**
- Remove markdown formatting
- Remove special symbols
- Use proper punctuation

### Audio Has Artifacts/Glitches

**Possible Causes:**
1. Text sanitization issue
2. Phonemization error
3. Memory corruption

**Solutions:**

**1. Check input text:**
```bash
# Test with simple text
curl -X POST http://localhost:5768/v1/audio/speech \
  -H 'Content-Type: application/json' \
  -d '{"model":"kokoro-q4","input":"Hello world","voice":"af"}' \
  --output test.wav
```

**2. Try different voice:**
```json
{
  "voice": "af_nicole"  // Clear, professional
}
```

**3. Restart service:**
```bash
docker compose restart
```

### Silent or Empty Audio

**Symptoms:** WAV file generated but no sound.

**Diagnosis:**

**1. Check file size:**
```bash
ls -lh output.wav
```

**2. Analyze WAV file:**
```bash
file output.wav
ffprobe output.wav
```

**3. Check for synthesis failure** in logs

**Solutions:**

**1. Service returns silent WAV on error** - Check logs for actual error

**2. Verify input not empty:**
```json
{
  "input": "Hello world"  // Must have text
}
```

**3. Try different model:**
```json
{
  "model": "kokoro-full"  // If kokoro-q4 fails
}
```

### Wrong Language Accent

**Symptoms:** English voice speaking Japanese, etc.

**Solution:**

**1. Always specify language:**
```json
{
  "input": "こんにちは",
  "language": "ja-jp",  // CRITICAL for non-English
  "voice": "af_bella"
}
```

**2. Use language-appropriate voice:**
- Currently all voices are English-trained
- Language parameter affects phonemization, not voice accent

---

## Debugging Tools

### Enable Verbose Logging

**appsettings.json:**
```json
{
  "Logging": {
    "LogLevel": {
      "Default": "Debug",
      "Microsoft.AspNetCore": "Information",
      "FastTTSR": "Trace"
    }
  }
}
```

### Monitor Docker Logs

**Real-time:**
```bash
docker compose logs -f fastttsr
```

**Last 100 lines:**
```bash
docker compose logs --tail=100 fastttsr
```

**Save to file:**
```bash
docker compose logs > logs.txt
```

### Check Container Health

```bash
# Health status
docker compose ps

# Detailed inspect
docker inspect fastttsr | jq '.[0].State.Health'

# Manual health check
curl http://localhost:5768/health
```

### Monitor Performance

**Resource usage:**
```bash
docker stats fastttsr
```

**System metrics:**
```bash
# CPU usage
top -p $(pgrep -f FastTTSR)

# Memory usage
ps aux | grep FastTTSR

# Disk I/O
iotop
```

### Test API Endpoints

**Health check:**
```bash
curl -v http://localhost:5768/health
```

**List models:**
```bash
curl http://localhost:5768/api/models | jq
```

**Test synthesis:**
```bash
curl -X POST http://localhost:5768/v1/audio/speech \
  -H 'Content-Type: application/json' \
  -d '{"model":"kokoro-q4","input":"Test","voice":"af"}' \
  --output test.wav -v
```

**Time request:**
```bash
time curl -X POST http://localhost:5768/v1/audio/speech \
  -H 'Content-Type: application/json' \
  -d '{"model":"kokoro-q4","input":"Test","voice":"af"}' \
  --output test.wav
```

### Validate Audio Output

**Play audio (Linux):**
```bash
aplay output.wav
```

**Play audio (macOS):**
```bash
afplay output.wav
```

**Analyze with ffmpeg:**
```bash
ffmpeg -i output.wav
ffprobe -show_format -show_streams output.wav
```

**Check audio properties:**
```bash
soxi output.wav
```

### Profile Performance

**dotnet-trace:**
```bash
dotnet tool install -g dotnet-trace
dotnet-trace collect --process-id $(pgrep -f FastTTSR)
```

**BenchmarkDotNet** (add to test project):
```csharp
[MemoryDiagnoser]
public class TtsBenchmarks
{
    [Benchmark]
    public async Task SynthesizeShortText()
    {
        // Benchmark code
    }
}
```

---

## Common Error Patterns

### Pattern: Intermittent Failures

**Symptoms:**
- Works sometimes, fails other times
- More frequent under load

**Likely Causes:**
1. Thread safety issue
2. Resource contention
3. Memory pressure

**Diagnosis:**
- Enable debug logging
- Monitor resource usage
- Test concurrent requests

### Pattern: Fails on Specific Text

**Symptoms:**
- English works, Japanese fails
- Simple text works, complex fails

**Likely Causes:**
1. Special characters
2. Language setting missing
3. espeak-ng phonemization failure

**Diagnosis:**
- Test with plain ASCII text
- Add language parameter
- Check espeak-ng support for language

### Pattern: First Request Slow, Others Fast

**Symptoms:**
- First request > 5 seconds
- Subsequent requests < 500ms

**Cause:** Model loading on first use (expected behavior)

**Solution:**
- Implement warmup
- Pre-load models
- Accept first-request latency

---

## Getting Help

### Before Asking for Help

1. ✅ Check this troubleshooting guide
2. ✅ Search existing GitHub issues
3. ✅ Review relevant documentation
4. ✅ Collect logs and error messages
5. ✅ Try minimal reproduction case

### Reporting Issues

**Include:**
- FastTTSR version / Docker image tag
- Operating system and version
- Full error message and stack trace
- Steps to reproduce
- Relevant logs
- Configuration (redact sensitive info)

**Example:**
```markdown
## Environment
- FastTTSR: latest (Docker)
- OS: Ubuntu 22.04
- Docker: 24.0.7

## Issue
Synthesis fails for Japanese text with "free(): invalid pointer"

## Steps to Reproduce
1. Start container: `docker compose up -d`
2. Send request with Japanese text
3. Observe crash in logs

## Logs
```
[error] free(): invalid pointer
Aborted (core dumped)
```

## Expected Behavior
Should synthesize Japanese audio without crashing

## Additional Context
Works fine for English text
```

### Resources

- **Documentation:** [docs/](../docs/)
- **GitHub Issues:** https://github.com/yourusername/FastTTSR/issues
- **Discussions:** https://github.com/yourusername/FastTTSR/discussions
- **Swagger API:** http://localhost:5768/swagger
