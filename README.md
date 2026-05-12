# FastTTSR

Web & REST API for TTS (OpenAI-compatible endpoint), with a Vue frontend and Docker packaging.

## Implemented scope

- C# REST API compatible with `POST /v1/audio/speech`
- Supported models:
  - `kokoro-q4` - Quantized Kokoro 82M ONNX model
  - `kokoro-full` - Full precision Kokoro 82M ONNX model
- Custom TTS engine using:
  - **Microsoft.ML.OnnxRuntime** for direct ONNX inference
  - **espeak-ng** for text-to-phoneme conversion (IPA phonemization)
  - **NumSharp** for tensor operations
- Model aliases/URLs loaded from `src/FastTTSR.Api/config.json` (or `MODEL_CONFIG_PATH`)
- Speaker voices (`.bin` files) from ONNX Community Kokoro repo, loaded dynamically
- Automatic model download from Hugging Face on startup into `MODEL_CACHE_DIR`
- Vue.js frontend (pnpm) with model/language/speaker selection
- Multi-stage Docker build with .NET 10 preview

## Backend API

### Interactive API Documentation

Swagger/OpenAPI documentation is available at:
- **Swagger UI**: `http://localhost:9090/swagger`
- **OpenAPI Spec**: `http://localhost:9090/swagger/v1/swagger.json`

### Endpoints

- `GET /health`
- `GET /api/models`
- `GET /v1/models` (OpenAI compatible)
- `POST /v1/audio/speech` (OpenAI compatible)

The backend uses a custom TTS engine with:
- **Direct ONNX inference** via Microsoft.ML.OnnxRuntime for model execution
- **Native espeak-ng** integration via P/Invoke for phonemization
- **Dynamic speaker voice loading** from `.bin` files (510 speaker embeddings × 256 dimensions)
- **24kHz 16-bit PCM mono WAV** output

If synthesis fails, the service returns generated silent WAV for graceful degradation.

Example request:

```bash
curl -X POST http://localhost:9090/v1/audio/speech \
  -H 'Content-Type: application/json' \
  -d '{
    "model": "kokoro-q4",
    "input": "Hello world, this is a test.",
    "voice": "af_bella",
    "speed": 1.0
  }' \
  --output speech.wav
```

Supported parameters:
- `model`: `kokoro-q4` or `kokoro-full`
- `input`: Text to synthesize
- `voice`: Speaker voice (full Kokoro speaker catalog exposed by `GET /api/models`; OpenAI aliases like `alloy` are accepted)
- `speed`: Playback speed (0.5-2.0, default: 1.0)
- `language`: Language code (`en-us`, `en-gb`, `es`, `fr-fr`, `hi`, `it`, `ja-jp`, `pt-br`, `zh-cn`; one-letter aliases like `a`, `b`, `j`, `z` are accepted)

## Frontend

```bash
cd frontend
corepack enable pnpm
pnpm install --ignore-scripts
pnpm dev
```

## Docker

Build and run with compose:

```bash
docker compose build
docker compose up -d
```

The service uses mounted volumes:
- `./model-cache` → `/cache` - Downloaded model files (ONNX models, speaker voice files)
- `./assets` → `/app/assets` - Static assets (tokens.txt, espeak-ng-data)

Environment variables:
- `MODEL_CACHE_DIR=/cache` - Model cache directory
- `ESPEAK_DATA_DIR=/app/assets/espeak-ng-data` - espeak-ng phoneme data
- `MODEL_IDLE_TIMEOUT_SECONDS=60` - Automatically release models from memory after N seconds of inactivity (default: 60, set to 0 to disable)

The service exposes port 8080 internally, mapped to port 9090 on the host.

### Memory Management

FastTTSR includes automatic model memory management to optimize resource usage:

- Models are loaded on first use and cached in memory for fast subsequent requests
- After a configurable idle period (default: 60 seconds), unused models are automatically released from memory
- This helps reduce memory footprint in production deployments while maintaining fast response times for active models
- Configure the timeout via the `MODEL_IDLE_TIMEOUT_SECONDS` environment variable
- Set to `0` to disable automatic release and keep all loaded models in memory

## Tests

### Local Testing

Unit tests:
```bash
dotnet test tests/FastTTSR.Api.Tests/FastTTSR.Api.Tests.csproj
```

Integration tests with Docker:
```bash
# Run all tests (unit + integration)
./tests/run-tests.sh all

# Run specific test types
./tests/run-tests.sh unit
./tests/run-tests.sh integration
```

Or use Docker Compose directly:
```bash
docker compose -f docker-compose.test.yml up --abort-on-container-exit
```

### Continuous Integration

GitHub Actions automatically runs tests on all pull requests:

- **Unit Tests**: Fast, isolated tests (~2 minutes)
- **Integration Tests**: Full API tests with model downloads (~5-15 minutes)
- **Test Reports**: Automated test summaries in PR checks

The CI workflow:
- Runs on PRs to `main`, `master`, or `develop` branches
- Caches model files (~1-2GB) to speed up subsequent runs
- Uploads test results and API logs as artifacts
- Reports test failures directly in PR checks

See [`.github/workflows/README.md`](.github/workflows/README.md) for details.

## Architecture

```
HTTP Request → KokoroTtsSynthesizer
                    ↓
              KokoroTtsEngine
                    ↓
        ┌───────────┴───────────┐
        ↓                       ↓
  EspeakWrapper          OnnxRuntime
  (phonemization)        (inference)
        ↓                       ↓
  libespeak-ng.so.1      model.onnx
        └───────────┬───────────┘
                    ↓
            PCM16 WAV Output
```

## Requirements

- .NET 10.0 SDK (preview)
- Docker with Compose v2
- Node.js 24+ with pnpm (for frontend development)
- espeak-ng library (included in Docker image)
