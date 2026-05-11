# FastTTSR

Web & REST API for TTS (OpenAI-compatible endpoint), with a Vue frontend and Docker packaging.

## Implemented scope

- C# REST API compatible with `POST /v1/audio/speech`
- Supported models in API contract:
  - `kokoro-q4`
  - `kokoro-full`
  - `supertonic-3`
- Model aliases/URLs are loaded from `src/FastTTSR.Api/config.json` (or `MODEL_CONFIG_PATH`)
- Kokoro voices are resolved from the `voices/` folder in the ONNX Community repo (speaker-based `.bin` files)
- Model download from Hugging Face on startup into cache directory (`MODEL_CACHE_DIR`)
- Vue.js frontend (pnpm) with model/language/speaker selection
- Dockerfile and docker-compose for a single service deployment

## Backend API

### Endpoints

- `GET /health`
- `GET /api/models`
- `POST /v1/audio/speech`

The backend uses in-process Sherpa-ONNX .NET bindings for inference.  
If direct synthesis cannot be initialized for a request, the service returns generated WAV output for API-flow testing.

Example request:

```bash
curl -X POST http://localhost:8080/v1/audio/speech \
  -H 'Content-Type: application/json' \
  -d '{"model":"kokoro-q4","input":"Hello world"}' \
  --output speech.wav
```

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

The service uses two mounted volumes:
- `./model-cache` → `/cache` - Downloaded model files (ONNX models, voice files)
- `./assets` → `/app/assets` - Shared resources (tokens.txt, espeak-ng-data)

### Requirements for Kokoro Models

Kokoro models require:
1. **tokens.txt** - Mounted from `./assets/tokens.txt` (no download needed)
2. **espeak-ng-data** - Mounted from `./assets/espeak-ng-data` (no download needed)

The Docker setup uses pre-existing assets from the `./assets` folder. See [assets/README.md](assets/README.md) for details on the assets folder structure.

## Tests

Unit tests:
```bash
dotnet test tests/FastTTSR.Api.Tests/FastTTSR.Api.Tests.csproj
```

Integration tests with Docker:
```bash
./run-tests.sh
```

See [TESTING.md](TESTING.md) for comprehensive testing documentation.
