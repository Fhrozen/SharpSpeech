# Architecture

FastTTSR is designed as a high-performance, production-ready Text-to-Speech service with a clean separation of concerns and optimized resource management.

## System Overview

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
│           │       (FastTTSR.Api)               │                │
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
- `GET /api/models` - Detailed model information
- `GET /v1/models` - OpenAI-compatible model listing
- `POST /v1/audio/speech` - OpenAI-compatible TTS synthesis

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
1. Define model in `config.json`
2. Implement engine in `Services/` if needed
3. Add metadata in `*Metadata.cs`
4. Register in `TtsSynthesizerRouter`

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
