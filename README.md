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

### Endpoints

- `GET /health`
- `GET /api/models`
- `POST /v1/audio/speech`

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
- `voice`: Speaker voice (e.g., `af_bella`, `af_nicole`, `am_adam`)
- `speed`: Playback speed (0.5-2.0, default: 1.0)
- `language`: Language code (e.g., `en-us`, `ja-jp`, default: `en-us`)

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

The service exposes port 8080 internally, mapped to port 9090 on the host.

## Tests

Unit tests:
```bash
dotnet test tests/FastTTSR.Api.Tests/FastTTSR.Api.Tests.csproj
```

Integration tests with Docker:
```bash
docker compose -f docker-compose.tests.yml up --abort-on-container-exit
```

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
