# Architecture

SharpAudio is designed as a high-performance, production-ready Text-to-Speech **and** Speech-to-Text
service with a clean separation of concerns and optimized resource management. Which task types a
deployment serves is controlled by the `SERVER_MODE` environment variable (`tts` | `asr` | `both`,
default `tts`); see [ASR Architecture](#asr-architecture) below for the transcription pipeline.

## System Overview

> The diagram below shows the TTS request pipeline. ASR follows an analogous but separate
> pipeline (own router, own engines, own worker process) - see
> [ASR Architecture](#asr-architecture).

```
┌─────────────────────────────────────────────────────────────────┐
│                         Client Layer                            │
│  ┌──────────────────┐              ┌─────────────────────┐     │
│  │   Web Browser    │              │   REST API Client   │     │
│  │  (Vue.js UI)     │              │   (curl, SDK, etc)  │     │
│  └────────┬─────────┘              └──────────┬──────────┘     │
└───────────┼────────────────────────────────────┼────────────────┘
            │                                    │
            │ HTTP                               │ HTTP/HTTPS
            │                                    │
┌───────────┼────────────────────────────────────┼────────────────┐
│           │        ASP.NET Core API            │                │
│           │       (SharpAudio.Api)               │                │
│  ┌────────▼────────────────────────────────────▼─────────┐     │
│  │           Minimal API Endpoints                       │     │
│  │  /health | /api/models | /v1/audio/speech            │     │
│  └────────────────────┬──────────────────────────────────┘     │
│                       │                                         │
│  ┌────────────────────▼──────────────────────────────────┐     │
│  │         Service Layer (Dependency Injection)          │     │
│  │  ┌──────────────┐  ┌──────────────┐  ┌─────────────┐ │     │
│  │  │ModelCatalog  │  │  ModelCache  │  │TtsSynthesizer│ │     │
│  │  │              │  │              │  │   Router    │ │     │
│  │  │- Load config │  │- Download    │  │- Route to   │ │     │
│  │  │- Validate    │  │- Load models │  │  engine     │ │     │
│  │  │- Normalize   │  │- Unload idle │  │- Synthesize │ │     │
│  │  └──────────────┘  └──────────────┘  └─────────────┘ │     │
│  └───────────────────────────────────────────────────────┘     │
│                       │                                         │
│  ┌────────────────────▼──────────────────────────────────┐     │
│  │           TTS Engine Layer                            │     │
│  │  ┌─────────────────────┐  ┌──────────────────────┐   │     │
│  │  │ KokoroTtsSynthesizer│  │SupertonicTtsSynthesizer│  │     │
│  │  │  - Text sanitization│  │  - Text sanitization │   │     │
│  │  │  - Phonemization    │  │  - Flow matching     │   │     │
│  │  │  - ONNX inference   │  │  - ONNX inference    │   │     │
│  │  │  - WAV generation   │  │  - WAV generation    │   │     │
│  │  └─────────────────────┘  └──────────────────────┘   │     │
│  └───────────────────────────────────────────────────────┘     │
│                       │                                         │
│  ┌────────────────────▼──────────────────────────────────┐     │
│  │           Native Layer (P/Invoke)                     │     │
│  │  ┌──────────────────┐  ┌─────────────────────────┐   │     │
│  │  │ OnnxRuntime      │  │  espeak-ng (libespeak)  │   │     │
│  │  │ (model.onnx)     │  │  (phonemization)        │   │     │
│  │  └──────────────────┘  └─────────────────────────┘   │     │
│  └───────────────────────────────────────────────────────┘     │
└─────────────────────────────────────────────────────────────────┘
```

## Worker Process Architecture (New)

SharpAudio now supports a **worker process mode** that isolates model loading and inference into separate processes. This provides true memory isolation and guaranteed cleanup when models become idle.

### Architecture Overview - Worker Mode

```
┌────────────────────────────────────────────────────────────────────┐
│                    SharpAudio.Api (Main Process)                     │
│  ┌──────────────────────────────────────────────────────────────┐ │
│  │                   HTTP Endpoints Layer                        │ │
│  │           /v1/audio/speech | /api/models | /health           │ │
│  └──────────────────────┬───────────────────────────────────────┘ │
│                         │                                          │
│  ┌──────────────────────▼───────────────────────────────────────┐ │
│  │              WorkerProxySynthesizer                          │ │
│  │  - Routes requests to worker processes via gRPC              │ │
│  │  - Maintains gRPC channel pool                               │ │
│  │  - Handles worker failures gracefully                        │ │
│  └──────────────────────┬───────────────────────────────────────┘ │
│                         │                                          │
│  ┌──────────────────────▼───────────────────────────────────────┐ │
│  │           WorkerProcessManager (IHostedService)              │ │
│  │  - Spawns worker processes on-demand                         │ │
│  │  - Allocates ports dynamically                               │ │
│  │  - Monitors worker health and lifecycle                      │ │
│  │  - Cleans up terminated workers                              │ │
│  └──────────────────────┬───────────────────────────────────────┘ │
└────────────────────────┼────────────────────────────────────────────┘
                         │ gRPC (localhost:5005X)
                         │
         ┌───────────────┼───────────────┐
         │               │               │
┌────────▼─────┐  ┌──────▼──────┐  ┌────▼───────────┐
│Worker Process│  │Worker Process│  │Worker Process │
│  (kokoro-q4) │  │(supertonic-3)│  │  (kokoro-f)   │
├──────────────┤  ├─────────────┤  ├───────────────┤
│ gRPC Server  │  │ gRPC Server │  │ gRPC Server   │
│ Port: 50051  │  │ Port: 50052 │  │ Port: 50053   │
├──────────────┤  ├─────────────┤  ├───────────────┤
│ Idle Monitor │  │ Idle Monitor│  │ Idle Monitor  │
│ Timeout: 60s │  │ Timeout: 60s│  │ Timeout: 60s  │
├──────────────┤  ├─────────────┤  ├───────────────┤
│ Model Engine │  │ Model Engine│  │ Model Engine  │
│ ONNX Runtime │  │ ONNX Runtime│  │ ONNX Runtime  │
│ espeak-ng    │  │ espeak-ng   │  │ espeak-ng     │
└──────────────┘  └─────────────┘  └───────────────┘
     │                  │                 │
     └──────────────────┴─────────────────┘
              Process.Exit(0)
              after idle timeout
```

### Key Benefits

1. **True Memory Isolation** - Each model runs in its own OS process
2. **Guaranteed Cleanup** - OS reclaims ALL memory when worker terminates
3. **Fault Isolation** - Worker crashes don't affect API process
4. **Automatic Respawn** - Failed workers restart on next request
5. **Reduced Base Memory** - API process stays lightweight (~50MB vs ~300MB+)

### Component Details

#### WorkerProcessManager

**Purpose:** Spawns and manages worker process lifecycle

**Responsibilities:**
- Spawn worker processes with unique ports
- Parse "READY:port" signal from worker stdout
- Maintain registry of active workers (modelKey → WorkerProcess)
- Monitor process exit and cleanup
- Port allocation and collision avoidance
- Worker health tracking

**Configuration:**
```json
{
  "WorkerOptions": {
    "Enabled": true,
    "ExecutablePath": "./SharpAudio.Worker",
    "IdleTimeoutSeconds": 60,
    "PortRangeStart": 50051,
    "MaxPortAttempts": 100,
    "StartupTimeoutSeconds": 30
  }
}
```

**Worker Spawn Command:**
```bash
./SharpAudio.Worker --port 50051 --model-key "kokoro:kokoro-q4" --idle-timeout 60
```

#### WorkerProxySynthesizer

**Purpose:** gRPC client proxy that routes synthesis requests to workers

**Responsibilities:**
- Get or spawn worker for requested model (via WorkerProcessManager)
- Create gRPC channel to worker (cached per model)
- Marshal synthesis request to gRPC message
- Unmarshal gRPC response to SynthesisResult
- Handle gRPC errors and worker failures
- Return fallback silence on errors

**gRPC Contract (synthesis.proto):**
```protobuf
service WorkerSynthesis {
  rpc Synthesize(SynthesizeRequest) returns (SynthesizeResponse);
  rpc HealthCheck(HealthCheckRequest) returns (HealthCheckResponse);
}
```

#### SharpAudio.Worker Process

**Purpose:** Self-contained worker process that loads and runs one model

**Lifecycle:**
1. Parse command-line arguments (port, model-key, idle-timeout)
2. Start Kestrel HTTP/2 server on specified port
3. Register gRPC service (WorkerSynthesisService)
4. Signal readiness: `Console.WriteLine("READY:{port}")`
5. Handle synthesis requests via gRPC
6. Track idle time with IdleMonitor
7. Self-terminate after idle timeout: `Environment.Exit(0)`

**Components:**
- **Program.cs** - Worker entry point, Kestrel configuration
- **WorkerSynthesisService** - gRPC service implementation, routes to KokoroTtsEngine or SupertonicTtsEngine
- **IdleMonitor** - Background timer that calls shutdown callback after timeout

### Worker Lifecycle

```
┌──────────────────────────────────────────────────────────────────┐
│                        Worker Lifecycle                          │
└──────────────────────────────────────────────────────────────────┘

1. Client Request
   ↓
2. API receives POST /v1/audio/speech
   ↓
3. WorkerProxySynthesizer.SynthesizeAsync()
   ↓
4. Check if worker exists for model
   ↓ (no)
5. WorkerProcessManager.SpawnWorkerAsync()
   - Allocate port (e.g., 50051)
   - Start: ./SharpAudio.Worker --port 50051 --model-key "kokoro:kokoro-q4"
   - Wait for "READY:50051" on stdout (max 30s)
   ↓
6. Worker process starts
   - Load ONNX model (~100-300MB)
   - Start gRPC server
   - Print "READY:50051"
   ↓
7. WorkerProxySynthesizer creates gRPC channel
   ↓
8. Send SynthesizeRequest via gRPC
   ↓
9. Worker processes request
   - Text sanitization
   - Phonemization (espeak-ng)
   - ONNX inference
   - WAV generation
   - Record activity (reset idle timer)
   ↓
10. Return SynthesizeResponse (audio bytes)
    ↓
11. API streams audio to client
    ↓
    ... time passes (60+ seconds) ...
    ↓
12. Worker idle timeout reached
    - IdleMonitor detects idle > 60s
    - Execute shutdown callback: Environment.Exit(0)
    ↓
13. WorkerProcessManager detects process exit
    - Remove worker from registry
    - Release port
    - Dispose gRPC channel
    ↓
14. Next request for same model → spawn new worker (back to step 5)
```

### Memory Management Comparison

#### In-Process Mode (Old)
```
API Process Memory = Base (50MB) + All Loaded Models
- kokoro-q4:  ~150MB
- kokoro-f:   ~380MB
- supertonic: ~250MB

Total if all loaded: 50MB + 150MB + 380MB + 250MB = 830MB

Issues:
- Models stay loaded even when idle
- Dispose() may not release native memory (ONNX, NumSharp)
- Garbage collection delays
- Memory fragmentation
```

#### Worker Mode (New)
```
API Process Memory = Base (50MB) - stays constant
Worker Processes:
- kokoro-q4 worker:  ~150MB (only when active)
- kokoro-f worker:   ~380MB (only when active)
- supertonic worker: ~250MB (only when active)

Total when all active: 50MB + 150MB + 380MB + 250MB = 830MB
Total when all idle:   50MB + 0MB + 0MB + 0MB = 50MB ✅

Benefits:
- OS guarantees memory reclamation on Process.Exit()
- No memory leaks from native libraries
- API stays lightweight
- Scales down automatically
```

### Configuration

#### Enable/Disable Worker Mode

**appsettings.json:**
```json
{
  "WorkerOptions": {
    "Enabled": true  // false = use in-process synthesizers
  }
}
```

**Environment Variable (Docker):**
```bash
docker run -e WorkerOptions__Enabled=true sharp-audio
```

#### Worker Idle Timeout

Controls how long a worker stays alive after last request:
```json
{
  "WorkerOptions": {
    "IdleTimeoutSeconds": 60  // Recommended: 30-300
  }
}
```

**Trade-offs:**
- **Lower (30s):** Faster memory reclamation, more frequent respawns
- **Higher (300s):** Better performance for bursty traffic, higher memory usage

#### Port Range

Workers bind to sequential ports starting from `PortRangeStart`:
```json
{
  "WorkerOptions": {
    "PortRangeStart": 50051,
    "MaxPortAttempts": 100
  }
}
```

**Firewall:** Ports are localhost-only, no external access needed

### Error Handling

#### Worker Spawn Failure
- **Cause:** Executable not found, port collision, timeout
- **Recovery:** Return error to client, log failure
- **Next Request:** Retry spawn

#### Worker Crash During Request
- **Cause:** ONNX inference error, OOM, segfault
- **Detection:** gRPC call fails with RpcException
- **Recovery:** Return fallback silence, remove dead worker
- **Next Request:** Spawn new worker

#### Worker Exit Between Requests
- **Detection:** Process.HasExited = true
- **Recovery:** Transparent respawn on next request
- **Impact:** First request after exit has +200-500ms latency (model load)

### Performance Characteristics

#### Latency
- **First Request (Cold Start):** +200-500ms (spawn + model load)
- **Subsequent Requests (Warm):** +1-3ms (gRPC overhead vs in-process)
- **After Idle Timeout:** Cold start again

#### Throughput
- **Single Worker:** Same as in-process (~50-100 req/s for Kokoro)
- **Multiple Models:** Better than in-process (no lock contention)

#### Memory
- **Baseline:** API process stays at ~50MB
- **Peak:** Sum of active worker processes
- **Idle:** Returns to baseline after timeout

### Docker Deployment

**Dockerfile:**
```dockerfile
# Build both API and Worker
RUN dotnet publish src/SharpAudio.Api/SharpAudio.Api.csproj -o /out/api
RUN dotnet publish src/SharpAudio.Worker/SharpAudio.Worker.csproj -o /out/worker

# Copy to runtime image
COPY --from=build /out/api/ ./
COPY --from=build /out/worker/ ./worker/

# Configure worker path
ENV WorkerOptions__ExecutablePath=/app/worker/SharpAudio.Worker
```

**Important:** Both API and Worker share the same model cache directory (`/cache`), so models are only downloaded once.

### Monitoring

#### Worker Health Check
```bash
# gRPC health check (from within worker)
grpc_cli call localhost:50051 synthesis.WorkerSynthesis.HealthCheck ""

# Response:
# is_ready: true
# model_loaded: "kokoro:kokoro-q4"
# idle_seconds: 23.4
```

#### Process Listing
```bash
# Check active workers
ps aux | grep SharpAudio.Worker

# Example output:
# ./SharpAudio.Worker --port 50051 --model-key "kokoro:kokoro-q4"
# ./SharpAudio.Worker --port 50052 --model-key "supertonic:supertonic-3"
```

#### Logs
```
[SharpAudio] Worker mode ENABLED - models will run in separate processes
[Worker] Starting on port 50051 for model: kokoro:kokoro-q4
[Worker] READY:50051
[Worker] Synthesis request: engine=kokoro, model=kokoro-q4
[Worker] Synthesis complete: 42 chars, 0.15s processing, 1.8s audio
[Worker] Idle timeout reached (60s). Shutting down...
```



## Core Components

### 1. API Layer (`Program.cs`)

**Responsibilities:**
- HTTP endpoint routing
- Request validation
- Response formatting
- Swagger/OpenAPI documentation
- CORS and security headers

**Key Endpoints:**
- `GET /health` - Health checks for load balancers
- `GET /api/server-info` - Which task types (TTS/ASR) this server instance was started with
- `GET /api/models` - Detailed TTS model information (present when `SERVER_MODE` enables TTS)
- `GET /api/asr-models` - Detailed ASR model information (present when `SERVER_MODE` enables ASR)
- `GET /v1/models` - OpenAI-compatible model listing (merges TTS + ASR catalogs)
- `POST /v1/audio/speech` - OpenAI-compatible TTS synthesis
- `POST /v1/audio/transcriptions` - OpenAI-compatible ASR transcription

### 2. Service Layer

#### ModelCatalog (`Services/ModelCatalog.cs`)

**Purpose:** Model registry and metadata management

**Responsibilities:**
- Load model definitions from `config.json`
- Provide fallback to default models
- Normalize model names and aliases
- Validate model existence
- Apply canonical metadata (languages, speakers)

**Configuration:**
```json
{
  "models": [
    {
      "name": "kokoro-q4",
      "engine": "kokoro",
      "modelPath": "onnx/model_q4.onnx",
      "assets": [...]
    }
  ]
}
```

#### ModelCache (`Services/ModelCache.cs`)

**Purpose:** Model lifecycle and resource management

**Responsibilities:**
- Download models from Hugging Face on first use
- Cache models in `MODEL_CACHE_DIR`
- Maintain in-memory model instances
- Load speaker voice embeddings (`.bin` files)
- Verify file integrity
- Thread-safe model access

**Caching Strategy:**
1. Check local cache (`/cache/model-name/`)
2. If missing, download from `assets.url`
3. Store locally for subsequent requests
4. Load into memory on first synthesis request

#### TtsSynthesizerRouter (`Services/TtsSynthesizerRouter.cs`)

**Purpose:** Route requests to appropriate TTS engine

**Responsibilities:**
- Detect engine type from model configuration
- Delegate to `KokoroTtsSynthesizer` or `SupertonicTtsSynthesizer`
- Implement `IIdleTrackingSynthesizer` interface
- Track last activity timestamp for idle monitoring

### 3. TTS Engine Layer

#### KokoroTtsSynthesizer (`Services/KokoroTtsSynthesizer.cs`)

**Purpose:** Kokoro-82M TTS inference engine

**Processing Pipeline:**
1. **Text Sanitization** (`TextSanitizer`)
   - Remove markdown formatting
   - Normalize CJK punctuation
   - Clean special characters

2. **Phonemization** (`EspeakWrapper`)
   - Convert text to IPA phonemes using espeak-ng
   - Thread-safe locking (critical for stability)
   - Language-specific pronunciation

3. **Token Encoding**
   - Map phonemes to token IDs
   - Pad/truncate to model input size

4. **ONNX Inference** (`KokoroTtsEngine`)
   - Load speaker voice embedding (510 x 256 dimensions)
   - Run ONNX model with OnnxRuntime
   - Generate mel-spectrogram

5. **Audio Generation**
   - Convert mel-spectrogram to 24kHz 16-bit PCM
   - Apply speed adjustment
   - Generate WAV file with proper headers

**Memory Layout:**
- Model weights: ~82MB (quantized) or ~328MB (full)
- Speaker embeddings: 510 voices × 256 float32 = ~500KB per voice
- Runtime tensors: ~10-50MB depending on input length

#### SupertonicTtsSynthesizer (`Services/SupertonicTtsSynthesizer.cs`)

**Purpose:** Supertonic-3 multilingual TTS engine

**Processing Pipeline:**
1. **Text Sanitization** (same as Kokoro)
2. **Multi-model ONNX Inference**
   - Text Encoder (convert text to embeddings)
   - Duration Predictor (predict phoneme durations)
   - Vector Estimator (compute style vectors)
   - Vocoder (generate audio waveform)

3. **Flow Matching**
   - Iterative refinement process
   - Higher quality than traditional vocoders

**Model Architecture:**
- 4 separate ONNX models (text_encoder, duration_predictor, vector_estimator, vocoder)
- JSON-based voice style definitions (F1-F5, M1-M5)
- 31 supported languages
- 22.05kHz output sample rate

### 4. Background Services

#### ModelWarmupService (`Services/ModelWarmupService.cs`)

**Purpose:** Pre-load models on startup

**Behavior:**
- Runs once during application startup
- Downloads required model files
- Optionally pre-loads models into memory
- Improves first request latency

#### ModelIdleMonitorService (`Services/ModelIdleMonitorService.cs`)

**Purpose:** Automatic memory management

**Configuration:**
```json
{
  "ModelIdleMonitor": {
    "IdleTimeoutSeconds": 60,
    "CheckIntervalSeconds": 10
  }
}
```

**Behavior:**
- Runs every `CheckIntervalSeconds`
- Checks last activity timestamp for each model
- Unloads models idle > `IdleTimeoutSeconds`
- Reduces memory footprint in production
- Models reload automatically on next request

### 5. ASR Layer (parallel to TTS)

Everything above has an ASR-side counterpart, following the exact same patterns:

| TTS | ASR | Role |
|-----|-----|------|
| `ModelCatalog` | `AsrModelCatalog` | Model registry (`config.json`'s `asrModels` section, or built-in defaults for `whisper-base`/`nemotron-3.5`) |
| `TtsSynthesizerRouter` | `AsrTranscriberRouter` | Routes by `model.Engine` (`whisper` vs `nemotron-3.5`) to the right transcriber |
| `KokoroTtsSynthesizer` | `WhisperAsrTranscriber` | Single-engine pooling (one `WhisperAsrEngine`/whisper.cpp instance per loaded model) |
| `SupertonicTtsSynthesizer` | `NemotronAsrTranscriber` | Multi-session-in-one-class (one `NemotronAsrEngine` wrapping 3 chained ONNX sessions: encoder/decoder/joint) |
| `WorkerProcessManager` (TTS) | `WorkerProcessManager` (keyed `"asr"`) | Same worker-process class, registered twice via keyed DI so TTS and ASR workers coexist without conflict |
| `ModelWarmupService`/`ModelIdleMonitorService` | `AsrModelWarmupService`/`AsrModelIdleMonitorService` | Same warmup/idle-unload behavior for ASR models |

**Engines:**
- **Whisper** (`WhisperAsrEngine`): wraps [Whisper.net](https://github.com/sandrohanea/whisper.net)
  (whisper.cpp/GGML models). Requires exactly 16kHz mono PCM input - `WavAudioUtils.
  ResampleToMono16kWav()` resamples whatever the client uploads (or whatever TTS produced, for the
  circular tests) before transcription. Whole-file batch transcription only.
- **Nemotron 3.5** (`NemotronAsrEngine`): raw `Microsoft.ML.OnnxRuntime`, cache-aware streaming
  FastConformer-RNNT. Chains 3 `InferenceSession`s (encoder → LSTM decoder/predictor → joint),
  threading `cache_last_channel`/`cache_last_time` tensors across 65-frame chunks to emulate
  streaming decoding even though today's HTTP API only exposes whole-file transcription (chunking
  happens *inside* the engine, not across separate HTTP requests).

**Worker process**: a single `SharpAudio.Worker.Asr` executable internally routes Whisper vs.
Nemotron by `model.Engine`, exactly mirroring how `SharpAudio.Worker` routes Kokoro vs. Supertonic -
one ASR worker executable total, not two.

## Data Flow

### Synthesis Request Flow

```
1. Client Request
   POST /v1/audio/speech
   {
     "model": "kokoro-q4",
     "input": "Hello world",
     "voice": "af_bella",
     "speed": 1.0,
     "language": "en-us"
   }

2. Request Validation (Program.cs)
   - Validate model exists
   - Normalize language/speaker aliases
   - Validate speed range

3. Model Resolution (ModelCatalog)
   - Lookup model definition
   - Get engine type, paths, metadata

4. Model Loading (ModelCache)
   - Check if model cached locally
   - Download if missing
   - Load into memory if not loaded
   - Load speaker voice embedding

5. TTS Synthesis (KokoroTtsSynthesizer)
   - Sanitize text
   - Phonemize with espeak-ng
   - Tokenize phonemes
   - Run ONNX inference
   - Generate WAV audio

6. Response
   - Return audio/wav stream
   - Include timing metrics in logs
```

## Thread Safety

### Critical Sections

#### espeak-ng Library
**Problem:** espeak_TextToPhonemes returns pointer to static global buffer

**Solution:** `SemaphoreSlim` serialization in `EspeakWrapper`
```csharp
_espeakLock.Wait();
try {
    var result = espeak_TextToPhonemes(...);
    var phoneme = Marshal.PtrToStringUTF8(result);  // Copy before release
    return phoneme;
}
finally {
    _espeakLock.Release();
}
```

#### ONNX Runtime
**Thread Safety:** OnnxRuntime sessions are thread-safe for inference

**Approach:** Multiple threads can call `InferenceSession.Run()` concurrently

#### Model Cache
**Thread Safety:** Dictionary access protected by locking

**Concurrent Access:**
- Read operations: Multiple threads can read concurrently
- Write operations: Serialized with lock

## Memory Management

### Model Memory Lifecycle

```
Startup → Download → Load → Idle → Unload → Reload (on demand)
```

**Memory Footprint:**
- Kokoro Q4: ~100MB (model + runtime)
- Kokoro Full: ~350MB (model + runtime)
- Supertonic-3: ~200MB (4 models + runtime)
- Per-request: ~20-50MB temporary tensors

**Optimization Strategies:**
1. **Lazy Loading** - Models loaded on first request
2. **Idle Unloading** - Release memory after inactivity
3. **Shared Assets** - Reuse tokens.txt, espeak-ng-data
4. **Streaming Response** - Don't buffer entire audio in memory

## Error Handling

### Graceful Degradation

**Synthesis Failure → Silent WAV**
- If ONNX inference fails, return valid silent WAV
- Prevents downstream clients from breaking
- Logs error for monitoring

**Model Download Failure → Retry**
- Automatic retry with exponential backoff
- Fallback to cached version if available

**Invalid Input → Descriptive Error**
- Return HTTP 400 with clear error message
- Include supported options in response

## Performance Characteristics

### Latency Breakdown

| Operation | Time | Notes |
|-----------|------|-------|
| Text Sanitization | <1ms | Regex operations |
| Phonemization | 5-20ms | espeak-ng, depends on text length |
| ONNX Inference (Kokoro) | 50-200ms | Depends on input length |
| ONNX Inference (Supertonic) | 100-400ms | Multiple models, higher quality |
| WAV Generation | 5-10ms | PCM encoding |
| **Total (Kokoro)** | **60-230ms** | For typical sentence |
| **Total (Supertonic)** | **110-430ms** | For typical sentence |

### Throughput

- **Single Instance:** 50-100 requests/sec (Kokoro), 20-40 requests/sec (Supertonic)
- **Concurrent Requests:** Limited by CPU cores and memory
- **Model Switching:** Adds 50-200ms for first request after switch

## Security Considerations

### Input Validation
- Text length limits (prevent abuse)
- Speed range validation (0.5-2.0)
- Model/speaker existence checks
- Sanitize special characters (prevent injection)

### Resource Limits
- Request timeout
- Maximum audio duration
- Memory limits via idle unloading
- Rate limiting (external, e.g., nginx)

### Dependencies
- Regular updates for security patches
- No user credentials stored
- No sensitive data in logs
- Read-only model files

## Deployment Considerations

### Horizontal Scaling
- **Stateless:** Each instance independent
- **Shared Storage:** Mount shared `/cache` volume
- **Load Balancer:** Round-robin or least-connections
- **Health Checks:** Use `/health` endpoint

### Vertical Scaling
- **CPU:** More cores → better concurrency
- **Memory:** More RAM → more cached models
- **Storage:** SSD for faster model loading

### Cloud Deployment
- **Container Registry:** Docker Hub, ACR, ECR
- **Orchestration:** Kubernetes, ECS, Azure Container Apps
- **Auto-scaling:** Based on CPU or request queue length
- **CDN:** Optional for static assets

## Extension Points

### Adding New Models
1. Define model in `config.json` (`models` for TTS, `asrModels` for ASR)
2. Implement engine in `Services/` if needed
3. Add metadata in `*Metadata.cs`
4. Register in `TtsSynthesizerRouter` (TTS) or `AsrTranscriberRouter` (ASR)

### Custom Phonemization
- Replace `EspeakWrapper` with custom implementation
- Implement same interface
- Register in DI container

### Custom Audio Processing
- Add post-processing in synthesizer
- Modify WAV generation pipeline
- Add effects (reverb, EQ, etc.)

## Monitoring and Observability

### Logging
- ASP.NET Core logging framework
- Configurable log levels
- Structured logging for analysis

### Metrics (Future)
- Request count by model
- Synthesis latency percentiles
- Error rates
- Model cache hit/miss ratio
- Memory usage per model

### Health Checks
- `/health` endpoint for liveness
- Model availability checks
- Storage availability checks
