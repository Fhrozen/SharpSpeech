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
docker compose up --build
```

The model cache is stored in the `model-cache` volume and mapped to `/cache` in the container.

## Tests

```bash
dotnet test tests/FastTTSR.Api.Tests/FastTTSR.Api.Tests.csproj
```
